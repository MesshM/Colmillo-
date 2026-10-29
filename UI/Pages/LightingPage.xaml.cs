using System.Windows;
using Colmillo.Core.Razer;
using Colmillo.UI.Controls;

namespace Colmillo.UI.Pages;

public partial class LightingPage : PageBase
{
    public LightingPage()
    {
        InitializeComponent();
        Color1.ColorChanged += hex =>
        {
            P.Color1 = hex;
            Changed();
        };
        Color2.ColorChanged += hex =>
        {
            P.Color2 = hex;
            Changed();
        };
        Reload();
    }

    public override void Reload()
    {
        Loading = true;
        FxOff.IsChecked = P.Effect == LightingEffect.Off;
        FxStatic.IsChecked = P.Effect == LightingEffect.Static;
        FxBreath.IsChecked = P.Effect is LightingEffect.Breathing or LightingEffect.BreathingDual or LightingEffect.BreathingRandom;
        FxSpectrum.IsChecked = P.Effect == LightingEffect.Spectrum;
        FxReactive.IsChecked = P.Effect == LightingEffect.Reactive;
        BrOne.IsChecked = P.Effect is not (LightingEffect.BreathingDual or LightingEffect.BreathingRandom);
        BrTwo.IsChecked = P.Effect == LightingEffect.BreathingDual;
        BrRandom.IsChecked = P.Effect == LightingEffect.BreathingRandom;
        Color1.Set(P.Color1);
        Color2.Set(P.Color2);
        Speed.Value = P.ReactiveSpeed;
        Brightness.Value = P.Brightness;
        Loading = false;
        UpdateView();
    }

    void Effect_Checked(object sender, RoutedEventArgs e)
    {
        if (Loading) return;
        P.Effect = sender switch
        {
            _ when sender == FxOff => LightingEffect.Off,
            _ when sender == FxStatic => LightingEffect.Static,
            _ when sender == FxSpectrum => LightingEffect.Spectrum,
            _ when sender == FxReactive => LightingEffect.Reactive,
            _ => BreathVariant(),
        };
        Changed();
    }

    LightingEffect BreathVariant() =>
        BrTwo.IsChecked == true ? LightingEffect.BreathingDual
        : BrRandom.IsChecked == true ? LightingEffect.BreathingRandom
        : LightingEffect.Breathing;

    void Breath_Checked(object sender, RoutedEventArgs e)
    {
        if (Loading) return;
        P.Effect = BreathVariant();
        Changed();
    }

    void Speed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Loading) return;
        P.ReactiveSpeed = (int)e.NewValue;
        Changed();
    }

    void Brightness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        BrightnessValue.Text = $"{(int)e.NewValue} %";
        if (Loading) return;
        P.Brightness = (int)e.NewValue;
        Changed();
    }

    void Changed()
    {
        UpdateView();
        Commit(Device.ApplyLightingAsync);
    }

    void UpdateView()
    {
        var fx = P.Effect;
        var breathing = fx is LightingEffect.Breathing or LightingEffect.BreathingDual or LightingEffect.BreathingRandom;
        BreathPanel.Visibility = breathing ? Visibility.Visible : Visibility.Collapsed;
        SpeedPanel.Visibility = fx == LightingEffect.Reactive ? Visibility.Visible : Visibility.Collapsed;
        ColorCard.Visibility = fx is LightingEffect.Static or LightingEffect.Reactive or LightingEffect.Breathing or LightingEffect.BreathingDual
            ? Visibility.Visible : Visibility.Collapsed;
        Color2Panel.Visibility = fx == LightingEffect.BreathingDual ? Visibility.Visible : Visibility.Collapsed;

        Preview.ShowEffect(fx, ColorPicker.Parse(P.Color1), ColorPicker.Parse(P.Color2), P.Brightness);
        PreviewLabel.Text = fx switch
        {
            LightingEffect.Off => "LUZ APAGADA",
            LightingEffect.Static => "ESTÁTICO",
            LightingEffect.Spectrum => "CICLO DE ESPECTRO",
            LightingEffect.Reactive => "SE ENCIENDE AL HACER CLIC",
            _ => "RESPIRACIÓN",
        };
    }
}
