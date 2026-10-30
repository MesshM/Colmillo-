using System.Runtime.InteropServices;

namespace Colmillo.Core.Gpu;

public readonly record struct FanReadings(double Temperature, double Hotspot, int Rpm);

/// <summary>Perfiles de AMD (los mismos que ofrece Gigabyte Control Center) y el overclock automatico.</summary>
public enum GpuTuning
{
    /// <summary>Lo que dejo otro programa o una combinacion: Colmillo no lo toca.</summary>
    Unknown,
    Quiet,
    Balanced,
    Rage,
    AutoOverclock,
}

public sealed record TuningInfo(bool Quiet, bool Balanced, bool Rage, bool AutoOverclock, GpuTuning Current);

/// <summary>Una lectura de la grafica y del sistema (ADLX). Lo que el driver no da queda en null.</summary>
public sealed record GpuMetrics(
    double? Usage, int? Clock, int? VramClock, int? VramMb, double? BoardPower,
    double? Temperature, double? Hotspot, int? FanRpm, int? Fps, double? CpuUsage, int? SystemRamMb);

public sealed record FanCurve(
    FanPoint[] Current,
    FanPoint[] Default,
    int SpeedMin, int SpeedMax,
    int TempMin, int TempMax,
    bool ZeroRpmSupported, bool ZeroRpm, bool DefaultZeroRpm);

/// <summary>
/// Graficas AMD por ADLX (amdadlx64.dll, viene con el driver), lo mismo que usa Adrenalin:
/// ventiladores, perfiles de tuning, overclock automatico y metricas. En RDNA 4 el Overdrive8 de ADL ya no responde (ADL_ERR_NOT_SUPPORTED).
/// ADLX es C++ con tablas virtuales; se llaman por indice, sacado de los encabezados del
/// SDK (GPUOpen-LibrariesAndSDKs/ADLX, SDK/Include). No necesita administrador.
/// <para>
/// Cada <see cref="Open"/> inicia ADLX y <see cref="Dispose"/> lo cierra: en la bandeja no
/// queda nada cargado.
/// </para>
/// </summary>
public sealed unsafe class AmdGpu : IDisposable
{
    const int AdlxOk = 0;
    // Version del SDK si el driver no informa la suya (2.0.0.125).
    const ulong SdkVersion = 0x000200000000007DUL;

    // IADLXInterface: 0 Acquire, 1 Release, 2 QueryInterface.
    const int Release_ = 1, QueryInterface = 2;
    // IADLXSystem (sin base).
    const int SysGetGpus = 1, SysGetTuning = 8, SysGetPerformance = 9;
    // IADLXGPUList.
    const int ListSize = 3, GpuListAt = 11;
    const int GpuName = 7;
    // IADLXGPUTuningServices.
    const int TuningIsSupportedFan = 10, TuningGetFan = 16;
    // IADLXManualFanTuning / IADLXManualFanTuning1.
    const int FanRanges = 3, FanStates = 4, FanEmptyStates = 5, FanIsValid = 6, FanSet = 7;
    const int FanZeroSupported = 8, FanZeroGet = 9, FanZeroSet = 10;
    const int Fan1DefaultStates = 23, Fan1DefaultZero = 27;
    // IADLXManualFanTuningStateList / State.
    const int StateListAt = 11;
    const int StateGetSpeed = 3, StateSetSpeed = 4, StateGetTemp = 5, StateSetTemp = 6;
    // IADLXPerformanceMonitoringServices / IADLXGPUMetrics.
    const int PerfCurrentGpuMetrics = 18, PerfCurrentSystemMetrics = 19, PerfCurrentFps = 20;
    const int MetricUsage = 4, MetricClock = 5, MetricVramClock = 6, MetricTemp = 7, MetricHotspot = 8;
    const int MetricBoardPower = 10, MetricFanRpm = 11, MetricVram = 12;
    const int SystemCpuUsage = 4, SystemRam = 5, FpsValue = 4;
    // IADLXGPUTuningServices: 5 ResetToFactory, 12 GetAutoTuning, 13 GetPresetTuning.
    const int TuningReset = 5, TuningGetAuto = 12, TuningGetPreset = 13;
    // IADLXGPUAutoTuning: 4 IsSupportedOverclockGPU, 7 IsCurrentOverclockGPU, 10 StartOverclockGPU.
    const int AutoSupportedOc = 4, AutoCurrentOc = 7, AutoStartOc = 10;
    // IADLXGPUPresetTuning: 3-7 IsSupported, 8-12 IsCurrent, 13-17 Set (PowerSaver, Quiet,
    // Balanced, Turbo, Rage). Se suma el desplazamiento del bloque al indice del perfil.
    const int PresetSupported = 0, PresetCurrent = 5, PresetSet = 10;
    const int AdlxNotSupported = 12, AdlxTimeout = 16, AdlxResetNeeded = 18;

