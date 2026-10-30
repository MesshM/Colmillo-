using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Colmillo.Core;

namespace Colmillo.UI.Pages;

/// <summary>
/// Curva de los ventiladores de la grafica. "De fabrica" vuelve a la curva por defecto
/// del driver; tocar un punto o elegir un punto de partida pasa a "Personalizada".
/// </summary>
public partial class FansPage : PageBase
{
    // Puntos de partida: temperatura (°C) y velocidad (%). Se ajustan a los rangos del driver.
    static readonly (string Name, (int T, int S)[] Points)[] PresetCurves =
    [
        ("Silenciosa", [(40, 20), (55, 30), (65, 45), (75, 65), (85, 100)]),
        ("Equilibrada", [(35, 25), (50, 35), (60, 50), (70, 70), (80, 100)]),
        ("Fresca", [(30, 35), (45, 50), (55, 65), (65, 85), (75, 100)]),
    ];

    readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1) };

    static AmdService Amd => App.Current.Amd;
    static GpuFanProfile F => App.Current.Settings.GpuFans;

    public FansPage()
    {
        InitializeComponent();
        Editor.Changed += final =>
        {
            if (Loading) return;
            F.Points = [.. Editor.Points.Select(p => new FanPoint(p.Temperature, p.Speed))];
            if (!F.Custom)
            {
                F.Custom = true;
                Loading = true;
                ModeCustom.IsChecked = true;
                Loading = false;
            }
            if (final) Apply();
        };
        foreach (var (name, points) in PresetCurves)
        {
            var b = new Button { Style = (Style)FindResource("Outline"), Content = name, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
            b.Click += (_, _) => UsePreset(points);
            Presets.Children.Add(b);
        }
        _poll.Tick += async (_, _) => await Poll();
        Loaded += async (_, _) =>
        {
            await Amd.BeginMonitorAsync();
            await Poll();
            _poll.Start();
        };
        Unloaded += (_, _) =>
        {
            _poll.Stop();
            Amd.EndMonitor();
        };
        Amd.StateChanged += OnFansChanged;
        Unloaded += (_, _) => Amd.StateChanged -= OnFansChanged;
        Reload();
    }

    void OnFansChanged() => Dispatcher.BeginInvoke(Reload);

    public override void Reload()
    {
        Loading = true;
        var curve = Amd.Curve;
        var ok = Amd.Available && curve is not null && F.Points is not null;
        CurveCard.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
        Missing.Visibility = ok || Amd.Status == "Buscando…" ? Visibility.Collapsed : Visibility.Visible;
        if (ok)
        {
            Subtitle.Text = $"{Amd.Name}. La curva la aplica el driver de AMD; Colmillo la vuelve a poner al iniciar.";
            ModeAuto.IsChecked = !F.Custom;
            ModeCustom.IsChecked = F.Custom;
            Editor.Set(F.Custom ? F.Points! : curve!.Default, curve!.TempMin, curve.TempMax, curve.SpeedMin, curve.SpeedMax);
            ZeroRpm.Visibility = curve.ZeroRpmSupported ? Visibility.Visible : Visibility.Collapsed;
            ZeroRpm.IsChecked = F.Custom ? F.ZeroRpm : curve.DefaultZeroRpm;
            CurveHint.Text = F.Custom
                ? "Arrastra los puntos. Antes del primero el ventilador va a su velocidad; pasado el último, al máximo que marque."
                : "La curva por defecto de la tarjeta. Mueve un punto o elige un punto de partida para personalizarla.";
        }
        var others = new List<string>();
        if (AmdService.AfterburnerRunning()) others.Add("MSI Afterburner");
        Warning.Visibility = others.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        WarningText.Text = $"{string.Join(" y ", others)} está abierto y también controla los ventiladores: puede pisar esta curva.";
        Loading = false;
    }

    async Task Poll()
    {
        var r = await Amd.ReadAsync();
        if (r is not { } v || v.Temperature <= 0) return;
        TempText.Text = $"{v.Temperature:0} °C";
        HotspotText.Text = v.Hotspot > 0 ? $"{v.Hotspot:0} °C" : "–";
        RpmText.Text = v.Rpm > 0 ? $"{v.Rpm} RPM" : "Detenido";
        Editor.SetCurrent(v.Temperature);
    }

    void UsePreset((int T, int S)[] points)
    {
        if (Amd.Curve is not { } c) return;
        F.Points = [.. points.Select(p => new FanPoint(
            Math.Clamp(p.T, c.TempMin, c.TempMax),
            Math.Clamp(p.S, c.SpeedMin, c.SpeedMax)))];
        F.Custom = true;
        Reload();
        Apply();
    }

    void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (Loading) return;
        F.Custom = ModeCustom.IsChecked == true;
        Reload();
        Apply();
    }

    void ZeroRpm_Changed(object sender, RoutedEventArgs e)
    {
        if (Loading) return;
        F.ZeroRpm = ZeroRpm.IsChecked == true;
        if (!F.Custom)
        {
            F.Custom = true;
            Reload();
        }
        Apply();
    }

    void Apply()
    {
        ApplyStatus.Text = "Aplicando…";
        Commit(async () =>
        {
            var error = await Amd.ApplyAsync();
            ApplyStatus.Text = error ?? (F.Custom ? "Curva propia aplicada." : "Curva de fábrica aplicada.");
        });
    }

    async void Retry_Click(object sender, RoutedEventArgs e)
    {
        await Amd.ConnectAndApplyAsync();
        Reload();
    }
}
