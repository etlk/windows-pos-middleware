using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.IO;
using System.Text.Json;
using MiddlewareApp.Models;
using WsWpfListener;

namespace MiddlewareApp.Views
{
    public partial class LocationView : UserControl
    {
        private Data? _workspace;

        public LocationView()
        {
            InitializeComponent();
            GreetingText.Text = GetGreeting();
            LoadLocations();
        }

        private void LoadLocations()
        {
            try
            {

                _workspace = WorkspaceStorage.Load();
                if (_workspace != null && _workspace.locations?.Count > 0)
                {


                    foreach (var location in _workspace.locations)
                    {
                        AddLocationItem(location);
                    }

                }
                else
                {
                    MessageBox.Show("locations.json file not found!");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading locations: {ex.Message}");
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

        private void AddLocationItem(Location location)
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

            // LEFT SIDE
            StackPanel left = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };
            string initials = location.name.Length >= 2 ? location.name.Substring(0, 2).ToUpper() : location.name.ToUpper();
            // Ellipse circle = new Ellipse
            // {
            //     Width = 30,
            //     Height = 30,
            //     Fill = Brushes.LightGray
            // };

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
                Text = location.name,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
                FontSize = 14
            };

            left.Children.Add(square);
            left.Children.Add(text);

            // RIGHT SIDE ARROW
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

            //  HOVER EFFECT
            border.MouseEnter += (s, e) =>
            {
                border.Background = new SolidColorBrush(Color.FromRgb(230, 240, 255));
            };

            border.MouseLeave += (s, e) =>
            {
                if (border.Tag == null) // not selected
                    border.Background = Brushes.White;
            };

            //  CLICK (SELECT)
            border.MouseLeftButtonUp += (s, e) =>
            {
                // Reset all items
                foreach (var child in LocationListPanel.Children)
                {
                    if (child is Border b)
                    {
                        b.Background = Brushes.White;
                        b.Tag = null;
                    }
                }

                // Highlight selected
                border.Background = new SolidColorBrush(Color.FromRgb(200, 220, 255));
                border.Tag = "selected";

                //MessageBox.Show($"Selected Location: {location.name}");

                // TODO: Navigate to Terminal screen
                var mainWindow = (MainWindow)Application.Current.MainWindow;
                mainWindow.Content = new Views.TerminalView(location,_workspace);
            };

            LocationListPanel.Children.Add(border);
        }
    }
}