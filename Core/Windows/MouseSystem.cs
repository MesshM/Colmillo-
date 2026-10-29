using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Colmillo.Core.Windows;

/// <summary>
/// Ajustes de puntero que Windows guarda por usuario. Todos se aplican con
/// SystemParametersInfo + SPIF_UPDATEINIFILE: persisten en HKCU y avisan a las
/// ventanas abiertas, igual que el Panel de control.
/// </summary>
public static class MouseSystem
{
    const uint SPI_GETMOUSE = 0x0003, SPI_SETMOUSE = 0x0004;
    const uint SPI_SETDOUBLECLICKTIME = 0x0020;
    const uint SPI_SETMOUSEBUTTONSWAP = 0x0021;
    const uint SPI_GETMOUSETRAILS = 0x005E, SPI_SETMOUSETRAILS = 0x005D;
    const uint SPI_GETWHEELSCROLLLINES = 0x0068, SPI_SETWHEELSCROLLLINES = 0x0069;
    const uint SPI_GETMOUSESPEED = 0x0070, SPI_SETMOUSESPEED = 0x0071;
    const uint SPI_GETWHEELSCROLLCHARS = 0x006C, SPI_SETWHEELSCROLLCHARS = 0x006D;
    const uint SPI_GETSNAPTODEFBUTTON = 0x005F, SPI_SETSNAPTODEFBUTTON = 0x0060;
    const uint SPI_GETMOUSESONAR = 0x101C, SPI_SETMOUSESONAR = 0x101D;
    const uint SPI_GETMOUSEVANISH = 0x1020, SPI_SETMOUSEVANISH = 0x1021;
    // No documentado, es el que usa Configuracion > Accesibilidad > Puntero del mouse.
    const uint SPI_GETCURSORBASESIZE = 0x2028, SPI_SETCURSORBASESIZE = 0x2029;
    const uint SPIF_UPDATEINIFILE = 0x01, SPIF_SENDCHANGE = 0x02;
    const uint Persist = SPIF_UPDATEINIFILE | SPIF_SENDCHANGE;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SystemParametersInfo(uint action, uint param, IntPtr pv, uint winIni);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SystemParametersInfo(uint action, uint param, int[] pv, uint winIni);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SystemParametersInfo(uint action, uint param, out int pv, uint winIni);

    [DllImport("user32.dll")]
    static extern uint GetDoubleClickTime();

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int index);

    static int GetInt(uint action) => SystemParametersInfo(action, 0, out int v, 0) ? v : 0;
    static bool GetBool(uint action) => GetInt(action) != 0;
    static void SetByParam(uint action, int value) => SystemParametersInfo(action, (uint)value, IntPtr.Zero, Persist);
    static void SetByPointer(uint action, int value) => SystemParametersInfo(action, 0, new IntPtr(value), Persist);

    /// <summary>Velocidad del puntero, 1-20 (10 es el valor de fabrica, sin escala).</summary>
    public static int PointerSpeed
    {
        get => GetInt(SPI_GETMOUSESPEED);
        // SPI_SETMOUSESPEED recibe el valor en pvParam, no en uiParam.
        set => SetByPointer(SPI_SETMOUSESPEED, Math.Clamp(value, 1, 20));
    }

    /// <summary>"Mejorar la precision del puntero" (aceleracion de Windows).</summary>
    public static bool EnhancePrecision
    {
        get
        {
            var v = new int[3];
            return SystemParametersInfo(SPI_GETMOUSE, 0, v, 0) && v[2] != 0;
        }
        // 6/10/1 son los umbrales que pone el Panel de control al activarla.
        set => SystemParametersInfo(SPI_SETMOUSE, 0, value ? [6, 10, 1] : [0, 0, 0], Persist);
    }

    /// <summary>Lineas por muesca de la rueda. -1 = una pantalla por muesca.</summary>
    public static int ScrollLines
    {
        get => GetInt(SPI_GETWHEELSCROLLLINES);
        set => SetByParam(SPI_SETWHEELSCROLLLINES, value < 0 ? unchecked((int)0xFFFFFFFF) : Math.Clamp(value, 1, 100));
    }

    public static int ScrollChars
    {
        get => GetInt(SPI_GETWHEELSCROLLCHARS);
        set => SetByParam(SPI_SETWHEELSCROLLCHARS, Math.Clamp(value, 1, 100));
    }

    /// <summary>Tiempo maximo entre clics de un doble clic, 200-900 ms.</summary>
    public static int DoubleClickTime
    {
        get => (int)GetDoubleClickTime();
        set => SetByParam(SPI_SETDOUBLECLICKTIME, Math.Clamp(value, 200, 900));
    }

    /// <summary>Intercambiar botones primario y secundario (zurdos).</summary>
    public static bool SwapButtons
    {
        get => GetSystemMetrics(23) != 0; // SM_SWAPBUTTON
        set => SetByParam(SPI_SETMOUSEBUTTONSWAP, value ? 1 : 0);
    }

    /// <summary>Mostrar la ubicacion del puntero al apretar Ctrl.</summary>
    public static bool Sonar
    {
        get => GetBool(SPI_GETMOUSESONAR);
        set => SetByPointer(SPI_SETMOUSESONAR, value ? 1 : 0);
    }

    /// <summary>Ocultar el puntero mientras se escribe.</summary>
    public static bool HideWhileTyping
    {
        get => GetBool(SPI_GETMOUSEVANISH);
        set => SetByPointer(SPI_SETMOUSEVANISH, value ? 1 : 0);
    }

    /// <summary>Saltar al boton predeterminado de los dialogos.</summary>
    public static bool SnapToDefault
    {
        get => GetBool(SPI_GETSNAPTODEFBUTTON);
        set => SetByParam(SPI_SETSNAPTODEFBUTTON, value ? 1 : 0);
    }

    /// <summary>Estela del puntero: 0 apagada, 2-7 largo.</summary>
    public static int Trails
    {
        get => GetInt(SPI_GETMOUSETRAILS);
        set => SetByParam(SPI_SETMOUSETRAILS, value <= 1 ? 0 : Math.Min(value, 7));
    }

    /// <summary>Tamano del puntero 1-15, la misma escala que Configuracion de Windows.</summary>
    public static int CursorSize
    {
        get
        {
            var px = GetInt(SPI_GETCURSORBASESIZE);
            return px <= 0 ? 1 : Math.Clamp((px - 32) / 16 + 1, 1, 15);
        }
        set
        {
            value = Math.Clamp(value, 1, 15);
            // Configuracion escribe las dos cosas: el tamano base en pixeles y el
            // valor de la barra en Accessibility, que es lo que relee al reiniciar.
            SystemParametersInfo(SPI_SETCURSORBASESIZE, 0, new IntPtr(32 + (value - 1) * 16), Persist);
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Accessibility");
            key.SetValue("CursorSize", value, RegistryValueKind.DWord);
        }
    }
}
