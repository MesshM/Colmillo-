using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Colmillo.Core;
using Colmillo.Core.Hid;
using Colmillo.Core.Ram;
using Colmillo.Core.Razer;
using Colmillo.UI.Controls;

namespace Colmillo.UI.Pages;

/// <summary>
/// Iluminacion de mouse y RAM. "Todos juntos" ofrece solo los efectos que tienen los dos
/// (apagado, estatico, respiracion, espectro) y escribe en el perfil de cada uno, asi al
/// pasar a "Por separado" cada dispositivo sigue donde quedo.
/// </summary>
public partial class LightingPage : PageBase
{
    enum Target { Together, Mouse, Ram }

    sealed record Fx(string Key, string Label, string Glyph, int Colors = 0, bool Speed = false);

    // Comunes a mouse y RAM. La grafica tambien los tiene, pero hoy no se puede controlar.
    static readonly Fx[] TogetherFx =
    [
        new("off", "Apagado", ""),
        new("static", "Estático", "", Colors: 1),
        new("breath", "Respiración", "", Colors: 1, Speed: true),
        new("spectrum", "Espectro", "", Speed: true),
    ];

    static readonly Fx[] MouseFx =
    [
        .. TogetherFx.Select(f => f with { Speed = false }),
        new("reactive", "Reactivo", "", Colors: 1, Speed: true),
    ];

    static readonly Fx[] RamFx =
    [
        .. TogetherFx,
        new("rainbow", "Arcoíris", "", Speed: true),
        new("dynamic", "Dinámico", "", Colors: 2, Speed: true),
        new("comet", "Cometa", "", Colors: 1, Speed: true),
        new("flame", "Llama", "", Speed: true),
        new("twilight", "Crepúsculo", "", Speed: true),
    ];

    Target _target;

    static RamService RamSvc => App.Current.Ram;
    static RamProfile R => App.Current.Settings.Ram;

    public LightingPage()
    {
        InitializeComponent();
        Color1.ColorChanged += hex => Write(c1: hex);
        Color2.ColorChanged += hex => Write(c2: hex);
        _target = Settings.LightingTogether ? Target.Together : Target.Mouse;
        Reload();
    }

    public override void Reload()
    {
        Loading = true;
        if (!RamSvc.Available && _target == Target.Ram) _target = Target.Mouse;
        ModeTogether.IsChecked = _target == Target.Together;
        ModeSeparate.IsChecked = _target != Target.Together;
        TargetMouse.IsChecked = _target == Target.Mouse;
        TargetRam.IsChecked = _target == Target.Ram;
        var separate = _target != Target.Together;
        TargetDivider.Visibility = TargetMouse.Visibility = TargetRam.Visibility = separate ? Visibility.Visible : Visibility.Collapsed;
        TargetRam.IsEnabled = RamSvc.Available;

        BuildDevices();
        BuildEffects();
        var (_, c1, c2, brightness) = Current();
        Color1.Set(c1);
        Color2.Set(c2);
        Brightness.Value = brightness;
        Loading = false;
        UpdateView();
    }

    // ---------- Estado actual segun destino ----------

    static string MouseKey(LightingEffect e) => e switch
    {
        LightingEffect.Off => "off",
        LightingEffect.Static => "static",
        LightingEffect.Spectrum => "spectrum",
        LightingEffect.Reactive => "reactive",
        _ => "breath",
    };

    static string RamKey(RamEffect e) => e.ToString().ToLowerInvariant() switch
    {
        "breathing" => "breath",
        var k => k,
    };

    (string Key, string C1, string C2, int Brightness) Current() => _target switch
    {
        Target.Ram => (RamKey(R.Effect), R.Color1, R.Color2, R.Brightness),
        // Juntos: manda el mouse; si tenia un efecto que no es comun, se muestra estatico.
        Target.Together => (TogetherFx.Any(f => f.Key == MouseKey(P.Effect)) ? MouseKey(P.Effect) : "static", P.Color1, P.Color2, P.Brightness),
        _ => (MouseKey(P.Effect), P.Color1, P.Color2, P.Brightness),
    };

    Fx[] Available => _target switch { Target.Ram => RamFx, Target.Mouse => MouseFx, _ => TogetherFx };

    bool TwoColorBreath => _target == Target.Ram ? R.TwoColors : P.Effect == LightingEffect.BreathingDual;

    // ---------- Escritura ----------

