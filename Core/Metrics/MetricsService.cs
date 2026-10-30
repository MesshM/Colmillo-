using System.Runtime.InteropServices;
using Colmillo.Core.Gpu;
using Colmillo.Shell;

namespace Colmillo.Core.Metrics;

/// <summary>Una lectura de todo lo que muestra el overlay.</summary>
public sealed record MetricsSnapshot(
    GpuMetrics? Gpu,
    CoreReading CpuTotal,
    CoreReading[] Cores,
    double? CpuTctl,
    double[] CpuCcds,
    double RamUsedMb,
    double RamTotalMb);

/// <summary>Las metricas que se pueden mostrar, por grupo, con su clave en los ajustes.</summary>
public static class MetricCatalog
{
    public sealed record Item(string Key, string Group, string Label);

    public static readonly Item[] All =
    [
        new("fps", "FPS", "Cuadros por segundo"),
        new("gpu_usage", "GPU", "Uso"),
        new("gpu_temp", "GPU", "Temperatura"),
        new("gpu_hotspot", "GPU", "Punto caliente"),
        new("gpu_clock", "GPU", "Reloj"),
        new("vram_clock", "GPU", "Reloj de la VRAM"),
        new("vram", "GPU", "VRAM en uso"),
        new("gpu_power", "GPU", "Consumo"),
        new("gpu_fan", "GPU", "Ventilador"),
        new("cpu_usage", "CPU", "Uso"),
        new("cpu_clock", "CPU", "Reloj"),
        new("cpu_temp", "CPU", "Temperatura (Tctl)"),
        new("cpu_ccd", "CPU", "Temperatura por CCD"),
        new("cpu_cores", "CPU", "Cada núcleo (uso y reloj)"),
        new("ram", "RAM", "En uso"),
    ];
}

/// <summary>
/// Junta las metricas una vez por segundo mientras alguien las mire (la pagina o el overlay):
/// grafica, FPS y uso de CPU de ADLX; uso y reloj por nucleo de los contadores de Windows;
/// temperatura del Ryzen por PawnIO. Sin nadie mirando no queda nada abierto.
/// </summary>
public sealed class MetricsService
{
    static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    readonly AmdService _amd;
    readonly object _gate = new();
    int _users;
    CancellationTokenSource? _run;
    CpuCounters? _cpu;
    RyzenTemperature? _temp;
    int[][] _coreMap = [];

    public event Action<MetricsSnapshot>? Updated;
    public MetricsSnapshot? Last { get; private set; }
    /// <summary>Hay lectura de temperatura del CPU (modulo incluido, administrador y CPU AMD).</summary>
    public bool CpuTemperatureAvailable { get; private set; }

    public MetricsService(AmdService amd) => _amd = amd;

    /// <summary>Al iniciar: deja en dispositivos.log si hay temperatura del CPU y cuanto marca.</summary>
    public static void LogCpuTemperature()
    {
        using var temp = SystemIntegration.IsElevated ? RyzenTemperature.Open() : null;
        DeviceLog.Write(temp?.Read() is { Tctl: { } t } r
            ? $"CPU: Tctl {t:0.0} °C, {r.Ccds.Length} CCD ({string.Join(", ", r.Ccds.Select(c => $"{c:0.0} °C"))})"
            : $"CPU: sin temperatura (módulo {(RyzenTemperature.ModuleBundled ? "incluido" : "ausente")}, administrador: {SystemIntegration.IsElevated})");
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_users++ > 0) return;
            _run = new CancellationTokenSource();
            _ = Loop(_run.Token);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (--_users > 0) return;
            _users = 0;
            _run?.Cancel();
            _run = null;
        }
    }

    async Task Loop(CancellationToken stop)
    {
        await _amd.BeginMonitorAsync();
        await Task.Run(() =>
        {
            _cpu = CpuCounters.Open();
            _temp = SystemIntegration.IsElevated ? RyzenTemperature.Open() : null;
            _coreMap = PhysicalCores();
        });
        CpuTemperatureAvailable = _temp is not null;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var gpu = await _amd.ReadMetricsAsync();
                var snapshot = await Task.Run(() => Read(gpu));
                Last = snapshot;
                Updated?.Invoke(snapshot);
                try { await Task.Delay(Interval, stop); }
                catch (TaskCanceledException) { break; }
            }
        }
        finally
        {
            _amd.EndMonitor();
            _cpu?.Dispose();
            _cpu = null;
            _temp?.Dispose();
            _temp = null;
        }
    }

    MetricsSnapshot Read(GpuMetrics? gpu)
    {
        var (total, threads) = _cpu?.Read() ?? (default, []);
        // Por nucleo fisico: el uso promedio de sus hilos y el reloj del mas rapido.
        var cores = _coreMap.Length > 0 && threads.Length > 0
            ? _coreMap.Select((t, i) =>
            {
                var mine = threads.Where(x => t.Contains(x.Index)).ToArray();
                return mine.Length == 0 ? new CoreReading(i, 0, 0) : new CoreReading(i, mine.Average(x => x.Usage), mine.Max(x => x.Mhz));
            }).ToArray()
            : threads;
        var (tctl, ccds) = _temp?.Read() ?? (null, []);
        var (used, ramTotal) = CpuCounters.Memory();
        return new MetricsSnapshot(gpu, total, cores, tctl, ccds, used, ramTotal);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct LogicalProcessorInfo
    {
        public UIntPtr Mask;
        public int Relationship;
        public ulong Reserved1, Reserved2;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetLogicalProcessorInformation(IntPtr buffer, ref int length);

    /// <summary>Hilos de cada nucleo fisico (los hermanos SMT van juntos).</summary>
    static int[][] PhysicalCores()
    {
        var length = 0;
        GetLogicalProcessorInformation(IntPtr.Zero, ref length);
        if (length == 0) return [];
        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            if (!GetLogicalProcessorInformation(buffer, ref length)) return [];
            var size = Marshal.SizeOf<LogicalProcessorInfo>();
            var cores = new List<int[]>();
            for (var offset = 0; offset + size <= length; offset += size)
            {
                var info = Marshal.PtrToStructure<LogicalProcessorInfo>(buffer + offset);
                if (info.Relationship != 0) continue; // RelationProcessorCore
                var mask = (ulong)info.Mask;
                cores.Add([.. Enumerable.Range(0, 64).Where(b => (mask & (1UL << b)) != 0)]);
            }
            return [.. cores.OrderBy(c => c[0])];
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
