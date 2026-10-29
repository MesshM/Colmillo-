using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Colmillo.Core.Windows;

public sealed record CursorScheme(string Name, string[] Files, bool IsSystem);

/// <summary>
/// Esquemas de punteros de Windows. Un esquema es una lista de 17 rutas separadas
/// por coma, en el orden de <see cref="Roles"/>; se aplica copiando cada ruta a
/// HKCU\Control Panel\Cursors y avisando con SPI_SETCURSORS.
/// </summary>
public static class CursorSchemes
{
    public sealed record Role(string Key, string Label);

    /// <summary>Orden fijo del valor de esquema en el registro (ver main.cpl).</summary>
    public static readonly Role[] Roles =
    [
        new("Arrow", "Selección normal"),
        new("Help", "Selección de ayuda"),
        new("AppStarting", "Trabajando en segundo plano"),
        new("Wait", "Ocupado"),
        new("Crosshair", "Selección de precisión"),
        new("IBeam", "Selección de texto"),
        new("NWPen", "Escritura a mano"),
        new("No", "No disponible"),
        new("SizeNS", "Cambio de tamaño vertical"),
        new("SizeWE", "Cambio de tamaño horizontal"),
        new("SizeNWSE", "Cambio de tamaño diagonal 1"),
        new("SizeNESW", "Cambio de tamaño diagonal 2"),
        new("SizeAll", "Mover"),
        new("UpArrow", "Selección alternativa"),
        new("Hand", "Selección de vínculo"),
        new("Pin", "Selección de ubicación"),
        new("Person", "Selección de persona"),
    ];

    const string UserCursors = @"Control Panel\Cursors";
    const string UserSchemes = @"Control Panel\Cursors\Schemes";
    const string SystemSchemes = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\Cursors\Schemes";
    const uint SPI_SETCURSORS = 0x0057;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SystemParametersInfo(uint action, uint param, IntPtr pv, uint winIni);

    public static List<CursorScheme> List()
    {
        var list = new List<CursorScheme>();
        Read(Registry.LocalMachine, SystemSchemes, isSystem: true, list);
        Read(Registry.CurrentUser, UserSchemes, isSystem: false, list);
        return list.OrderBy(s => s.IsSystem ? 0 : 1).ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    static void Read(RegistryKey root, string path, bool isSystem, List<CursorScheme> into)
    {
        using var key = root.OpenSubKey(path);
        if (key is null) return;
        foreach (var name in key.GetValueNames())
        {
            if (key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string raw) continue;
            var files = raw.Split(',');
            Array.Resize(ref files, Roles.Length);
            into.Add(new CursorScheme(name, files.Select(f => f ?? "").ToArray(), isSystem));
        }
    }

    /// <summary>Nombre del esquema activo ("" = Ninguno / personalizado sin guardar).</summary>
    public static string Current()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UserCursors);
        return key?.GetValue("") as string ?? "";
    }

    public static string[] CurrentFiles()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UserCursors);
        return Roles.Select(r => key?.GetValue(r.Key, "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "").ToArray();
    }

    public static void Apply(CursorScheme scheme)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(UserCursors))
        {
            for (var i = 0; i < Roles.Length; i++)
                key.SetValue(Roles[i].Key, scheme.Files[i] ?? "", RegistryValueKind.ExpandString);
            key.SetValue("", scheme.Name);
            // 1 = esquema del sistema, 2 = del usuario. Lo lee main.cpl para mostrar el combo.
            key.SetValue("Scheme Source", scheme.IsSystem ? 1 : 2, RegistryValueKind.DWord);
        }
        Reload();
    }

    /// <summary>Guarda un esquema propio con los archivos dados y lo aplica.</summary>
    public static CursorScheme SaveUserScheme(string name, string[] files)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(UserSchemes))
            key.SetValue(name, string.Join(",", files), RegistryValueKind.ExpandString);
        var scheme = new CursorScheme(name, files, IsSystem: false);
        Apply(scheme);
        return scheme;
    }

    public static void DeleteUserScheme(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(UserSchemes, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    /// <summary>Vuelve a los punteros de Windows (Aero), como el boton "Usar predeterminado".</summary>
    public static void ResetToDefault()
    {
        var aero = List().FirstOrDefault(s => s.IsSystem && s.Name.StartsWith("Windows Default", StringComparison.OrdinalIgnoreCase))
                   ?? List().FirstOrDefault(s => s.IsSystem);
        if (aero is not null) Apply(aero);
    }

    // SPIF_UPDATEINIFILE | SPIF_SENDCHANGE: recarga desde el registro y avisa.
    static void Reload() => SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0x03);

    public static string Expand(string path) => Environment.ExpandEnvironmentVariables(path ?? "");
}
