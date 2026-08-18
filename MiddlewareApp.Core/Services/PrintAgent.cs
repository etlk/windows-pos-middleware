using MiddlewareApp.Core.Models;

namespace MiddlewareApp.Core.Services;

/// <summary>
/// The background print agent (spec §8). Lifecycle rules ported exactly:
/// started after every successful print-config load; if the same
/// business/location/device agent is already running, just refresh configs +
/// persistence (no reconnect). Only stopped by "Clear all &amp; reconfigure" (or Quit).
/// </summary>
public class PrintAgent
{
    public static PrintAgent Instance { get; } = new();

    private readonly PusherListenerService _listener = new();
    private readonly PrintQueue _queue = new();
    private readonly ReceiptPrinter _printer;

    /// <summary>Set by the host app to enable logo printing (System.Drawing on Windows).</summary>
    public static IReceiptImageDecoder? ImageDecoder { get; set; }

    public AgentSession? Session { get; private set; }
    public AgentConfigs? Configs { get; private set; }
    public bool IsRunning { get; private set; }

    /// <summary>disconnected | connecting | connected | failed | unavailable</summary>
    public string ListenerState => MissingPusherKey ? "failed" : _listener.State;
    public string? ChannelName => _listener.ChannelName;
    public string? LastJobMessage { get; private set; }
    public bool MissingPusherKey => string.IsNullOrWhiteSpace(AppConfig.PusherKey);

    /// <summary>Raised on any state change (listener state, last-job line, start/stop).</summary>
    public event Action? Changed;

    private PrintAgent()
    {
        // The decoder wrapper lets the host app install ImageDecoder after startup.
        ReceiptPrinter? printerRef = null;
        var renderer = new EscPosRenderer(new LazyDecoder(), url => printerRef!.FetchImageAsync(url));
        _printer = new ReceiptPrinter(renderer);
        printerRef = _printer;

        _listener.StateChanged += _ => Changed?.Invoke();
        _listener.JobReceived += raw => _ = HandleJobAsync(raw);
    }

    private sealed class LazyDecoder : IReceiptImageDecoder
    {
        public MonoImage? Decode(byte[] data, int maxWidthDots) => ImageDecoder?.Decode(data, maxWidthDots);
    }

    public async Task StartAsync(AgentSession session, AgentConfigs configs)
    {
        session.Enabled = true;

        if (IsRunning && Session != null && Session.SameTarget(session))
        {
            // Same agent already running: refresh configs + persistence, don't reconnect.
            Session = session;
            Configs = configs;
            AgentStorage.SaveSession(session);
            AgentStorage.SaveConfigs(configs);
            Changed?.Invoke();
            return;
        }

        Session = session;
        Configs = configs;
        AgentStorage.SaveSession(session);
        AgentStorage.SaveConfigs(configs);
        IsRunning = true;
        Changed?.Invoke();

        await _listener.StartAsync(session.BusinessCode, session.LocationId).ConfigureAwait(false);
        Changed?.Invoke();
    }

    /// <summary>Refresh configs on the running agent (60 s poll) and persist them.</summary>
    public void UpdateConfigs(AgentConfigs configs)
    {
        Configs = configs;
        AgentStorage.SaveConfigs(configs);
        Changed?.Invoke();
    }

    /// <summary>Stop + clear persistence ("Clear all &amp; reconfigure").</summary>
    public async Task StopAsync()
    {
        IsRunning = false;
        Session = null;
        Configs = null;
        LastJobMessage = null;
        AgentStorage.Clear();
        await _listener.StopAsync().ConfigureAwait(false);
        Changed?.Invoke();
    }

    /// <summary>Reconnect on window focus / OS resume / network change if disconnected or failed.</summary>
    public async Task ReconnectIfNeededAsync()
    {
        if (!IsRunning || Session == null || MissingPusherKey) return;
        if (ListenerState is not ("disconnected" or "failed")) return;
        try
        {
            await _listener.StartAsync(Session.BusinessCode, Session.LocationId).ConfigureAwait(false);
        }
        catch
        {
            // state events already reflect the failure
        }
        Changed?.Invoke();
    }

    private async Task HandleJobAsync(string rawPayload)
    {
        var configs = Configs;
        if (configs == null) return;

        var evaluation = PrintJobHandler.Evaluate(rawPayload, configs);
        if (!evaluation.ShouldPrint)
        {
            SetLastJob(evaluation.Message!);
            return;
        }

        var printer = evaluation.Printer!;
        var port = printer.Port > 0 ? printer.Port : 9100;
        var key = $"{printer.Ip}:{port}";
        try
        {
            await _queue.Enqueue(key, () => _printer.PrintAsync(evaluation.Html!, printer)).ConfigureAwait(false);
            SetLastJob($"Printed to {printer.Ip}:{port}");
        }
        catch (Exception ex)
        {
            SetLastJob($"Print failed: {ex.Message}");
        }
    }

    private void SetLastJob(string message)
    {
        LastJobMessage = message;
        Changed?.Invoke();
    }
}
