using System.Windows;
using System.Windows.Controls;
using MiddlewareApp.Views.Controls;

namespace MiddlewareApp.Views;

/// <summary>Screen 2 — Select branch (spec §6.2). Avatar = first 2 letters of location code.</summary>
public partial class LocationScreen : UserControl
{
    public LocationScreen()
    {
        InitializeComponent();
        BuildList();
    }

    private void BuildList()
    {
        ListPanel.Children.Clear();
        foreach (var location in AppState.Locations)
        {
            var item = location;
            ListPanel.Children.Add(ListItemFactory.Create(
                ListItemFactory.Initials(item.Code),
                item.Name,
                item.City,
                () =>
                {
                    AppState.SelectedLocation = item;
                    ((MainWindow)Window.GetWindow(this)!).Navigate(new DeviceScreen());
                }));
        }
        EmptyText.Visibility = AppState.Locations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        ((MainWindow)Window.GetWindow(this)!).Navigate(new BusinessCodeScreen());
}
