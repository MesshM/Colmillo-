using System.IO;
using Microsoft.Win32.SafeHandles;

namespace Colmillo.Core.Hid;

/// <summary>
/// Escucha los avisos que manda el firmware por su cuenta. El Cobra los publica como
/// reporte de entrada en la coleccion MI_01&amp;COL05: <c>[05 aviso, 02 DPI, xH, xL, yH, yL]</c>
/// cada vez que se aprieta el boton de DPI (verificado contra el mouse, tambien en
/// modo normal). Es un hilo bloqueado en ReadFile: no consume CPU mientras espera.
/// </summary>
public sealed class RazerEvents : IDisposable
{
    const uint GENERIC_READ = 0x80000000;
    const byte NotificationEvents = 0x05;
    const byte DpiChange = 0x02;

    readonly FileStream _stream;
    readonly int _length;

    public event Action<int>? DpiChanged;

    RazerEvents(SafeFileHandle handle, int length)
    {
        _length = length;
        _stream = new FileStream(handle, FileAccess.Read, length, isAsync: false);
        new Thread(Loop) { IsBackground = true, Name = "avisos-razer" }.Start();
    }

    public static RazerEvents? Open(ushort vendorId, ushort productId)
    {
        var needle = $"vid_{vendorId:x4}&pid_{productId:x4}&mi_01&col05";
        foreach (var path in HidNative.EnumerateHidPaths())
        {
            if (!path.ToLowerInvariant().Contains(needle)) continue;
            var handle = HidNative.CreateFile(path, GENERIC_READ, HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE,
                IntPtr.Zero, HidNative.OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle.IsInvalid) continue;
            var length = InputLength(handle);
            if (length > 0) return new RazerEvents(handle, length);
            handle.Dispose();
        }
        return null;
    }

    static int InputLength(SafeFileHandle handle)
    {
        if (!HidNative.HidD_GetPreparsedData(handle, out var pre)) return 0;
        try
        {
            return HidNative.HidP_GetCaps(pre, out var caps) == HidNative.HIDP_STATUS_SUCCESS ? caps.InputReportByteLength : 0;
        }
        finally
        {
            HidNative.HidD_FreePreparsedData(pre);
        }
    }

    void Loop()
    {
        var buf = new byte[_length];
        try
        {
            while (true)
            {
                var n = _stream.Read(buf, 0, _length);
                if (n >= 6 && buf[0] == NotificationEvents && buf[1] == DpiChange)
                    DpiChanged?.Invoke(buf[2] << 8 | buf[3]);
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // Mouse desconectado o Dispose: el hilo termina; DeviceService abre otro al reconectar.
        }
    }

    public void Dispose() => _stream.Dispose();
}
