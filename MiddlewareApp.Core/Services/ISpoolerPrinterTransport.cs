namespace MiddlewareApp.Core.Services;

/// <summary>
/// Sends raw ESC/POS bytes to a Windows printer queue (installed driver / spooler).
/// Implemented by the WPF host so Core stays free of Win32.
/// </summary>
public interface ISpoolerPrinterTransport
{
    Task SendAsync(string printerName, byte[] data, CancellationToken ct = default);
}
