using System.Globalization;

namespace Colmillo.Core.Metrics;

/// <summary>Texto de cada metrica y las lineas del overlay, al estilo de RivaTuner.</summary>
public static class MetricsFormat
{
    static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es");

    /// <summary>Valor de una metrica, o null si no hay dato (sin juego no hay FPS, etc.).</summary>
    public static string? Value(MetricsSnapshot s, string key)
    {
        var g = s.Gpu;
        return key switch
        {
            "fps" => g?.Fps is { } f ? $"{f}" : null,
            "gpu_usage" => g?.Usage is { } u ? $"{u:0} %" : null,
            "gpu_temp" => g?.Temperature is { } t and > 0 ? $"{t:0} °C" : null,
            "gpu_hotspot" => g?.Hotspot is { } h and > 0 ? $"{h:0} °C" : null,
            "gpu_clock" => g?.Clock is { } c ? $"{c} MHz" : null,
            "vram_clock" => g?.VramClock is { } v ? $"{v} MHz" : null,
            "vram" => g?.VramMb is { } m ? Gb(m) : null,
            "gpu_power" => g?.BoardPower is { } p and > 0 ? $"{p:0} W" : null,
            "gpu_fan" => g?.FanRpm is { } r ? (r > 0 ? $"{r} RPM" : "0 RPM") : null,
            "cpu_usage" => s.Cores.Length > 0 || s.CpuTotal.Usage > 0 ? $"{s.CpuTotal.Usage:0} %" : g?.CpuUsage is { } cu ? $"{cu:0} %" : null,
            "cpu_clock" => s.CpuTotal.Mhz > 0 ? Ghz(s.Cores.Length > 0 ? s.Cores.Max(c => c.Mhz) : s.CpuTotal.Mhz) : null,
            "cpu_temp" => s.CpuTctl is { } tc ? $"{tc:0} °C" : null,
            "cpu_ccd" => s.CpuCcds.Length > 0 ? string.Join(" · ", s.CpuCcds.Select((c, i) => $"CCD{i + 1} {c:0} °C")) : null,
            "cpu_cores" => s.Cores.Length > 0 ? $"{s.Cores.Length} núcleos" : null,
            "ram" => s.RamTotalMb > 0 ? $"{Gb(s.RamUsedMb)} de {Gb(s.RamTotalMb)}" : null,
            _ => null,
        };
    }

    static string Gb(double mb) => (mb / 1024).ToString(mb >= 10240 ? "0" : "0.0", Es) + " GB";
    static string Ghz(double mhz) => (mhz / 1000).ToString("0.00", Es) + " GHz";

    /// <summary>Lineas del overlay: etiqueta del grupo y sus valores, en el orden del catalogo.</summary>
    public static List<(string Label, string Values)> Lines(MetricsSnapshot s, ICollection<string> shown)
    {
        var lines = new List<(string, string)>();
        foreach (var group in MetricCatalog.All.GroupBy(m => m.Group))
        {
            var values = group
                .Where(m => shown.Contains(m.Key) && m.Key != "cpu_cores")
                .Select(m => Value(s, m.Key))
                .OfType<string>()
                .ToList();
            if (values.Count > 0) lines.Add((group.Key, string.Join("   ", values)));
            if (group.Key == "CPU" && shown.Contains("cpu_cores"))
                foreach (var c in s.Cores)
                    lines.Add(($"C{c.Index + 1}", $"{c.Usage,3:0} %   {Ghz(c.Mhz)}"));
        }
        return lines;
    }
}