    [StructLayout(LayoutKind.Sequential)]
    struct IntRange
    {
        public int Min, Max, Step;
    }

    readonly IntPtr _lib;
    readonly void* _gpu;
    readonly void* _fan;
    readonly void* _fan1;
    readonly void* _perf;
    readonly void* _tuning;
    readonly void* _auto;
    readonly void* _preset;

    public string Name { get; }

    AmdGpu(IntPtr lib, void* gpu, void* fan, void* fan1, void* perf, void* tuning, string name)
    {
        _tuning = tuning;
        _auto = Item(tuning, gpu, TuningGetAuto, "IADLXGPUAutoTuning");
        _preset = Item(tuning, gpu, TuningGetPreset, "IADLXGPUPresetTuning");
        _lib = lib;
        _gpu = gpu;
        _fan = fan;
        _fan1 = fan1;
        _perf = perf;
        Name = name;
    }

    static void* Fn(void* obj, int index) => (*(void***)obj)[index];

    static void Release(void* obj)
    {
        if (obj != null) ((delegate* unmanaged<void*, int>)Fn(obj, Release_))(obj);
    }

    static void* Query(void* obj, string iid)
    {
        void* result = null;
        fixed (char* id = iid)
            ((delegate* unmanaged<void*, char*, void**, int>)Fn(obj, QueryInterface))(obj, id, &result);
        return result;
    }

    /// <summary>Null si no hay driver de AMD o ninguna grafica admite control manual del ventilador.</summary>
    public static AmdGpu? Open()
    {
        if (!NativeLibrary.TryLoad("amdadlx64.dll", out var lib)) return null;
        AmdGpu? fans = null;
        try
        {
            var query = (delegate* unmanaged<ulong*, int>)NativeLibrary.GetExport(lib, "ADLXQueryFullVersion");
            var init = (delegate* unmanaged<ulong, void**, int>)NativeLibrary.GetExport(lib, "ADLXInitialize");
            ulong version;
            if (query(&version) != AdlxOk) version = SdkVersion;
            void* sys;
            if (init(version, &sys) != AdlxOk || sys == null) return null;

            void* gpus = null, tuning = null, perf = null;
            ((delegate* unmanaged<void*, void**, int>)Fn(sys, SysGetGpus))(sys, &gpus);
            ((delegate* unmanaged<void*, void**, int>)Fn(sys, SysGetTuning))(sys, &tuning);
            ((delegate* unmanaged<void*, void**, int>)Fn(sys, SysGetPerformance))(sys, &perf);
            if (gpus == null || tuning == null) return Fail(gpus, tuning, perf);

            var count = ((delegate* unmanaged<void*, uint>)Fn(gpus, ListSize))(gpus);
            for (uint i = 0; i < count && fans is null; i++)
            {
                void* gpu = null;
                if (((delegate* unmanaged<void*, uint, void**, int>)Fn(gpus, GpuListAt))(gpus, i, &gpu) != AdlxOk || gpu == null) continue;
                byte supported = 0;
                ((delegate* unmanaged<void*, void*, byte*, int>)Fn(tuning, TuningIsSupportedFan))(tuning, gpu, &supported);
                void* item = null;
                if (supported != 0)
                    ((delegate* unmanaged<void*, void*, void**, int>)Fn(tuning, TuningGetFan))(tuning, gpu, &item);
                if (item == null)
                {
                    Release(gpu);
                    continue;
                }
                // GetManualFanTuning devuelve la interfaz base: se pide la de ventiladores.
                var fan = Query(item, "IADLXManualFanTuning");
                var fan1 = Query(item, "IADLXManualFanTuning1");
                Release(item);
                if (fan == null)
                {
                    Release(fan1);
                    Release(gpu);
                    continue;
                }
                byte* name = null;
                ((delegate* unmanaged<void*, byte**, int>)Fn(gpu, GpuName))(gpu, &name);
                fans = new AmdGpu(lib, gpu, fan, fan1, perf, tuning, Marshal.PtrToStringAnsi((IntPtr)name) ?? "Gráfica AMD");
            }
            Release(gpus);
            if (fans is null)
            {
                Release(tuning);
                Release(perf);
            }
            return fans;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            if (fans is null)
            {
                Terminate(lib);
                NativeLibrary.Free(lib);
            }
        }

        static AmdGpu? Fail(void* a, void* b, void* c)
        {
            Release(a);
            Release(b);
            Release(c);
            return null;
        }
    }

