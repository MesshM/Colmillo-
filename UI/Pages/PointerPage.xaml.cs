using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Colmillo.Core.Windows;
using Colmillo.UI.Controls;
using Microsoft.Win32;

namespace Colmillo.UI.Pages;

/// <summary>
/// Ajustes de Windows: se leen del sistema al abrir y se aplican al instante. No se
/// guardan en ajustes.json porque Windows ya los guarda (y otro programa los puede cambiar).
/// </summary>
public partial class PointerPage : PageBase
{
    // Formas que se muestran en la tarjeta de cada esquema: flecha, mano, texto, ocupado.
    static readonly int[] PreviewRoles = [0, 14, 5, 3];
    string[] _customFiles = [];

    public PointerPage()
    {
        InitializeComponent();
        Reload();
    }

    public override void Reload()
    {
        Loading = true;
        Speed.Value = MouseSystem.PointerSpeed;
        Precision.IsChecked = MouseSystem.EnhancePrecision;
        Swap.IsChecked = MouseSystem.SwapButtons;
        Snap.IsChecked = MouseSystem.SnapToDefault;
        var lines = MouseSystem.ScrollLines;
        PageScroll.IsChecked = lines < 0;
        Lines.Value = lines < 0 ? 3 : lines;
        Lines.IsEnabled = lines >= 0;
        Chars.Value = MouseSystem.ScrollChars;
        DoubleSlider.Value = MouseSystem.DoubleClickTime;
        SizeSlider.Value = MouseSystem.CursorSize;
        Trails.Value = Math.Max(1, MouseSystem.Trails);
        Sonar.IsChecked = MouseSystem.Sonar;
        Vanish.IsChecked = MouseSystem.HideWhileTyping;
        UpdateLabels();
        BuildSchemes();
        RestoreButton.IsEnabled = Settings.OriginalCursorScheme is not null;
        Loading = false;
    }

    void UpdateLabels()
    {
        SpeedValue.Text = $"{(int)Speed.Value}";
        LinesValue.Text = PageScroll.IsChecked == true ? "Pantalla" : $"{(int)Lines.Value}";
        CharsValue.Text = $"{(int)Chars.Value}";
        DoubleValue.Text = $"{(int)DoubleSlider.Value} ms";
        SizeValue.Text = $"{(int)SizeSlider.Value}";
        TrailsValue.Text = Trails.Value <= 1 ? "Apagada" : $"{(int)Trails.Value}";
    }

