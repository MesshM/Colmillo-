using System.IO;
using System.Runtime.InteropServices;
using Colmillo.Core.Hid;

namespace Colmillo.Core.Metrics;

/// <summary>
/// Temperatura de CPU AMD Ryzen (Zen 2/3/4) por SMN, con el modulo AMDFamily17.bin de
/// PawnIO.Modules (LGPL-2.1, el mismo proyecto que el de la RAM). Ryzen no tiene sensor por
/// nucleo: da Tctl (la de control, la que usa el ventilador) y una por CCD (cada chiplet de 8
/// nucleos). Necesita administrador, como la RAM.
/// </summary>
public sealed class RyzenTemperature : IDisposable
{
    // THM_TCON_CUR_TMP: bits 31:21 en octavos de grado; el bit 19 corre la escala 49 °C.
    const uint TctlAddress = 0x00059800;
    // Zen 2/3: una palabra por CCD; bit 11 = valida, bits 10:0 en octavos, menos 49 °C.
    const uint CcdBase = 0x00059954;
    const int MaxCcds = 8;

    delegate int OpenFn(out IntPtr handle);
    delegate int LoadFn(IntPtr handle, byte[] blob, UIntPtr size);
    delegate int ExecuteFn(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string name, ulong[] input, UIntPtr inSize, ulong[] output, UIntPtr outSize, out UIntPtr returned);
    delegate int CloseFn(IntPtr handle);

    readonly IntPtr _lib, _handle;
    readonly ExecuteFn _execute;
    readonly CloseFn _close;
    readonly int _ccds;

    RyzenTemperature(IntPtr lib, IntPtr handle, ExecuteFn execute, CloseFn close)
    {
        (_lib, _handle, _execute, _close) = (lib, handle, execute, close);
        for (var i = 0; i < MaxCcds; i++)
            if (Smn(CcdBase + (uint)i * 4) is { } v && (v & 0x800) != 0) _ccds = i + 1;
    }

    /// <summary>Esta incluido el modulo de PawnIO para AMD (se agrega al compilar si esta en Assets).</summary>
    public static bool ModuleBundled => typeof(RyzenTemperature).Assembly.GetManifestResourceInfo("Colmillo.AMDFamily17.bin") is not null;

    public static RyzenTemperature? Open()
    {
        if (!ModuleBundled || PawnIoSmbus.FindLibrary() is not { } path || !NativeLibrary.TryLoad(path, out var lib)) return null;
        var open = Marshal.GetDelegateForFunctionPointer<OpenFn>(NativeLibrary.GetExport(lib, "pawnio_open"));
        var load = Marshal.GetDelegateForFunctionPointer<LoadFn>(NativeLibrary.GetExport(lib, "pawnio_load"));
        var execute = Marshal.GetDelegateForFunctionPointer<ExecuteFn>(NativeLibrary.GetExport(lib, "pawnio_execute"));
        var close = Marshal.GetDelegateForFunctionPointer<CloseFn>(NativeLibrary.GetExport(lib, "pawnio_close"));
        if (open(out var handle) != 0)
        {
            NativeLibrary.Free(lib);
            return null;
        }
        using var stream = typeof(RyzenTemperature).Assembly.GetManifestResourceStream("Colmillo.AMDFamily17.bin")!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var blob = ms.ToArray();
        // El modulo se niega a cargar si el CPU no es AMD de la familia 17h o posterior.
        if (load(handle, blob, (UIntPtr)blob.Length) != 0)
        {
            close(handle);
            NativeLibrary.Free(lib);
            return null;
        }
        return new RyzenTemperature(lib, handle, execute, close);
    }

    uint? Smn(uint address)
    {
        var output = new ulong[1];
        return _execute(_handle, "ioctl_read_smn", [address], (UIntPtr)1, output, (UIntPtr)1, out _) == 0 ? (uint)output[0] : null;
    }

    /// <summary>Tctl y la de cada CCD, en °C. Null si no se pudo leer.</summary>
    public (double? Tctl, double[] Ccds) Read()
    {
        double? tctl = null;
        if (Smn(TctlAddress) is { } v)
        {
            var t = ((v >> 21) & 0x7FF) * 0.125;
            if ((v & (1 << 19)) != 0) t -= 49;
            tctl = t;
        }
        var ccds = new List<double>();
        for (var i = 0; i < _ccds; i++)
            if (Smn(CcdBase + (uint)i * 4) is { } c && (c & 0x800) != 0) ccds.Add((c & 0x7FF) * 0.125 - 49);
        return (tctl, [.. ccds]);
    }

    public void Dispose()
    {
        _close(_handle);
        NativeLibrary.Free(_lib);
    }
}