    public FanCurve ReadCurve()
    {
        IntRange speed, temp;
        ((delegate* unmanaged<void*, IntRange*, IntRange*, int>)Fn(_fan, FanRanges))(_fan, &speed, &temp);
        void* list = null;
        ((delegate* unmanaged<void*, void**, int>)Fn(_fan, FanStates))(_fan, &list);
        var current = Points(list);
        var defaults = current;
        byte zeroSupported = 0, zero = 0, zeroDefault = 1;
        ((delegate* unmanaged<void*, byte*, int>)Fn(_fan, FanZeroSupported))(_fan, &zeroSupported);
        if (zeroSupported != 0) ((delegate* unmanaged<void*, byte*, int>)Fn(_fan, FanZeroGet))(_fan, &zero);
        if (_fan1 != null)
        {
            void* d = null;
            ((delegate* unmanaged<void*, void**, int>)Fn(_fan1, Fan1DefaultStates))(_fan1, &d);
            if (d != null) defaults = Points(d);
            ((delegate* unmanaged<void*, byte*, int>)Fn(_fan1, Fan1DefaultZero))(_fan1, &zeroDefault);
        }
        return new FanCurve(current, defaults, speed.Min, speed.Max, temp.Min, temp.Max,
            zeroSupported != 0, zero != 0, zeroDefault != 0);
    }

    /// <summary>Lee y libera una lista de estados.</summary>
    static FanPoint[] Points(void* list)
    {
        if (list == null) return [];
        var n = ((delegate* unmanaged<void*, uint>)Fn(list, ListSize))(list);
        var points = new FanPoint[n];
        for (uint i = 0; i < n; i++)
        {
            void* state = null;
            ((delegate* unmanaged<void*, uint, void**, int>)Fn(list, StateListAt))(list, i, &state);
            int s = 0, t = 0;
            if (state != null)
            {
                ((delegate* unmanaged<void*, int*, int>)Fn(state, StateGetSpeed))(state, &s);
                ((delegate* unmanaged<void*, int*, int>)Fn(state, StateGetTemp))(state, &t);
                Release(state);
            }
            points[i] = new FanPoint(t, s);
        }
        Release(list);
        return points;
    }

    /// <summary>
    /// Escribe la curva (tantos puntos como pide el driver, en la practica 5) y el modo de
    /// cero RPM. Devuelve null si salio bien, o el motivo.
    /// </summary>
    public string? SetCurve(IReadOnlyList<FanPoint> points, bool zeroRpm)
    {
        void* list = null;
        if (((delegate* unmanaged<void*, void**, int>)Fn(_fan, FanEmptyStates))(_fan, &list) != AdlxOk || list == null)
            return "El driver no entregó la curva para editar.";
        try
        {
            var n = ((delegate* unmanaged<void*, uint>)Fn(list, ListSize))(list);
            if (n != points.Count) return $"El driver espera {n} puntos.";
            for (uint i = 0; i < n; i++)
            {
                void* state = null;
                ((delegate* unmanaged<void*, uint, void**, int>)Fn(list, StateListAt))(list, i, &state);
                if (state == null) return "El driver no entregó la curva para editar.";
                ((delegate* unmanaged<void*, int, int>)Fn(state, StateSetSpeed))(state, points[(int)i].Speed);
                ((delegate* unmanaged<void*, int, int>)Fn(state, StateSetTemp))(state, points[(int)i].Temperature);
                Release(state);
            }
            var bad = -1;
            if (((delegate* unmanaged<void*, void*, int*, int>)Fn(_fan, FanIsValid))(_fan, list, &bad) != AdlxOk)
                return bad >= 0 ? $"El driver rechazó el punto {bad + 1}." : "El driver rechazó la curva.";
            if (((delegate* unmanaged<void*, void*, int>)Fn(_fan, FanSet))(_fan, list) != AdlxOk)
                return "El driver no aceptó la curva.";
        }
        finally
        {
            Release(list);
        }
        byte supported = 0;
        ((delegate* unmanaged<void*, byte*, int>)Fn(_fan, FanZeroSupported))(_fan, &supported);
        if (supported != 0) ((delegate* unmanaged<void*, byte, int>)Fn(_fan, FanZeroSet))(_fan, (byte)(zeroRpm ? 1 : 0));
        return null;
    }

