using System.Runtime.InteropServices;
using System.Windows.Threading;
using Colmillo.Core;

namespace Colmillo.Shell;

/// <summary>
/// Vigila el hilo de UI desde afuera: si deja de atender (bloqueado o con la cola de mensajes
/// llena) lo deja en dispositivos.log, con la hora y la pagina abierta. Una vez se vio la cola
/// llena ("Not enough quota") y Colmillo dejo de recibir hasta la orden de cerrarse.
/// </summary>
public sealed class UiWatchdog : IDisposable
{
    static readonly TimeSpan Every = TimeSpan.FromSeconds(5), Late = TimeSpan.FromSeconds(10);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);

    readonly Dispatcher _ui;
    readonly IntPtr _window;
    readonly Func<string> _where;
    readonly Timer _timer;
    bool _stuck, _full;

    /// <param name="window">Una ventana del hilo de UI (la de la bandeja) para probar su cola.</param>
    /// <param name="where">Que estaba haciendo la app (pagina abierta), para el registro.</param>
    public UiWatchdog(Dispatcher ui, IntPtr window, Func<string> where)
    {
        (_ui, _window, _where) = (ui, window, where);
        _timer = new Timer(_ => Check(), null, Every, Every);
    }

    void Check()
    {
        // WM_NULL no hace nada; si no entra, la cola esta llena.
        var posted = PostMessage(_window, 0, IntPtr.Zero, IntPtr.Zero);
        if (!posted && !_full) DeviceLog.Write($"UI: la cola de mensajes está llena (error {Marshal.GetLastWin32Error()})");
        _full = !posted;

        var answered = _ui.InvokeAsync(() => { }, DispatcherPriority.Send).Task.Wait(Late);
        if (!answered && !_stuck) DeviceLog.Write("UI: el hilo de la interfaz no responde hace 10 s");
        else if (answered && _stuck) DeviceLog.Write("UI: el hilo de la interfaz volvió a responder");
        _stuck = !answered;
        if (!answered || !posted)
        {
            try { DeviceLog.Write($"UI: estado: {_where()}"); }
            catch (Exception) { }
        }
    }

    public void Dispose() => _timer.Dispose();
}
