using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Colmillo.Core;
using Colmillo.Core.Gpu;
using Colmillo.Core.Hid;
using Colmillo.Core.Ram;
using Colmillo.Core.Razer;
using Colmillo.UI.Controls;

namespace Colmillo.UI.Pages;

/// <summary>
/// Iluminacion de mouse, RAM y grafica. "Todos juntos" ofrece solo los efectos que tienen
/// todos (apagado, estatico, respiracion, espectro) y escribe en el perfil de cada uno, asi
/// al pasar a "Por separado" cada dispositivo sigue donde quedo.
/// </summary>
public partial class LightingPage : PageBase
{
    enum Target { Together, Mouse, Ram, Gpu }

    sealed record Fx(string Key, string Label, string Glyph, int Colors = 0, bool Speed = false);

    // Comunes a mouse, RAM y grafica.
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

    // La grafica no tiene velocidad en estatico ni dos colores en respiracion.
    static readonly Fx[] GpuFx =
    [
        .. TogetherFx,
        new("flash", "Destello", "", Colors: 1, Speed: true),
        new("dflash", "Doble destello", "", Colors: 1, Speed: true),
    ];

    Target _target;

    static RamService RamSvc => App.Current.Ram;
    static RamProfile R => App.Current.Settings.Ram;
    static GpuService GpuSvc => App.Current.Gpu;
    static GpuProfile G => App.Current.Settings.Gpu;

    public LightingPage()
    {
        InitializeComponent();
        Color1.ColorChanged += hex =>
        {
            // Con paleta, el selector edita el color elegido de la paleta.
            if (PaletteActive)
            {
                R.Palette[_paletteIndex] = hex;
                BuildPalette();
                Write();
            }
            else Write(c1: hex);
        };
        Color2.ColorChanged += hex => Write(c2: hex);
        _target = Settings.LightingTogether ? Target.Together : Target.Mouse;
        Reload();
    }

