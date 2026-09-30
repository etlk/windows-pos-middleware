using System.Drawing.Printing;

namespace MiddlewareApp.Services;

/// <summary>Lists printers installed in Windows (drivers present).</summary>
public static class WindowsPrinterDiscovery
{
    public static IReadOnlyList<string> ListInstalledPrinterNames()
    {
        var names = new List<string>();
        foreach (string name in PrinterSettings.InstalledPrinters)
        {
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name);
        }
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }
}
