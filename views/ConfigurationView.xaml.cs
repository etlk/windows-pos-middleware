using System.Windows;
using System.Windows.Controls;
using System.Drawing.Printing;
using MiddlewareApp.Models;
using System.IO;
using System.Text.Json;
using WsWpfListener;
using System.Drawing;
using System.Net.Http;
using System.Diagnostics;
using PusherClient;


namespace MiddlewareApp.Views
{
    public partial class PrinterConfigView : UserControl
    {
        private Location selectedLocation;
        private Device selectedDevice;
        private Pusher _pusher;
        public string BreadcrumbText { get; set; }
        public string Terminal { get; }
        private Data _workspace;
        private readonly PrinterHelperNormal _printerHelper = new();
        public PrinterConfigView(Location location, Device device, Data workspace)
        {
            InitializeComponent();
            selectedLocation = location;
            selectedDevice = device;
            _workspace = workspace;
            var locationName = selectedLocation?.name ?? "Unknown";
            var terminalName = selectedDevice?.device_name ?? "Unknown";

            BreadcrumbText = $"{WsWpfListener.Properties.Settings.Default.WorkspaceUrl} > {locationName} > {terminalName}";
            Terminal = terminalName;
            this.DataContext = this;
            LoadPrinters();
            LoadDepartments();
            Loaded += PrinterConfigView_Loaded;
        }
        private async void PrinterConfigView_Loaded(object sender, RoutedEventArgs e)
        {
            await Listen();
        }
        private void LoadPrinters()
        {
            foreach (string printer in PrinterSettings.InstalledPrinters)
            {
                // Default combo for terminal (we can add for departments later)
                PrinterComboBox.Items.Add(printer);
            }

            // Optional: select default printer
            PrinterSettings settings = new PrinterSettings();
            string defaultPrinter = settings.PrinterName;
            PrinterComboBox.SelectedItem = defaultPrinter;
        }

        private PrintConfigRequest BuildPayload()
        {
            return new PrintConfigRequest
            {
                device = new DeviceConfig
                {
                    is_middleware_configured = selectedDevice.configured,
                    print_config = selectedDevice.configured
                        ? new PrintConfig
                        {
                            port = 9100,
                            paper_size = "80mm"
                        }
                        : null
                },

                departments = selectedLocation.departments.Select(d => new DepartmentConfig
                {
                    id = d.id,
                    is_middleware_configured = d.configured,
                    print_config = d.configured
                        ? new PrintConfig
                        {
                            port = 2000,
                            paper_size = "80mm"
                        }
                        : null
                }).ToList()
            };
        }

        private static readonly HttpClient client = new HttpClient();

