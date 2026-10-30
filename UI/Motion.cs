using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Colmillo.UI;

/// <summary>
/// Animaciones cortas de la interfaz: entrar una pagina, abrir una capsula, abrir y cerrar
/// la ventana. Respetan "Mostrar animaciones en Windows" (Accesibilidad > Efectos visuales).
/// </summary>
public static class Motion
{
    static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };
    static readonly Duration Fast = TimeSpan.FromMilliseconds(220);

    static bool Enabled => SystemParameters.ClientAreaAnimation;

    /// <summary>Aparece desde transparente, subiendo <paramref name="offset"/> px o con un leve zoom.</summary>
    public static void Appear(FrameworkElement el, double offset = 0, bool scale = false)
    {
        if (!Enabled)
        {
            el.Opacity = 1;
            return;
        }
        el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Fast) { EasingFunction = Ease });
        if (offset != 0)
        {
            var move = new TranslateTransform(0, offset);
            el.RenderTransform = move;
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset, 0, Fast) { EasingFunction = Ease });
        }
        if (scale)
        {
            var zoom = new ScaleTransform(0.98, 0.98);
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            el.RenderTransform = zoom;
            var a = new DoubleAnimation(0.98, 1, Fast) { EasingFunction = Ease };
            zoom.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            zoom.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
    }

    /// <summary>Se desvanece con un leve zoom hacia adentro y despues llama a <paramref name="done"/>.</summary>
    public static void Disappear(FrameworkElement el, Action done)
    {
        if (!Enabled)
        {
            done();
            return;
        }
        var t = TimeSpan.FromMilliseconds(150);
        var fade = new DoubleAnimation(1, 0, t) { EasingFunction = Ease };
        fade.Completed += (_, _) => done();
        var zoom = new ScaleTransform(1, 1);
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = zoom;
        var shrink = new DoubleAnimation(1, 0.98, t) { EasingFunction = Ease };
        zoom.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        zoom.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        el.BeginAnimation(UIElement.OpacityProperty, fade);
    }
}
