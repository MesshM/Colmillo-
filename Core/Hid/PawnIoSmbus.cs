using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Colmillo.Core.Hid;

public enum SmbusState
{
    Ready,
    /// <summary>PawnIO no esta instalado.</summary>
    NoDriver,
    /// <summary>PawnIO solo atiende a procesos con privilegios de administrador.</summary>
    NeedsAdmin,
    /// <summary>El modulo no reconocio el controlador SMBus de la placa.</summary>
    Unsupported,
}

/// <summary>
/// Bus SMBus de la placa (AMD, compatible PIIX4) a traves de PawnIO, un driver firmado y de
/// codigo abierto. Colmillo trae el modulo SmbusPIIX4.bin de PawnIO.Modules (LGPL-2.1,
/// ver Assets/PawnIO/COPYING). Interfaz del modulo: ioctl_smbus_xfer con
/// [direccion, lectura(1)/escritura(0), comando, protocolo, datos].
/// </summary>
public sealed class PawnIoSmbus : IDisposable
{
    const int ProtoByte = 1;
    const int ProtoByteData = 2;
    const int ProtoWordData = 3;
    // Mutex que comparten todos los programas que tocan el SMBus (OpenRGB, HWiNFO, iCUE,
    // LibreHardwareMonitor...): sin el, dos transferencias simultaneas se pisan.
    const string BusMutexName = @"Global\Access_SMBUS.HTP.Method";

    delegate int OpenFn(out IntPtr handle);
    delegate int LoadFn(IntPtr handle, byte[] blob, UIntPtr size);
    delegate int ExecuteFn(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string name, ulong[] input, UIntPtr inSize, ulong[] output, UIntPtr outSize, out UIntPtr returned);
    delegate int CloseFn(IntPtr handle);

    readonly IntPtr _lib;
    readonly IntPtr _handle;
    readonly ExecuteFn _execute;
    readonly CloseFn _close;
    readonly Mutex _busMutex;

    PawnIoSmbus(IntPtr lib, IntPtr handle, ExecuteFn execute, CloseFn close)
    {
        _lib = lib;
        _handle = handle;
        _execute = execute;
        _close = close;
        _busMutex = new Mutex(false, BusMutexName);
    }

    public static (PawnIoSmbus? Bus, SmbusState State) Open()
    {
        var libPath = FindLibrary();
        if (libPath is null || !NativeLibrary.TryLoad(libPath, out var lib)) return (null, SmbusState.NoDriver);

        var open = Marshal.GetDelegateForFunctionPointer<OpenFn>(NativeLibrary.GetExport(lib, "pawnio_open"));
        var load = Marshal.GetDelegateForFunctionPointer<LoadFn>(NativeLibrary.GetExport(lib, "pawnio_load"));
        var execute = Marshal.GetDelegateForFunctionPointer<ExecuteFn>(NativeLibrary.GetExport(lib, "pawnio_execute"));
        var close = Marshal.GetDelegateForFunctionPointer<CloseFn>(NativeLibrary.GetExport(lib, "pawnio_close"));

        var hr = open(out var handle);
        if (hr != 0)
        {
            NativeLibrary.Free(lib);
            // E_ACCESSDENIED: el driver esta pero el proceso no es administrador.
            return (null, hr == unchecked((int)0x80070005) ? SmbusState.NeedsAdmin : SmbusState.NoDriver);
        }

        var module = LoadModule();
        if (load(handle, module, (UIntPtr)module.Length) != 0)
        {
            close(handle);
            NativeLibrary.Free(lib);
            return (null, SmbusState.Unsupported);
        }
        return (new PawnIoSmbus(lib, handle, execute, close), SmbusState.Ready);
    }

    internal static string? FindLibrary()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
        var dir = key?.GetValue("InstallLocation") as string;
        foreach (var candidate in new[] { dir, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PawnIO") })
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            var path = Path.Combine(candidate, "PawnIOLib.dll");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    static byte[] LoadModule()
    {
        using var stream = typeof(PawnIoSmbus).Assembly.GetManifestResourceStream("Colmillo.SmbusPIIX4.bin")
            ?? throw new InvalidOperationException("Falta el modulo SmbusPIIX4.bin incrustado");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    int Xfer(byte address, bool read, byte command, int protocol, byte data, out ulong result)
    {
        var output = new ulong[1];
        using var bus = Lock();
        var hr = _execute(_handle, "ioctl_smbus_xfer",
            [address, read ? 1UL : 0UL, command, (ulong)protocol, data], (UIntPtr)5,
            output, (UIntPtr)1, out _);
        result = output[0];
        return hr;
    }

    /// <summary>
    /// Toma el bus para una secuencia entera, como MSI Center: con el mutex solo por
    /// transferencia, el servicio de Kingston (FURY CTRL) o Mystic Light se meten entre dos
    /// bytes y el controlador queda a medio cambiar. El mutex de Windows es reentrante, asi
    /// que cada transferencia de adentro lo vuelve a tomar sin bloquearse.
    /// </summary>
    public IDisposable Lock()
    {
        try
        {
            // Otro programa puede quedarse con el bus; pasado el plazo se sigue igual, como MSI.
            return _busMutex.WaitOne(TimeSpan.FromSeconds(2)) ? new Release(_busMutex) : new Release(null);
        }
        catch (AbandonedMutexException)
        {
            // El dueno anterior murio con el bus tomado: el mutex ya es nuestro.
            return new Release(_busMutex);
        }
    }

    sealed class Release(Mutex? mutex) : IDisposable
    {
        public void Dispose() => mutex?.ReleaseMutex();
    }

    /// <summary>
    /// Escribe un byte. Los controladores de luz de la RAM a veces no responden (NACK) si el
    /// bus esta ocupado o todavia procesan el comando anterior: MSI Center reintenta hasta
    /// 30 veces cada escritura, sin rendirse a la quinta como hacia Colmillo.
    /// </summary>
    public bool WriteByte(byte address, byte command, byte value)
    {
        for (var attempt = 1; attempt <= 30; attempt++)
        {
            if (Xfer(address, read: false, command, ProtoByteData, value, out _) == 0) return true;
            Thread.Sleep(attempt < 5 ? 2 : 10);
        }
        return false;
    }

    /// <summary>
    /// Elige la pagina del SPD de DDR4 (0: bytes 0-255, 1: bytes 256-511) con un "send byte"
    /// a 0x36 / 0x37, como CPU-Z o el driver ee1004 de Linux. Algunos modulos no confirman
    /// el comando (NACK) aunque cambian de pagina: el resultado no se mira.
    /// </summary>
    public void SpdPage(int page) => Xfer(page == 0 ? (byte)0x36 : (byte)0x37, read: false, 0, ProtoByte, 0, out _);

    /// <summary>Lee un byte. Null si el dispositivo no responde.</summary>
    public byte? ReadByte(byte address, byte command) =>
        Xfer(address, read: true, command, ProtoByteData, 0, out var value) == 0 ? (byte)value : null;

    /// <summary>Lee una palabra (16 bits). Null si el dispositivo no responde.</summary>
    public ushort? ReadWord(byte address, byte command)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            if (Xfer(address, read: true, command, ProtoWordData, 0, out var value) == 0) return (ushort)value;
            Thread.Sleep(30 * attempt);
        }
        return null;
    }

    public void Dispose()
    {
        _close(_handle);
        NativeLibrary.Free(_lib);
        _busMutex.Dispose();
    }
}
