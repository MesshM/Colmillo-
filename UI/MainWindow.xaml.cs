using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Colmillo.Core;
using Colmillo.Shell;
using Colmillo.UI.Pages;

namespace Colmillo.UI;

public partial class MainWindow : Window
{
    readonly DispatcherTimer _synapseTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    Action? _bannerAction;

    static DeviceService Device => App.Current.Device;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => SystemIntegration.RoundCorners(new WindowInteropHelper(this).Handle);
        StateChanged += (_, _) => FitMaximized();
        Device.StateChanged += OnDeviceChanged;
        Closed += (_, _) =>
        {
            Device.StateChanged -= OnDeviceChanged;
            _synapseTimer.Stop();
        };
        // Synapse se puede abrir o cerrar en cualquier momento; se revisa solo con la ventana visible.
        _synapseTimer.Tick += (_, _) => UpdateBanner();
        _synapseTimer.Start();

        NavPerformance.IsChecked = true;
        UpdateStatus();
        UpdateBanner();
    }

    void OnDeviceChanged() => Dispatcher.BeginInvoke(() =>
    {
        UpdateStatus();
        UpdateBanner();
        Refresh();
    });

    /// <summary>Recarga la pagina actual (p. ej. tras cambiar la etapa de DPI desde la bandeja).</summary>
    public void Refresh()
    {
        if (Page.Content is IPage page) page.Reload();
    }

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        // Cada pagina se crea al entrar y se suelta al salir: lo que no se ve no ocupa memoria.
        Page.Content = (string)((RadioButton)sender).Content switch
        {
            "Rendimiento" => new PerformancePage(),
            "Iluminación" => new LightingPage(),
            "Botones" => new ButtonsPage(),
            "Macros" => new MacrosPage(),
            "Puntero de Windows" => new PointerPage(),
            _ => new SettingsPage(),
        };
        Scroller.ScrollToTop();
    }

    void UpdateStatus()
    {
        var connected = Device.Connected;
        StatusDot.Fill = (Brush)FindResource(connected ? "Ok" : "Ink300");
        StatusText.Text = connected ? $"Conectado · fw {Device.Firmware}" : "Desconectado";
    }

    void UpdateBanner()
    {
        if (DeviceService.SynapseRunning())
        {
            ShowBanner("Razer Synapse está abierto. Vuelve a poner su iluminación y su modo encima de lo que hagas acá.",
                "Cerrar Synapse", () =>
                {
                    DeviceService.CloseSynapse();
                    _ = Device.ConnectAndApplyAsync();
                });
        }
        else if (!Device.Connected)
        {
            ShowBanner(Device.LastError ?? "Buscando el Razer Cobra...", "Reintentar", () => _ = Device.ConnectAndApplyAsync());
        }
        else
        {
            Banner.Visibility = Visibility.Collapsed;
        }
    }

    void ShowBanner(string text, string action, Action onClick)
    {
        BannerText.Text = text;
        BannerAction.Content = action;
        _bannerAction = onClick;
        Banner.Visibility = Visibility.Visible;
    }

    void BannerAction_Click(object sender, RoutedEventArgs e)
    {
        _bannerAction?.Invoke();
        Dispatcher.BeginInvoke(UpdateBanner, DispatcherPriority.Background);
    }

    // ---------- Dialogo de confirmacion ----------

    TaskCompletionSource<bool>? _dialog;

    /// <summary>Pide confirmacion con el dialogo propio de la app. Devuelve true si se acepta.</summary>
    public Task<bool> ConfirmAsync(string title, string message, string accept, bool danger = true)
    {
        _dialog?.TrySetResult(false);
        _dialog = new TaskCompletionSource<bool>();
        DialogTitle.Text = title;
        DialogText.Text = message;
        DialogOk.Content = accept;
        DialogOk.Style = (Style)FindResource(danger ? "DangerButton" : "Cta");
        DialogIcon.Text = danger ? "" : "";
        DialogIcon.Foreground = (Brush)FindResource(danger ? "Danger" : "Accent600");
        DialogIconBg.Fill = (Brush)FindResource(danger ? "DangerBg" : "AccentSoft");

        Dialog.Visibility = Visibility.Visible;
        var ease = new System.Windows.Media.Animation.CubicEase();
        var t = TimeSpan.FromMilliseconds(160);
        Dialog.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, t) { EasingFunction = ease });
        DialogScale.BeginAnimation(ScaleTransform.ScaleXProperty, new System.Windows.Media.Animation.DoubleAnimation(0.97, 1, t) { EasingFunction = ease });
        DialogScale.BeginAnimation(ScaleTransform.ScaleYProperty, new System.Windows.Media.Animation.DoubleAnimation(0.97, 1, t) { EasingFunction = ease });
        DialogOk.Focus();
        return _dialog.Task;
    }

    void CloseDialog(bool result)
    {
        Dialog.BeginAnimation(OpacityProperty, null);
        Dialog.Opacity = 0;
        Dialog.Visibility = Visibility.Collapsed;
        _dialog?.TrySetResult(result);
        _dialog = null;
    }

    void DialogOk_Click(object sender, RoutedEventArgs e) => CloseDialog(true);
    void DialogCancel_Click(object sender, RoutedEventArgs e) => CloseDialog(false);
    void DialogVeil_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => CloseDialog(false);

    /// <summary>
    /// Una ventana sin marco maximizada se pasa del monitor por el grosor del borde
    /// de redimension: se compensa con margen.
    /// </summary>
    void FitMaximized()
    {
        var max = WindowState == WindowState.Maximized;
        Root.Margin = max ? new Thickness(7) : new Thickness(0);
        MaxButton.Content = max ? "" : "";
    }

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}

/// <summary>Pagina que sabe volver a leer los ajustes.</summary>
public interface IPage
{
    void Reload();
}
