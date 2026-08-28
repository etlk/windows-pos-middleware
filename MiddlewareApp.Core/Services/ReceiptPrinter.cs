using MiddlewareApp.Core.Models;

namespace MiddlewareApp.Core.Services;

/// <summary>
/// Composes formatter → renderer → TCP transport for one job, including the image
/// reachability check and the retry-without-logo fallback (spec §5.1): if the print
/// fails and the payload contains a logo, retry once with all images stripped —
/// the bill must still print if logo rendering breaks.
/// </summary>
public class ReceiptPrinter
{
    private static readonly TimeSpan ImageCheckTimeout = TimeSpan.FromSeconds(4);

    private readonly EscPosRenderer _renderer;
    private readonly TcpPrinterTransport _transport;
    private readonly HttpClient _http;

    public ReceiptPrinter(EscPosRenderer renderer, TcpPrinterTransport? transport = null, HttpClient? http = null)
    {
        _renderer = renderer;
        _transport = transport ?? new TcpPrinterTransport();
        _http = http ?? new HttpClient();
    }

    public async Task PrintAsync(
        string html,
        PrintConfig config,
        bool openCashbox = false,
        CancellationToken ct = default)
    {
        var width = ReceiptFormatter.CharsPerLine(config.PaperSize);
        var lines = ReceiptFormatter.Format(html, width).ToList();

        // Drop unreachable image URLs before printing (HEAD then GET, 4 s timeout).
        var reachableLines = new List<ReceiptLine>(lines.Count);
        foreach (var line in lines)
        {
            if (line is ImageLine img && !await IsReachableAsync(img.Url, ct).ConfigureAwait(false))
                continue;
            reachableLines.Add(line);
        }

        var port = config.Port > 0 ? config.Port : 9100;
        var hasImages = reachableLines.Any(l => l is ImageLine);
        try
        {
            var bytes = await _renderer.RenderAsync(reachableLines, width, includeImages: true, openCashbox)
                .ConfigureAwait(false);
            await _transport.SendAsync(config.Ip, port, bytes, ct).ConfigureAwait(false);
        }
        catch when (hasImages && !ct.IsCancellationRequested)
        {
            var stripped = reachableLines.Where(l => l is not ImageLine).ToList();
            var bytes = await _renderer.RenderAsync(stripped, width, includeImages: false, openCashbox)
                .ConfigureAwait(false);
            await _transport.SendAsync(config.Ip, port, bytes, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Downloads an image for the renderer (used as its imageFetcher).</summary>
    public async Task<byte[]?> FetchImageAsync(string url)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            return await _http.GetByteArrayAsync(url, cts.Token).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> IsReachableAsync(string url, CancellationToken ct)
    {
        if (await ProbeAsync(HttpMethod.Head, url, ct).ConfigureAwait(false)) return true;
        return await ProbeAsync(HttpMethod.Get, url, ct).ConfigureAwait(false);
    }

    private async Task<bool> ProbeAsync(HttpMethod method, string url, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ImageCheckTimeout);
            using var request = new HttpRequestMessage(method, url);
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                .ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
