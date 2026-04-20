using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using MiddlewareApp.Views;
using System;
using System.Windows;
using System.Windows.Forms; // Forms namespace for NotifyIcon
using Application = System.Windows.Application; // avoid ambiguity

namespace MiddlewareApp;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
     private NotifyIcon _notifyIcon;
      private bool _isExit = false;
    public MainWindow()
    {
        InitializeComponent();
        CreateNotifyIcon();
        MainContent.Content = new BusinessCodeView();
    }

         private void CreateNotifyIcon()
        {
            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = new System.Drawing.Icon("app.ico"); // add your icon here
            _notifyIcon.Visible = true;
            _notifyIcon.Text = "MiddlewareApp";

            // Right-click context menu
            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Open").Click += (s, e) => ShowMainWindow();
            contextMenu.Items.Add("Exit").Click += (s, e) => ExitApplication();

            _notifyIcon.ContextMenuStrip = contextMenu;

            // Double-click to open
            _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();
        }

        private void ShowMainWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        private void ExitApplication()
        {
            _isExit = true;
            _notifyIcon.Dispose();
            Application.Current.Shutdown();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            if (this.WindowState == WindowState.Minimized)
            {
                this.Hide(); // hide to tray when minimized
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExit)
            {
                e.Cancel = true;
                this.Hide();
                _notifyIcon.ShowBalloonTip(1000, "MiddlewareApp", "Application minimized to tray", ToolTipIcon.Info);
            }
            base.OnClosing(e);
        }

}