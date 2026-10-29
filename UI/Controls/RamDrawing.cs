using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Colmillo.UI.Controls;

/// <summary>Dos modulos de RAM con su barra de luz, animada segun el efecto.</summary>
public sealed class RamDrawing : Canvas
{
    const int Segments = 10; // LEDs por modulo en FURY Beast DDR4
    readonly List<SolidColorBrush> _leds = [];
    readonly List<Rectangle> _ledShapes = [];

    // XAML necesita un constructor sin parametros (los opcionales no cuentan).
    public RamDrawing() : this(2) { }

    public RamDrawing(int modules)
    {
        Width = 70 * modules + 10;
        Height = 230;
        for (var m = 0; m < modules; m++)
        {
            var x = 10 + m * 70;
            // PCB + disipador
            var stick = new Rectangle
            {
                Width = 46,
                Height = 200,
                RadiusX = 6,
                RadiusY = 6,
                Fill = new LinearGradientBrush(Color.FromRgb(0x2E, 0x2F, 0x35), Color.FromRgb(0x16, 0x17, 0x1B), 0),
                Stroke = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                StrokeThickness = 1,
            };
            SetLeft(stick, x);
            SetTop(stick, 24);
            Children.Add(stick);
            // Contactos dorados abajo
            var pins = new Rectangle { Width = 40, Height = 8, Fill = new SolidColorBrush(Color.FromRgb(0xB8, 0x95, 0x4A)), RadiusX = 1, RadiusY = 1 };
            SetLeft(pins, x + 3);
            SetTop(pins, 220);
            Children.Add(pins);
            // Barra de luz arriba, 10 segmentos
            for (var i = 0; i < Segments; i++)
            {
                var brush = new SolidColorBrush(Colors.Transparent);
                var led = new Rectangle { Width = 4.2, Height = 22, Fill = brush, RadiusX = 1.5, RadiusY = 1.5 };
                led.Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 0, BlurRadius = 10, Color = Colors.White, Opacity = 0.45 };
                SetLeft(led, x + 1 + i * 4.5);
                SetTop(led, 2);
                Children.Add(led);
                _leds.Add(brush);
                _ledShapes.Add(led);
            }
        }
    }

    /// <summary>Efecto por clave (la misma que usa LightingPage).</summary>
    public void ShowEffect(string effect, Color c1, Color c2, double brightness)
    {
        var level = Math.Clamp(brightness / 100.0, 0, 1);
        for (var i = 0; i < _leds.Count; i++)
        {
            var brush = _leds[i];
            var shape = _ledShapes[i];
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            shape.BeginAnimation(OpacityProperty, null);
            shape.Opacity = 0.15 + 0.85 * level;
            var pos = i % Segments;

            switch (effect)
            {
                case "off":
                    brush.Color = Color.FromRgb(0x30, 0x31, 0x36);
                    shape.Opacity = 1;
                    break;
                case "static":
                    brush.Color = c1;
                    break;
                case "breath":
                    brush.Color = c1;
                    Pulse(shape, 0.15 + 0.85 * level, 3.5, 0);
                    break;
                case "dynamic":
                    Cycle(brush, [c1, c2], 3, 0);
                    break;
                case "comet":
                    brush.Color = c1;
                    Pulse(shape, 0.15 + 0.85 * level, 1.2, pos * 0.08);
                    break;
                case "flame":
                    Cycle(brush, [Color.FromRgb(255, 60, 0), Color.FromRgb(255, 140, 0), Color.FromRgb(255, 30, 0)], 0.9, pos * 0.07);
                    break;
                case "twilight":
                    Cycle(brush, [Color.FromRgb(120, 60, 255), Color.FromRgb(255, 80, 160), Color.FromRgb(40, 120, 255)], 6, pos * 0.2);
                    break;
                case "rainbow":
                    Rainbow(brush, 4, pos * 0.15);
                    break;
                default: // spectrum: todo el modulo del mismo color
                    Rainbow(brush, 6, 0);
                    break;
            }
        }
    }

    static void Pulse(Rectangle shape, double high, double seconds, double delay)
    {
        shape.BeginAnimation(OpacityProperty, new DoubleAnimation(high, 0.05, TimeSpan.FromSeconds(seconds / 2))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromSeconds(delay),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    static void Cycle(SolidColorBrush brush, Color[] colors, double seconds, double delay)
    {
        brush.Color = colors[0];
        var anim = new ColorAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(seconds),
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromSeconds(delay),
        };
        for (var i = 0; i <= colors.Length; i++)
            anim.KeyFrames.Add(new LinearColorKeyFrame(colors[i % colors.Length], KeyTime.FromPercent((double)i / colors.Length)));
        brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
    }

    static void Rainbow(SolidColorBrush brush, double seconds, double delay)
    {
        var anim = new ColorAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(seconds),
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromSeconds(delay),
        };
        for (var i = 0; i <= 6; i++)
            anim.KeyFrames.Add(new LinearColorKeyFrame(ColorPicker.FromHue(i * 60), KeyTime.FromPercent(i / 6.0)));
        brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
    }
}
