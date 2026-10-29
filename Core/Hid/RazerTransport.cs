using System.IO;
using Microsoft.Win32.SafeHandles;

namespace Colmillo.Core.Hid;

/// <summary>Respuesta de un comando: estado del firmware y argumentos devueltos.</summary>
public readonly record struct RazerReply(RazerStatus Status, byte[] Args)
{
    public bool Ok => Status == RazerStatus.Success;
}

public enum RazerStatus : byte
{
    New = 0,
    Busy = 1,
    Success = 2,
    Failure = 3,
    Timeout = 4,
    NotSupported = 5,
}

/// <summary>
/// Transporte del protocolo Razer 2.5 sobre reportes feature HID de 90 bytes.
/// Ver PROTOCOLO.md. Un solo hilo a la vez: el firmware no encola comandos.
/// </summary>
public sealed class RazerTransport : IDisposable
{
    const int ReportLength = 91; // report id 0 + 90 bytes
    // Synapse rota el id 0..30; el firmware no lo valida contra nada, 0x1F es lo que usa OpenRazer.
    const byte TransactionId = 0x1F;
    // Tiempos de rzDevice25 en Synapse: 5 ms entre escritura y lectura, hasta 30 lecturas.
    const int SleepOutIn = 5;
    const int MaxRetryIn = 30;

    readonly SafeFileHandle _handle;
    readonly object _lock = new();

    public string Path { get; }

    RazerTransport(SafeFileHandle handle, string path)
    {
        _handle = handle;
        Path = path;
    }

    /// <summary>
    /// Busca la coleccion de control (interfaz 0, reporte feature de 91 bytes) del
    /// dispositivo. Se abre con acceso 0: Windows tiene el mouse en exclusiva, pero
    /// los reportes feature pasan igual.
    /// </summary>
    public static RazerTransport? Open(ushort vendorId, ushort productId)
    {
        var needle = $"vid_{vendorId:x4}&pid_{productId:x4}";
        foreach (var path in HidNative.EnumerateHidPaths())
        {
            var lower = path.ToLowerInvariant();
            if (!lower.Contains(needle) || !lower.Contains("&mi_00")) continue;

            var handle = HidNative.CreateFile(path, 0, HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE,
                IntPtr.Zero, HidNative.OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle.IsInvalid) continue;

            if (FeatureLength(handle) >= ReportLength)
                return new RazerTransport(handle, path);
            handle.Dispose();
        }
        return null;
    }

    static int FeatureLength(SafeFileHandle handle)
    {
        if (!HidNative.HidD_GetPreparsedData(handle, out var pre)) return 0;
        try
        {
            return HidNative.HidP_GetCaps(pre, out var caps) == HidNative.HIDP_STATUS_SUCCESS
                ? caps.FeatureReportByteLength
                : 0;
        }
        finally
        {
            HidNative.HidD_FreePreparsedData(pre);
        }
    }

    /// <summary>Envia un comando y espera la respuesta del firmware.</summary>
    public RazerReply Send(byte commandClass, byte commandId, byte dataSize, ReadOnlySpan<byte> args = default)
    {
        var report = new byte[ReportLength];
        report[2] = TransactionId;
        report[6] = dataSize;
        report[7] = commandClass;
        report[8] = commandId;
        args[..Math.Min(args.Length, 80)].CopyTo(report.AsSpan(9));
        byte crc = 0;
        for (var i = 3; i < 89; i++) crc ^= report[i];
        report[89] = crc;

        lock (_lock)
        {
            // Falla de I/O = el mouse se desconecto; la excepcion la atrapa DeviceService.
            if (!HidNative.HidD_SetFeature(_handle, report, ReportLength))
                throw new IOException("SetFeature fallo");

            var reply = new byte[ReportLength];
            for (var attempt = 0; attempt < MaxRetryIn; attempt++)
            {
                Thread.Sleep(SleepOutIn);
                Array.Clear(reply);
                if (!HidNative.HidD_GetFeature(_handle, reply, ReportLength))
                    throw new IOException("GetFeature fallo");

                var status = (RazerStatus)reply[1];
                if (status == RazerStatus.Busy) continue;
                // Una respuesta de otro comando (p.ej. de Synapse si sigue abierto) no es nuestra.
                if (reply[7] != commandClass || (reply[8] & 0x7F) != (commandId & 0x7F)) continue;
                return new RazerReply(status, reply.AsSpan(9, Math.Min((int)reply[6], 80)).ToArray());
            }
            return new RazerReply(RazerStatus.Timeout, []);
        }
    }

    public void Dispose() => _handle.Dispose();
}
