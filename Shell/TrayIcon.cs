using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Colmillo.Shell;

/// <summary>
/// Icono de bandeja con Shell_NotifyIcon directo. Se evita NotifyIcon de WinForms:
/// cargar WinForms entero solo para esto cuesta varios MB de memoria residente.
/// La misma ventana oculta recibe los avisos de USB conectado/desconectado y de
/// reanudar desde suspension.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    const int WM_APP_TRAY = 0x8001;
    const int WM_DEVICECHANGE = 0x0219;
    const int WM_POWERBROADCAST = 0x0218;
    const int PBT_APMRESUMEAUTOMATIC = 0x12;
    const int DBT_DEVICEARRIVAL = 0x8000, DBT_DEVICEREMOVECOMPLETE = 0x8004;
    const int WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205;
    const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    const int NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DEV_BROADCAST_DEVICEINTERFACE
    {
        public int dbcc_size;
        public int dbcc_devicetype;
        public int dbcc_reserved;
        public Guid dbcc_classguid;
        public short dbcc_name;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr LoadImage(IntPtr hInst, IntPtr name, int type, int cx, int cy, int flags);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern IntPtr RegisterDeviceNotification(IntPtr recipient, ref DEV_BROADCAST_DEVICEINTERFACE filter, int flags);

    [DllImport("user32.dll")]
    static extern bool UnregisterDeviceNotification(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int RegisterWindowMessage(string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string? name);

    readonly HwndSource _window;
    readonly IntPtr _icon;
    readonly IntPtr _devNotify;
    readonly int _taskbarCreated;
    NOTIFYICONDATA _data;

    public event Action? Click;
    public event Action? RightClick;
    public event Action? DevicesChanged;
    public event Action? Resumed;

    public IntPtr Handle => _window.Handle;

    public TrayIcon(string tooltip)
    {
        // Popup invisible y de herramientas (sin boton en la barra de tareas). No sirve
        // una ventana de solo mensajes: no reciben difusiones como WM_POWERBROADCAST
        // ni "TaskbarCreated".
        _window = new HwndSource(new HwndSourceParameters("ColmilloTray")
        {
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP, sin WS_VISIBLE
            ExtendedWindowStyle = 0x80, // WS_EX_TOOLWINDOW
            Width = 0,
            Height = 0,
        });
        _window.AddHook(WndProc);

        // 32512 = recurso del icono de la aplicacion que el SDK incrusta desde ApplicationIcon.
        var size = GetSystemMetrics(49); // SM_CXSMICON
        _icon = LoadImage(GetModuleHandle(null), new IntPtr(32512), 1, size, size, 0);

        _data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _window.Handle,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_APP_TRAY,
            hIcon = _icon,
            szTip = tooltip,
            szInfo = "",
            szInfoTitle = "",
        };
        Shell_NotifyIcon(NIM_ADD, ref _data);
        _data.uVersion = 4;
        Shell_NotifyIcon(NIM_SETVERSION, ref _data);

        // Si explorer.exe se reinicia, la bandeja se vacia y hay que volver a agregarse.
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");

        // Avisos de interfaces HID (llegan al enchufar o quitar cualquier HID).
        var filter = new DEV_BROADCAST_DEVICEINTERFACE
        {
            dbcc_size = Marshal.SizeOf<DEV_BROADCAST_DEVICEINTERFACE>(),
            dbcc_devicetype = 5, // DBT_DEVTYP_DEVICEINTERFACE
            dbcc_classguid = new Guid("4d1e55b2-f16f-11cf-88cb-001111000030"),
        };
        _devNotify = RegisterDeviceNotification(_window.Handle, ref filter, 0);
    }

    public void SetTooltip(string text)
    {
        _data.szTip = text.Length > 127 ? text[..127] : text;
        _data.uFlags = NIF_TIP;
        Shell_NotifyIcon(NIM_MODIFY, ref _data);
    }

    /// <summary>Hace falta antes de abrir un menu desde la bandeja, si no no se cierra al hacer clic afuera.</summary>
    public void FocusForMenu() => SetForegroundWindow(_window.Handle);

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_APP_TRAY)
        {
            // Con NOTIFYICON_VERSION_4 el evento viene en la palabra baja de lParam.
            var ev = (int)lParam & 0xFFFF;
            if (ev == WM_LBUTTONUP) Click?.Invoke();
            else if (ev == WM_RBUTTONUP) RightClick?.Invoke();
        }
        else if (msg == WM_DEVICECHANGE && (int)wParam is DBT_DEVICEARRIVAL or DBT_DEVICEREMOVECOMPLETE)
        {
            DevicesChanged?.Invoke();
        }
        else if (msg == WM_POWERBROADCAST && (int)wParam == PBT_APMRESUMEAUTOMATIC)
        {
            Resumed?.Invoke();
        }
        else if (msg == _taskbarCreated)
        {
            _data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
            Shell_NotifyIcon(NIM_ADD, ref _data);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Shell_NotifyIcon(NIM_DELETE, ref _data);
        if (_devNotify != IntPtr.Zero) UnregisterDeviceNotification(_devNotify);
        if (_icon != IntPtr.Zero) DestroyIcon(_icon);
        _window.Dispose();
    }
}
