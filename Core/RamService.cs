using Colmillo.Core.Hid;
using Colmillo.Core.Ram;
using Colmillo.Core.Razer;
using Colmillo.Shell;

namespace Colmillo.Core;

/// <summary>
/// Luz de la RAM. El SMBus se abre una vez y queda abierto; cada cambio corre en el pool,
/// de a uno: el controlador de Kingston no admite dos secuencias mezcladas.
/// <para>
/// Si los modulos no aparecen (al iniciar sesion MSI Center y el servicio de Kingston
/// tambien recorren el bus) o dejan de responder a mitad de un cambio, se vuelven a
/// buscar: antes quedaban perdidos hasta reiniciar Colmillo.
/// </para>
/// </summary>
public sealed class RamService : IDisposable
{
    static readonly TimeSpan[] DetectRetries = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];

    readonly AppSettings _settings;
    readonly SemaphoreSlim _io = new(1, 1);
    PawnIoSmbus? _bus;
    KingstonFury? _ram;
    bool _searching = true;

    public SmbusState State { get; private set; } = SmbusState.NoDriver;
    public bool Available => _ram is not null;
    public int Modules => _ram?.Modules ?? 0;
    public event Action? StateChanged;

    public RamService(AppSettings settings) => _settings = settings;

    public string Status => State switch
    {
        _ when _searching && _ram is null => "Buscando…",
        SmbusState.NoDriver => "Falta instalar PawnIO",
        SmbusState.NeedsAdmin => "Colmillo tiene que correr como administrador",
        SmbusState.Unsupported => "La placa no tiene un SMBus compatible",
        _ when _ram is null => "No se encontraron módulos Kingston FURY RGB",
        _ => $"Kingston FURY · {Modules} módulo{(Modules == 1 ? "" : "s")}",
    };

    /// <summary>Al arrancar: prepara el driver, busca los modulos (con reintentos) y aplica el perfil.</summary>
    public Task ConnectAndApplyAsync() => Task.Run(async () =>
    {
        if (SystemIntegration.IsElevated)
        {
            if (!Requirements.EnsurePawnIo()) DeviceLog.Write("RAM: no se pudo iniciar PawnIO");
            if (_settings.StopLightingConflicts && Requirements.FuryServiceRunning)
                DeviceLog.Write(Requirements.StopFuryService() ? "RAM: servicio de Kingston FURY detenido" : "RAM: no se pudo detener el servicio de Kingston FURY");
            if (_settings.StopLightingConflicts && Requirements.LedKeeperRunning)
                DeviceLog.Write(Requirements.StopLedKeeper() ? "RAM: LEDKeeper2 de Mystic Light cerrado" : "RAM: no se pudo cerrar LEDKeeper2");
        }
        await Run(connect: true);
        foreach (var wait in DetectRetries)
        {
            if (_ram is not null || State is SmbusState.NoDriver or SmbusState.NeedsAdmin or SmbusState.Unsupported) break;
            await Task.Delay(wait);
            await Run(connect: true);
        }
        _searching = false;
        DeviceLog.Write($"RAM: {Status}");
        StateChanged?.Invoke();
    });

    /// <summary>Vuelve a buscar y reaplica (al volver de suspension o desde el boton Reintentar).</summary>
    public Task RecheckAsync() => Task.Run(async () =>
    {
        await _io.WaitAsync();
        try
        {
            _ram = null;
            _searching = true;
        }
        finally
        {
            _io.Release();
        }
        StateChanged?.Invoke();
        await ConnectAndApplyAsync();
    });

    public Task ApplyAsync(LightSync? sync = null) => LightSync.Join(sync, start => Run(connect: false, start));

    Task Run(bool connect, Action? start = null) => Task.Run(async () =>
    {
        await _io.WaitAsync();
        var changed = false;
        try
        {
            if (connect && _bus is null)
            {
                (_bus, State) = PawnIoSmbus.Open();
                changed = true;
            }
            if (_bus is not null && _ram is null && connect)
            {
                _ram = KingstonFury.Detect(_bus);
                changed = true;
            }
            if (_ram is null) return;
            _expected = Math.Max(_expected, _ram.Modules);

            Apply(_ram, start);
            if (_ram.Lost || _ram.Modules < _expected)
            {
                // Un modulo no respondio ni con 30 reintentos (le paso con el espectro mientras
                // Mystic Light tambien escribia): se vuelve a buscar y se repite el cambio, asi no
                // queda una barra con el efecto viejo. Si sigue sin aparecer, se reintenta mas tarde.
                _ram = KingstonFury.Detect(_bus!);
                DeviceLog.Write($"RAM: un módulo no respondió; al volver a buscar hay {_ram?.Modules ?? 0} de {_expected}");
                if (_ram is not null) Apply(_ram, null);
                if ((_ram?.Modules ?? 0) < _expected) _ = RecoverLater();
                changed = true;
            }
        }
        finally
        {
            _io.Release();
            if (changed) StateChanged?.Invoke();
        }
    });

    int _expected;
    int _recovering;

    /// <summary>Vuelve a buscar el modulo perdido a los 2, 5, 15 y 60 s; cuando aparece, reaplica.</summary>
    async Task RecoverLater()
    {
        if (Interlocked.Exchange(ref _recovering, 1) == 1) return;
        try
        {
            foreach (var wait in new[] { 2, 5, 15, 60 })
            {
                await Task.Delay(TimeSpan.FromSeconds(wait));
                await _io.WaitAsync();
                try
                {
                    if (_bus is null) return;
                    var found = KingstonFury.Detect(_bus);
                    if (found is null || found.Modules < _expected) continue;
                    _ram = found;
                    Apply(found, null);
                    DeviceLog.Write($"RAM: volvieron los {found.Modules} módulos");
                }
                finally
                {
                    _io.Release();
                }
                StateChanged?.Invoke();
                return;
            }
            DeviceLog.Write("RAM: el módulo sigue sin responder; hace falta reiniciar la PC");
        }
        finally
        {
            _recovering = 0;
        }
    }

    void Apply(KingstonFury ram, Action? start)
    {
        var p = _settings.Ram;
        var together = _settings.LightingTogether;
        // La paleta solo aplica por separado: en "todos juntos" la RAM copia al mouse, con la
        // velocidad mas lenta y la respiracion al ritmo calibrado para el mouse.
        var palette = p.UsePalette && !together ? p.Palette.Select(Rgb.Parse).ToArray() : null;
        ram.Apply(p.Effect, Rgb.Parse(p.Color1), Rgb.Parse(p.Color2), p.Brightness, together ? 1 : p.Speed, p.TwoColors, palette,
            together ? _settings.SyncBreathStep : null, start);
    }

    public void Dispose() => _bus?.Dispose();
}
