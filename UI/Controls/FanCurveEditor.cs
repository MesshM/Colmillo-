using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Colmillo.Core;

namespace Colmillo.UI.Controls;

/// <summary>
/// Curva del ventilador: temperatura (horizontal) contra velocidad (vertical), con los
/// puntos arrastrables. Cada punto queda entre sus vecinos: la temperatura siempre sube y
/// la velocidad nunca baja, como exige el driver.
/// </summary>
public sealed class FanCurveEditor : FrameworkElement
{
    const double PadLeft = 44, PadRight = 16, PadTop = 14, PadBottom = 30, Hit = 12;

    List<FanPoint> _points = [];
    int _tempMin = 25, _tempMax = 100, _speedMin = 20, _speedMax = 100;
    double? _current;
    int _drag = -1, _hover = -1;

    /// <summary>La curva cambio; el bool dice si el arrastre termino (hay que aplicarla).</summary>
    public event Action<bool>? Changed;

    public FanCurveEditor()
    {
        Height = 280;
        Cursor = Cursors.Arrow;
        Focusable = false;
    }

    public void Set(IEnumerable<FanPoint> points, int tempMin, int tempMax, int speedMin, int speedMax)
    {
        _points = [.. points.Select(p => new FanPoint(p.Temperature, p.Speed))];
        (_tempMin, _tempMax, _speedMin, _speedMax) = (tempMin, tempMax, speedMin, speedMax);
        InvalidateVisual();
    }

    public IReadOnlyList<FanPoint> Points => _points;

    /// <summary>Temperatura actual de la grafica, marcada con una linea.</summary>
    public void SetCurrent(double? temperature)
    {
        _current = temperature;
        InvalidateVisual();
    }

    Rect Plot => new(PadLeft, PadTop, Math.Max(1, ActualWidth - PadLeft - PadRight), Math.Max(1, ActualHeight - PadTop - PadBottom));

    Point ToScreen(double temp, double speed)
    {
        var r = Plot;
        return new Point(
            r.Left + (temp - _tempMin) / (_tempMax - _tempMin) * r.Width,
            r.Bottom - (speed - _speedMin) / (_speedMax - _speedMin) * r.Height);
    }

    (int Temp, int Speed) FromScreen(Point p)
    {
        var r = Plot;
        var t = _tempMin + (p.X - r.Left) / r.Width * (_tempMax - _tempMin);
        var s = _speedMin + (r.Bottom - p.Y) / r.Height * (_speedMax - _speedMin);
        return ((int)Math.Round(t), (int)Math.Round(s));
    }

    Brush Res(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    protected override void OnRender(DrawingContext dc)
    {
        var r = Plot;
        var line = Res("Line", Brushes.Gray);
        var ink = Res("Ink400", Brushes.Gray);
        var accent = Res("Accent600", Brushes.Crimson);
        var fog = Res("Fog", Brushes.Transparent);
        var gridPen = new Pen(line, 1);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        dc.DrawRoundedRectangle(fog, null, r, 10, 10);
        for (var t = (_tempMin + 9) / 10 * 10; t <= _tempMax; t += 10)
        {
            var x = ToScreen(t, _speedMin).X;
            dc.DrawLine(gridPen, new Point(x, r.Top), new Point(x, r.Bottom));
            Label(dc, $"{t}°", new Point(x, r.Bottom + 6), ink, dpi, center: true);
        }
        for (var s = (_speedMin + 9) / 10 * 10; s <= _speedMax; s += 20)
        {
            var y = ToScreen(_tempMin, s).Y;
            dc.DrawLine(gridPen, new Point(r.Left, y), new Point(r.Right, y));
            Label(dc, $"{s} %", new Point(r.Left - 8, y - 8), ink, dpi, center: false);
        }

        if (_current is { } now && now >= _tempMin && now <= _tempMax)
        {
            var x = ToScreen(now, _speedMin).X;
            var pen = new Pen(Res("Warn", Brushes.Orange), 1.5) { DashStyle = DashStyles.Dash };
            dc.DrawLine(pen, new Point(x, r.Top), new Point(x, r.Bottom));
        }

        if (_points.Count == 0) return;
        // Antes del primer punto el ventilador va al minimo del primero; despues del ultimo, al del ultimo.
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            var first = ToScreen(_tempMin, _points[0].Speed);
            g.BeginFigure(new Point(first.X, r.Bottom), true, true);
            g.LineTo(first, true, false);
            foreach (var p in _points) g.LineTo(ToScreen(p.Temperature, p.Speed), true, false);
            var last = ToScreen(_tempMax, _points[^1].Speed);
            g.LineTo(last, true, false);
            g.LineTo(new Point(last.X, r.Bottom), true, false);
        }
        var fill = accent.Clone();
        fill.Opacity = 0.14;
        dc.DrawGeometry(fill, new Pen(accent, 2.5) { LineJoin = PenLineJoin.Round }, geo);

        for (var i = 0; i < _points.Count; i++)
        {
            var c = ToScreen(_points[i].Temperature, _points[i].Speed);
            var big = i == _drag || i == _hover;
            dc.DrawEllipse(accent, new Pen(Brushes.White, 2.5), c, big ? 8 : 6.5, big ? 8 : 6.5);
            if (big) Label(dc, $"{_points[i].Temperature} °C · {_points[i].Speed} %", new Point(c.X, c.Y - 28), accent, dpi, center: true);
        }
    }

    static void Label(DrawingContext dc, string text, Point at, Brush brush, double dpi, bool center)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 11, brush, dpi);
        dc.DrawText(ft, new Point(center ? at.X - ft.Width / 2 : at.X - ft.Width, at.Y));
    }

    int HitTest(Point p)
    {
        for (var i = 0; i < _points.Count; i++)
            if ((ToScreen(_points[i].Temperature, _points[i].Speed) - p).Length <= Hit) return i;
        return -1;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _drag = HitTest(e.GetPosition(this));
        if (_drag >= 0) CaptureMouse();
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (_drag < 0)
        {
            var h = HitTest(pos);
            if (h != _hover)
            {
                _hover = h;
                Cursor = h >= 0 ? Cursors.Hand : Cursors.Arrow;
                InvalidateVisual();
            }
            return;
        }
        var (t, s) = FromScreen(pos);
        var lowT = _drag > 0 ? _points[_drag - 1].Temperature + 1 : _tempMin;
        var highT = _drag < _points.Count - 1 ? _points[_drag + 1].Temperature - 1 : _tempMax;
        var lowS = _drag > 0 ? _points[_drag - 1].Speed : _speedMin;
        var highS = _drag < _points.Count - 1 ? _points[_drag + 1].Speed : _speedMax;
        var p = _points[_drag];
        p.Temperature = Math.Clamp(t, lowT, Math.Max(lowT, highT));
        p.Speed = Math.Clamp(s, lowS, Math.Max(lowS, highS));
        InvalidateVisual();
        Changed?.Invoke(false);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_drag < 0) return;
        _drag = -1;
        ReleaseMouseCapture();
        InvalidateVisual();
        Changed?.Invoke(true);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_hover < 0) return;
        _hover = -1;
        InvalidateVisual();
    }
}
