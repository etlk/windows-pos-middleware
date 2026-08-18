using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MiddlewareApp.Core.Models;

namespace MiddlewareApp.Views.Controls;

/// <summary>
/// One printable slot's configuration card (spec §6.4): status badge, assigned-printer
/// box with Remove, per-card printer chip selection, per-card manual IP entry, and
/// Save/Update. The parent Middleware screen owns the shared printer list and the API.
/// </summary>
public partial class ConfigCard : UserControl
{
    private SlotConfig? _slot;
    private IReadOnlyList<DiscoveredPrinter> _printers = Array.Empty<DiscoveredPrinter>();
    private bool _scanning;
    private string? _selectedIp;
    private bool _saving;

    /// <summary>"device" or "dept-{id}" — used by the parent to build the PATCH payload.</summary>
    public string SlotKey { get; }
    public bool IsDevice { get; }
    public int SlotId => _slot?.Id ?? 0;
    public string SlotName => _slot?.Name ?? "";

    /// <summary>Raised with the chosen printer when Save/Update is clicked.</summary>
    public event Action<ConfigCard, DiscoveredPrinter>? SaveRequested;
    /// <summary>Raised after the user confirms removing the assigned printer.</summary>
    public event Action<ConfigCard>? RemoveRequested;
    /// <summary>Parent adds a manual printer to the shared list (dedupe by IP) and returns it.</summary>
    public Func<string, int, DiscoveredPrinter>? AddManualPrinter { get; set; }

    public ConfigCard(string slotKey, bool isDevice, string title)
    {
        SlotKey = slotKey;
        IsDevice = isDevice;
        InitializeComponent();
        TitleText.Text = title;
    }

    /// <summary>Update assigned state from a fresh config load without touching the
    /// user's per-card selection or manual fields (60 s silent refresh).</summary>
    public void SetSlot(SlotConfig slot)
    {
        _slot = slot;
        var assigned = slot.PrintConfig != null && !string.IsNullOrWhiteSpace(slot.PrintConfig.Ip);

        if (assigned)
        {
            Badge.Background = (Brush)FindResource("BadgeGreenBgBrush");
            BadgeText.Foreground = (Brush)FindResource("BadgeGreenBrush");
            BadgeText.Text = "Configured";
            AssignedBox.Visibility = Visibility.Visible;
            NoPrinterText.Visibility = Visibility.Collapsed;
            AssignedIpText.Text = $"{slot.PrintConfig!.Ip}:{slot.PrintConfig.Port}";
            AssignedPaperText.Text = $"Paper: {slot.PrintConfig.PaperSize}";
            SelectLabel.Text = "Change printer";
            SaveLabel.Text = "Update printer";
            _selectedIp ??= slot.PrintConfig.Ip; // pre-select the assigned IP
        }
        else
        {
            Badge.Background = (Brush)FindResource("FaintBgBrush");
            BadgeText.Foreground = (Brush)FindResource("TextMutedBrush");
            BadgeText.Text = "Not set";
            AssignedBox.Visibility = Visibility.Collapsed;
            NoPrinterText.Visibility = Visibility.Visible;
            SelectLabel.Text = "Select printer";
            SaveLabel.Text = "Save printer";
        }
        RebuildChips();
    }

    public void SetPrinters(IReadOnlyList<DiscoveredPrinter> printers, bool scanning)
    {
        _printers = printers;
        _scanning = scanning;
        RebuildChips();
    }

    /// <summary>Disable actions while any save/clear is in flight (parent-driven).</summary>
    public void SetBusy(bool busy)
    {
        SaveButton.IsEnabled = !busy;
        RemoveButton.IsEnabled = !busy;
        UseIpButton.IsEnabled = !busy;
    }

    public void SetSaving(bool saving)
    {
        _saving = saving;
        SaveLabel.Visibility = saving ? Visibility.Collapsed : Visibility.Visible;
        SaveSpinner.Visibility = saving ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RebuildChips()
    {
        ChipsPanel.Children.Clear();

        var hasPrinters = _printers.Count > 0;
        ScanStatusPanel.Visibility = _scanning && !hasPrinters ? Visibility.Visible : Visibility.Collapsed;
        ChipsScroll.Visibility = hasPrinters ? Visibility.Visible : Visibility.Collapsed;
        NoneFoundText.Visibility = !_scanning && !hasPrinters ? Visibility.Visible : Visibility.Collapsed;

        foreach (var printer in _printers)
            ChipsPanel.Children.Add(BuildChip(printer));
    }

    private Border BuildChip(DiscoveredPrinter printer)
    {
        var selected = printer.Ip == _selectedIp;
        var chip = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand,
            Background = selected
                ? (Brush)FindResource("PrimaryBrush")
                : (Brush)FindResource("BgBrush"),
            BorderBrush = (Brush)FindResource("HairlineBrush"),
            BorderThickness = new Thickness(selected ? 0 : 1),
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = printer.Name,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = selected ? Brushes.White : (Brush)FindResource("TextBrush"),
        });
        stack.Children.Add(new TextBlock
        {
            Text = $"{printer.Ip}:{printer.Port} · {printer.Source}",
            FontSize = 10,
            Margin = new Thickness(0, 2, 0, 0),
            Foreground = selected ? Brushes.White : (Brush)FindResource("TextMutedBrush"),
        });
        chip.Child = stack;

        chip.MouseLeftButtonUp += (_, _) =>
        {
            _selectedIp = printer.Ip;
            RebuildChips();
        };
        return chip;
    }

    private void UseIp_Click(object sender, RoutedEventArgs e)
    {
        var ip = ManualIpBox.Text.Trim();
        if (!IsValidIPv4(ip))
        {
            MessageBox.Show("Enter a valid IPv4 address (e.g. 192.168.1.100)", "Invalid IP",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var port = int.TryParse(ManualPortBox.Text.Trim(), out var p) && p > 0 && p <= 65535 ? p : 9100;
        var printer = AddManualPrinter?.Invoke(ip, port);
        if (printer == null) return;

        _selectedIp = printer.Ip;
        RebuildChips();
        MessageBox.Show($"Printer {printer.Ip}:{printer.Port} is ready to configure.",
            "Printer ready", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static bool IsValidIPv4(string ip)
    {
        if (!Regex.IsMatch(ip, @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$")) return false;
        return ip.Split('.').All(o => int.TryParse(o, out var v) && v is >= 0 and <= 255);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_saving) return;
        var printer = _printers.FirstOrDefault(p => p.Ip == _selectedIp);
        // The assigned printer pre-selects its IP even when the scan didn't find it —
        // Update must still work in that case.
        if (printer == null && _selectedIp != null && _slot?.PrintConfig?.Ip == _selectedIp)
            printer = new DiscoveredPrinter(_selectedIp, _selectedIp, _slot.PrintConfig.Port, "manual");
        if (printer == null)
        {
            MessageBox.Show("Select a printer first", "No printer selected",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        SaveRequested?.Invoke(this, printer);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(SlotName) ? TitleText.Text : SlotName;
        var result = MessageBox.Show($"Remove the assigned printer from {name}?", "Remove printer",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (result == MessageBoxResult.OK)
            RemoveRequested?.Invoke(this);
    }
}
