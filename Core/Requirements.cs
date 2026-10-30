using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Colmillo.Core;

/// <summary>
/// Lo que cada componente necesita para que Colmillo lo encuentre, preparado al arrancar:
/// <list type="bullet">
/// <item>RAM: el driver PawnIO arranca "a demanda"; si nadie lo inicio, el SMBus no se abre
/// y la RAM aparece como no disponible. Colmillo lo inicia.</item>
/// <item>RAM: el servicio de Kingston FURY CTRL (lo instala tambien MSI Center) reaplica su
/// efecto y escribe el mismo bus. Dos programas escribiendo a la vez dejan al controlador a
/// medio cambiar (apagado o desfasado). Colmillo lo detiene si el ajuste lo permite.</item>
/// <item>Grafica: no necesita nada. La luz va por la API del driver de AMD (ADL) y los
/// ventiladores por ADLX; Gigabyte Control Center no hace falta y, abierto, pisa la luz.</item>
/// </list>
/// </summary>
public static class Requirements
{
    public const string PawnIoService = "PawnIO";
    public const string FuryService = "FURYController_Service";

    /// <summary>Programas que tambien controlan la luz de la RAM o la grafica.</summary>
    static readonly (string Process, string Name)[] LightingApps =
    [
        ("FURYCTRL", "Kingston FURY CTRL"),
        ("LEDKeeper2", "Mystic Light de MSI Center"),
        ("GCC", "Gigabyte Control Center"),
    ];

    /// <summary>Inicia PawnIO si esta instalado y detenido. Solo con administrador.</summary>
    public static bool EnsurePawnIo() => Services.Start(PawnIoService);

    /// <summary>Detiene el servicio de Kingston si esta corriendo. Solo con administrador.</summary>
    public static bool StopFuryService() => Services.Stop(FuryService);

    /// <summary>
    /// Cierra LEDKeeper2, el proceso de Mystic Light (MSI Center) que reaplica su efecto a la
    /// RAM. Lo lanza una tarea de MSI al iniciar sesion; no se desinstala nada.
    /// </summary>
    public static bool StopLedKeeper()
    {
        var stopped = false;
        foreach (var p in Process.GetProcessesByName("LEDKeeper2"))
        {
            try
            {
                p.Kill();
                stopped = p.WaitForExit(3000);
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            finally { p.Dispose(); }
        }
        return stopped;
    }

    public static bool LedKeeperRunning
    {
        get
        {
            var procs = Process.GetProcessesByName("LEDKeeper2");
            foreach (var p in procs) p.Dispose();
            return procs.Length > 0;
        }
    }

    public static bool FuryServiceRunning => Services.State(FuryService) == Services.Running;

    /// <summary>Nombres de los programas abiertos que pueden pisar la luz.</summary>
    public static List<string> ConflictingApps()
    {
        var found = new List<string>();
        foreach (var (process, name) in LightingApps)
        {
            var procs = Process.GetProcessesByName(process);
            if (procs.Length > 0) found.Add(name);
            foreach (var p in procs) p.Dispose();
        }
        if (FuryServiceRunning) found.Add("servicio de Kingston FURY");
        return found;
    }
}

/// <summary>Administrador de servicios de Windows sin System.ServiceProcess (un paquete aparte).</summary>
static class Services
{
    public const int Stopped = 1, StartPending = 2, StopPending = 3, Running = 4;

    const uint ScManagerConnect = 0x0001;
    const uint ServiceQueryStatus = 0x0004, ServiceStart = 0x0010, ServiceStop = 0x0020;
    const uint ControlStop = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    struct ServiceStatus
    {
        public int Type, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr OpenSCManager(string? machine, string? database, uint access);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr OpenService(IntPtr manager, string name, uint access);

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool QueryServiceStatus(IntPtr service, out ServiceStatus status);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool StartService(IntPtr service, int argc, string[]? argv);

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool ControlService(IntPtr service, uint control, out ServiceStatus status);

    [DllImport("advapi32.dll")]
    static extern bool CloseServiceHandle(IntPtr handle);

    /// <summary>Estado del servicio, o 0 si no existe o no se puede consultar.</summary>
    public static int State(string name) => With(name, ServiceQueryStatus, s => QueryServiceStatus(s, out var st) ? st.CurrentState : 0);

    public static bool Start(string name) => With(name, ServiceQueryStatus | ServiceStart, s =>
    {
        if (!QueryServiceStatus(s, out var st)) return false;
        if (st.CurrentState == Running) return true;
        if (st.CurrentState == Stopped && !StartService(s, 0, null)) return false;
        return WaitFor(s, Running);
    });

    public static bool Stop(string name) => With(name, ServiceQueryStatus | ServiceStop, s =>
    {
        if (!QueryServiceStatus(s, out var st)) return false;
        if (st.CurrentState == Stopped) return true;
        if (st.CurrentState != StopPending && !ControlService(s, ControlStop, out _)) return false;
        return WaitFor(s, Stopped);
    });

    static bool WaitFor(IntPtr service, int state)
    {
        for (var i = 0; i < 50; i++)
        {
            if (QueryServiceStatus(service, out var st) && st.CurrentState == state) return true;
            Thread.Sleep(100);
        }
        return false;
    }

    static T With<T>(string name, uint access, Func<IntPtr, T> action)
    {
        var manager = OpenSCManager(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero) return default!;
        try
        {
            var service = OpenService(manager, name, access);
            if (service == IntPtr.Zero) return default!;
            try { return action(service); }
            finally { CloseServiceHandle(service); }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }
}