        private async Task SendPrintConfig()
        {
            try
            {
                string workspaceUrl = WsWpfListener.Properties.Settings.Default.WorkspaceUrl.TrimEnd('/');

                string baseUrl = $"https://{workspaceUrl}.etpos.store/api/v1";

                string url = $"{baseUrl}/locations/{selectedLocation.id}/devices/{selectedDevice.id}/print-config";

                var payload = BuildPayload();

                var json = JsonSerializer.Serialize(payload);

                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(new HttpMethod("PATCH"), url)
                {
                    Content = content
                };

                var response = await client.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    MessageBox.Show(" Configuration synced to server!");
                }
                else
                {
                    string error = await response.Content.ReadAsStringAsync();
                    MessageBox.Show($" API Error: {response.StatusCode}\n{error}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);
            }
        }

        public async Task Listen()
        {
            var options = new PusherOptions
            {
                Cluster = "ap1"
            };

            string workspaceRaw = WsWpfListener.Properties.Settings.Default.WorkspaceUrl;

            string workspace = workspaceRaw.Contains(".")
                ? workspaceRaw.Split('.')[0]
                : workspaceRaw;

            string channelName = $"merchant.{workspace}.location.{selectedLocation.id}";

            Debug.WriteLine("Subscribing to: " + channelName);

            _pusher = new Pusher("72e6aeaeb45fc01084ad", options);
            await _pusher.ConnectAsync();

            var channel = await _pusher.SubscribeAsync(channelName);

            channel.BindAll((eventName, data) =>
            {
                try
                {
                    string raw = data.ToString();

                    Debug.WriteLine("RAW: " + raw);

                    //  Find "data ="
                    int dataIndex = raw.IndexOf("data =");
                    if (dataIndex == -1) return;

                    //  Get everything after "data ="
                    string temp = raw.Substring(dataIndex + 6).Trim();

                    //  Extract ONLY JSON object
                    int start = temp.IndexOf('{');
                    int braceCount = 0;
                    int end = -1;

                    for (int i = start; i < temp.Length; i++)
                    {
                        if (temp[i] == '{') braceCount++;
                        if (temp[i] == '}') braceCount--;

                        if (braceCount == 0)
                        {
                            end = i;
                            break;
                        }
                    }

                    if (start == -1 || end == -1) return;

                    string json = temp.Substring(start, end - start + 1);

                    Debug.WriteLine("CLEAN JSON: " + json);


                    //  Now safe to parse
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    string command = root.GetProperty("command").GetString();
                    string html = root.GetProperty("html").GetString();
                  
                    int? departmentId = root.TryGetProperty("department_id", out var deptProp) && deptProp.ValueKind != JsonValueKind.Null
                        ? deptProp.GetInt32()
                        : (int?)null;


                    if (command == "PRINT_RECEIPT")
                    {
                        Dispatcher.Invoke(() =>
                        {
                            _printerHelper.PrintHtmlReceipt(html, selectedDevice.selected_printer, null);
                        });
                    }

                    if (command == "PRINT_KOT")
                    {
                        Dispatcher.Invoke(() =>
                        {
                            string printer = GetPrinterByDepartment(departmentId);

                            _printerHelper.PrintHtmlReceipt(html, printer, null);
                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("ERROR: " + ex.Message);
                }
            });
        }
        private string GetPrinterByDepartment(int? departmentId)
        {
            if (departmentId == null)
                return selectedDevice.selected_printer;

            var dept = selectedLocation.departments
                .FirstOrDefault(d => d.id == departmentId.Value);

            if (dept != null && !string.IsNullOrWhiteSpace(dept.selected_printer))
                return dept.selected_printer;

            return selectedDevice.selected_printer;
        }

        private async void TerminalConfigure_Click(object sender, RoutedEventArgs e)
        {
            string selectedPrinter = PrinterComboBox.SelectedItem?.ToString();
            selectedDevice.selected_printer = selectedPrinter;
            if (selectedDevice.configured == true)
            {
                selectedDevice.configured = false;
                TerminalConfigureButton.Content = "Configure";
                TerminalConfigureButton.Background = System.Windows.Media.Brushes.Blue;

            }
            else
            {
                selectedDevice.configured = true;
                TerminalConfigureButton.Content = "Remove Configuration";
                TerminalConfigureButton.Background = System.Windows.Media.Brushes.Red;
            }
            WorkspaceStorage.Save(_workspace);
            await SendPrintConfig();
        }

        private void LoadDepartments()
        {
            if (selectedLocation.departments == null || selectedLocation.departments.Count == 0)
            {
                TextBlock noDept = new TextBlock
                {
                    Text = "No departments available.",
                    Foreground = System.Windows.Media.Brushes.Gray
                };
                DepartmentsPanel.Children.Add(noDept);
                return;
            }

            foreach (var dept in selectedLocation.departments)
            {
                DepartmentsPanel.Children.Add(CreateDepartmentUI(dept));
            }
        }

        private void SaveToJson()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "locations.json");
            var root = LoadJson();

            if (root == null)
            {
                MessageBox.Show("Could not load JSON.");
                return;
            }

            // Find the location in JSON
            var location = root.data.locations.FirstOrDefault(l => l.id == selectedLocation.id);
            if (location != null)
            {
                // Update terminal/device printer
                var device = location.devices.FirstOrDefault(d => d.id == selectedDevice.id);
                if (device != null)
                {
                    device.selected_printer = selectedDevice.selected_printer;
                }

                // Update all departments' printers
                foreach (var dept in location.departments)
                {
                    var updatedDept = selectedLocation.departments.FirstOrDefault(d => d.id == dept.id);
                    if (updatedDept != null)
                    {
                        dept.selected_printer = updatedDept.selected_printer;
                    }
                }
            }

            // Save back to file
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path, JsonSerializer.Serialize(root, options));

            MessageBox.Show("Printers saved to JSON successfully!");
        }

        private RootData LoadJson()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "locations.json");

            if (!File.Exists(path))
                return null;

            string json = File.ReadAllText(path);
            var root = JsonSerializer.Deserialize<RootData>(json);
            return root;
        }
        private UIElement CreateDepartmentUI(Department dept)
        {
            // Container border
            Border border = new Border
            {
                Background = System.Windows.Media.Brushes.White,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(15),
                Margin = new Thickness(0, 0, 0, 15)
            };

            StackPanel panel = new StackPanel();

            // Title row with name + ID
            DockPanel dock = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };

            TextBlock title = new TextBlock
            {
                Text = $"{dept.name}",
                FontWeight = FontWeights.SemiBold
            };
            DockPanel.SetDock(title, Dock.Left);
            dock.Children.Add(title);


            // Printer ComboBox
            ComboBox printerCombo = new ComboBox
            {
                Height = 40,
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(5),
                FontSize = 14,
                Name = $"PrinterCombo_{dept.id}"
            };

            // Fill printers
            foreach (string printer in PrinterSettings.InstalledPrinters)
            {
                printerCombo.Items.Add(printer);
            }
            printerCombo.SelectedItem = new PrinterSettings().PrinterName;


            // Configure Button
            Button configureBtn = new Button
            {
                Content = "Configure",
                Height = 35,
                Background = System.Windows.Media.Brushes.Blue,
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = printerCombo // store combo for easy access
            };
            configureBtn.Click += async (s, e) =>
            {
                var selectedPrinter = (configureBtn.Tag as ComboBox)?.SelectedItem?.ToString();
                dept.selected_printer = selectedPrinter;
                if (dept.configured == true)
                {
                    dept.configured = false;
                    configureBtn.Content = "Configure";
                    configureBtn.Background = System.Windows.Media.Brushes.Blue;

                }
                else
                {
                    dept.configured = true;
                    configureBtn.Content = "Remove Configuration";
                    configureBtn.Background = System.Windows.Media.Brushes.Red;
                }
                WorkspaceStorage.Save(_workspace);
                await SendPrintConfig();
                // MessageBox.Show($"Department '{dept.name}' Printer: {selectedPrinter}");
            };

            panel.Children.Add(dock);
            panel.Children.Add(printerCombo);
            panel.Children.Add(configureBtn);

            border.Child = panel;
            return border;
        }
    }


}