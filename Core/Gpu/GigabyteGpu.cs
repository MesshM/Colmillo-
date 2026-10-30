using System.Runtime.InteropServices;
using Colmillo.Core.Razer;

namespace Colmillo.Core.Gpu;

/// <summary>Efectos del firmware; el valor es el id que va en el paquete.</summary>
public enum GpuEffect
{
    Off = 0,
    Static = 1,
    Pulse = 2,
    Flash = 3,
    DoubleFlash = 4,
    Cycle = 5,
}

/// <summary>
/// Luz de las graficas Gigabyte con el controlador RGB Fusion nuevo (RX 9060 XT y
/// otras con "UiId 1" en el device.db de Gigabyte). Va por I2C de la API del driver de
/// AMD (ADL), linea 1, direccion 0x75, con paquetes de 64 bytes. Sacado de ucVga.dll de
/// Gigabyte Control Center (GvLedApiEx/I2CApiEx). Ver PROTOCOLO.md.
/// <para>
/// No queda nada cargado entre usos: cada <see cref="Open"/> carga atiadlxx.dll y
/// <see cref="Dispose"/> la libera.
/// </para>
/// </summary>
public sealed unsafe class GigabyteGpu : IDisposable
{
    const int Line = 1;
    const int Address = 0x75 << 1;
    const int ActionRead = 1, ActionWrite = 2;
    // Velocidades del bus en kHz, las mismas que usa Gigabyte.
    const int WriteSpeed = 50, ReadSpeed = 100;
    const int PacketSize = 64;
    // 0x10 (version) tambien existe, pero el controlador responde una sola lectura por
    // sesion de ADL: la siguiente falla hasta reabrir. Solo se lee el modelo.
    const byte CmdModel = 0x11, CmdSet = 0x12, CmdSave = 0x13;
    // 0x18 "LedAPPClose": saca al controlador del modo en que lo maneja una app (la
    // sincronizacion de RGB Fusion, 0x16 cuadro a cuadro). Tras reiniciar la PC arranca asi
    // y no hace caso a 0x12 hasta recibirlo: por eso solo funcionaba con RGB Fusion abierto.
    // Es lo que hace Gigabyte Control Center (LedControlEx.SettingApply) antes de cada cambio.
    const byte CmdAppClose = 0x18;

    [StructLayout(LayoutKind.Sequential)]
    struct AdlI2C
    {
        public int Size, Line, Address, Offset, Action, Speed, DataSize;
        public byte* Data;
    }

    readonly IntPtr _lib;
    readonly IntPtr _ctx;
    readonly delegate* unmanaged<IntPtr, int, AdlI2C*, int> _i2c;
    readonly delegate* unmanaged<IntPtr, int> _destroy;

    public int Adapter { get; private set; } = -1;

    GigabyteGpu(IntPtr lib, IntPtr ctx)
    {
        _lib = lib;
        _ctx = ctx;
        _i2c = (delegate* unmanaged<IntPtr, int, AdlI2C*, int>)NativeLibrary.GetExport(lib, "ADL2_Display_WriteAndReadI2C");
        _destroy = (delegate* unmanaged<IntPtr, int>)NativeLibrary.GetExport(lib, "ADL2_Main_Control_Destroy");
    }

