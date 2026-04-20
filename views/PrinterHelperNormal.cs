using System;
using System;
using System.Drawing;
using System.Drawing.Printing;
using System.Linq;
using System.Threading.Tasks;
using PrintHTML.Core.Services;


namespace WsWpfListener
{
    public class PrinterHelperNormal
    {


        public void PrintHtmlReceipt(string html, string printerName, Action<string>? log = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(printerName))
                {
                    log?.Invoke("No printer selected.");
                    return;
                }

                bool printerExists = PrinterSettings.InstalledPrinters
                    .Cast<string>()
                    .Any(p => string.Equals(p, printerName, StringComparison.OrdinalIgnoreCase));

                if (!printerExists)
                {
                    log?.Invoke("Selected printer not found.");
                    return;
                }

                // PrintLogoEscPos(printerName, log);
                // Thread.Sleep(200);

                // 1 Print HTML
                var printer = new PrinterService();
                printer.DoPrint(html, printerName, 42); // 32 = 58mm, 42 = 80mm

                // 2 Wait until printing finishes
                Thread.Sleep(400); // increase if printer is slow

                // 3 Feed paper
                // RawPrinterHelper.SendBytesToPrinter(
                //     printerName,
                //     new byte[] { 0x1B, 0x64, 5 }, // ESC d 5 (feed 5 lines)
                //     3
                // );

                // 4 Cut paper
                // RawPrinterHelper.SendBytesToPrinter(
                //     printerName,
                //     new byte[] { 0x1D, 0x56, 0x00 }, // GS V 0 (full cut)
                //     3
                // );

                log?.Invoke("Printed, fed, and cut successfully.");
            }
            catch (Exception ex)
            {
                log?.Invoke("Print error: " + ex.ToString());
            }
        }

        public void PrintLogoEscPos(string printerName, Action<string>? log = null)
        {
            try
            {
                using Bitmap original = new Bitmap(@"C:\POS\logo.png");

                // Create new bitmap with extra width for padding
                int padding = 80; // pixels
                Bitmap padded = new Bitmap(original.Width + padding, original.Height);

                using (Graphics g = Graphics.FromImage(padded))
                {
                    g.Clear(Color.White); // fill background with white
                    g.DrawImage(original, padding, 0); // draw logo shifted right
                }

                // Convert padded image to ESC/POS bytes
                byte[] logoBytes = ThermalPrinterHelper.GetLogoBytes(padded);
                RawPrinterHelper.SendBytesToPrinter(printerName, logoBytes, logoBytes.Length);


                log?.Invoke("Logo printed successfully.");
            }
            catch (Exception ex)
            {
                log?.Invoke("Logo print error: " + ex);
            }

        }



    }
}