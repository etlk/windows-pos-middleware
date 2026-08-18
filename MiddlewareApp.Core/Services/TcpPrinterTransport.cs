using System.Net.Sockets;

namespace MiddlewareApp.Core.Services;

/// <summary>Raw TCP transport to {ip}:{port} (default 9100), 30 s timeout (spec §5.2).</summary>
public class TcpPrinterTransport
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public virtual async Task SendAsync(string ip, int port, byte[] data, CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(Timeout);
        var token = timeoutCts.Token;

        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(ip, port, token).ConfigureAwait(false);
            var stream = client.GetStream();
            await stream.WriteAsync(data, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Printer {ip}:{port} did not respond within {Timeout.TotalSeconds:0} s");
        }
    }
}
