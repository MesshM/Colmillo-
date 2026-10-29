using System.Diagnostics;
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

    public static void SetStartWithWindows(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(RunName, $"\"{Environment.ProcessPath}\" --bandeja");
        else
            key.DeleteValue(RunName, throwOnMissingValue: false);
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
