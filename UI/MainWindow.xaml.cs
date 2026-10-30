using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    static RamService Ram => App.Current.Ram;
    static GpuService Gpu => App.Current.Gpu;
    static AmdService Amd => App.Current.Amd;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SystemIntegration.RoundCorners(hwnd);
            // Sin barra de titulo Windows no anima minimizar, maximizar ni restaurar: se le
            // devuelven los estilos de una ventana normal (WindowChrome igual dibuja la nuestra).
            SystemIntegration.EnableWindowAnimations(hwnd);
        };
        // Al abrir: aparece con un leve zoom.
        Root.Opacity = 0;
        Loaded += (_, _) => Motion.Appear(Root, scale: true);
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

        NavLighting.IsChecked = true;
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
        UpdateStatus();
        if (Page.Content is IPage page) page.Reload();
    }

    /// <summary>Pagina abierta (para el registro del vigilante; se lee desde otro hilo).</summary>
    public string CurrentPage { get; private set; } = "";

    bool _ramLighting;

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        var item = (RadioButton)sender;
        // Cada pagina se crea al entrar y se suelta al salir: lo que no se ve no ocupa memoria.
        Page.Content = (string)item.Content switch
        {
            "Rendimiento" => new PerformancePage(),
            "Iluminación" => new LightingPage(ram: _ramLighting),
            "Botones" => new ButtonsPage(),
            "Macros" => new MacrosPage(),
            "Puntero de Windows" => new PointerPage(),
            "Ventiladores" => new FansPage(),
            "Overclock" => new OverclockPage(),
            "Métricas" => new MetricsPage(),
            _ => new SettingsPage(),
        };
        _ramLighting = false;
        CurrentPage = (string)item.Content;
        Scroller.ScrollToTop();
        if (Page.Content is FrameworkElement page) Motion.Appear(page, offset: 14);
        // Solo queda abierta la capsula del dispositivo de la pagina actual.
        Expand(MouseOptions.Children.Contains(item) ? MouseCapsule : GpuOptions.Children.Contains(item) ? GpuCapsule : null);
    }

    void Expand(ToggleButton? capsule)
    {
        MouseCapsule.IsChecked = capsule == MouseCapsule;
        GpuCapsule.IsChecked = capsule == GpuCapsule;
        RamCapsule.IsChecked = false;
        Reveal(MouseOptions, capsule == MouseCapsule);
        Reveal(GpuOptions, capsule == GpuCapsule);
        UpdateStatus();
    }

    void Capsule_Click(object sender, RoutedEventArgs e)
    {
        if (sender == RamCapsule)
        {
            // La RAM no tiene opciones propias: se abre su luz.
            _ramLighting = true;
            if (NavLighting.IsChecked == true) Nav_Checked(NavLighting, e);
            else NavLighting.IsChecked = true;
            RamCapsule.IsChecked = true;
            return;
        }
        var options = sender == MouseCapsule ? MouseOptions : GpuOptions;
        if (options.Visibility == Visibility.Visible && options.Children.OfType<RadioButton>().Any(r => r.IsChecked == true))
        {
            // Abierta y con una de sus paginas: se pliega sin cambiar de pagina.
            options.Visibility = Visibility.Collapsed;
            ((ToggleButton)sender).IsChecked = false;
            UpdateStatus();
            return;
        }
        var first = options.Children.OfType<RadioButton>().First();
        if (first.IsChecked == true) Expand((ToggleButton)sender);
        else first.IsChecked = true;
    }

    void UpdateStatus()
    {
        var connected = Device.Connected;
        MouseCapsule.Content = Capsule("", "Razer Cobra", connected ? $"Conectado · fw {Device.Firmware}" : "Desconectado",
            connected ? "Ok" : "Ink300", chevron: true, open: MouseOptions.Visibility == Visibility.Visible);

        var gpuName = Amd.Name is { Length: > 0 } n ? n.Replace("AMD Radeon ", "") : "Gráfica";
        var gpuStatus = (Gpu.Available, Amd.Available) switch
        {
            (true, true) => "Luz y ventiladores",
            (true, false) => "Luz RGB Fusion",
            (false, true) => "Ventiladores",
            _ when Gpu.Status == "Buscando…" || Amd.Status == "Buscando…" => "Buscando…",
            _ => "No encontrada",
        };
        GpuCapsule.Content = Capsule("", gpuName, gpuStatus, Gpu.Available || Amd.Available ? "Ok" : "Ink300",
            chevron: true, open: GpuOptions.Visibility == Visibility.Visible);

        RamCapsule.Content = Capsule("", "Memoria RAM", Ram.Available ? $"{Ram.Modules} módulo{(Ram.Modules == 1 ? "" : "s")} FURY" : Ram.Status, Ram.Available ? "Ok" : Ram.Status == "Buscando…" ? "Ink300" : "Warn",
            chevron: false, open: false);
    }

    FrameworkElement Capsule(string glyph, string name, string status, string dot, bool chevron, bool open)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new Grid { Width = 34, Height = 34 };
        icon.Children.Add(new System.Windows.Shapes.Ellipse { Fill = (Brush)FindResource("AccentSoft") });
        icon.Children.Add(new TextBlock { Style = (Style)FindResource("Icon"), Text = glyph, FontSize = 16, Foreground = (Brush)FindResource("Accent600") });
        icon.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 10, Height = 10, Fill = (Brush)FindResource(dot), Stroke = (Brush)FindResource("Bg"), StrokeThickness = 2,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
        });
        var texts = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = name, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("Ink800"), TextTrimming = TextTrimming.CharacterEllipsis });
        texts.Children.Add(new TextBlock { Text = status, FontSize = 11.5, Foreground = (Brush)FindResource("Ink400"), TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(texts, 1);
        grid.Children.Add(icon);
        grid.Children.Add(texts);
        if (chevron)
        {
            var arrow = new TextBlock
            {
                Style = (Style)FindResource("Icon"), Text = open ? "" : "", FontSize = 10,
                Foreground = (Brush)FindResource("Ink400"), Margin = new Thickness(6, 0, 2, 0),
            };
            Grid.SetColumn(arrow, 2);
            grid.Children.Add(arrow);
        }
        return grid;
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

    static void Reveal(FrameworkElement options, bool open)
    {
        var was = options.Visibility == Visibility.Visible;
        options.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (open && !was) Motion.Appear(options, offset: -6);
    }

    bool _closing;

    /// <summary>Se desvanece y despues se cierra (a la bandeja o del todo).</summary>
    void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_closing) return;
        _closing = true;
        Motion.Disappear(Root, Close);
    }
}

/// <summary>Pagina que sabe volver a leer los ajustes.</summary>
public interface IPage
{
    void Reload();
}
