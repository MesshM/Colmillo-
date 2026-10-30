using System.Runtime.InteropServices;

namespace Colmillo.Core.Metrics;

public readonly record struct CoreReading(int Index, double Usage, double Mhz);

/// <summary>
/// Uso y reloj efectivo del CPU, total y por nucleo logico, con los contadores de Windows
/// (PDH, "Processor Information"): lo mismo que muestra el Administrador de tareas, sin
/// driver ni administrador. Reloj = frecuencia nominal x "% Processor Performance".
/// </summary>
public sealed class CpuCounters : IDisposable
{
    const int PdhFmtDouble = 0x00000200;
    const int PdhMoreData = unchecked((int)0x800007D2);

    [StructLayout(LayoutKind.Sequential)]
    struct FmtValue
    {
        public int Status;
        public double Value;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct FmtItem
    {
        public IntPtr Name;
        public FmtValue Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    static extern int PdhOpenQueryW(string? source, IntPtr user, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    static extern int PdhAddEnglishCounterW(IntPtr query, string path, IntPtr user, out IntPtr counter);

    [DllImport("pdh.dll")]
    static extern int PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    static extern int PdhGetFormattedCounterArrayW(IntPtr counter, int format, ref int bufferSize, out int count, IntPtr buffer);

    [DllImport("pdh.dll")]
    static extern int PdhCloseQuery(IntPtr query);

    readonly IntPtr _query;
    readonly IntPtr _utility, _performance, _frequency;

    CpuCounters(IntPtr query, IntPtr utility, IntPtr performance, IntPtr frequency)
    {
        (_query, _utility, _performance, _frequency) = (query, utility, performance, frequency);
        // Los contadores de porcentaje necesitan dos muestras: la primera solo arranca.
        PdhCollectQueryData(_query);
    }

    public static CpuCounters? Open()
    {
        if (PdhOpenQueryW(null, IntPtr.Zero, out var q) != 0) return null;
        if (PdhAddEnglishCounterW(q, @"\Processor Information(*)\% Processor Utility", IntPtr.Zero, out var u) != 0 ||
            PdhAddEnglishCounterW(q, @"\Processor Information(*)\% Processor Performance", IntPtr.Zero, out var p) != 0 ||
            PdhAddEnglishCounterW(q, @"\Processor Information(*)\Processor Frequency", IntPtr.Zero, out var f) != 0)
        {
            PdhCloseQuery(q);
            return null;
        }
        return new CpuCounters(q, u, p, f);
    }

    /// <summary>Total (indice -1) y cada nucleo logico, en orden.</summary>
    public (CoreReading Total, CoreReading[] Cores) Read()
    {
        PdhCollectQueryData(_query);
        var util = Values(_utility);
        var perf = Values(_performance);
        var freq = Values(_frequency);
        CoreReading Make(string name, int index) => new(index,
            Math.Clamp(util.GetValueOrDefault(name), 0, 100),
            freq.GetValueOrDefault(name) * perf.GetValueOrDefault(name) / 100);
        // Instancias "0,3" (grupo, nucleo); "_Total" y "0,_Total" son resumenes.
        var cores = util.Keys
            .Where(k => !k.Contains("_Total"))
            .Select(k => (Key: k, Index: int.TryParse(k.Split(',').Last(), out var i) ? i : -1))
            .Where(k => k.Index >= 0)
            .OrderBy(k => k.Index)
            .Select(k => Make(k.Key, k.Index))
            .ToArray();
        return (Make("_Total", -1), cores);
    }

    Dictionary<string, double> Values(IntPtr counter)
    {
        var result = new Dictionary<string, double>();
        var size = 0;
        if (PdhGetFormattedCounterArrayW(counter, PdhFmtDouble, ref size, out _, IntPtr.Zero) != PdhMoreData || size == 0) return result;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (PdhGetFormattedCounterArrayW(counter, PdhFmtDouble, ref size, out var count, buffer) != 0) return result;
            var item = Marshal.SizeOf<FmtItem>();
            for (var i = 0; i < count; i++)
            {
                var it = Marshal.PtrToStructure<FmtItem>(buffer + i * item);
                if (it.Value.Status == 0) result[Marshal.PtrToStringUni(it.Name) ?? ""] = it.Value.Value;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatus
    {
        public int Length, Load;
        public ulong TotalPhys, AvailPhys, TotalPage, AvailPage, TotalVirtual, AvailVirtual, AvailExtended;
    }

    [DllImport("kernel32.dll")]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    /// <summary>RAM en uso y total, en MB.</summary>
    public static (double UsedMb, double TotalMb) Memory()
    {
        var m = new MemoryStatus { Length = Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref m)) return (0, 0);
        return ((m.TotalPhys - m.AvailPhys) / 1048576.0, m.TotalPhys / 1048576.0);
    }

    public void Dispose() => PdhCloseQuery(_query);
}
