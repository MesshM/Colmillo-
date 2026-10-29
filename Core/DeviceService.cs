using System.IO;
using System.Diagnostics;
using Colmillo.Core.Input;
using Colmillo.Core.Razer;

namespace Colmillo.Core;

/// <summary>
/// Dueno de la conexion con el Cobra. Todo el I/O HID corre fuera del hilo de UI y
/// en serie (un comando a la vez); los cambios de la UI llaman a los Apply* y
/// estos guardan el ajuste y lo mandan al mouse.
/// </summary>
public sealed class DeviceService : IDisposable
{
    readonly AppSettings _settings;
    readonly SemaphoreSlim _io = new(1, 1);
    readonly SoftwareActions _software = new();
    readonly Action<Action> _onUi;
    CobraMouse? _mouse;
    Hid.RazerEvents? _events;

    public event Action? StateChanged;
    /// <summary>El boton de DPI del mouse cambio la sensibilidad (llega desde un hilo de fondo).</summary>
    public event Action<int>? DpiChanged;

    public bool Connected => _mouse is not null;
    public string Firmware => _mouse?.Firmware ?? "";
    public string? LastError { get; private set; }

    /// <param name="onUi">Despacha al hilo de UI (Dispatcher.BeginInvoke).</param>
    public DeviceService(AppSettings settings, Action<Action> onUi)
    {
        _settings = settings;
        _onUi = onUi;
    }

    DeviceProfile P => _settings.Device;

    /// <summary>Synapse sigue corriendo: re-impone su iluminacion y vuelve el mouse a modo driver.</summary>
    public static bool SynapseRunning()
    {
        var procs = Process.GetProcessesByName("RazerAppEngine");
        foreach (var p in procs) p.Dispose();
        return procs.Length > 0;
    }

    public static void CloseSynapse()
    {
        foreach (var p in Process.GetProcessesByName("RazerAppEngine"))
        {
            try { p.Kill(entireProcessTree: true); }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            finally { p.Dispose(); }
        }
    }

    /// <summary>Conecta si hace falta y aplica el perfil completo. Seguro de llamar varias veces.</summary>
    public Task ConnectAndApplyAsync() => Run(m => ApplyAll(m), connect: true);

    /// <summary>Revisa si el mouse sigue ahi (tras un cambio de dispositivos USB).</summary>
    public Task RecheckAsync() => Task.Run(async () =>
    {
        await _io.WaitAsync();
        try
        {
            _mouse?.GetDeviceMode();
        }
        catch (IOException)
        {
            Disconnect();
            StateChanged?.Invoke();
        }
        finally
        {
            _io.Release();
        }
        if (_mouse is null) await ConnectAndApplyAsync();
    });

    public Task ApplyPerformanceAsync() => Run(m =>
    {
        m.SetPollingRate(P.PollingRate);
        ApplyDpi(m);
    });

    public Task ApplyLightingAsync() => Run(ApplyLighting);

    public Task ApplyButtonsAsync() => Run(ApplyButtons);

    void ApplyAll(CobraMouse m)
    {
        // Modo normal: en modo driver (como lo deja Synapse) el boton de DPI espera a un software.
        m.SetDeviceMode(0);
        m.SetPollingRate(P.PollingRate);
        ApplyDpi(m);
        ApplyLighting(m);
        ApplyButtons(m);
    }

    void ApplyDpi(CobraMouse m)
    {
        var stages = P.DpiStages.Count > 0 ? P.DpiStages : [800];
        var active = Math.Clamp(P.ActiveStage, 1, stages.Count);
        m.SetDpiStages(stages, active);
        m.SetDpi(stages[active - 1]);
    }

    void ApplyLighting(CobraMouse m)
    {
        m.SetEffect(P.Effect, Rgb.Parse(P.Color1), Rgb.Parse(P.Color2), P.ReactiveSpeed);
        m.SetBrightness(P.Brightness);
    }

    void ApplyButtons(CobraMouse m)
    {
        var plan = Plan();
        foreach (var (info, hypershift, action, key) in plan)
            m.SetButton(info.Id, hypershift, action, key);
        _onUi(RebindSoftware);
    }

    /// <summary>
    /// Asigna F13..F24 en orden fijo a las acciones por software. Si se acaban las
    /// teclas, el boton queda con su funcion de fabrica en vez de quedar muerto.
    /// </summary>
    List<(CobraButtons.Info Info, bool Hypershift, ButtonAction Action, byte Key)> Plan()
    {
        var plan = new List<(CobraButtons.Info, bool, ButtonAction, byte)>();
        var nextKey = CobraButtons.FirstSoftwareKey;
        foreach (var hypershift in new[] { false, true })
            foreach (var info in CobraButtons.All)
            {
                if (!info.Remappable) continue;
                var action = P.Resolve(info.Id, hypershift);
                byte key = 0;
                if (action.IsSoftware && nextKey < CobraButtons.FirstSoftwareKey + CobraButtons.SoftwareKeyCount)
                    key = nextKey++;
                else if (action.IsSoftware)
                    action = info.Factory;
                plan.Add((info, hypershift, action, key));
            }
        return plan;
    }

    /// <summary>
    /// Re-instala el hook con la asignacion actual. Tiene que correr en el hilo de
    /// UI: un hook de bajo nivel recibe eventos por el bucle de mensajes del hilo
    /// que lo instalo, y los hilos del pool no tienen uno.
    /// </summary>
    public void RebindSoftware()
    {
        var bindings = Plan().Where(p => p.Key != 0).ToDictionary(p => p.Key, p => p.Action);
        _software.Update(bindings, id => _settings.Macros.FirstOrDefault(x => x.Id == id));
    }

    Task Run(Action<CobraMouse> work, bool connect = false) => Task.Run(async () =>
    {
        await _io.WaitAsync();
        var changed = false;
        try
        {
            if (_mouse is null && connect)
            {
                _mouse = CobraMouse.Connect();
                if (_mouse is not null)
                {
                    _events = Hid.RazerEvents.Open(CobraMouse.VendorId, CobraMouse.ProductId);
                    if (_events is not null) _events.DpiChanged += dpi => DpiChanged?.Invoke(dpi);
                }
                changed = true;
            }
            if (_mouse is null)
            {
                LastError = "No se encontró el Razer Cobra. Revisa que esté conectado por USB.";
                return;
            }
            work(_mouse);
            LastError = null;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException)
        {
            LastError = "Se perdió la conexión con el mouse.";
            Disconnect();
            changed = true;
        }
        finally
        {
            _io.Release();
            if (changed || LastError is not null) StateChanged?.Invoke();
        }
    });

    void Disconnect()
    {
        _events?.Dispose();
        _events = null;
        _mouse?.Dispose();
        _mouse = null;
    }

    public void Dispose()
    {
        _software.Dispose();
        Disconnect();
    }
}
