using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Colmillo.UI.Controls;

/// <summary>Grafica de tres ventiladores vista de frente, con la franja de luz arriba.</summary>
public sealed class GpuDrawing : Canvas
{
    readonly SolidColorBrush _light = new(Colors.Transparent);
    readonly Rectangle _strip;

    public GpuDrawing()
    {
        Width = 320;
        Height = 108;
        // Cubierta
        var body = new Rectangle
        {
            Width = 316,
            Height = 92,
            RadiusX = 10,
            RadiusY = 10,
            Fill = new LinearGradientBrush(Color.FromRgb(0x2E, 0x2F, 0x35), Color.FromRgb(0x16, 0x17, 0x1B), 90),
            Stroke = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
            StrokeThickness = 1,
        };
        SetLeft(body, 2);
        SetTop(body, 14);
        Children.Add(body);
        // Ventiladores
        for (var i = 0; i < 3; i++)
        {
            var cx = 58 + i * 102;
            var fan = new Ellipse
            {
                Width = 78,
                Height = 78,
                Fill = new RadialGradientBrush(Color.FromRgb(0x24, 0x25, 0x2A), Color.FromRgb(0x0E, 0x0F, 0x12)),
                Stroke = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                StrokeThickness = 1,
            };
            SetLeft(fan, cx - 39);
            SetTop(fan, 21);
            Children.Add(fan);
            var hub = new Ellipse { Width = 20, Height = 20, Fill = new SolidColorBrush(Color.FromRgb(0x3A, 0x3B, 0x42)) };
            SetLeft(hub, cx - 10);
            SetTop(hub, 50);
            Children.Add(hub);
        }
        // Franja de luz sobre el borde superior
        _strip = new Rectangle
        {
            Width = 220,
            Height = 6,
            RadiusX = 3,
            RadiusY = 3,
            Fill = _light,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = 14, Color = Colors.White, Opacity = 0.5 },
        };
        SetLeft(_strip, 50);
        SetTop(_strip, 4);
        Children.Add(_strip);
    }

    /// <summary>Efecto por clave (la misma que usa LightingPage).</summary>
    public void ShowEffect(string effect, Color color, double brightness)
    {
        var level = 0.15 + 0.85 * Math.Clamp(brightness / 100.0, 0, 1);
        _light.BeginAnimation(SolidColorBrush.ColorProperty, null);
        _strip.BeginAnimation(OpacityProperty, null);
        _strip.Opacity = level;
        _light.Color = color;
        switch (effect)
        {
            case "off":
                _light.Color = Color.FromRgb(0x30, 0x31, 0x36);
                _strip.Opacity = 1;
                break;
            case "breath":
                _strip.BeginAnimation(OpacityProperty, new DoubleAnimation(level, 0.05, TimeSpan.FromSeconds(1.75))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                });
                break;
            case "flash":
                Blink(level, [0.0, 0.15], 1.2);
                break;
            case "dflash":
                Blink(level, [0.0, 0.12, 0.24, 0.36], 1.4);
                break;
            case "spectrum":
                var anim = new ColorAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(6), RepeatBehavior = RepeatBehavior.Forever };
                for (var i = 0; i <= 6; i++)
                    anim.KeyFrames.Add(new LinearColorKeyFrame(ColorPicker.FromHue(i * 60), KeyTime.FromPercent(i / 6.0)));
                _light.BeginAnimation(SolidColorBrush.ColorProperty, anim);
                break;
        }
    }

    /// <summary>Destellos: prendido y apagado alternando en los segundos dados, despues apagado.</summary>
    void Blink(double level, double[] toggles, double period)
    {
        var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(period), RepeatBehavior = RepeatBehavior.Forever };
        for (var i = 0; i < toggles.Length; i++)
            anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(i % 2 == 0 ? level : 0.05, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(toggles[i]))));
        _strip.BeginAnimation(OpacityProperty, anim);
    }
}
