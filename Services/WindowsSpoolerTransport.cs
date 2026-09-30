using System.Runtime.InteropServices;
using MiddlewareApp.Core.Services;

namespace MiddlewareApp.Services;

/// <summary>
/// Writes ESC/POS bytes to an installed Windows printer queue as RAW
/// (OpenPrinter / WritePrinter). Requires a driver that accepts RAW jobs.
/// </summary>
public sealed class WindowsSpoolerTransport : ISpoolerPrinterTransport
{
    public Task SendAsync(string printerName, byte[] data, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(printerName))
            throw new ArgumentException("Windows printer name is required", nameof(printerName));
        if (data == null || data.Length == 0)
            throw new ArgumentException("Print data is empty", nameof(data));

        ct.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            WriteRaw(printerName.Trim(), data);
        }, ct);
    }

    private static void WriteRaw(string printerName, byte[] data)
    {
        if (!OpenPrinter(printerName, out var handle, IntPtr.Zero))
            throw new InvalidOperationException(
                $"Cannot open Windows printer \"{printerName}\". Is it installed and online?");

        try
        {
            var di = new DOCINFOA
            {
                pDocName = "Cloud POS Middleware",
                pDataType = "RAW",
            };

            if (StartDocPrinter(handle, 1, di) == 0)
                throw new InvalidOperationException(
                    $"StartDocPrinter failed for \"{printerName}\" (error {Marshal.GetLastWin32Error()}). " +
                    "The driver may not accept RAW ESC/POS — try a thermal RAW/direct driver or use LAN.");

            try
            {
                if (!StartPagePrinter(handle))
                    throw new InvalidOperationException(
                        $"StartPagePrinter failed for \"{printerName}\" (error {Marshal.GetLastWin32Error()})");

                try
                {
                    var written = 0;
                    if (!WritePrinter(handle, data, data.Length, ref written))
                        throw new InvalidOperationException(
                            $"WritePrinter failed for \"{printerName}\" (error {Marshal.GetLastWin32Error()})");
                    if (written != data.Length)
                        throw new InvalidOperationException(
                            $"WritePrinter wrote {written}/{data.Length} bytes to \"{printerName}\"");
                }
                finally
                {
                    EndPagePrinter(handle);
                }
            }
            finally
            {
                EndDocPrinter(handle);
            }
        }
        finally
        {
            ClosePrinter(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private class DOCINFOA
    {
        [MarshalAs(UnmanagedType.LPStr)] public string? pDocName;
        [MarshalAs(UnmanagedType.LPStr)] public string? pOutputFile;
        [MarshalAs(UnmanagedType.LPStr)] public string? pDataType;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern bool OpenPrinter(string szPrinter, out IntPtr hPrinter, IntPtr pd);

    [DllImport("winspool.drv", EntryPoint = "ClosePrinter", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int StartDocPrinter(IntPtr hPrinter, int level, [In] DOCINFOA di);

    [DllImport("winspool.drv", EntryPoint = "EndDocPrinter", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartPagePrinter", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "EndPagePrinter", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "WritePrinter", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, byte[] pBytes, int dwCount, ref int dwWritten);
}
