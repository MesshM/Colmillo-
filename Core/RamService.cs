using Colmillo.Core.Hid;
using Colmillo.Core.Ram;
using Colmillo.Core.Razer;

namespace Colmillo.Core;

/// <summary>
/// Luz de la RAM. El SMBus se abre una vez y queda abierto; cada cambio corre en el pool,
/// de a uno: el controlador de Kingston no admite dos secuencias mezcladas.
/// </summary>
public sealed class RamService : IDisposable
{
    readonly AppSettings _settings;
    readonly SemaphoreSlim _io = new(1, 1);
    PawnIoSmbus? _bus;
    KingstonFury? _ram;

    public SmbusState State { get; private set; } = SmbusState.NoDriver;
    public bool Available => _ram is not null;
    public int Modules => _ram?.Modules ?? 0;
    public event Action? StateChanged;

    public RamService(AppSettings settings) => _settings = settings;

    public string Status => State switch
    {
        SmbusState.NoDriver => "Falta instalar PawnIO",
        SmbusState.NeedsAdmin => "Colmillo tiene que correr como administrador",
        SmbusState.Unsupported => "La placa no tiene un SMBus compatible",
        _ when _ram is null => "No se encontraron módulos Kingston FURY RGB",
        _ => $"Kingston FURY · {Modules} módulo{(Modules == 1 ? "" : "s")}",
    };

    public Task ConnectAndApplyAsync() => Run(connect: true);

    public Task ApplyAsync() => Run(connect: false);

    Task Run(bool connect) => Task.Run(async () =>
    {
        await _io.WaitAsync();
        try
        {
            if (_bus is null && connect)
            {
                (_bus, State) = PawnIoSmbus.Open();
                if (_bus is not null) _ram = KingstonFury.Detect(_bus);
                StateChanged?.Invoke();
            }
            if (_ram is null) return;
            var p = _settings.Ram;
            _ram.Apply(p.Effect, Rgb.Parse(p.Color1), Rgb.Parse(p.Color2), p.Brightness, p.Speed, p.TwoColors);
        }
        finally
        {
            _io.Release();
        }
    });

    public void Dispose() => _bus?.Dispose();
}
