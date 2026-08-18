using PusherClient;

namespace MiddlewareApp.Core.Services;

/// <summary>
/// Pusher WebSocket listener (spec §4). Surfaces connection states
/// disconnected | connecting | connected | failed | unavailable, and raw job payloads.
/// Starting a listener always tears down any previous one first.
/// </summary>
public class PusherListenerService
{
    private Pusher? _pusher;
    private Channel? _channel;

    public string State { get; private set; } = "disconnected";
    public string? ChannelName { get; private set; }

    public event Action<string>? StateChanged;
    public event Action<string>? JobReceived;

    public async Task StartAsync(string businessCode, int locationId)
    {
        await StopAsync().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(AppConfig.PusherKey))
        {
            SetState("failed");
            return;
        }

        var code = AppConfig.NormalizeBusinessCode(businessCode);
        ChannelName = $"merchant.{code}.location.{locationId}";

        var pusher = new Pusher(AppConfig.PusherKey, new PusherOptions
        {
            Cluster = AppConfig.PusherCluster,
            Encrypted = true, // forceTLS
        });
        _pusher = pusher;

        pusher.ConnectionStateChanged += (_, state) => SetState(MapState(state));
        pusher.Error += (_, _) => SetState("failed");

        SetState("connecting");
        await pusher.ConnectAsync().ConfigureAwait(false);

        var channel = await pusher.SubscribeAsync(ChannelName).ConfigureAwait(false);
        _channel = channel;

        if (!string.IsNullOrWhiteSpace(AppConfig.PusherEvent))
        {
            // Bind both the plain and the Laravel dot-prefixed event name.
            channel.Bind(AppConfig.PusherEvent, (PusherEvent evt) => JobReceived?.Invoke(evt.Data));
            channel.Bind("." + AppConfig.PusherEvent, (PusherEvent evt) => JobReceived?.Invoke(evt.Data));
        }
        else
        {
            channel.BindAll((string eventName, PusherEvent evt) =>
            {
                if (eventName.StartsWith("pusher:", StringComparison.OrdinalIgnoreCase)) return;
                JobReceived?.Invoke(evt.Data);
            });
        }
    }

    public async Task StopAsync()
    {
        var pusher = _pusher;
        _pusher = null;
        var channel = _channel;
        _channel = null;

        if (pusher == null)
        {
            SetState("disconnected");
            return;
        }

        try
        {
            channel?.UnbindAll();
            await pusher.UnsubscribeAllAsync().ConfigureAwait(false);
            await pusher.DisconnectAsync().ConfigureAwait(false);
        }
        catch
        {
            // teardown is best effort
        }
        SetState("disconnected");
    }

    private static string MapState(ConnectionState state) => state switch
    {
        ConnectionState.Connected => "connected",
        ConnectionState.Connecting or ConnectionState.WaitingToReconnect => "connecting",
        ConnectionState.Disconnected or ConnectionState.Disconnecting => "disconnected",
        _ => "unavailable",
    };

    private void SetState(string state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(state);
    }
}
