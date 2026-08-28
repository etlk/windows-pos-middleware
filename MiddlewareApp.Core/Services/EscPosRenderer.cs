using System.Text;

namespace MiddlewareApp.Core.Services;

/// <summary>1-bit raster image, rows packed MSB-first, BytesPerRow = (Width + 7) / 8.</summary>
public sealed class MonoImage
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required byte[] Rows { get; init; }
}

/// <summary>Decodes an encoded image (PNG/JPG bytes) into a printable mono raster. Implemented by the app (System.Drawing).</summary>
public interface IReceiptImageDecoder
{
    /// <summary>
    /// exactWidth scales to targetWidthDots in both directions (the template asked
    /// for that width); otherwise targetWidthDots is only a downscale cap.
    /// </summary>
    MonoImage? Decode(byte[] data, int targetWidthDots, bool exactWidth);
}

/// <summary>
/// Renders formatted receipt lines to raw ESC/POS bytes (printer DPI 203, spec §5).
/// Text alignment is baked in with spaces so output matches the Android renderer's
/// column math exactly.
/// </summary>
public class EscPosRenderer
{
    private const int MaxDashes = 48;

    private readonly IReceiptImageDecoder? _imageDecoder;
    private readonly Func<string, Task<byte[]?>>? _imageFetcher;

    public EscPosRenderer(IReceiptImageDecoder? imageDecoder = null, Func<string, Task<byte[]?>>? imageFetcher = null)
    {
        _imageDecoder = imageDecoder;
        _imageFetcher = imageFetcher;
    }

    public async Task<byte[]> RenderAsync(
        IReadOnlyList<ReceiptLine> lines,
        int width,
        bool includeImages = true,
        bool openCashbox = false)
    {
        var ms = new MemoryStream();
        void Emit(params byte[] bytes) => ms.Write(bytes, 0, bytes.Length);

        Emit(0x1B, 0x40); // ESC @ — initialize

        foreach (var line in lines)
        {
            switch (line)
            {
                case TextLine t:
                    EmitText(ms, t, width);
                    break;

                case DashLine:
                    var dashes = new string('-', Math.Min(width, MaxDashes));
                    EmitPlainText(ms, PadLine(dashes, LineAlign.Center, width));
                    break;

                case ImageLine img when includeImages:
                    await EmitImageAsync(ms, img, width).ConfigureAwait(false);
                    break;
            }
        }

        Emit(0x1B, 0x64, 0x04); // ESC d 4 — feed so the cut clears the footer
        Emit(0x1D, 0x56, 0x00); // GS V 0 — full cut
        if (openCashbox)
            Emit(0x1B, 0x70, 0x00, 0x3C, 0xFF); // ESC p — cash drawer kick (DantSu parity)
        return ms.ToArray();
    }

    private static string PadLine(string text, LineAlign align, int width)
    {
        if (text.Length >= width) return text[..Math.Min(text.Length, width)];
        return align switch
        {
            LineAlign.Center => new string(' ', (width - text.Length) / 2) + text,
            LineAlign.Right => new string(' ', width - text.Length) + text,
            _ => text,
        };
    }

    private static void EmitText(MemoryStream ms, TextLine line, int width)
    {
        // Wide lines print double width+height (GS ! 0x11), so column math runs at half width.
        var effWidth = line.Wide ? Math.Max(8, width / 2) : width;
        var padded = PadLine(line.Text, line.Align, effWidth);

        if (line.Bold) ms.Write(new byte[] { 0x1B, 0x45, 0x01 }); // ESC E 1 — bold on
        if (line.Wide) ms.Write(new byte[] { 0x1D, 0x21, 0x11 }); // GS ! — double width + height

        var bytes = Encoding.ASCII.GetBytes(Asciify(padded));
        ms.Write(bytes, 0, bytes.Length);
        ms.WriteByte(0x0A); // feed while the size is still active so the line advances full height

        if (line.Wide) ms.Write(new byte[] { 0x1D, 0x21, 0x00 });
        if (line.Bold) ms.Write(new byte[] { 0x1B, 0x45, 0x00 });
    }

    private static void EmitPlainText(MemoryStream ms, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(Asciify(text));
        ms.Write(bytes, 0, bytes.Length);
        ms.WriteByte(0x0A);
    }

    /// <summary>Fold common typography to ASCII; anything else unprintable becomes '?'.</summary>
    private static string Asciify(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            sb.Append(c switch
            {
                '‘' or '’' => '\'',
                '“' or '”' => '"',
                '–' or '—' => '-',
                ' ' => ' ',
                '•' => '*',
                '×' => 'x',
                _ when c <= 0x7E && c >= 0x20 => c,
                _ => '?',
            });
        }
        return sb.ToString();
    }

    private async Task EmitImageAsync(MemoryStream ms, ImageLine img, int width)
    {
        if (_imageDecoder == null || _imageFetcher == null) return;

        byte[]? data;
        try { data = await _imageFetcher(img.Url).ConfigureAwait(false); }
        catch { return; } // unreachable images were already dropped; a late failure just skips the logo

        if (data == null || data.Length == 0) return;

        var (printWidthMm, printableDots) = width <= 32 ? (48f, 384) : (72f, 576);
        int targetDots;
        var exact = false;
        if (img.WidthPx is int widthPx && widthPx > 0)
        {
            // The template specified the logo width. CSS px print at 96 dpi, the
            // printer runs 203 dpi, so scale to exactly widthPx × 203/96 dots
            // (clamped to something visible and to the printable area).
            targetDots = Math.Clamp((int)Math.Round(widthPx * 203.0 / 96.0), 60, printableDots);
            exact = true;
        }
        else
        {
            // No width in the HTML: same logic as the Android middleware's
            // scaleLogoForReceipt, capped at 75% of the printable width in dots at
            // 203 dpi (min 120). 80 mm roll (72 mm printable, 576 dots) ⇒ ~431 dots;
            // 58 mm roll (48 mm printable, 384 dots) ⇒ ~287 dots. Downscale only.
            targetDots = (int)(printWidthMm / 25.4f * 203 * 0.75f);
            targetDots = Math.Clamp(targetDots, 120, printableDots);
        }
        var mono = _imageDecoder.Decode(data, targetDots, exact);
        if (mono == null) return;

        var bytesPerRow = (mono.Width + 7) / 8;
        ms.Write(new byte[] { 0x1B, 0x61, 0x01 }); // ESC a 1 — center
        ms.Write(new byte[]
        {
            0x1D, 0x76, 0x30, 0x00, // GS v 0 — raster bit image, normal mode
            (byte)(bytesPerRow & 0xFF), (byte)((bytesPerRow >> 8) & 0xFF),
            (byte)(mono.Height & 0xFF), (byte)((mono.Height >> 8) & 0xFF),
        });
        ms.Write(mono.Rows, 0, mono.Rows.Length);
        ms.Write(new byte[] { 0x1B, 0x61, 0x00 }); // ESC a 0 — back to left
        ms.WriteByte(0x0A);
    }
}
