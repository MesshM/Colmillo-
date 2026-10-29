using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Colmillo.Core.Razer;

namespace Colmillo.UI.Controls;

/// <summary>
/// Vista superior del Cobra dibujada con geometria propia (no se usa el SVG de Razer).
/// Muestra el brillo del logo con el efecto elegido y, opcionalmente, marcadores
/// sobre cada boton.
/// </summary>
public sealed class MouseDrawing : Canvas
{
    // Silueta simetrica en un lienzo de 200x320.
    const string Body = "M100,8 C150,8 178,40 182,104 C186,170 184,236 168,280 C156,308 132,316 100,316 " +
                        "C68,316 44,308 32,280 C16,236 14,170 18,104 C22,40 50,8 100,8 Z";
    const string Split = "M100,10 L100,118";
    const string Wheel = "M100,46 m-9,0 a9,9 0 0 1 18,0 v34 a9,9 0 0 1 -18,0 Z";

    readonly Ellipse _glow;
    readonly Path _logo;
    readonly SolidColorBrush _glowBrush = new(Colors.Transparent);
    readonly SolidColorBrush _logoBrush = new(Colors.Transparent);

    public MouseDrawing()
    {
        Width = 200;
        Height = 320;
        SnapsToDevicePixels = true;

        // Halo de luz por debajo (underglow del logo).
        _glow = new Ellipse { Width = 170, Height = 170, Fill = _glowBrush, Opacity = 0.55 };
        _glow.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 46 };
        SetLeft(_glow, 15);
        SetTop(_glow, 150);
        Children.Add(_glow);

        var shell = new Path
        {
            Data = Geometry.Parse(Body),
            Stroke = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
            StrokeThickness = 1.2,
            Fill = new LinearGradientBrush(Color.FromRgb(0x3A, 0x3B, 0x42), Color.FromRgb(0x17, 0x18, 0x1C), 90),
        };
        Children.Add(shell);

        Children.Add(new Path
        {
            Data = Geometry.Parse(Split),
            Stroke = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)),
            StrokeThickness = 2,
        });
        Children.Add(new Path
        {
            Data = Geometry.Parse(Wheel),
            Fill = new SolidColorBrush(Color.FromRgb(0x0E, 0x0F, 0x12)),
            Stroke = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
            StrokeThickness = 1,
        });

        // Logo: tres trazos tipo "garra", iluminado.
        _logo = new Path
        {
            Data = Geometry.Parse("M86,214 C90,196 96,186 104,180 M96,222 C100,204 106,194 114,188 M106,230 C110,212 116,202 124,196"),
            Stroke = _logoBrush,
            StrokeThickness = 3.2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        _logo.Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = 14, Color = Colors.White, Opacity = 0.6 };
        SetLeft(_logo, -5);
        Children.Add(_logo);
    }

    /// <summary>Anima el logo segun el efecto. Las animaciones mueren con el control.</summary>
    public void ShowEffect(LightingEffect effect, Color c1, Color c2, double brightness)
    {
        _glowBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        _logoBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        _logo.BeginAnimation(OpacityProperty, null);
        _glow.BeginAnimation(OpacityProperty, null);

        var level = Math.Clamp(brightness / 100.0, 0, 1);
        _logo.Opacity = 0.15 + 0.85 * level;
        _glow.Opacity = 0.6 * level;

        switch (effect)
        {
            case LightingEffect.Off:
                _logoBrush.Color = Color.FromRgb(0x30, 0x31, 0x36);
                _glowBrush.Color = Colors.Transparent;
                break;
            case LightingEffect.Static:
            case LightingEffect.Reactive:
                _logoBrush.Color = c1;
                _glowBrush.Color = c1;
                if (effect == LightingEffect.Reactive) Pulse(0.15 + 0.85 * level, 0.1, 0.9);
                break;
            case LightingEffect.Breathing:
            case LightingEffect.BreathingDual:
            case LightingEffect.BreathingRandom:
                _logoBrush.Color = c1;
                _glowBrush.Color = c1;
                if (effect != LightingEffect.Breathing)
                {
                    var b = effect == LightingEffect.BreathingDual ? c2 : ColorPicker.FromHue(200);
                    var cycle = new ColorAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(7), RepeatBehavior = RepeatBehavior.Forever };
                    cycle.KeyFrames.Add(new DiscreteColorKeyFrame(c1, KeyTime.FromPercent(0)));
                    cycle.KeyFrames.Add(new DiscreteColorKeyFrame(b, KeyTime.FromPercent(0.5)));
                    _logoBrush.BeginAnimation(SolidColorBrush.ColorProperty, cycle);
                    _glowBrush.BeginAnimation(SolidColorBrush.ColorProperty, cycle);
                }
                Pulse(0.15 + 0.85 * level, 0.05, 3.5);
                break;
            case LightingEffect.Spectrum:
                var spectrum = new ColorAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(6), RepeatBehavior = RepeatBehavior.Forever };
                for (var i = 0; i <= 6; i++)
                    spectrum.KeyFrames.Add(new LinearColorKeyFrame(ColorPicker.FromHue(i * 60), KeyTime.FromPercent(i / 6.0)));
                _logoBrush.BeginAnimation(SolidColorBrush.ColorProperty, spectrum);
                _glowBrush.BeginAnimation(SolidColorBrush.ColorProperty, spectrum);
                break;
        }
    }

    void Pulse(double high, double low, double seconds)
    {
        var anim = new DoubleAnimation(high, low, TimeSpan.FromSeconds(seconds / 2))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        _logo.BeginAnimation(OpacityProperty, anim);
        _glow.BeginAnimation(OpacityProperty, anim);
    }
}
