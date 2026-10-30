using Colmillo.Core.Gpu;

namespace Colmillo.Core;

/// <summary>
/// Grafica AMD por ADLX: ventiladores, perfil de tuning y metricas. ADLX admite una sola
/// sesion por proceso, asi que pasa todo por aca. Al arrancar aplica lo guardado y cierra
/// ADLX; mientras haya una pagina o el overlay de metricas abierto, la sesion queda abierta
/// (con contador: se cierra cuando se va el ultimo).
/// </summary>
public sealed class AmdService
{
    static readonly TimeSpan[] SearchRetries = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)];

    readonly AppSettings _settings;
    readonly SemaphoreSlim _io = new(1, 1);
    AmdGpu? _session;
    int _users;
    bool _searching = true;

    public bool Available { get; private set; }
    public string Name { get; private set; } = "";
    public FanCurve? Curve { get; private set; }
    public event Action? StateChanged;

    public AmdService(AppSettings settings) => _settings = settings;

    GpuFanProfile F => _settings.GpuFans;

    public string Status =>
        Available ? (F.Custom ? "Curva propia" : "Curva de fábrica") :
        _searching ? "Buscando…" : "Sin control de ventiladores";

    public static bool AfterburnerRunning()
    {
        var procs = System.Diagnostics.Process.GetProcessesByName("MSIAfterburner");
        foreach (var p in procs) p.Dispose();
        return procs.Length > 0;
    }

    public Task ConnectAndApplyAsync() => Task.Run(async () =>
    {
        _searching = true;
        await Connect();
        foreach (var wait in SearchRetries)
        {
            if (Available) break;
            await Task.Delay(wait);
            await Connect();
        }
        _searching = false;
        StateChanged?.Invoke();
    });

    async Task Connect()
    {
        await Use(fans =>
        {
            Name = fans.Name;
            Curve = fans.ReadCurve();
            if (F.Points is null || F.Points.Count != Curve.Current.Length)
            {
                // Primera vez: se adopta lo que tiene el driver (puede venir de Adrenalin o
                // Afterburner) para no cambiar nada sin que se pida.
                F.Points = [.. Curve.Current.Select(p => new FanPoint(p.Temperature, p.Speed))];
                F.ZeroRpm = Curve.ZeroRpm;
                F.Custom = !Same(Curve.Current, Curve.Default) || Curve.ZeroRpm != Curve.DefaultZeroRpm;
                App.Current.Dispatcher.BeginInvoke(App.Current.SaveSoon);
            }
            else if (F.Custom)
            {
                fans.SetCurve(F.Points, F.ZeroRpm);
                Curve = fans.ReadCurve();
            }
            Tuning = fans.ReadTuning();
            // El perfil guardado se reaplica solo si el driver tiene otro (el overclock
            // automatico tarda: no se repite si ya esta puesto).
            if (_settings.GpuTuning != GpuTuning.Unknown && Tuning.Current != _settings.GpuTuning)
            {
                SetTuning(fans, _settings.GpuTuning);
            }
            return true;
        });
    }

    static bool Same(FanPoint[] a, FanPoint[] b) =>
        a.Length == b.Length && a.Zip(b).All(p => p.First.Temperature == p.Second.Temperature && p.First.Speed == p.Second.Speed);

    /// <summary>Aplica la curva propia, o la de fabrica si se eligio automatico. Null si salio bien.</summary>
    public async Task<string?> ApplyAsync()
    {
        var error = await Use(fans =>
        {
            var curve = Curve ?? fans.ReadCurve();
            var e = F.Custom && F.Points is not null
                ? fans.SetCurve(F.Points, F.ZeroRpm)
                : fans.SetCurve(curve.Default, curve.DefaultZeroRpm);
            Curve = fans.ReadCurve();
            return e;
        });
        return error ?? (Available ? null : "No se encontró la gráfica.");
    }

    public TuningInfo? Tuning { get; private set; }

    /// <summary>Cambia el perfil de la grafica y lo guarda para reaplicarlo al iniciar. Null si salio bien.</summary>
    public async Task<string?> SetTuningAsync(GpuTuning tuning)
    {
        _settings.GpuTuning = tuning;
        var error = await Use(gpu => SetTuning(gpu, tuning));
        return error ?? (Available ? null : "No se encontró la gráfica.");
    }

    string? SetTuning(AmdGpu gpu, GpuTuning tuning)
    {
        var error = gpu.SetTuning(tuning, out var wasReset);
        // Volver a fabrica (lo pide el driver para salir de un tuning automatico) borra la
        // curva del ventilador: se repone.
        if (wasReset && F.Custom && F.Points is not null) gpu.SetCurve(F.Points, F.ZeroRpm);
        Tuning = gpu.ReadTuning();
        Curve = gpu.ReadCurve();
        DeviceLog.Write($"Gráfica: perfil {tuning} -> {error ?? "aplicado"}");
        return error;
    }

    /// <summary>Mantiene ADLX abierto (pagina de ventiladores, overlay de metricas).</summary>
    public async Task BeginMonitorAsync()
    {
        await _io.WaitAsync().ConfigureAwait(false);
        try
        {
            _users++;
            _session ??= AmdGpu.Open();
        }
        finally
        {
            _io.Release();
        }
    }

    /// <summary>
    /// Suelta la sesion sin bloquear: lo llaman los Unloaded en el hilo de UI, y esperar ahi el
    /// semaforo trababa la app si una lectura en curso tenia que volver a ese mismo hilo.
    /// </summary>
    public void EndMonitor() => _ = Task.Run(async () =>
    {
        await _io.WaitAsync().ConfigureAwait(false);
        try
        {
            if (--_users > 0) return;
            _users = 0;
            _session?.Dispose();
            _session = null;
        }
        finally
        {
            _io.Release();
        }
    });

    public Task<FanReadings?> ReadAsync() => Use<FanReadings?>(fans => fans.Read());

    public Task<GpuMetrics?> ReadMetricsAsync() => Use(gpu => gpu.ReadMetrics());

    async Task<T?> Use<T>(Func<AmdGpu, T> action)
    {
        // Nada de esto vuelve al hilo de UI: el semaforo se suelta en el pool.
        await _io.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                if (_session is not null) return action(_session);
                using var fans = AmdGpu.Open();
                var was = Available;
                Available = fans is not null;
                if (was != Available) StateChanged?.Invoke();
                return fans is null ? default : action(fans);
            }).ConfigureAwait(false);
        }
        finally
        {
            _io.Release();
        }
    }
}
