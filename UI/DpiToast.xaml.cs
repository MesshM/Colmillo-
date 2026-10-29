using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Colmillo.UI;

/// <summary>
/// Aviso de DPI al apretar el boton del mouse, como el de Synapse: abajo a la derecha,
/// sobre la barra de tareas. No toma el foco ni intercepta clics, y se destruye al
/// ocultarse para no dejar una ventana viva en la bandeja.
/// </summary>
public partial class DpiToast : Window
{
    const int GWL_EXSTYLE = -20;
    const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    static readonly TimeSpan Visible = TimeSpan.FromMilliseconds(1600);

    [DllImport("user32.dll")]
    static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    static DpiToast? _current;
    readonly DispatcherTimer _timer = new() { Interval = Visible };
    bool _hiding;

    DpiToast()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        };
        Loaded += (_, _) => Place();
        _timer.Tick += (_, _) => Hide(animated: true);
    }

    /// <summary>Muestra o actualiza el aviso. Se llama desde el hilo de UI.</summary>
    public static void Show(int dpi, int stage, int stageCount, Action onClosed)
    {
        if (_current is null)
        {
            _current = new DpiToast();
            _current.Closed += (_, _) =>
            {
                _current = null;
                onClosed();
            };
            _current.Fill(dpi, stage, stageCount);
            _current.Show();
            _current.Animate(1, 0, 180);
        }
        else
        {
            _current.Fill(dpi, stage, stageCount);
            if (_current._hiding)
            {
                _current._hiding = false;
                _current.Animate(1, 0, 120);
            }
        }
        _current._timer.Stop();
        _current._timer.Start();
    }

    void Fill(int dpi, int stage, int stageCount)
    {
        Value.Text = $"{dpi}";
        Dots.Children.Clear();
        for (var i = 1; i <= stageCount; i++)
        {
            Dots.Children.Add(new Rectangle
            {
                Width = i == stage ? 16 : 6,
                Height = 6,
                RadiusX = 3,
                RadiusY = 3,
                Margin = new Thickness(0, 0, 4, 0),
                Fill = (Brush)FindResource(i == stage ? "Accent600" : "Ink300"),
            });
        }
        StageText.Text = stage > 0 ? $"Etapa {stage} de {stageCount}" : "Fuera de las etapas";
    }

    /// <summary>Esquina inferior derecha del area de trabajo: queda justo arriba de la barra de tareas.</summary>
    void Place()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth + 6;
        Top = area.Bottom - ActualHeight + 6;
    }

    void Animate(double opacity, double y, int ms)
    {
        var duration = TimeSpan.FromMilliseconds(ms);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Card.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, duration) { EasingFunction = ease });
        Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, duration) { EasingFunction = ease });
    }

    void Hide(bool animated)
    {
        _timer.Stop();
        if (!animated)
        {
            Close();
            return;
        }
        _hiding = true;
        Animate(0, 10, 220);
        var close = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
        close.Tick += (_, _) =>
        {
            close.Stop();
            // Si volvio a apretarse mientras se iba, se queda.
            if (_hiding) Close();
        };
        close.Start();
    }
}