    void Write(string? key = null, string? c1 = null, string? c2 = null, int? brightness = null, int? speed = null, string? breathVariant = null)
    {
        if (Loading) return;
        var toMouse = _target is Target.Mouse or Target.Together;
        var toRam = (_target is Target.Ram or Target.Together) && RamSvc.Available;
        var currentKey = key ?? Current().Key;
        var variant = breathVariant ?? (TwoColorBreath ? "two" : P.Effect == LightingEffect.BreathingRandom ? "random" : "one");

        if (toMouse)
        {
            P.Effect = currentKey switch
            {
                "off" => LightingEffect.Off,
                "static" => LightingEffect.Static,
                "spectrum" => LightingEffect.Spectrum,
                "reactive" => LightingEffect.Reactive,
                _ => variant switch { "two" => LightingEffect.BreathingDual, "random" => LightingEffect.BreathingRandom, _ => LightingEffect.Breathing },
            };
            if (c1 is not null) P.Color1 = c1;
            if (c2 is not null) P.Color2 = c2;
            if (brightness is not null) P.Brightness = brightness.Value;
            // El slider de velocidad del mouse solo existe para "reactivo" (duracion 1-4).
            if (speed is not null && _target == Target.Mouse) P.ReactiveSpeed = speed.Value;
        }
        if (toRam)
        {
            R.Effect = currentKey switch
            {
                "off" => RamEffect.Off,
                "static" => RamEffect.Static,
                "breath" => RamEffect.Breathing,
                "spectrum" => RamEffect.Spectrum,
                "rainbow" => RamEffect.Rainbow,
                "dynamic" => RamEffect.Dynamic,
                "comet" => RamEffect.Comet,
                "flame" => RamEffect.Flame,
                "twilight" => RamEffect.Twilight,
                _ => R.Effect,
            };
            R.TwoColors = variant == "two";
            if (c1 is not null) R.Color1 = c1;
            if (c2 is not null) R.Color2 = c2;
            if (brightness is not null) R.Brightness = brightness.Value;
            if (speed is not null && _target != Target.Mouse) R.Speed = speed.Value;
        }

        UpdateView();
        Commit(async () =>
        {
            var tasks = new List<Task>();
            if (toMouse) tasks.Add(Device.ApplyLightingAsync());
            if (toRam) tasks.Add(RamSvc.ApplyAsync());
            await Task.WhenAll(tasks);
        });
    }

    // ---------- Construccion ----------

    void BuildEffects()
    {
        Effects.Children.Clear();
        var key = Current().Key;
        foreach (var fx in Available)
        {
            var card = new RadioButton
            {
                Style = (Style)FindResource("OptionCard"),
                GroupName = "fx",
                Tag = fx.Glyph,
                Content = fx.Label,
                IsChecked = fx.Key == key,
            };
            card.Checked += (_, _) => Write(key: fx.Key);
            Effects.Children.Add(card);
        }
        EffectsHint.Text = _target switch
        {
            Target.Together => RamSvc.Available
                ? "Solo los efectos que tienen el mouse y la RAM. Para el resto, usa Por separado."
                : "La RAM no está disponible: por ahora se aplica solo al mouse.",
            Target.Ram => "Se aplica a todos los módulos a la vez, sincronizados.",
            _ => "Logo Chroma del Razer Cobra.",
        };
    }

    void BuildDevices()
    {
        Devices.Children.Clear();
        Devices.Children.Add(Chip("", "Razer Cobra", Device.Connected ? "Conectado" : "Desconectado", Device.Connected ? "Ok" : "Ink300"));

        var ramOk = RamSvc.Available;
        var ram = Chip("", "Memoria RAM", RamSvc.Status, ramOk ? "Ok" : "Warn");
        if (RamSvc.State == SmbusState.NeedsAdmin)
        {
            var activate = new Button { Style = (Style)FindResource("Outline"), Content = "Activar", Height = 30, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(10, 0, 0, 0) };
            activate.Click += (_, _) => App.Current.RelaunchElevated();
            ((DockPanel)ram.Child).Children.Insert(0, activate);
            DockPanel.SetDock(activate, Dock.Right);
        }
        Devices.Children.Add(ram);

        var gpu = Chip("", "Gigabyte RX 9060 XT", "No disponible", "Ink300");
        gpu.ToolTip = "El driver actual de AMD rechaza los comandos de luz de la gráfica. Gigabyte Control Center tampoco puede: " +
                      "sus registros muestran el mismo error (GvWriteI2C fail). Se puede volver a probar tras actualizar el driver.";
        gpu.Opacity = 0.7;
        Devices.Children.Add(gpu);
    }

