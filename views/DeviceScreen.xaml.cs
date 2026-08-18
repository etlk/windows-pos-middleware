using System.Windows;
using System.Windows.Controls;
using MiddlewareApp.Core.Models;
using MiddlewareApp.Views.Controls;

namespace MiddlewareApp.Views;

/// <summary>Screen 3 — Select device (spec §6.3). Avatar = first 2 letters of the location code.</summary>
public partial class DeviceScreen : UserControl
{
    public DeviceScreen()
    {
        InitializeComponent();
        BuildList();
    }

    private void BuildList()
    {
        ListPanel.Children.Clear();
        var location = AppState.SelectedLocation;
        if (location == null) return;

        foreach (var device in location.Devices)
        {
            var item = device;
            ListPanel.Children.Add(ListItemFactory.Create(
                ListItemFactory.Initials(location.Code),
                item.DeviceName,
                $"#{item.SerialNumber}",
                () =>
                {
                    AppState.SelectedDevice = item;
                    var session = new AgentSession
                    {
                        BusinessCode = AppState.BusinessCode,
                        LocationId = location.Id,
                        LocationName = location.Name,
                        DeviceId = item.Id,
                        DeviceName = item.DeviceName,
                        Enabled = true,
                    };
                    ((MainWindow)Window.GetWindow(this)!).Navigate(new MiddlewareScreen(session));
                }));
        }
        EmptyText.Visibility = location.Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        ((MainWindow)Window.GetWindow(this)!).Navigate(new LocationScreen());
}
