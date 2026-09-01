using System.IO;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms; // NotifyIcon
using Microsoft.Win32;
using UserControl = System.Windows.Controls.UserControl;
using MiddlewareApp.Core.Services;
using MiddlewareApp.Services;
using MiddlewareApp.Views;
using Application = System.Windows.Application;

namespace MiddlewareApp;

/// <summary>
/// Tray-resident shell (spec §8): closing hides to the tray and printing continues;
/// Quit only via the tray menu. Hosts the 4-screen wizard.
/// </summary>
public partial class MainWindow : Window
{
    private NotifyIcon? _notifyIcon;
    private ToolStripMenuItem? _statusItem;
    private ToolStripMenuItem? _autoStartItem;
    private bool _isExit;
    private bool _balloonShown;

    public MainWindow()
    {
        InitializeComponent();

        // Landscape window at roughly half the screen in both dimensions.
        var workArea = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, workArea.Width * 0.5);
        Height = Math.Max(MinHeight, workArea.Height * 0.5);

        CreateNotifyIcon();

        SingleInstance.StartWatching(() =>
            Dispatcher.BeginInvoke(ShowMainWindow));

        PrintAgent.Instance.Changed += OnAgentChanged;
        Activated += async (_, _) => await PrintAgent.Instance.ReconnectIfNeededAsync();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

        Navigate(new BootScreen());
        // Boot must run even when starting minimized (window never shown).
        _ = Dispatcher.InvokeAsync(() => _ = BootAsync());
    }

    /// <summary>Boot / resume (spec §6.5): restore a persisted enabled session and jump
    /// straight to the Middleware screen with the listener already starting.</summary>
    private async Task BootAsync()
    {
        var (session, configs) = await Task.Run(() =>
            (AgentStorage.LoadSession(), AgentStorage.LoadConfigs()));

        if (session is { Enabled: true } && configs != null)
        {
            AppState.BusinessCode = session.BusinessCode;
            _ = PrintAgent.Instance.StartAsync(session, configs); // start listening immediately
            Navigate(new MiddlewareScreen(session));
        }
        else
        {
            Navigate(new BusinessCodeScreen());
        }
    }

    public void Navigate(UserControl screen) => ScreenHost.Content = screen;

    // ===== Tray =====

    private void CreateNotifyIcon()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Visible = true,
            Text = "Cloud POS Middleware",
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowMainWindow());
        _statusItem = new ToolStripMenuItem("Print listener OFF") { Enabled = false };
        menu.Items.Add(_statusItem);
        _autoStartItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = AutoStart.IsEnabled(),
        };
        _autoStartItem.CheckedChanged += (_, _) => AutoStart.SetEnabled(_autoStartItem.Checked);
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => ExitApplication());

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
        UpdateTrayState();
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var resource = Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/tray-icon.png"));
            if (resource != null)
            {
                using var bmp = new System.Drawing.Bitmap(resource.Stream);
                using var sized = new System.Drawing.Bitmap(bmp, new System.Drawing.Size(32, 32));
                return System.Drawing.Icon.FromHandle(sized.GetHicon());
            }
        }
        catch { }

        var exePath = Environment.ProcessPath;
        if (exePath != null && File.Exists(exePath))
        {
            var extracted = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (extracted != null) return extracted;
        }
        return System.Drawing.SystemIcons.Application;
    }

    private void OnAgentChanged() => Dispatcher.BeginInvoke(UpdateTrayState);

    private void UpdateTrayState()
    {
        if (_notifyIcon == null) return;
        var agent = PrintAgent.Instance;

        var tooltip = agent.IsRunning && agent.Session != null
            ? $"Cloud POS Middleware — Listening · {agent.Session.DeviceName}"
            : "Cloud POS Middleware";
        _notifyIcon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;

        if (_statusItem != null)
        {
            _statusItem.Text = agent.ListenerState switch
            {
                "connected" => "Background listener ON",
                "connecting" => "Print listener connecting…",
                _ => "Print listener OFF",
            };
        }
    }

    private void ShowMainWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _isExit = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        _notifyIcon?.Dispose();
        Application.Current.Shutdown();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExit)
        {
            // Close button hides to tray — the app keeps running & printing (spec §8).
            e.Cancel = true;
            Hide();
            if (!_balloonShown)
            {
                _balloonShown = true;
                _notifyIcon?.ShowBalloonTip(1500, "Cloud POS Middleware",
                    "Still listening for print jobs", ToolTipIcon.Info);
            }
        }
        base.OnClosing(e);
    }

    // ===== Reconnect on OS resume / network change (spec §8) =====

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            _ = PrintAgent.Instance.ReconnectIfNeededAsync();
    }

    private void OnNetworkChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (e.IsAvailable)
            _ = PrintAgent.Instance.ReconnectIfNeededAsync();
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        _ = PrintAgent.Instance.ReconnectIfNeededAsync();
    }
}