    /// <summary>Pide una interfaz de tuning (devuelve la base) y la convierte a la que se usa.</summary>
    static void* Item(void* tuning, void* gpu, int index, string iid)
    {
        void* item = null;
        ((delegate* unmanaged<void*, void*, void**, int>)Fn(tuning, index))(tuning, gpu, &item);
        if (item == null) return null;
        var result = Query(item, iid);
        Release(item);
        return result;
    }

    static bool Flag(void* obj, int index)
    {
        if (obj == null) return false;
        byte value = 0;
        return ((delegate* unmanaged<void*, byte*, int>)Fn(obj, index))(obj, &value) == AdlxOk && value != 0;
    }

    static int PresetIndex(GpuTuning t) => t switch { GpuTuning.Quiet => 4, GpuTuning.Balanced => 5, _ => 7 };

    public TuningInfo ReadTuning()
    {
        var current = Flag(_auto, AutoCurrentOc) ? GpuTuning.AutoOverclock
            : Flag(_preset, PresetIndex(GpuTuning.Rage) + PresetCurrent) ? GpuTuning.Rage
            : Flag(_preset, PresetIndex(GpuTuning.Quiet) + PresetCurrent) ? GpuTuning.Quiet
            : Flag(_preset, PresetIndex(GpuTuning.Balanced) + PresetCurrent) ? GpuTuning.Balanced
            : GpuTuning.Unknown;
        return new TuningInfo(
            Flag(_preset, PresetIndex(GpuTuning.Quiet) + PresetSupported),
            Flag(_preset, PresetIndex(GpuTuning.Balanced) + PresetSupported),
            Flag(_preset, PresetIndex(GpuTuning.Rage) + PresetSupported),
            Flag(_auto, AutoSupportedOc),
            current);
    }

    /// <summary>
    /// Aplica un perfil. El overclock automatico lo calcula el driver en segundo plano: se
    /// espera su aviso (hasta 2 min). Si un tuning automatico anterior lo impide, el driver pide
    /// volver a fabrica (borra tambien la curva del ventilador: quien llama la reaplica).
    /// Null si salio bien.
    /// </summary>
    public string? SetTuning(GpuTuning tuning, out bool wasReset)
    {
        wasReset = false;
        var r = Start(tuning);
        if (r == AdlxResetNeeded)
        {
            ((delegate* unmanaged<void*, void*, int>)Fn(_tuning, TuningReset))(_tuning, _gpu);
            wasReset = true;
            r = Start(tuning);
        }
        return r switch
        {
            AdlxOk => null,
            AdlxNotSupported => "La gráfica no admite este modo.",
            AdlxTimeout => "El overclock automático no terminó a tiempo.",
            _ => $"El driver no aceptó el cambio (código {r}).",
        };
    }

    int Start(GpuTuning tuning)
    {
        if (tuning != GpuTuning.AutoOverclock)
            return _preset == null ? AdlxNotSupported : ((delegate* unmanaged<void*, int>)Fn(_preset, PresetIndex(tuning) + PresetSet))(_preset);
        if (_auto == null) return AdlxNotSupported;
        var listener = TuningListener.Create();
        try
        {
            var r = ((delegate* unmanaged<void*, void*, int>)Fn(_auto, AutoStartOc))(_auto, listener);
            if (r != AdlxOk) return r;
            for (var i = 0; i < 1200 && !TuningListener.Done(listener); i++) Thread.Sleep(100);
            return TuningListener.Done(listener) || Flag(_auto, AutoCurrentOc) ? AdlxOk : AdlxTimeout;
        }
        finally
        {
            TuningListener.Free(listener);
        }
    }

    /// <summary>
    /// IADLXGPUAutoTuningCompleteListener en memoria nativa: puntero a una tabla con un solo
    /// metodo (OnGPUAutoTuningComplete) y, detras, la marca de terminado.
    /// </summary>
    static class TuningListener
    {
        static readonly IntPtr Vtbl = CreateVtbl();

