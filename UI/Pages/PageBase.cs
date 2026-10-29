using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Colmillo.Core;

namespace Colmillo.UI.Pages;

/// <summary>Base de las paginas: acceso a ajustes y envio diferido al mouse.</summary>
public class PageBase : UserControl, IPage
{
    readonly DispatcherTimer _apply = new() { Interval = TimeSpan.FromMilliseconds(180) };
    Func<Task>? _pending;
    /// <summary>
    /// Evita que los eventos de los controles escriban ajustes mientras la pagina se
    /// carga. Empieza en true: un Slider con Minimum &gt; 0 dispara ValueChanged dentro
    /// de InitializeComponent, antes de que existan los demas controles.
    /// </summary>
    protected bool Loading = true;

    protected static AppSettings Settings => App.Current.Settings;
    protected static DeviceProfile P => App.Current.Settings.Device;
    protected static DeviceService Device => App.Current.Device;

    public PageBase()
    {
        _apply.Tick += (_, _) =>
        {
            _apply.Stop();
            _ = _pending?.Invoke();
        };
    }

    public virtual void Reload() { }

    /// <summary>
    /// Guarda y manda al mouse cuando el usuario deja de mover el control. Un slider
    /// genera decenas de eventos por segundo; el firmware tarda ~10 ms por comando.
    /// </summary>
    protected void Commit(Func<Task> apply)
    {
        if (Loading) return;
        App.Current.SaveSoon();
        _pending = apply;
        _apply.Stop();
        _apply.Start();
    }

    protected Brush Res(string key) => (Brush)FindResource(key);

    /// <summary>Confirmacion con el dialogo propio de la ventana (no el MessageBox de Windows).</summary>
    protected Task<bool> Confirm(string title, string message, string accept, bool danger = true) =>
        Window.GetWindow(this) is MainWindow w ? w.ConfirmAsync(title, message, accept, danger) : Task.FromResult(false);

    protected static TextBlock Text(string text, string style) =>
        new() { Text = text, Style = (Style)Application.Current.FindResource(style) };
}
