using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Colmillo.Shell;

/// <summary>Atajo de teclado global (RegisterHotKey) sobre una ventana de solo mensajes.</summary>
public sealed class HotKey : IDisposable
{
    const int WmHotKey = 0x0312;
    public const uint Alt = 0x1, Control = 0x2, Shift = 0x4, NoRepeat = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    readonly HwndSource _window;
    readonly int _id;

    public bool Registered { get; }
    public event Action? Pressed;

    public HotKey(uint modifiers, uint vk, int id = 1)
    {
        _id = id;
        // HWND_MESSAGE como padre: no se ve ni aparece en Alt+Tab.
        _window = new HwndSource(new HwndSourceParameters("Colmillo.Atajos") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
        _window.AddHook(Hook);
        Registered = RegisterHotKey(_window.Handle, id, modifiers | NoRepeat, vk);
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == _id)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterHotKey(_window.Handle, _id);
        _window.Dispose();
    }
}
