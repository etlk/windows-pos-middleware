using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MiddlewareApp.Views.Controls;

/// <summary>Simple rotating-arc spinner. Set Width/Height to size it; Color to tint it.</summary>
public partial class Spinner : UserControl
{
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(Brush), typeof(Spinner),
        new PropertyMetadata(null, OnColorChanged));

    public Brush? Color
    {
        get => (Brush?)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public Spinner()
    {
        InitializeComponent();
    }

    private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var spinner = (Spinner)d;
        if (e.NewValue is Brush brush)
        {
            spinner.Ring.Stroke = brush;
            spinner.Arc.Stroke = brush;
        }
    }
}
