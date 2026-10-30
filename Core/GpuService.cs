using Colmillo.Core.Gpu;
using Colmillo.Core.Razer;

namespace Colmillo.Core;

/// <summary>
/// Luz de la grafica. Cada cambio se aplica al instante y se guarda en la grafica unos
/// segundos despues del ultimo: guardar escribe su memoria, y un slider manda decenas.
/// <para>
/// La busqueda se reintenta: justo al iniciar sesion el driver de AMD a veces todavia no
/// atiende el I2C, y el controlador de luz deja sin responder alguna lectura. Antes se
/// buscaba una sola vez, y por eso parecia que hacia falta abrir RGB Fusion.
/// </para>
/// </summary>
public sealed class GpuService
{
    static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(3);
    static readonly TimeSpan[] SearchRetries =
    [
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30),
    ];

    readonly AppSettings _settings;
    readonly SemaphoreSlim _io = new(1, 1);
    int? _adapter;
    bool _searched;
    bool _searching;
    CancellationTokenSource? _pendingSave;

    public bool Available => _adapter is not null;
    public event Action? StateChanged;

    public GpuService(AppSettings settings) => _settings = settings;

    public string Status =>
        !_searched || _searching && !Available ? "Buscando…" :
        Available ? "RGB Fusion · lista" :
        "No se encontró una gráfica Gigabyte compatible";

    /// <summary>Al arrancar: busca la grafica (con reintentos) y le aplica el perfil sin volver a guardarlo.</summary>
    public Task ConnectAndApplyAsync() => Task.Run(async () =>
    {
        _searching = true;
        await Run(connect: true);
        foreach (var wait in SearchRetries)
        {
            if (Available) break;
            await Task.Delay(wait);
            _searched = false;
            await Run(connect: true);
        }
        _searching = false;
        DeviceLog.Write($"Gráfica: {Status}");
        StateChanged?.Invoke();
    });

    /// <summary>Vuelve a buscarla (al volver de suspension o desde Reintentar).</summary>
    public Task RecheckAsync()
    {
        _adapter = null;
        _searched = false;
        StateChanged?.Invoke();
        return ConnectAndApplyAsync();
    }

    public async Task ApplyAsync(LightSync? sync = null)
    {
        await LightSync.Join(sync, start => Run(connect: false, start));
        if (!Available) return;
        _pendingSave?.Cancel();
        var cts = _pendingSave = new CancellationTokenSource();
        try
        {
            await Task.Delay(SaveDelay, cts.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        _pendingSave = null;
        await Use(gpu => gpu.Save());
    }

    /// <summary>Al cerrar Colmillo: si quedo un guardado pendiente, se hace ya.</summary>
    public void Flush()
    {
        if (Interlocked.Exchange(ref _pendingSave, null) is not { } cts) return;
        cts.Cancel();
        Use(gpu => gpu.Save()).Wait(TimeSpan.FromSeconds(2));
    }

    Task Run(bool connect, Action? start = null) => Task.Run(async () =>
    {
        if (connect && !_searched)
        {
            await _io.WaitAsync().ConfigureAwait(false);
            try
            {
                using var found = GigabyteGpu.Open();
                _adapter = found?.Adapter;
            }
            finally
            {
                _searched = true;
                _io.Release();
            }
            StateChanged?.Invoke();
        }
        var g = _settings.Gpu;
        // Juntos: la velocidad mas lenta, la que mejor sigue al mouse.
        var speed = _settings.LightingTogether ? 1 : g.Speed;
        await Use(gpu => gpu.Apply(g.Effect, Rgb.Parse(g.Color), g.Brightness, speed, start));
    });

    async Task Use(Func<GigabyteGpu, bool> action)
    {
        if (_adapter is null) return;
        await _io.WaitAsync().ConfigureAwait(false);
        try
        {
            using var gpu = GigabyteGpu.Open(_adapter);
            if (gpu is null || !action(gpu)) DeviceLog.Write("Gráfica: el controlador de luz no aceptó el comando");
        }
        finally
        {
            _io.Release();
        }
    }
}