        static IntPtr CreateVtbl()
        {
            var v = Marshal.AllocHGlobal(IntPtr.Size);
            *(IntPtr*)v = (IntPtr)(delegate* unmanaged<void*, void*, byte>)&OnComplete;
            return v;
        }

        [UnmanagedCallersOnly]
        static byte OnComplete(void* self, void* e)
        {
            Volatile.Write(ref ((int*)self)[2], 1);
            return 1;
        }

        public static void* Create()
        {
            var p = (void*)Marshal.AllocHGlobal(16);
            *(IntPtr*)p = Vtbl;
            ((int*)p)[2] = 0;
            return p;
        }

        public static bool Done(void* p) => Volatile.Read(ref ((int*)p)[2]) != 0;
        public static void Free(void* p) => Marshal.FreeHGlobal((IntPtr)p);
    }

    public GpuMetrics ReadMetrics()
    {
        double? usage = null, temp = null, hotspot = null, power = null, cpu = null;
        int? clock = null, vclock = null, vram = null, rpm = null, fps = null, ram = null;
        void* m = null;
        if (_perf != null && ((delegate* unmanaged<void*, void*, void**, int>)Fn(_perf, PerfCurrentGpuMetrics))(_perf, _gpu, &m) == AdlxOk && m != null)
        {
            usage = D(m, MetricUsage);
            clock = I(m, MetricClock);
            vclock = I(m, MetricVramClock);
            temp = D(m, MetricTemp);
            hotspot = D(m, MetricHotspot);
            power = D(m, MetricBoardPower);
            rpm = I(m, MetricFanRpm);
            vram = I(m, MetricVram);
            Release(m);
        }
        void* f = null;
        // Sin un juego 3D abierto el driver no da FPS (devuelve -1).
        if (_perf != null && ((delegate* unmanaged<void*, void**, int>)Fn(_perf, PerfCurrentFps))(_perf, &f) == AdlxOk && f != null)
        {
            if (I(f, FpsValue) is > 0 and var v) fps = v;
            Release(f);
        }
        void* sys = null;
        if (_perf != null && ((delegate* unmanaged<void*, void**, int>)Fn(_perf, PerfCurrentSystemMetrics))(_perf, &sys) == AdlxOk && sys != null)
        {
            cpu = D(sys, SystemCpuUsage);
            ram = I(sys, SystemRam);
            Release(sys);
        }
        return new GpuMetrics(usage, clock, vclock, vram, power, temp, hotspot, rpm, fps, cpu, ram);

        static double? D(void* o, int i)
        {
            double v;
            return ((delegate* unmanaged<void*, double*, int>)Fn(o, i))(o, &v) == AdlxOk ? v : null;
        }

        static int? I(void* o, int i)
        {
            int v;
            return ((delegate* unmanaged<void*, int*, int>)Fn(o, i))(o, &v) == AdlxOk ? v : null;
        }
    }

    public FanReadings Read()
    {
        if (_perf == null) return default;
        void* metrics = null;
        if (((delegate* unmanaged<void*, void*, void**, int>)Fn(_perf, PerfCurrentGpuMetrics))(_perf, _gpu, &metrics) != AdlxOk || metrics == null)
            return default;
        double temp = 0, hotspot = 0;
        int rpm = 0;
        ((delegate* unmanaged<void*, double*, int>)Fn(metrics, MetricTemp))(metrics, &temp);
        ((delegate* unmanaged<void*, double*, int>)Fn(metrics, MetricHotspot))(metrics, &hotspot);
        ((delegate* unmanaged<void*, int*, int>)Fn(metrics, MetricFanRpm))(metrics, &rpm);
        Release(metrics);
        return new FanReadings(temp, hotspot, rpm);
    }

    static void Terminate(IntPtr lib)
    {
        if (NativeLibrary.TryGetExport(lib, "ADLXTerminate", out var fn)) ((delegate* unmanaged<int>)fn)();
    }

    public void Dispose()
    {
        // Todas las interfaces se sueltan antes de cerrar ADLX.
        Release(_auto);
        Release(_preset);
        Release(_tuning);
        Release(_fan1);
        Release(_fan);
        Release(_perf);
        Release(_gpu);
        Terminate(_lib);
        NativeLibrary.Free(_lib);
    }
}
