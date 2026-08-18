using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MiddlewareApp.Views.Controls;

/// <summary>
/// Shared list-item card (spec §6.0): 40 px round avatar in primary blue with white
/// bold 13 px initials; title 16 px semibold; subtitle 13 px muted; trailing chevron.
/// </summary>
public static class ListItemFactory
{
    public static Border Create(string initials, string title, string? subtitle, Action onClick)
    {
        var app = Application.Current;
        var card = new Border
        {
            Background = (Brush)app.FindResource("CardBrush"),
            BorderBrush = (Brush)app.FindResource("HairlineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 10),
            Cursor = Cursors.Hand,
        };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var avatar = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(20),
            Background = (Brush)app.FindResource("PrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = initials,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(avatar, 0);

        var texts = new StackPanel
        {
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        texts.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)app.FindResource("TextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            texts.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 13,
                Foreground = (Brush)app.FindResource("TextMutedBrush"),
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        Grid.SetColumn(texts, 1);

        var chevron = new TextBlock
        {
            Text = "›",
            FontSize = 20,
            Foreground = (Brush)app.FindResource("TextMutedBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(chevron, 2);

        row.Children.Add(avatar);
        row.Children.Add(texts);
        row.Children.Add(chevron);
        card.Child = row;

        card.MouseEnter += (_, _) => card.Background = new SolidColorBrush(Color.FromRgb(0xE6, 0xF0, 0xFF));
        card.MouseLeave += (_, _) => card.Background = (Brush)app.FindResource("CardBrush");
        card.MouseLeftButtonUp += (_, _) => onClick();
        // Keyboard operability: list items are focusable and Enter/Space activates.
        card.Focusable = true;
        card.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space) onClick();
        };

        return card;
    }

    public static string Initials(string source) =>
        string.IsNullOrWhiteSpace(source)
            ? "?"
            : (source.Trim().Length >= 2 ? source.Trim()[..2] : source.Trim()).ToUpperInvariant();
}
