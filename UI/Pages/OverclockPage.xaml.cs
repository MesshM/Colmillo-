using System.Windows;
using System.Windows.Controls;
using Colmillo.Core;
using Colmillo.Core.Gpu;

namespace Colmillo.UI.Pages;

/// <summary>Perfiles automaticos de la grafica: silencioso, equilibrado, rage y overclock automatico.</summary>
public partial class OverclockPage : PageBase
{
    sealed record Mode(GpuTuning Tuning, string Label, string Glyph, string Hint);

    static readonly Mode[] All =
    [
        new(GpuTuning.Quiet, "Silencioso", "", "Menos potencia y menos ruido. Algo menos de rendimiento."),
        new(GpuTuning.Balanced, "Equilibrado", "", "El de fábrica."),
        new(GpuTuning.Rage, "Rage", "", "Más límite de potencia: algo más de rendimiento, más calor y ruido."),
        new(GpuTuning.AutoOverclock, "Overclock automático", "", "El driver busca un overclock estable para esta tarjeta."),
    ];

    static AmdService Amd => App.Current.Amd;
    bool _busy;

    public OverclockPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            await Amd.BeginMonitorAsync();
            Reload();
        };
        Unloaded += (_, _) => Amd.EndMonitor();
        Reload();
    }

    public override void Reload()
    {
        Loading = true;
        var info = Amd.Tuning;
        ModesCard.Visibility = info is null ? Visibility.Collapsed : Visibility.Visible;
        Missing.Visibility = info is null && Amd.Status != "Buscando…" ? Visibility.Visible : Visibility.Collapsed;
        Modes.Children.Clear();
        if (info is not null)
        {
            var selected = Settings.GpuTuning != GpuTuning.Unknown ? Settings.GpuTuning : info.Current;
            foreach (var mode in All)
            {
                var supported = mode.Tuning switch
                {
                    GpuTuning.Quiet => info.Quiet,
                    GpuTuning.Balanced => info.Balanced,
                    GpuTuning.Rage => info.Rage,
                    _ => info.AutoOverclock,
                };
                if (!supported) continue;
                var card = new RadioButton
                {
                    Style = (Style)FindResource("OptionCard"),
                    GroupName = "tuning",
                    Tag = mode.Glyph,
                    Content = mode.Label,
                    Width = 150,
                    IsChecked = mode.Tuning == selected,
                    IsEnabled = !_busy,
                };
                card.Checked += (_, _) => Choose(mode);
                Modes.Children.Add(card);
            }
            CurrentText.Text = info.Current switch
            {
                GpuTuning.Quiet => "El driver tiene puesto: Silencioso.",
                GpuTuning.Balanced => "El driver tiene puesto: Equilibrado.",
                GpuTuning.Rage => "El driver tiene puesto: Rage.",
                GpuTuning.AutoOverclock => "El driver tiene puesto: overclock automático.",
                _ => "El driver tiene un ajuste manual (por ejemplo, la curva de ventiladores o algo hecho en Adrenalin).",
            };
            ModeHint.Text = All.FirstOrDefault(m => m.Tuning == selected)?.Hint ?? "";
        }
        Loading = false;
    }

    async void Choose(Mode mode)
    {
        if (Loading || _busy) return;
        _busy = true;
        foreach (var c in Modes.Children.OfType<RadioButton>()) c.IsEnabled = false;
        ApplyStatus.Text = mode.Tuning == GpuTuning.AutoOverclock ? "Calculando el overclock… puede tardar un poco." : "Aplicando…";
        App.Current.SaveSoon();
        var error = await Amd.SetTuningAsync(mode.Tuning);
        _busy = false;
        ApplyStatus.Text = error ?? $"{mode.Label} aplicado.";
        Reload();
    }
}
