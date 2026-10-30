using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Colmillo.Shell;

public static class SystemIntegration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "Colmillo";

    [DllImport("psapi.dll")]
    static extern bool EmptyWorkingSet(IntPtr process);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    // Dos tareas: una al iniciar sesion (bandeja) y otra sin disparador que usa el acceso
    // directo para abrir la ventana elevada sin UAC. Una sola tarea con una marca en HKCU
    // no funciono: la copia que lanza el Programador de tareas no veia el valor escrito
    // por la copia sin elevar.
    const string TaskName = "Colmillo";
    const string OpenTaskName = "Colmillo Abrir";

    public static bool IsElevated
    {
        get
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
    }

    public static bool HasStartupTask => Schtasks($"/Query /TN \"{OpenTaskName}\"") == 0;

    /// <summary>
    /// Inicio con Windows. Elevado: tarea programada al iniciar sesion con privilegios
    /// maximos, que arranca Colmillo como administrador sin aviso UAC (PawnIO lo exige
    /// para la RAM). Sin elevar: la clave Run de siempre.
    /// </summary>
    public static void SetStartWithWindows(bool enabled)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (enabled && !IsElevated && !HasStartupTask)
                key.SetValue(RunName, $"\"{Environment.ProcessPath}\" --bandeja");
            else
                key.DeleteValue(RunName, throwOnMissingValue: false);
        }
        if (!IsElevated) return;
        // La de abrir existe siempre que se haya elevado una vez: sirve aunque no se quiera
        // el inicio con Windows.
        CreateTask(OpenTaskName, logon: false, args: "");
        if (enabled) CreateTask(TaskName, logon: true, args: "--bandeja");
        else Schtasks($"/Delete /TN \"{TaskName}\" /F");
    }

    static void CreateTask(string name, bool logon, string args)
    {
        var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        // XML y no /Create con argumentos: por defecto Windows corta una tarea a las 72 h,
        // no la arranca con bateria y le da prioridad baja (7), lo que tambien baja la de
        // los macros. Parallel: abrir Colmillo a mano tiene que poder lanzar otra copia.
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Inicia Colmillo como administrador para controlar la RAM.</Description></RegistrationInfo>
              <Triggers>{(logon ? $"<LogonTrigger><Enabled>true</Enabled><UserId>{System.Security.SecurityElement.Escape(user)}</UserId></LogonTrigger>" : "")}</Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{System.Security.SecurityElement.Escape(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>Parallel</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{System.Security.SecurityElement.Escape(Environment.ProcessPath)}</Command>
                  <Arguments>{args}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
        var file = Path.Combine(Path.GetTempPath(), "colmillo-tarea.xml");
        File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
        try { Schtasks($"/Create /TN \"{name}\" /XML \"{file}\" /F"); }
        finally { File.Delete(file); }
    }

    /// <summary>Desinstalacion: quita las dos tareas y la clave Run.</summary>
    public static void RemoveStartup()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
            key?.DeleteValue(RunName, throwOnMissingValue: false);
        Schtasks($"/Delete /TN \"{TaskName}\" /F");
        Schtasks($"/Delete /TN \"{OpenTaskName}\" /F");
    }

    /// <summary>
    /// Arranca la copia elevada por una tarea, sin UAC. <paramref name="tray"/>: la de inicio
    /// (sin ventana) si existe; si no, la de abrir.
    /// </summary>
    public static bool RunStartupTask(bool tray = false) =>
        (tray && Schtasks($"/Run /TN \"{TaskName}\"") == 0) || Schtasks($"/Run /TN \"{OpenTaskName}\"") == 0;

    /// <summary>Relanza esta app como administrador (aviso UAC). False si el usuario dijo que no.</summary>
    public static bool StartElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--relanzado") { UseShellExecute = true, Verb = "runas" })?.Dispose();
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    static int Schtasks(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        p.WaitForExit(10_000);
        return p.HasExited ? p.ExitCode : -1;
    }

    /// <summary>
    /// Al cerrar la ventana la app queda solo en la bandeja: se junta la basura y se
    /// le devuelve al sistema la memoria de la UI que ya no se usa.
    /// </summary>
    public static void TrimMemory()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        using var self = Process.GetCurrentProcess();
        EmptyWorkingSet(self.Handle);
    }

    public static long WorkingSetMb()
    {
        using var self = Process.GetCurrentProcess();
        return self.WorkingSet64 / (1024 * 1024);
    }

    public static long PrivateMb()
    {
        using var self = Process.GetCurrentProcess();
        return self.PrivateMemorySize64 / (1024 * 1024);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    /// <summary>
    /// Una ventana con WindowStyle=None no tiene WS_CAPTION ni los botones de sistema, y DWM
    /// no anima minimizar/maximizar/restaurar. Con los estilos de vuelta las animaciones son
    /// las de Windows; WindowChrome sigue ocupando todo el area y no se ve barra de titulo.
    /// </summary>
    public static void EnableWindowAnimations(IntPtr hwnd)
    {
        const int GwlStyle = -16;
        const long WsCaption = 0x00C00000, WsSysMenu = 0x00080000, WsMinimizeBox = 0x00020000, WsMaximizeBox = 0x00010000;
        var style = (long)GetWindowLongPtr(hwnd, GwlStyle);
        SetWindowLongPtr(hwnd, GwlStyle, (IntPtr)(style | WsCaption | WsSysMenu | WsMinimizeBox | WsMaximizeBox));
    }

    /// <summary>Esquinas redondeadas de Windows 11 para una ventana sin marco.</summary>
    public static void RoundCorners(IntPtr hwnd)
    {
        var pref = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, 33, ref pref, sizeof(int));
    }

    /// <summary>Tema de apps de Windows (Configuracion > Personalizacion > Colores).</summary>
    public static bool SystemUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not int v || v != 0;
    }
}
