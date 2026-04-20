using System;
using System.Drawing;
using System.IO;

public static class ThermalPrinterHelper
{
    public static byte[] GetLogoBytes(Bitmap bmp)
    {
        // Resize if needed
        bmp = new Bitmap(bmp, new Size(bmp.Width, bmp.Height));

        using MemoryStream ms = new MemoryStream();

        // 1️⃣ Initialize printer
        ms.Write(new byte[] { 0x1B, 0x40 }, 0, 2); // ESC @

        // 2️⃣ Print bitmap in chunks of 24 dots height
        for (int y = 0; y < bmp.Height; y += 24)
        {
            ms.Write(new byte[] { 0x1B, 0x2A, 33, (byte)(bmp.Width % 256), (byte)(bmp.Width / 256) }, 0, 5); // ESC * m nL nH

            for (int x = 0; x < bmp.Width; x++)
            {
                for (int k = 0; k < 3; k++)
                {
                    byte slice = 0;
                    for (int b = 0; b < 8; b++)
                    {
                        int yb = y + (k * 8) + b;
                        if (yb >= bmp.Height) continue;

                        Color pixel = bmp.GetPixel(x, yb);
                        int luminance = (pixel.R + pixel.G + pixel.B) / 3;
                        if (luminance < 128) // black
                            slice |= (byte)(1 << (7 - b));
                    }
                    ms.WriteByte(slice);
                }
            }

            // New line after each 24-dot chunk
            ms.WriteByte(0x0A);
        }

        return ms.ToArray();
    }
}
