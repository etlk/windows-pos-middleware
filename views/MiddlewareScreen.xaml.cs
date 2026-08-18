using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using MiddlewareApp.Core.Models;
using MiddlewareApp.Core.Services;
using MiddlewareApp.Services;
using MiddlewareApp.Views.Controls;

namespace MiddlewareApp.Views;

/// <summary>
/// Screen 4 — Middleware (spec §6.4): connection badge driven by the print-config GET
/// (60 s silent poll), listener banner, assigned-printers overview, terminal +
/// department ConfigCards, printer discovery, clear-all, back.
/// </summary>
public partial class MiddlewareScreen : UserControl
{
    private readonly AgentSession _session;
    private readonly ApiService _api = new();
    private readonly PrinterDiscovery _discovery = new();

    private PrintConfigState? _config;
    private bool _configLoadedOnce;
    private bool _lastGetSucceeded;
    private bool _busy; // a save/remove/clear is in flight
    private bool _everConnected;

    private readonly List<DiscoveredPrinter> _printers = new();
    private bool _scanning;
    private CancellationTokenSource? _scanCts;

    private ConfigCard? _deviceCard;
    private readonly Dictionary<int, ConfigCard> _deptCards = new();

    private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromSeconds(60) };

    public MiddlewareScreen(AgentSession session)
    {
        _session = session;
        InitializeComponent();

        BuildBreadcrumb();
        AutoStartCheck.IsChecked = AutoStart.IsEnabled();

        PrintAgent.Instance.Changed += OnAgentChanged;
        _pollTimer.Tick += async (_, _) => await LoadConfigAsync(silent: true);

        Loaded += async (_, _) =>
        {
            UpdateListenerBanner();
            _pollTimer.Start();
            StartScan();
            await LoadConfigAsync(silent: false);
        };
        Unloaded += (_, _) =>
        {
            // The print agent itself keeps running (spec §8) — only screen-local
            // work stops here.
            _pollTimer.Stop();
            _scanCts?.Cancel();
            PrintAgent.Instance.Changed -= OnAgentChanged;
        };
    }

    private void BuildBreadcrumb()
    {
        Breadcrumb.Inlines.Clear();
        Breadcrumb.Inlines.Add(new Run($"{_session.BusinessCode} › {_session.LocationName} › "));
        Breadcrumb.Inlines.Add(new Run(_session.DeviceName)
        {
            Foreground = (Brush)FindResource("TextBrush"),
            FontWeight = FontWeights.SemiBold,
        });
    }

    // ===== Config load / poll =====

    private async Task LoadConfigAsync(bool silent)
    {
        try
        {
            var config = await _api.GetPrintConfigAsync(_session.BusinessCode, _session.LocationId, _session.DeviceId);
            _config = config;
            _lastGetSucceeded = true;
            _configLoadedOnce = true;

            BuildContent();

            // startPrintAgent after every successful print-config load (spec §8).
            await PrintAgent.Instance.StartAsync(_session, ToAgentConfigs(config));
        }
        catch (Exception ex)
        {
            _lastGetSucceeded = false;
            if (!silent && !_configLoadedOnce)
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            FirstLoadSpinner.Visibility = _configLoadedOnce ? Visibility.Collapsed : Visibility.Visible;
            ContentPanel.Visibility = _configLoadedOnce ? Visibility.Visible : Visibility.Collapsed;
            LeftActionsPanel.Visibility = ContentPanel.Visibility;
            UpdateConnBadge();
        }
    }

    private AgentConfigs ToAgentConfigs(PrintConfigState config) => new()
    {
        Device = config.Device,
        Departments = config.Departments,
        SelectedDeviceId = _session.DeviceId,
    };

    private void UpdateConnBadge()
    {
        if (_lastGetSucceeded)
        {
            ConnDot.Fill = (Brush)FindResource("SuccessBrush");
            ConnText.Foreground = (Brush)FindResource("SuccessBrush");
            ConnText.Text = "Middleware Connected";
        }
        else
        {
            ConnDot.Fill = (Brush)FindResource("DangerBrush");
            ConnText.Foreground = (Brush)FindResource("DangerBrush");
            ConnText.Text = "Connecting…";
        }
    }

    // ===== Content build =====

    private void BuildContent()
    {
        if (_config == null) return;

        TerminalHeading.Text = _session.DeviceName;
        BuildOverview();
        BuildCards();
    }

    private void BuildOverview()
    {
        OverviewRows.Children.Clear();
        var config = _config!;

        var slots = new List<(string name, string kind, PrintConfig? cfg)>
        {
            (_session.DeviceName, "Terminal", config.Device.PrintConfig),
        };
        slots.AddRange(config.Departments.Select(d =>
            (d.Name ?? $"Department {d.Id}", $"Department · {d.Id}", d.PrintConfig)));

        var anyAssigned = slots.Any(s => s.cfg != null && !string.IsNullOrWhiteSpace(s.cfg.Ip));
        for (var i = 0; i < slots.Count; i++)
        {
            var (name, kind, cfg) = slots[i];
            // Hairline divider between rows; the last row keeps it only when the
            // "No printers assigned yet." note follows.
            var isLast = i == slots.Count - 1 && anyAssigned;
            OverviewRows.Children.Add(BuildOverviewRow(name, kind, cfg, isLast));
        }

        if (!anyAssigned)
        {
            OverviewRows.Children.Add(new TextBlock
            {
                Text = "No printers assigned yet.",
                FontSize = 13,
                Foreground = (Brush)FindResource("TextMutedBrush"),
                Margin = new Thickness(0, 10, 0, 12),
            });
        }
    }

    private UIElement BuildOverviewRow(string name, string kind, PrintConfig? cfg, bool isLast)
    {
        var row = new Grid { Margin = new Thickness(0, 10, 0, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();
        left.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextBrush"),
        });
        left.Children.Add(new TextBlock
        {
            Text = kind,
            FontSize = 12,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 2, 0, 0),
        });
        Grid.SetColumn(left, 0);
        row.Children.Add(left);

        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        if (cfg != null && !string.IsNullOrWhiteSpace(cfg.Ip))
        {
            right.Children.Add(new TextBlock
            {
                Text = $"{cfg.Ip}:{cfg.Port}",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextBrush"),
                HorizontalAlignment = HorizontalAlignment.Right,
            });
            right.Children.Add(new TextBlock
            {
                Text = cfg.PaperSize,
                FontSize = 12,
                Foreground = (Brush)FindResource("TextMutedBrush"),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }
        else
        {
            right.Children.Add(new TextBlock
            {
                Text = "Not set",
                FontSize = 13,
                Foreground = (Brush)FindResource("TextMutedBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        Grid.SetColumn(right, 1);
        row.Children.Add(right);

        var container = new StackPanel();
        container.Children.Add(row);
        if (!isLast)
        {
            container.Children.Add(new Border
            {
                Height = 1,
                Background = (Brush)FindResource("HairlineBrush"),
            });
        }
        return container;
    }

    private void BuildCards()
    {
        var config = _config!;

        if (_deviceCard == null)
        {
            _deviceCard = CreateCard("device", isDevice: true, title: _session.DeviceName);
            DeviceCardHost.Content = _deviceCard;
        }
        _deviceCard.SetSlot(config.Device);

        // Rebuild department cards only when the set of departments changes.
        var deptIds = config.Departments.Select(d => d.Id).ToList();
        if (!deptIds.SequenceEqual(_deptCards.Keys))
        {
            DeptCardsPanel.Children.Clear();
            _deptCards.Clear();
            foreach (var dept in config.Departments)
            {
                var card = CreateCard($"dept-{dept.Id}", isDevice: false,
                    title: dept.Name ?? $"Department {dept.Id}");
                _deptCards[dept.Id] = card;
                DeptCardsPanel.Children.Add(card);
            }
        }
        foreach (var dept in config.Departments)
            _deptCards[dept.Id].SetSlot(dept);

        DepartmentsSection.Visibility = config.Departments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PushPrintersToCards();
    }

    private ConfigCard CreateCard(string slotKey, bool isDevice, string title)
    {
        var card = new ConfigCard(slotKey, isDevice, title)
        {
            AddManualPrinter = AddManualPrinter,
        };
        card.SaveRequested += async (c, printer) => await SavePrinterAsync(c, printer);
        card.RemoveRequested += async c => await RemovePrinterAsync(c);
        return card;
    }

    // ===== Printer discovery =====

    private void StartScan()
    {
        _scanCts?.Cancel();
        var cts = new CancellationTokenSource();
        _scanCts = cts;

        // Keep manual entries across re-scans; drop stale discovered ones.
        _printers.RemoveAll(p => p.Source != "manual");
        _scanning = true;
        UpdateScanBar();
        PushPrintersToCards();

        _ = Task.Run(async () =>
        {
            try
            {
                await _discovery.ScanAsync(printer => Dispatcher.BeginInvoke(() =>
                {
                    if (cts.IsCancellationRequested) return;
                    if (_printers.Any(p => p.Ip == printer.Ip)) return;
                    _printers.Add(printer);
                    UpdateScanBar();
                    PushPrintersToCards();
                }), cts.Token);
            }
            catch { /* discovery is best-effort */ }
            finally
            {
                await Dispatcher.BeginInvoke(() =>
                {
                    if (cts.IsCancellationRequested) return;
                    _scanning = false;
                    UpdateScanBar();
                    PushPrintersToCards();
                });
            }
        }, cts.Token);
    }

    private DiscoveredPrinter AddManualPrinter(string ip, int port)
    {
        var existing = _printers.FirstOrDefault(p => p.Ip == ip);
        if (existing != null) return existing;

        var printer = new DiscoveredPrinter($"Manual {ip}", ip, port, "manual");
        _printers.Add(printer);
        UpdateScanBar();
        PushPrintersToCards();
        return printer;
    }

    private void PushPrintersToCards()
    {
        var snapshot = _printers.ToList();
        _deviceCard?.SetPrinters(snapshot, _scanning);
        foreach (var card in _deptCards.Values)
            card.SetPrinters(snapshot, _scanning);
    }

    private void UpdateScanBar()
    {
        if (_scanning)
        {
            ScanSpinner.Visibility = Visibility.Visible;
            ScanDot.Visibility = Visibility.Collapsed;
            RescanButton.Visibility = Visibility.Collapsed;
            ScanText.Text = "Scanning mDNS + subnet (port 9100)…";
        }
        else
        {
            ScanSpinner.Visibility = Visibility.Collapsed;
            ScanDot.Visibility = Visibility.Visible;
            RescanButton.Visibility = Visibility.Visible;
            ScanText.Text = $"{_printers.Count} printer(s) found on network";
        }
    }

    private void Rescan_Click(object sender, RoutedEventArgs e) => StartScan();

    // ===== Save / remove / clear (all send the FULL state — spec §3.3) =====

    private async Task SavePrinterAsync(ConfigCard card, DiscoveredPrinter printer)
    {
        if (_busy || _config == null) return;

        var payload = _config.Clone();
        var slot = card.IsDevice
            ? payload.Device
            : payload.Departments.FirstOrDefault(d => $"dept-{d.Id}" == card.SlotKey);
        if (slot == null) return;

        slot.IsMiddlewareConfigured = true;
        slot.PrintConfig = new PrintConfig
        {
            Ip = printer.Ip,
            Port = printer.Port > 0 ? printer.Port : 9100,
            PaperSize = "80mm", // always 80mm — no UI to choose (spec §3.3)
        };

        SetBusy(true);
        card.SetSaving(true);
        try
        {
            await _api.PatchPrintConfigAsync(_session.BusinessCode, _session.LocationId, _session.DeviceId, payload);
            MessageBox.Show("Printer configured successfully", "Saved",
                MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadConfigAsync(silent: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            card.SetSaving(false);
            SetBusy(false);
        }
    }

    private async Task RemovePrinterAsync(ConfigCard card)
    {
        if (_busy || _config == null) return;

        var payload = _config.Clone();
        var slot = card.IsDevice
            ? payload.Device
            : payload.Departments.FirstOrDefault(d => $"dept-{d.Id}" == card.SlotKey);
        if (slot == null) return;

        slot.IsMiddlewareConfigured = false;
        slot.PrintConfig = null;

        SetBusy(true);
        try
        {
            await _api.PatchPrintConfigAsync(_session.BusinessCode, _session.LocationId, _session.DeviceId, payload);
            await LoadConfigAsync(silent: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _config == null) return;

        var result = MessageBox.Show(
            "This clears every printer for this terminal and all departments, then restarts setup. " +
            "To remove only one printer, use Remove printer on that card.",
            "Reconfigure", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK) return;

        var payload = _config.Clone();
        payload.Device.IsMiddlewareConfigured = false;
        payload.Device.PrintConfig = null;
        foreach (var dept in payload.Departments)
        {
            dept.IsMiddlewareConfigured = false;
            dept.PrintConfig = null;
        }

        SetBusy(true);
        ClearAllLabel.Visibility = Visibility.Collapsed;
        ClearAllSpinner.Visibility = Visibility.Visible;
        try
        {
            await _api.PatchPrintConfigAsync(_session.BusinessCode, _session.LocationId, _session.DeviceId, payload);
            await PrintAgent.Instance.StopAsync(); // stops listener + clears the stored session
            AppState.Reset();
            ((MainWindow)Window.GetWindow(this)!).Navigate(new BusinessCodeScreen());
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ClearAllLabel.Visibility = Visibility.Visible;
            ClearAllSpinner.Visibility = Visibility.Collapsed;
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ClearAllButton.IsEnabled = !busy;
        _deviceCard?.SetBusy(busy);
        foreach (var card in _deptCards.Values)
            card.SetBusy(busy);
    }

    // ===== Listener banner =====

    private void OnAgentChanged() => Dispatcher.BeginInvoke(UpdateListenerBanner);

    private void UpdateListenerBanner()
    {
        var agent = PrintAgent.Instance;
        var state = agent.ListenerState;
        if (state == "connected") _everConnected = true;

        Brush dot;
        string text;
        var spinning = false;

        if (agent.MissingPusherKey)
        {
            dot = (Brush)FindResource("DangerBrush");
            text = "Set PUSHER_KEY to enable the print listener.";
        }
        else switch (state)
        {
            case "connected":
                dot = (Brush)FindResource("SuccessBrush");
                text = "Background listener ON (safe to close this window)";
                break;
            case "connecting":
                dot = (Brush)FindResource("PrimaryBrush");
                text = _everConnected ? "Print listener connecting…" : "Starting background listener…";
                spinning = !_everConnected;
                break;
            default:
                dot = (Brush)FindResource("DangerBrush");
                text = "Print listener OFF";
                break;
        }

        ListenerDot.Fill = dot;
        ListenerText.Text = text;
        ListenerSpinner.Visibility = spinning ? Visibility.Visible : Visibility.Collapsed;

        var channel = agent.ChannelName;
        ChannelText.Text = channel != null ? $"Channel: {channel}" : "";
        ChannelText.Visibility = channel != null ? Visibility.Visible : Visibility.Collapsed;

        var lastJob = agent.LastJobMessage;
        LastJobText.Text = lastJob != null ? $"Last job: {lastJob}" : "";
        LastJobText.Visibility = lastJob != null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AutoStart_Toggled(object sender, RoutedEventArgs e) =>
        AutoStart.SetEnabled(AutoStartCheck.IsChecked == true);

    // ===== Navigation =====

    private async void Back_Click(object sender, RoutedEventArgs e)
    {
        // The agent keeps running when leaving this screen (spec §8).
        var main = (MainWindow)Window.GetWindow(this)!;

        if (AppState.SelectedLocation == null)
        {
            // Resumed session: the wizard state isn't in memory — refetch locations.
            try
            {
                AppState.Locations = await _api.GetLocationsAsync(_session.BusinessCode);
                AppState.SelectedLocation = AppState.Locations.FirstOrDefault(l => l.Id == _session.LocationId);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        if (AppState.SelectedLocation != null)
            main.Navigate(new DeviceScreen());
        else
            main.Navigate(new BusinessCodeScreen());
    }
}
