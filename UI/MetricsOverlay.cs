using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Colmillo.Core;
using Colmillo.Core.Metrics;

namespace Colmillo.UI;

/// <summary>
/// Metricas encima de los juegos, como RivaTuner. Es una ventana transparente, siempre
/// arriba, que no recibe clics ni foco: no se inyecta nada en el juego, asi que no la ven los
/// antitrampas (Vanguard, EAC). Se ve en juegos en ventana sin bordes y en pantalla completa
/// con las optimizaciones de Windows (lo normal en Windows 11); no en pantalla completa
/// exclusiva de DirectX 9/11 antiguos.
/// </summary>
public sealed class MetricsOverlay : Window
{
    const int GwlExStyle = -20;
    const long WsExTransparent = 0x20, WsExToolWindow = 0x80, WsExLayered = 0x80000, WsExNoActivate = 0x08000000;
    static readonly IntPtr HwndTopmost = new(-1);
    const uint SwpNoMove = 0x2, SwpNoSize = 0x1, SwpNoActivate = 0x10;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    readonly MetricsService _metrics;
    readonly MetricsProfile _profile;
    readonly Grid _table = new();
    IntPtr _hwnd;

    public MetricsOverlay(MetricsService metrics, MetricsProfile profile)
    {
        _metrics = metrics;
        _profile = profile;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        _table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Content = new Border
        {
            Child = _table,
            Padding = new Thickness(10, 6, 12, 6),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0x70, 0x10, 0x10, 0x14)),
        };
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            var ex = (long)GetWindowLongPtr(_hwnd, GwlExStyle);
            SetWindowLongPtr(_hwnd, GwlExStyle, (IntPtr)(ex | WsExTransparent | WsExToolWindow | WsExLayered | WsExNoActivate));
        };
        SizeChanged += (_, _) => Place();
        _metrics.Updated += OnUpdated;
        Closed += (_, _) => _metrics.Updated -= OnUpdated;
        _metrics.Start();
        Closed += (_, _) => _metrics.Stop();
    }

    void OnUpdated(MetricsSnapshot s) => Dispatcher.BeginInvoke(() => Render(s));

    void Render(MetricsSnapshot s)
    {
        // Solo en juegos: el driver cuenta FPS cuando hay una app 3D en primer plano.
        var show = !_profile.OnlyInGames || s.Gpu?.Fps is not null;
        if (!show)
        {
            if (IsVisible) Hide();
            return;
        }
        var lines = MetricsFormat.Lines(s, _profile.Shown);
        _table.Children.Clear();
        _table.RowDefinitions.Clear();
        var accent = (Brush)Application.Current.FindResource("Accent500");
        for (var i = 0; i < lines.Count; i++)
        {
            _table.RowDefinitions.Add(new RowDefinition());
            var label = Cell(lines[i].Label, accent, bold: true);
            var value = Cell(lines[i].Values, Brushes.White, bold: false);
            value.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetRow(label, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            _table.Children.Add(label);
            _table.Children.Add(value);
        }
        if (!IsVisible) Show();
        // Un juego que pasa a primer plano puede quedar encima: se reafirma cada segundo.
        if (_hwnd != IntPtr.Zero) SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    TextBlock Cell(string text, Brush brush, bool bold) => new()
    {
        Text = text,
        Foreground = brush,
        FontSize = _profile.FontSize,
        FontWeight = bold ? FontWeights.Bold : FontWeights.SemiBold,
        FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
        Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Opacity = 0.9, Color = Colors.Black },
    };

    /// <summary>En la esquina elegida del monitor principal, con un margen.</summary>
    public void Place()
    {
        const double margin = 14;
        var area = SystemParameters.PrimaryScreenWidth;
        var height = SystemParameters.PrimaryScreenHeight;
        Left = _profile.Corner.EndsWith("der") ? area - ActualWidth - margin : margin;
        Top = _profile.Corner.StartsWith("abajo") ? height - ActualHeight - margin - 40 : margin;
    }
}
