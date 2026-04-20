using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MiddlewareApp.Models;
using WsWpfListener;

namespace MiddlewareApp.Views
{
    public partial class BusinessCodeView : UserControl
    {
        public BusinessCodeView()
        {
            InitializeComponent();
            GreetingText.Text = GetGreeting();
        }


        private string GetGreeting()
        {
            int hour = DateTime.Now.Hour;

            if (hour < 12)
                return "Good Morning!";
            else if (hour < 17)
                return "Good Afternoon!";
            else
                return "Good Evening!";
        }

        private void SaveJsonToFile(string json)
        {
            string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "locations.json");

            File.WriteAllText(path, json);
        }
        private async void Continue_Click(object sender, RoutedEventArgs e)
        {
            string code = BusinessCodeTextBox.Text?.Trim();

            if (string.IsNullOrWhiteSpace(code))
            {
                MessageBox.Show("Please enter business code");
                return;
            }

            string url = $"https://{code}.etpos.store/api/v1/locations";

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(15);

                    var response = await client.GetAsync(url);

                    if (!response.IsSuccessStatusCode)
                    {
                        MessageBox.Show("Invalid business code or server error");
                        return;
                    }

                    string json = await response.Content.ReadAsStringAsync();

                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };

                    var _data = JsonSerializer.Deserialize<RootData>(json, options);

                    if (_data?.data?.locations == null || _data.data.locations.Count == 0)
                    {
                        MessageBox.Show("No locations found.");
                        return;
                    }

                    // Save globally
                    WsWpfListener.Properties.Settings.Default.WorkspaceUrl = code;
                    WsWpfListener.Properties.Settings.Default.Save();
                    // WsWpfListener.Properties.Settings.Default.BaseUrl = url;
                    // WsWpfListener.Properties.Settings.Default.Save();
                    WorkspaceStorage.Save(_data.data);

                    // Navigate
                    var mainWindow = (MainWindow)Application.Current.MainWindow;
                    mainWindow.Content = new Views.LocationView();
                }
            }
            catch (HttpRequestException)
            {
                MessageBox.Show("Cannot connect to server. Check internet.");
            }
            catch (TaskCanceledException)
            {
                MessageBox.Show("Request timeout.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);
            }
        }
    }
}