    /// <summary>
    /// Abre ADL y busca la grafica. Con <paramref name="adapter"/> (de una apertura
    /// anterior) no vuelve a buscar. Devuelve null si no hay driver de AMD o ninguna
    /// grafica responde con el controlador de Gigabyte.
    /// </summary>
    public static GigabyteGpu? Open(int? adapter = null)
    {
        if (!NativeLibrary.TryLoad("atiadlxx.dll", out var lib)) return null;
        GigabyteGpu? gpu = null;
        try
        {
            var create = (delegate* unmanaged<delegate* unmanaged<int, IntPtr>, int, IntPtr*, int>)NativeLibrary.GetExport(lib, "ADL2_Main_Control_Create");
            var count = (delegate* unmanaged<IntPtr, int*, int>)NativeLibrary.GetExport(lib, "ADL2_Adapter_NumberOfAdapters_Get");
            IntPtr ctx;
            if (create(&Alloc, 1, &ctx) != 0) return null;
            gpu = new GigabyteGpu(lib, ctx);
            if (adapter is not null)
            {
                gpu.Adapter = adapter.Value;
                return gpu;
            }
            int n;
            if (count(ctx, &n) != 0) n = 0;
            // Una grafica aparece como varios adaptadores (uno por salida): sirve el primero que responda.
            for (var i = 0; i < n; i++)
            {
                if (gpu.Query(i, CmdModel) is not { } model || (model[2] | model[3]) == 0) continue;
                gpu.Adapter = i;
                return gpu;
            }
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            if (gpu is null) NativeLibrary.Free(lib);
            else if (gpu.Adapter < 0) gpu.Dispose();
        }
    }

    /// <summary>
    /// Cambia el efecto al instante (no se guarda en la grafica: ver <see cref="Save"/>).
    /// Brillo 0-100 y velocidad 1-5, como el resto de Colmillo; el firmware usa 0-10.
    /// </summary>
    /// <param name="start">Se llama justo antes del comando que arranca el efecto (sincronia).</param>
    public bool Apply(GpuEffect effect, Rgb color, int brightness, int speed, Action? start = null)
    {
        // Sin leer 0x17: en esta tarjeta esa lectura falla y deja la sesion de ADL sin poder
        // escribir (probado); Gigabyte espera 200 ms tras 0x18 y sigue igual si falla.
        if (Write(Packet(CmdAppClose))) Thread.Sleep(80);
        var c = effect == GpuEffect.Off ? default : color.ToLed();
        var p = Packet(CmdSet);
        p[2] = (byte)effect;
        p[3] = (byte)(Math.Clamp(speed, 1, 5) * 2);
        p[4] = (byte)Math.Clamp((int)Math.Round(brightness / 10.0), 0, 10);
        p[5] = c.R;
        p[6] = c.G;
        p[7] = c.B;
        start?.Invoke();
        return Write(p);
    }

    /// <summary>Guarda el efecto actual en la memoria de la grafica (sigue sin Colmillo).</summary>
    public bool Save() => Write(Packet(CmdSave));

    static byte[] Packet(byte command)
    {
        var p = new byte[PacketSize];
        p[0] = command;
        p[1] = 1;
        return p;
    }

    bool Write(byte[] packet)
    {
        // Gigabyte reintenta una vez; el bus a veces rechaza la primera escritura.
        for (var i = 0; i < 3; i++)
        {
            if (Transfer(Adapter, ActionWrite, WriteSpeed, packet) == 0)
            {
                Thread.Sleep(120);
                return true;
            }
            Thread.Sleep(30);
        }
        return false;
    }

    /// <summary>Comando de lectura: se escribe el pedido y se leen 4 bytes de respuesta.</summary>
    byte[]? Query(int adapter, byte command)
    {
        if (Transfer(adapter, ActionWrite, WriteSpeed, Packet(command)) != 0) return null;
        Thread.Sleep(50);
        var answer = new byte[4];
        var ok = Transfer(adapter, ActionRead, ReadSpeed, answer) == 0;
        Thread.Sleep(200);
        return ok ? answer : null;
    }

    int Transfer(int adapter, int action, int speed, byte[] data)
    {
        fixed (byte* p = data)
        {
            var r = new AdlI2C
            {
                Size = sizeof(AdlI2C),
                Line = Line,
                Address = Address,
                Action = action,
                Speed = speed,
                DataSize = data.Length,
                Data = p,
            };
            return _i2c(_ctx, adapter, &r);
        }
    }

    [UnmanagedCallersOnly]
    static IntPtr Alloc(int size) => Marshal.AllocCoTaskMem(size);

    public void Dispose()
    {
        _destroy(_ctx);
        NativeLibrary.Free(_lib);
    }
}
