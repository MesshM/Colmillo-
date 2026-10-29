using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Colmillo.Core;
using Colmillo.Shell;
using Colmillo.UI;
using Microsoft.Win32;

namespace Colmillo;

public partial class App : Application
{
    const string MutexName = @"Local\Colmillo";
    const string ShowEventName = @"Local\Colmillo.Mostrar";

    Mutex? _single;
    EventWaitHandle? _showEvent;
    TrayIcon? _tray;
    MainWindow? _window;
    readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    readonly DispatcherTimer _recheckTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };

    public static new App Current => (App)Application.Current;
    public AppSettings Settings { get; private set; } = new();
    public DeviceService Device { get; private set; } = null!;
    public RamService Ram { get; private set; } = null!;
    public GpuService Gpu { get; private set; } = null!;

    /// <summary>
    /// Relanza Colmillo como administrador (un aviso UAC). La copia elevada crea la tarea de
    /// inicio y desde ahi arranca siempre elevada sin volver a preguntar.
    /// </summary>
    public void RelaunchElevated()
    {
        if (!SystemIntegration.StartElevated()) return;
        Quit();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, a) =>
        {
            // Un error de UI no tiene que tirar la app de la bandeja: se registra y se sigue.
            LogError(a.Exception);
            a.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, a) => LogError(a.ExceptionObject as Exception);

        if (e.Args.Contains("--desinstalar"))
        {
            // Lo llama el desinstalador (elevado): quita las tareas programadas y la clave Run.
            SystemIntegration.RemoveStartup();
            Shutdown();
            return;
        }

        _single = new Mutex(true, MutexName, out var first);
        if (!first && e.Args.Contains("--relanzado"))
        {
            // La copia sin elevar que nos lanzo todavia se esta cerrando: se espera su turno.
            try { first = _single.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { first = true; }
        }
        if (!first)
        {
            // Ya hay una instancia en la bandeja: se le pide que muestre su ventana. Si esa
            // instancia corre elevada, una sin elevar no puede tocar su evento (Windows no
            // deja escribir "hacia arriba" de nivel de integridad): se pasa por la tarea,
            // que arranca otra elevada y esa si puede avisarle.
            if (!e.Args.Contains("--bandeja"))
            {
                try { EventWaitHandle.OpenExisting(ShowEventName).Set(); }
                catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException)
                {
                    SystemIntegration.RunStartupTask();
                }
            }
            Shutdown();
            return;
        }

        // Sin elevar y con la tarea creada: se relanza por la tarea (elevada, sin UAC) para
        // que PawnIO acepte el acceso a la RAM.
        if (!SystemIntegration.IsElevated && SystemIntegration.HasStartupTask)
        {
            _single.ReleaseMutex();
            _single.Dispose();
            _single = null;
            if (SystemIntegration.RunStartupTask(tray: e.Args.Contains("--bandeja")))
            {
                Shutdown();
                return;
            }
            _single = new Mutex(true, MutexName, out _);
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        new Thread(() =>
        {
            while (_showEvent.WaitOne()) Dispatcher.BeginInvoke(ShowWindow);
        }) { IsBackground = true, Name = "mostrar" }.Start();

        Settings = AppSettings.Load();
        if (e.Args.Contains("--sin-inicio"))
        {
            // El instalador desmarco "Iniciar con Windows".
            Settings.StartWithWindows = false;
            Settings.Save();
        }
        ApplyTheme();
        SystemEvents.UserPreferenceChanged += (_, a) =>
        {
            if (a.Category == UserPreferenceCategory.General && Settings.Theme == "sistema")
                Dispatcher.BeginInvoke(ApplyTheme);
        };

        Device = new DeviceService(Settings, a => Dispatcher.BeginInvoke(a));
        Device.StateChanged += () => Dispatcher.BeginInvoke(UpdateTray);
        Device.DpiChanged += dpi => Dispatcher.BeginInvoke(() => OnHardwareDpi(dpi));

        _tray = new TrayIcon("Colmillo");
        _tray.Click += ShowWindow;
        _tray.RightClick += ShowTrayMenu;
        _tray.CloseRequested += Quit;
        // Al enchufar algo USB llegan varios avisos seguidos: se espera a que se calme.
        _tray.DevicesChanged += () => { _recheckTimer.Stop(); _recheckTimer.Start(); };
        _tray.Resumed += () => _ = Device.ConnectAndApplyAsync();
        _recheckTimer.Tick += (_, _) => { _recheckTimer.Stop(); _ = Device.RecheckAsync(); };

        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Settings.Save(); };

        if (Settings.LightingTogether) Settings.SyncToMouse();
        Ram = new RamService(Settings);
        Ram.StateChanged += () => Dispatcher.BeginInvoke(() => _window?.Refresh());
        Gpu = new GpuService(Settings);
        Gpu.StateChanged += () => Dispatcher.BeginInvoke(() => _window?.Refresh());

        SystemIntegration.SetStartWithWindows(Settings.StartWithWindows);
        // Arrancando en la bandeja, se recorta la memoria recien despues de conectar:
        // el JIT y el primer I/O HID son lo que mas infla el arranque.
        Task.WhenAll(Device.ConnectAndApplyAsync(), Ram.ConnectAndApplyAsync(), Gpu.ConnectAndApplyAsync()).ContinueWith(_ => Dispatcher.BeginInvoke(() =>
        {
            if (_window is null) SystemIntegration.TrimMemory();
        }));

        if (!e.Args.Contains("--bandeja")) ShowWindow();
    }

    /// <summary>Corre el instalador de una actualizacion ya verificada y cierra Colmillo.</summary>
    public void InstallUpdate(string setupPath)
    {
        // /VERYSILENT sin preguntas; el instalador vuelve a abrir Colmillo al terminar.
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(setupPath,
            "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /ACTUALIZACION=1") { UseShellExecute = true })?.Dispose();
        Quit();
    }

    static void LogError(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Colmillo");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "errores.log"), $"[{DateTime.Now:s}] {ex}\n\n");
        }
        catch (System.IO.IOException)
        {
        }
    }

    /// <summary>
    /// El boton de DPI del mouse cambio de etapa por su cuenta: se refleja en los
    /// ajustes y en la ventana, y se muestra el aviso. No se reenvia nada al mouse.
    /// </summary>
    void OnHardwareDpi(int dpi)
    {
        var p = Settings.Device;
        var stage = p.DpiStages.IndexOf(dpi) + 1;
        if (stage > 0 && stage != p.ActiveStage)
        {
            p.ActiveStage = stage;
            SaveSoon();
        }
        _window?.Refresh();
        UI.DpiToast.Show(dpi, stage, p.DpiStages.Count, () =>
        {
            // El aviso arranco el render de WPF; si no hay ventana, se devuelve esa memoria.
            if (_window is null) SystemIntegration.TrimMemory();
        });
    }

    /// <summary>Guarda los ajustes un rato despues del ultimo cambio (un slider manda decenas).</summary>
    public void SaveSoon()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void ApplyTheme()
    {
        var dark = Settings.Theme switch
        {
            "oscuro" => true,
            "claro" => false,
            _ => !SystemIntegration.SystemUsesLightTheme(),
        };
        var dict = new ResourceDictionary { Source = new Uri(dark ? "Theme/Dark.xaml" : "Theme/Light.xaml", UriKind.Relative) };
        Resources.MergedDictionaries[0] = dict;
    }

    public void ShowWindow()
    {
        if (_window is null)
        {
            _window = new MainWindow();
            _window.Closed += (_, _) =>
            {
                _window = null;
                if (_quitting) return;
                if (Settings.CloseToTray) SystemIntegration.TrimMemory();
                else Quit();
            };
            _window.Show();
        }
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    void UpdateTray()
    {
        _tray?.SetTooltip(Device.Connected ? $"Colmillo — Razer Cobra (fw {Device.Firmware})" : "Colmillo — mouse desconectado");
    }

    void ShowTrayMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Abrir Colmillo", ShowWindow));
        menu.Items.Add(new Separator { Opacity = 0.3 });

        var p = Settings.Device;
        for (var i = 0; i < p.DpiStages.Count; i++)
        {
            var stage = i + 1;
            var mark = stage == p.ActiveStage ? "● " : "    ";
            menu.Items.Add(Item($"{mark}{p.DpiStages[i]} DPI", () =>
            {
                p.ActiveStage = stage;
                SaveSoon();
                _ = Device.ApplyPerformanceAsync();
                _window?.Refresh();
            }));
        }

        menu.Items.Add(new Separator { Opacity = 0.3 });
        menu.Items.Add(Item("Salir", Quit));
        _tray?.FocusForMenu();
        menu.IsOpen = true;
    }

    static MenuItem Item(string header, Action onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => onClick();
        return item;
    }

    bool _quitting;

    public void Quit()
    {
        // Shutdown cierra la ventana, y su Closed vuelve a llamar aca.
        if (_quitting) return;
        _quitting = true;
        _saveTimer.Stop();
        Settings.Save();
        _tray?.Dispose();
        Device.Dispose();
        Ram.Dispose();
        Gpu.Flush();
        Shutdown();
    }
}
