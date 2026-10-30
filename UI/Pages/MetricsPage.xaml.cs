using System.Windows;
using System.Windows.Controls;
using Colmillo.Core;
using Colmillo.Core.Metrics;
using Colmillo.Shell;

namespace Colmillo.UI.Pages;

/// <summary>Overlay de metricas: encenderlo, donde va y que muestra, con los valores en vivo.</summary>
public partial class MetricsPage : PageBase
{
    static MetricsService Metrics => App.Current.Metrics;
    static MetricsProfile M => App.Current.Settings.Metrics;

    readonly Dictionary<string, TextBlock> _values = [];

    public MetricsPage()
    {
        InitializeComponent();
        BuildGroups();
        Metrics.Updated += OnUpdated;
        Metrics.Start();
        Unloaded += (_, _) =>
        {
            Metrics.Updated -= OnUpdated;
            Metrics.Stop();
        };
        Reload();
    }

    public override void Reload()
    {
        Loading = true;
        OverlayOn.IsChecked = M.Overlay;
        OnlyGames.IsChecked = M.OnlyInGames;
        foreach (var r in Corners.Children.OfType<RadioButton>())
            r.IsChecked = (string)r.Tag == M.Corner;
        FontSizeSlider.Value = M.FontSize;
        SizeValue.Text = $"{M.FontSize} px";
        HotKeyText.Text = App.Current.OverlayHotKeyRegistered
            ? "Ctrl + Shift + O lo muestra u oculta en cualquier momento, también jugando."
            : "Ctrl + Shift + O no se pudo registrar: otro programa ya lo usa.";
        CpuNote.Text = !RyzenTemperature.ModuleBundled
            ? "La temperatura del CPU necesita el módulo AMDFamily17 de PawnIO, que todavía no está incluido."
            : !SystemIntegration.IsElevated
                ? "La temperatura del CPU necesita que Colmillo corra como administrador (como la RAM)."
                : "Los Ryzen no tienen un sensor por núcleo: se muestra Tctl (la del ventilador) y una por CCD. Por núcleo hay uso y reloj.";
        Loading = false;
    }

    void BuildGroups()
    {
        Groups.Children.Clear();
        _values.Clear();
        foreach (var group in MetricCatalog.All.GroupBy(m => m.Group))
        {
            Groups.Children.Add(new TextBlock
            {
                Style = (Style)FindResource("Label"),
                Text = group.Key switch { "GPU" => "GRÁFICA", var g => g },
                Margin = new Thickness(2, 14, 0, 4),
            });
            foreach (var item in group)
            {
                var value = new TextBlock { Style = (Style)FindResource("Body"), Foreground = Res("Accent600"), MinWidth = 110, TextAlignment = TextAlignment.Right };
                _values[item.Key] = value;
                var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
                var check = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = M.Shown.Contains(item.Key), Content = new TextBlock { Style = (Style)FindResource("Body"), Text = item.Label } };
                check.Checked += (_, _) => Toggle(item.Key, true);
                check.Unchecked += (_, _) => Toggle(item.Key, false);
                DockPanel.SetDock(value, Dock.Right);
                row.Children.Add(value);
                row.Children.Add(check);
                Groups.Children.Add(row);
            }
        }
        if (Metrics.Last is { } last) Show(last);
    }

    void OnUpdated(MetricsSnapshot s) => Dispatcher.BeginInvoke(() => Show(s));

    void Show(MetricsSnapshot s)
    {
        foreach (var (key, block) in _values) block.Text = MetricsFormat.Value(s, key) ?? "–";
    }

    void Toggle(string key, bool on)
    {
        if (Loading) return;
        M.Shown.Remove(key);
        if (on) M.Shown.Add(key);
        App.Current.SaveSoon();
    }

    void Overlay_Changed(object sender, RoutedEventArgs e)
    {
        if (Loading) return;
        M.Overlay = OverlayOn.IsChecked == true;
        App.Current.SaveSoon();
        App.Current.UpdateOverlay();
    }

    void OnlyGames_Changed(object sender, RoutedEventArgs e)
    {
        if (Loading) return;
        M.OnlyInGames = OnlyGames.IsChecked == true;
        App.Current.SaveSoon();
        App.Current.UpdateOverlay(rebuild: true);
    }

    void Corner_Checked(object sender, RoutedEventArgs e)
    {
        if (Loading) return;
        M.Corner = (string)((RadioButton)sender).Tag;
        App.Current.SaveSoon();
        App.Current.UpdateOverlay(rebuild: true);
    }

    void Size_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Loading) return;
        M.FontSize = (int)e.NewValue;
        SizeValue.Text = $"{M.FontSize} px";
        App.Current.SaveSoon();
    }

    void Size_Released(object sender, System.Windows.Input.MouseButtonEventArgs e) => App.Current.UpdateOverlay(rebuild: true);
}
