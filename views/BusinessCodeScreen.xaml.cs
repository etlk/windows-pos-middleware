using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MiddlewareApp.Core.Services;

namespace MiddlewareApp.Views;

/// <summary>Screen 1 — Business code (spec §6.1). Input is masked like a password.</summary>
public partial class BusinessCodeScreen : UserControl
{
    private readonly ApiService _api = new();
    private bool _busy;

    public BusinessCodeScreen()
    {
        InitializeComponent();
        Loaded += (_, _) => CodeBox.Focus();
    }

    private void CodeBox_PasswordChanged(object sender, RoutedEventArgs e) =>
        CodeHint.Visibility = CodeBox.Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    // Enter submits via the button's IsDefault (spec: fully keyboard-operable).

    private async void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var code = CodeBox.Password.Trim();
        if (string.IsNullOrEmpty(code))
        {
            MessageBox.Show("Enter a business code", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetBusy(true);
        try
        {
            var locations = await _api.GetLocationsAsync(code);
            AppState.BusinessCode = code;
            AppState.Locations = locations;
            ((MainWindow)Window.GetWindow(this)!).Navigate(new LocationScreen());
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

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ContinueButton.IsEnabled = !busy;
        ContinueLabel.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        ContinueSpinner.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }
}