    void Speed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Loading) return;
        MouseSystem.PointerSpeed = (int)e.NewValue;
        UpdateLabels();
    }

    void Lines_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Loading) return;
        MouseSystem.ScrollLines = (int)e.NewValue;
        UpdateLabels();
    }

    void Chars_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Loading) return;
        MouseSystem.ScrollChars = (int)e.NewValue;
        UpdateLabels();
    }

    void Double_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Loading) return;
        MouseSystem.DoubleClickTime = (int)e.NewValue;
        UpdateLabels();
    }

    void Size_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Loading) return;
        MouseSystem.CursorSize = (int)e.NewValue;
        UpdateLabels();
    }

    void Trails_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Loading) return;
        MouseSystem.Trails = (int)e.NewValue;
        UpdateLabels();
    }

    void Toggle_Changed(object sender, RoutedEventArgs e)
    {
        if (Loading) return;
        var on = ((CheckBox)sender).IsChecked == true;
        if (sender == Precision) MouseSystem.EnhancePrecision = on;
        else if (sender == Swap) MouseSystem.SwapButtons = on;
        else if (sender == Snap) MouseSystem.SnapToDefault = on;
        else if (sender == Sonar) MouseSystem.Sonar = on;
        else if (sender == Vanish) MouseSystem.HideWhileTyping = on;
        else if (sender == PageScroll)
        {
            MouseSystem.ScrollLines = on ? -1 : (int)Lines.Value;
            Lines.IsEnabled = !on;
        }
        UpdateLabels();
    }

    void DoubleTest_Down(object sender, MouseButtonEventArgs e)
    {
        // WPF cuenta los clics con el tiempo de doble clic del sistema: prueba real.
        if (e.ClickCount < 2) return;
        var open = DoubleIcon.Text == "";
        DoubleIcon.Text = open ? "" : "";
        DoubleIcon.Foreground = Res(open ? "Ink400" : "Accent600");
    }

    // ---------- Esquemas ----------

    void BuildSchemes()
    {
        Schemes.Children.Clear();
        var current = CursorSchemes.Current();
        foreach (var scheme in CursorSchemes.List())
            Schemes.Children.Add(SchemeCard(scheme, scheme.Name == current));
    }

    Border SchemeCard(CursorScheme scheme, bool active)
    {
        var icons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var role in PreviewRoles)
        {
            var src = CursorPreview.Load(scheme.Files.ElementAtOrDefault(role) ?? "");
            icons.Children.Add(new Image { Source = src, Width = 24, Height = 24, Margin = new Thickness(2) });
        }
        var stack = new StackPanel();
        stack.Children.Add(icons);
        stack.Children.Add(new TextBlock
        {
            Text = Friendly(scheme.Name),
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            Foreground = Res(active ? "Accent700" : "Ink600"),
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 6, 0, 0),
        });
        var card = new Border
        {
            Width = 136,
            Padding = new Thickness(8, 10, 8, 8),
            Margin = new Thickness(0, 0, 8, 8),
            CornerRadius = new CornerRadius(14),
            Background = Res(active ? "PillActive" : "Fog"),
            BorderBrush = active ? Res("Accent500") : Brushes.Transparent,
            BorderThickness = new Thickness(1.5),
            Cursor = Cursors.Hand,
            Child = stack,
            ToolTip = scheme.Name,
        };
        card.MouseLeftButtonUp += (_, _) =>
        {
            RememberOriginal();
            CursorSchemes.Apply(scheme);
            Reload();
        };
        return card;
    }

    /// <summary>Los nombres de los esquemas de Windows vienen en ingles; se traducen los conocidos.</summary>
    static string Friendly(string name) => name
        .Replace("Windows Default", "Windows")
        .Replace("Windows Black", "Windows negro")
        .Replace("Windows Inverted", "Windows invertido")
        .Replace("Windows Standard", "Windows estándar")
        .Replace("(extra large)", "(extra grande)")
        .Replace("(large)", "(grande)")
        .Replace("(system scheme)", "")
        .Trim();

    /// <summary>Antes del primer cambio se guarda lo que tenia el usuario, para poder volver.</summary>
    void RememberOriginal()
    {
        if (Settings.OriginalCursorScheme is not null) return;
        Settings.OriginalCursorScheme = CursorSchemes.Current() + "\n" + string.Join(",", CursorSchemes.CurrentFiles());
        App.Current.SaveSoon();
    }

    void RestoreOriginal_Click(object sender, RoutedEventArgs e)
    {
        if (Settings.OriginalCursorScheme is not { } saved) return;
        var parts = saved.Split('\n', 2);
        var files = parts.Length > 1 ? parts[1].Split(',') : [];
        Array.Resize(ref files, CursorSchemes.Roles.Length);
        CursorSchemes.Apply(new CursorScheme(parts[0], files.Select(f => f ?? "").ToArray(), IsSystem: false));
        Reload();
    }

    void Default_Click(object sender, RoutedEventArgs e)
    {
        RememberOriginal();
        CursorSchemes.ResetToDefault();
        Reload();
    }

    void Customize_Click(object sender, RoutedEventArgs e)
    {
        CustomPanel.Visibility = CustomPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (CustomPanel.Visibility != Visibility.Visible) return;
        _customFiles = CursorSchemes.CurrentFiles();
        BuildRoles();
    }

    void BuildRoles()
    {
        Roles.Children.Clear();
        for (var i = 0; i < CursorSchemes.Roles.Length; i++)
        {
            var index = i;
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var img = new Image { Source = CursorPreview.Load(_customFiles[i]), Width = 24, Height = 24 };
            var label = Text(CursorSchemes.Roles[i].Label, "Body");
            label.FontSize = 12.5;
            label.Margin = new Thickness(6, 0, 0, 0);
            var pick = new Button { Style = (Style)FindResource("Outline"), Content = "Cambiar", Height = 30, Padding = new Thickness(12, 0, 12, 0) };
            pick.Click += (_, _) =>
            {
                var dlg = new OpenFileDialog
                {
                    Title = CursorSchemes.Roles[index].Label,
                    Filter = "Cursores|*.cur;*.ani",
                    InitialDirectory = Environment.ExpandEnvironmentVariables(@"%SystemRoot%\Cursors"),
                };
                if (dlg.ShowDialog() != true) return;
                _customFiles[index] = dlg.FileName;
                img.Source = CursorPreview.Load(dlg.FileName);
            };
            Grid.SetColumn(label, 1);
            Grid.SetColumn(pick, 2);
            row.Children.Add(img);
            row.Children.Add(label);
            row.Children.Add(pick);
            Roles.Children.Add(row);
        }
    }

    void SaveCustom_Click(object sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(CustomName.Text) ? "Mi esquema" : CustomName.Text.Trim().Replace(",", "");
        RememberOriginal();
        CursorSchemes.SaveUserScheme(name, _customFiles);
        CustomPanel.Visibility = Visibility.Collapsed;
        Reload();
    }
}
