using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MiddlewareApp.Models;
namespace MiddlewareApp.Views
{
    public partial class TerminalView : UserControl
    {
        public Location SelectedLocation { get; }
        private Data _workspace;

        public TerminalView(Location location,Data workspace)
        {
            InitializeComponent();
            _workspace = workspace;
             GreetingText.Text = GetGreeting();
             SelectedLocation = location;
             LoadTerminals(location);
          
        }

        private void LoadTerminals(Location location)
        {
            if (location.devices == null || location.devices.Count == 0)
                return;

            foreach (var device in location.devices)
            {
                AddTerminalItem(device);
            }
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

        private void AddTerminalItem(Device device)
        {
            Border border = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 5, 0, 5),
                Cursor = Cursors.Hand
            };

            Grid row = new Grid();

            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // LEFT
            StackPanel left = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            // Ellipse circle = new Ellipse
            // {
            //     Width = 30,
            //     Height = 30,
            //     Fill = Brushes.LightGray
            // };

            string initials = device.device_name.Length >= 2 ? device.device_name.Substring(0, 2).ToUpper() : device.device_name.ToUpper();
            Border square = new Border
            {
                Width = 30,
                Height = 30,
                Background = Brushes.LightGray,  // background of square
                CornerRadius = new CornerRadius(15),
                Child = new TextBlock
                {
                    Text = initials,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.Black,   // initials color
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12
                }
            };

            TextBlock text = new TextBlock
            {
                Text = device.device_name,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
                FontSize = 14
            };

            left.Children.Add(square);
            left.Children.Add(text);

            // RIGHT ARROW
            TextBlock arrow = new TextBlock
            {
                Text = "›",
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Gray
            };

            Grid.SetColumn(left, 0);
            Grid.SetColumn(arrow, 1);

            row.Children.Add(left);
            row.Children.Add(arrow);

            border.Child = row;

            //  Hover
            border.MouseEnter += (s, e) =>
            {
                border.Background = new SolidColorBrush(Color.FromRgb(230, 240, 255));
            };

            border.MouseLeave += (s, e) =>
            {
                if (border.Tag == null)
                    border.Background = Brushes.White;
            };

            //  Select
            border.MouseLeftButtonUp += (s, e) =>
            {
                foreach (var child in TerminalListPanel.Children)
                {
                    if (child is Border b)
                    {
                        b.Background = Brushes.White;
                        b.Tag = null;
                    }
                }

                border.Background = new SolidColorBrush(Color.FromRgb(200, 220, 255));
                border.Tag = "selected";

                //MessageBox.Show($"Selected Terminal: {device.device_name}");

                // TODO: Navigate to Printer Config screen
                var mainWindow = (MainWindow)Application.Current.MainWindow;
                mainWindow.Content = new Views.PrinterConfigView(SelectedLocation,device,_workspace);
            };

            TerminalListPanel.Children.Add(border);
        }
    }
}