    public override void Reload()
    {
        Loading = true;
        if (!RamSvc.Available && _target == Target.Ram || !GpuSvc.Available && _target == Target.Gpu) _target = Target.Mouse;
        ModeTogether.IsChecked = _target == Target.Together;
        ModeSeparate.IsChecked = _target != Target.Together;
        TargetMouse.IsChecked = _target == Target.Mouse;
        TargetRam.IsChecked = _target == Target.Ram;
        TargetGpu.IsChecked = _target == Target.Gpu;
        var separate = _target != Target.Together;
        TargetDivider.Visibility = TargetMouse.Visibility = TargetRam.Visibility = TargetGpu.Visibility =
            separate ? Visibility.Visible : Visibility.Collapsed;
        TargetRam.IsEnabled = RamSvc.Available;
        TargetGpu.IsEnabled = GpuSvc.Available;

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

    static string GpuKey(GpuEffect e) => e switch
    {
        GpuEffect.Off => "off",
        GpuEffect.Pulse => "breath",
        GpuEffect.Flash => "flash",
        GpuEffect.DoubleFlash => "dflash",
        GpuEffect.Cycle => "spectrum",
        _ => "static",
    };

    (string Key, string C1, string C2, int Brightness) Current() => _target switch
    {
        Target.Ram => (RamKey(R.Effect), R.Color1, R.Color2, R.Brightness),
        Target.Gpu => (GpuKey(G.Effect), G.Color, G.Color, G.Brightness),
        // Juntos: manda el mouse; si tenia un efecto que no es comun, se muestra estatico.
        Target.Together => (TogetherFx.Any(f => f.Key == MouseKey(P.Effect)) ? MouseKey(P.Effect) : "static", P.Color1, P.Color2, P.Brightness),
        _ => (MouseKey(P.Effect), P.Color1, P.Color2, P.Brightness),
    };

    Fx[] Available => _target switch { Target.Ram => RamFx, Target.Mouse => MouseFx, Target.Gpu => GpuFx, _ => TogetherFx };

    bool TwoColorBreath => _target switch
    {
        Target.Ram => R.TwoColors,
        Target.Gpu => false,
        _ => P.Effect == LightingEffect.BreathingDual,
    };

    // ---------- Escritura ----------

    void Write(string? key = null, string? c1 = null, string? c2 = null, int? brightness = null, int? speed = null, string? breathVariant = null)
    {
        if (Loading) return;
        var toMouse = _target is Target.Mouse or Target.Together;
        var toRam = (_target is Target.Ram or Target.Together) && RamSvc.Available;
        var toGpu = (_target is Target.Gpu or Target.Together) && GpuSvc.Available;
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
        if (toGpu)
        {
            G.Effect = currentKey switch
            {
                "off" => GpuEffect.Off,
                "static" => GpuEffect.Static,
                "breath" => GpuEffect.Pulse,
                "spectrum" => GpuEffect.Cycle,
                "flash" => GpuEffect.Flash,
                "dflash" => GpuEffect.DoubleFlash,
                _ => G.Effect,
            };
            if (c1 is not null) G.Color = c1;
            if (brightness is not null) G.Brightness = brightness.Value;
            if (speed is not null && _target != Target.Mouse) G.Speed = speed.Value;
        }

        UpdateView();
        Commit(async () =>
        {
            var tasks = new List<Task>();
            if (toMouse) tasks.Add(Device.ApplyLightingAsync());
            if (toRam) tasks.Add(RamSvc.ApplyAsync());
            if (toGpu) tasks.Add(GpuSvc.ApplyAsync());
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
            Target.Together => (RamSvc.Available, GpuSvc.Available) switch
            {
                (true, true) => "Solo los efectos que tienen el mouse, la RAM y la gráfica. Para el resto, usa Por separado.",
                (true, false) => "Solo los efectos que tienen el mouse y la RAM. Para el resto, usa Por separado.",
                (false, true) => "Solo los efectos que tienen el mouse y la gráfica. Para el resto, usa Por separado.",
                _ => "La RAM y la gráfica no están disponibles: por ahora se aplica solo al mouse.",
            },
            Target.Ram => "Se aplica a todos los módulos a la vez, sincronizados.",
            Target.Gpu => "Franja RGB Fusion de la gráfica. Se guarda en la tarjeta unos segundos después del último cambio.",
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

        var gpu = Chip("", "Gráfica Gigabyte", GpuSvc.Status, GpuSvc.Available ? "Ok" : "Ink300");
        if (!GpuSvc.Available) gpu.Opacity = 0.7;
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

    // ---------- Paleta de la RAM ----------

    int _paletteIndex;

    bool PaletteSupported => _target == Target.Ram && Current().Key is "breath" or "dynamic" or "comet";
    bool PaletteActive => PaletteSupported && R.UsePalette;

    void BuildPalette()
    {
        PaletteChips.Children.Clear();
        if (R.Palette.Count == 0) R.Palette.Add(R.Color1);
        _paletteIndex = Math.Clamp(_paletteIndex, 0, R.Palette.Count - 1);
        for (var i = 0; i < R.Palette.Count; i++)
        {
            var index = i;
            var selected = i == _paletteIndex;
            var chip = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(17),
                Margin = new Thickness(0, 0, 8, 8),
                Background = new SolidColorBrush(ColorPicker.Parse(R.Palette[i])),
                BorderBrush = selected ? Res("Accent500") : new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
                BorderThickness = new Thickness(selected ? 3 : 1),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = $"Color {i + 1}",
            };
            chip.MouseLeftButtonUp += (_, _) =>
            {
                _paletteIndex = index;
                Loading = true;
                Color1.Set(R.Palette[index]);
                Loading = false;
                BuildPalette();
            };
            PaletteChips.Children.Add(chip);
        }
        var add = new Button { Style = (Style)FindResource("IconButton"), Content = "", ToolTip = "Agregar color", IsEnabled = R.Palette.Count < 10 };
        add.Click += (_, _) =>
        {
            R.Palette.Add(R.Palette[_paletteIndex]);
            _paletteIndex = R.Palette.Count - 1;
            BuildPalette();
            Write();
        };
        var remove = new Button { Style = (Style)FindResource("IconButton"), Content = "", ToolTip = "Quitar el color elegido", IsEnabled = R.Palette.Count > 1 };
        remove.Click += (_, _) =>
        {
            R.Palette.RemoveAt(_paletteIndex);
            _paletteIndex = Math.Max(0, _paletteIndex - 1);
            Loading = true;
            Color1.Set(R.Palette[_paletteIndex]);
            Loading = false;
            BuildPalette();
            Write();
        };
        PaletteChips.Children.Add(add);
        PaletteChips.Children.Add(remove);
    }

    void UsePalette_Changed(object sender, RoutedEventArgs e)
    {
        if (Loading) return;
        R.UsePalette = UsePalette.IsChecked == true;
        _paletteIndex = 0;
        Loading = true;
        Color1.Set(R.UsePalette ? R.Palette[0] : R.Color1);
        Loading = false;
        Write();
    }

    // ---------- Eventos ----------

    void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (Loading) return;
        Settings.LightingTogether = ModeTogether.IsChecked == true;
        App.Current.SaveSoon();
        _target = Settings.LightingTogether ? Target.Together
            : TargetRam.IsChecked == true && RamSvc.Available ? Target.Ram
            : TargetGpu.IsChecked == true && GpuSvc.Available ? Target.Gpu
            : Target.Mouse;
        if (Settings.LightingTogether)
        {
            // Al volver a "juntos", la RAM y la grafica se igualan al mouse en el acto.
            Settings.SyncToMouse();
            if (RamSvc.Available) _ = RamSvc.ApplyAsync();
            if (GpuSvc.Available) _ = GpuSvc.ApplyAsync();
        }
        Reload();
    }

    void Target_Checked(object sender, RoutedEventArgs e)
    {
        if (Loading) return;
        _target = sender == TargetRam ? Target.Ram : sender == TargetGpu ? Target.Gpu : Target.Mouse;
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
        BreathPanel.Visibility = key == "breath" && _target != Target.Gpu ? Visibility.Visible : Visibility.Collapsed;
        BrRandom.Visibility = _target == Target.Mouse ? Visibility.Visible : Visibility.Collapsed;
        var two = TwoColorBreath;
        BrOne.IsChecked = !two && !(P.Effect == LightingEffect.BreathingRandom && _target == Target.Mouse);
        BrTwo.IsChecked = two;
        BrRandom.IsChecked = _target == Target.Mouse && P.Effect == LightingEffect.BreathingRandom;
        var random = BrRandom.IsChecked == true;

        // Velocidad: en el mouse solo existe la duracion del reactivo; en juntos, la de RAM y grafica.
        var showSpeed = fx.Speed && (_target != Target.Together || RamSvc.Available || GpuSvc.Available) && !(key == "breath" && _target == Target.Mouse);
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
            SpeedLabel.Text = _target != Target.Together ? "VELOCIDAD"
                : (RamSvc.Available, GpuSvc.Available) switch
                {
                    (true, true) => "VELOCIDAD DE LA RAM Y LA GRÁFICA",
                    (false, true) => "VELOCIDAD DE LA GRÁFICA",
                    _ => "VELOCIDAD DE LA RAM",
                };
            SpeedMin.Text = "Lenta";
            SpeedMax.Text = "Rápida";
            Speed.Maximum = 5;
            Speed.Value = _target == Target.Gpu || _target == Target.Together && !RamSvc.Available ? G.Speed : R.Speed;
        }

        var colors = random ? 0 : key == "breath" && two ? 2 : fx.Colors;
        ColorCard.Visibility = colors > 0 ? Visibility.Visible : Visibility.Collapsed;
        Color2Panel.Visibility = colors > 1 && !PaletteActive ? Visibility.Visible : Visibility.Collapsed;
        PalettePanel.Visibility = PaletteSupported ? Visibility.Visible : Visibility.Collapsed;
        UsePalette.IsChecked = R.UsePalette;
        PaletteChips.Visibility = PaletteActive ? Visibility.Visible : Visibility.Collapsed;
        // Con paleta, la respiracion de dos colores no aplica: manda la paleta.
        if (PaletteActive)
        {
            BreathPanel.Visibility = Visibility.Collapsed;
            BuildPalette();
            Color1.Set(R.Palette[Math.Clamp(_paletteIndex, 0, R.Palette.Count - 1)]);
        }
        Loading = wasLoading;

        // Vista previa
        var together = _target == Target.Together;
        MouseBox.Visibility = _target is Target.Mouse or Target.Together ? Visibility.Visible : Visibility.Collapsed;
        RamBox.Visibility = (_target == Target.Ram || together) && RamSvc.Available ? Visibility.Visible : Visibility.Collapsed;
        GpuBox.Visibility = (_target == Target.Gpu || together) && GpuSvc.Available ? Visibility.Visible : Visibility.Collapsed;
        var withGpu = together && GpuSvc.Available;
        MouseBox.Height = !together ? 320 : withGpu ? 210 : RamSvc.Available ? 250 : 320;
        RamBox.Height = together ? (withGpu ? 150 : 180) : 260;
        var col1 = ColorPicker.Parse(c1);
        var col2 = ColorPicker.Parse(c2);
        if (MouseBox.Visibility == Visibility.Visible) Preview.ShowEffect(P.Effect, col1, col2, P.Brightness);
        if (RamBox.Visibility == Visibility.Visible)
            RamPreview.ShowEffect(key == "breath" && two ? "dynamic" : key, col1, col2, R.Brightness);
        if (GpuBox.Visibility == Visibility.Visible) GpuPreview.ShowEffect(key, col1, G.Brightness);
        PreviewLabel.Text = fx.Label.ToUpperInvariant();
    }
}
