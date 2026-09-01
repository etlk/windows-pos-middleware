using System.Windows;
using MiddlewareApp.Core.Services;
using MiddlewareApp.Services;

namespace MiddlewareApp;

public partial class App : Application
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        if (!SingleInstance.TryAcquire())
        {
            SingleInstance.NotifyExistingInstance();
            Shutdown();
            return;
        }

        Exit += (_, _) => SingleInstance.Dispose();

        PrintAgent.ImageDecoder = new DrawingImageDecoder();

        var startMinimized = e.Args.Any(a =>
            string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));

        var window = new MainWindow();
        MainWindow = window;
        if (!startMinimized)
            window.Show();
        // When started minimized (autostart at login) the window stays hidden in the
        // tray; boot/resume still runs so the listener comes back automatically.
    }
}
