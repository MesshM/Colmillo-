using Colmillo.Core.Gpu;
using Colmillo.Core.Razer;

namespace Colmillo.Core;

/// <summary>
/// Luz de la grafica. Cada cambio se aplica al instante y se guarda en la grafica unos
/// segundos despues del ultimo: guardar escribe su memoria, y un slider manda decenas.
/// </summary>
public sealed class GpuService
{
    static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(3);

    readonly AppSettings _settings;
    readonly SemaphoreSlim _io = new(1, 1);
    int? _adapter;
    bool _searched;
    CancellationTokenSource? _pendingSave;

    public bool Available => _adapter is not null;
    public event Action? StateChanged;

    public GpuService(AppSettings settings) => _settings = settings;

    public string Status =>
        !_searched ? "Buscando…" :
        Available ? "RGB Fusion · lista" :
        "No se encontró una gráfica Gigabyte compatible";

    /// <summary>Al arrancar: busca la grafica y le aplica el perfil sin volver a guardarlo.</summary>
    public Task ConnectAndApplyAsync() => Run(connect: true);

    public async Task ApplyAsync()
    {
        await Run(connect: false);
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

    Task Run(bool connect) => Task.Run(async () =>
    {
        if (connect && !_searched)
        {
            await _io.WaitAsync();
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
        await Use(gpu => gpu.Apply(g.Effect, Rgb.Parse(g.Color), g.Brightness, g.Speed));
    });

    async Task Use(Func<GigabyteGpu, bool> action)
    {
        if (_adapter is null) return;
        await _io.WaitAsync();
        try
        {
            using var gpu = GigabyteGpu.Open(_adapter);
            if (gpu is not null) action(gpu);
        }
        finally
        {
            _io.Release();
        }
    }
}