    Border Chip(string glyph, string name, string status, string dot)
    {
        var dock = new DockPanel();
        var icon = new Grid { Width = 32, Height = 32, Margin = new Thickness(0, 0, 10, 0) };
        icon.Children.Add(new System.Windows.Shapes.Ellipse { Fill = Res("AccentSoft") });
        icon.Children.Add(new TextBlock { Style = (Style)FindResource("Icon"), Text = glyph, FontSize = 14, Foreground = Res("Accent600") });
        icon.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 9, Height = 9, Fill = Res(dot), Stroke = Res("Bg"), StrokeThickness = 2,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
        });
        DockPanel.SetDock(icon, Dock.Left);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = name, FontSize = 12.5, FontWeight = FontWeights.Bold, Foreground = Res("Ink800") });
        texts.Children.Add(new TextBlock { Text = status, FontSize = 11.5, Foreground = Res("Ink400") });
        dock.Children.Add(icon);
        dock.Children.Add(texts);
        return new Border
        {
            Child = dock,
            Padding = new Thickness(10, 8, 14, 8),
            Margin = new Thickness(0, 0, 10, 10),
            CornerRadius = new CornerRadius(14),
            Background = Res("PillHover"),
            BorderBrush = Res("GlassBorder"),
            BorderThickness = new Thickness(1),
        };
    }

    // ---------- Eventos ----------

    void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (Loading) return;
        Settings.LightingTogether = ModeTogether.IsChecked == true;
        App.Current.SaveSoon();
        _target = Settings.LightingTogether ? Target.Together : TargetRam.IsChecked == true && RamSvc.Available ? Target.Ram : Target.Mouse;
        if (Settings.LightingTogether && RamSvc.Available)
        {
            // Al volver a "juntos", la RAM se iguala al mouse en el acto.
            Settings.SyncRamToMouse();
            _ = RamSvc.ApplyAsync();
        }
        Reload();
    }

    void Target_Checked(object sender, RoutedEventArgs e)
    {
        if (Loading) return;
        _target = sender == TargetRam ? Target.Ram : Target.Mouse;
        Reload();
    }

    void Breath_Checked(object sender, RoutedEventArgs e) =>
        Write(key: "breath", breathVariant: sender == BrTwo ? "two" : sender == BrRandom ? "random" : "one");

    void Speed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => Write(speed: (int)e.NewValue);

    void Brightness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        BrightnessValue.Text = $"{(int)e.NewValue} %";
        Write(brightness: (int)e.NewValue);
    }

    // ---------- Vista ----------

    void UpdateView()
    {
        var (key, c1, c2, brightness) = Current();
        var fx = Available.FirstOrDefault(f => f.Key == key) ?? Available[0];
        var wasLoading = Loading;
        Loading = true;

        // Respiracion: el aleatorio solo lo tiene el mouse.
        BreathPanel.Visibility = key == "breath" ? Visibility.Visible : Visibility.Collapsed;
        BrRandom.Visibility = _target == Target.Mouse ? Visibility.Visible : Visibility.Collapsed;
        var two = TwoColorBreath;
        BrOne.IsChecked = !two && !(P.Effect == LightingEffect.BreathingRandom && _target == Target.Mouse);
        BrTwo.IsChecked = two;
        BrRandom.IsChecked = _target == Target.Mouse && P.Effect == LightingEffect.BreathingRandom;
        var random = BrRandom.IsChecked == true;

        // Velocidad: en el mouse solo existe la duracion del reactivo; en juntos, la de la RAM.
        var showSpeed = fx.Speed && (_target != Target.Together || RamSvc.Available) && !(key == "breath" && _target == Target.Mouse);
        SpeedPanel.Visibility = showSpeed ? Visibility.Visible : Visibility.Collapsed;
        if (_target == Target.Mouse)
        {
            SpeedLabel.Text = "DURACIÓN DEL BRILLO AL HACER CLIC";
            SpeedMin.Text = "Corta";
            SpeedMax.Text = "Larga";
            Speed.Maximum = 4;
            Speed.Value = P.ReactiveSpeed;
        }
        else
        {
            SpeedLabel.Text = _target == Target.Together ? "VELOCIDAD DE LA RAM" : "VELOCIDAD";
            SpeedMin.Text = "Lenta";
            SpeedMax.Text = "Rápida";
            Speed.Maximum = 5;
            Speed.Value = R.Speed;
        }

        var colors = random ? 0 : key == "breath" && two ? 2 : fx.Colors;
        ColorCard.Visibility = colors > 0 ? Visibility.Visible : Visibility.Collapsed;
        Color2Panel.Visibility = colors > 1 ? Visibility.Visible : Visibility.Collapsed;
        Loading = wasLoading;

        // Vista previa
        MouseBox.Visibility = _target == Target.Ram ? Visibility.Collapsed : Visibility.Visible;
        RamBox.Visibility = _target == Target.Mouse || !RamSvc.Available ? Visibility.Collapsed : Visibility.Visible;
        MouseBox.Height = _target == Target.Together && RamSvc.Available ? 250 : 320;
        RamBox.Height = _target == Target.Together ? 180 : 260;
        var col1 = ColorPicker.Parse(c1);
        var col2 = ColorPicker.Parse(c2);
        if (MouseBox.Visibility == Visibility.Visible) Preview.ShowEffect(P.Effect, col1, col2, P.Brightness);
        if (RamBox.Visibility == Visibility.Visible)
            RamPreview.ShowEffect(key == "breath" && two ? "dynamic" : key, col1, col2, _target == Target.Mouse ? P.Brightness : R.Brightness);
        PreviewLabel.Text = fx.Label.ToUpperInvariant();
    }
}
