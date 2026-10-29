using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Colmillo.UI.Controls;

/// <summary>Muestras rapidas + barra de tono + campo hex. Sin dependencias.</summary>
public sealed class ColorPicker : StackPanel
{
    static readonly string[] Swatches =
    [
        "#D7264F", "#FF3B30", "#FF8A00", "#FFD60A", "#44D62C", "#00C7A0",
        "#00B4FF", "#3D5AFE", "#8E44FF", "#FF4FD8", "#F5E6C8", "#FFFFFF",
    ];

    readonly Slider _hue;
    readonly TextBox _hex;
    readonly Ellipse _preview;
    bool _updating;

    public event Action<string>? ColorChanged;
    public string Value { get; private set; } = "#FFFFFF";

    public ColorPicker()
    {
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var hex in Swatches)
        {
            var dot = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Margin = new Thickness(0, 0, 8, 8),
                Background = new SolidColorBrush(Parse(hex)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = hex,
            };
            dot.MouseLeftButtonUp += (_, _) => Set(hex, notify: true);
            row.Children.Add(dot);
        }
        Children.Add(row);

        var line = new Grid();
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition());
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });

        _preview = new Ellipse { Width = 34, Height = 34, Stroke = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0)), Margin = new Thickness(0, 0, 14, 0) };

        var rainbow = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (var i = 0; i <= 6; i++) rainbow.GradientStops.Add(new GradientStop(FromHue(i * 60), i / 6.0));
        var hueTrack = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        hueTrack.Children.Add(new Border { Height = 12, CornerRadius = new CornerRadius(6), Background = rainbow, Margin = new Thickness(9, 0, 9, 0) });
        _hue = new Slider { Minimum = 0, Maximum = 360, Style = (Style)Application.Current.FindResource("BareSlider") };
        _hue.ValueChanged += (_, e) =>
        {
            if (!_updating) Set(ToHex(FromHue(e.NewValue)), notify: true);
        };
        hueTrack.Children.Add(_hue);

        _hex = new TextBox { TextAlignment = TextAlignment.Center, MaxLength = 7 };
        _hex.LostFocus += (_, _) => CommitHex();
        _hex.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) CommitHex();
        };

        Grid.SetColumn(hueTrack, 1);
        Grid.SetColumn(_hex, 2);
        line.Children.Add(_preview);
        line.Children.Add(hueTrack);
        line.Children.Add(_hex);
        Children.Add(line);
    }

    void CommitHex()
    {
        var text = _hex.Text.Trim();
        if (!text.StartsWith('#')) text = "#" + text;
        if (text.Length == 7 && int.TryParse(text[1..], System.Globalization.NumberStyles.HexNumber, null, out _))
            Set(text.ToUpperInvariant(), notify: true);
        else
            _hex.Text = Value;
    }

    public void Set(string hex, bool notify = false)
    {
        Value = hex;
        _updating = true;
        var c = Parse(hex);
        _preview.Fill = new SolidColorBrush(c);
        _hex.Text = hex;
        var (h, s, _) = ToHsv(c);
        if (s > 0.05) _hue.Value = h;
        _updating = false;
        if (notify) ColorChanged?.Invoke(hex);
    }

    public static Color Parse(string hex)
    {
        var v = Convert.ToInt32(hex.TrimStart('#'), 16);
        return Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Tono puro (saturacion y valor al maximo): es lo que mejor se ve en un LED.</summary>
    public static Color FromHue(double h)
    {
        h = (h % 360 + 360) % 360 / 60;
        var x = 1 - Math.Abs(h % 2 - 1);
        var (r, g, b) = (int)h switch
        {
            0 => (1.0, x, 0.0),
            1 => (x, 1.0, 0.0),
            2 => (0.0, 1.0, x),
            3 => (0.0, x, 1.0),
            4 => (x, 0.0, 1.0),
            _ => (1.0, 0.0, x),
        };
        return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    static (double H, double S, double V) ToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d == 0 ? 0 : max == r ? 60 * ((g - b) / d % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        return ((h + 360) % 360, max == 0 ? 0 : d / max, max);
    }
}
