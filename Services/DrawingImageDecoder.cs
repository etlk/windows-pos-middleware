using System.Drawing;
using System.IO;
using MiddlewareApp.Core.Services;

namespace MiddlewareApp.Services;

/// <summary>
/// Decodes receipt logo images (PNG/JPG bytes) into a 1-bit raster for ESC/POS
/// GS v 0, scaled down to the printable width.
/// </summary>
public class DrawingImageDecoder : IReceiptImageDecoder
{
    private const double LuminanceThreshold = 160;

    public MonoImage? Decode(byte[] data, int maxWidthDots)
    {
        try
        {
            using var ms = new MemoryStream(data);
            using var source = new Bitmap(ms);

            var width = source.Width;
            var height = source.Height;
            if (width > maxWidthDots)
            {
                height = Math.Max(1, (int)Math.Round(height * (double)maxWidthDots / width));
                width = maxWidthDots;
            }

            using var bitmap = width == source.Width && height == source.Height
                ? new Bitmap(source)
                : new Bitmap(source, new Size(width, height));

            var bytesPerRow = (width + 7) / 8;
            var rows = new byte[bytesPerRow * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var c = bitmap.GetPixel(x, y);
                    if (c.A < 128) continue; // transparent = white
                    var lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
                    if (lum < LuminanceThreshold)
                        rows[y * bytesPerRow + x / 8] |= (byte)(0x80 >> (x % 8));
                }
            }

            return new MonoImage { Width = width, Height = height, Rows = rows };
        }
        catch
        {
            return null; // undecodable logo — receipt prints without it
        }
    }
}
