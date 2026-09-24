using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MediaColor = System.Windows.Media.Color;

namespace FolderManaging.Companion.Controls;

/// <summary>HSV color wheel: click/drag a point on the disc (hue + saturation). Value comes from <see cref="Value"/>.</summary>
public sealed class ColorWheelControl : FrameworkElement
{
    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor),
        typeof(MediaColor),
        typeof(ColorWheelControl),
        new FrameworkPropertyMetadata(Colors.Red, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedColorChanged));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(double),
        typeof(ColorWheelControl),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged),
        v => v is double d && d >= 0 && d <= 1);

    WriteableBitmap? _wheel;
    double _hue;
    double _sat = 1;
    bool _suppress;
    bool _dragging;

    public MediaColor SelectedColor
    {
        get => (MediaColor)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    /// <summary>Brightness 0..1 (V in HSV).</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public event EventHandler? ColorChanged;

    static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (ColorWheelControl)d;
        if (c._suppress)
            return;
        RgbToHsv((MediaColor)e.NewValue, out c._hue, out c._sat, out var v);
        c._suppress = true;
        try { c.Value = v; }
        finally { c._suppress = false; }
        c.InvalidateVisual();
    }

    static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (ColorWheelControl)d;
        if (c._suppress)
            return;
        c.PushColorFromHsv();
        c.RebuildWheel();
        c.InvalidateVisual();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        RebuildWheel();
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        CaptureMouse();
        _dragging = true;
        UpdateFromPoint(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed)
            return;
        UpdateFromPoint(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging)
            return;
        _dragging = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    void UpdateFromPoint(System.Windows.Point p)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size < 8)
            return;
        var cx = ActualWidth / 2;
        var cy = ActualHeight / 2;
        var radius = size / 2 - 4;
        var dx = p.X - cx;
        var dy = p.Y - cy;
        var dist = Math.Sqrt(dx * dx + dy * dy);
        _sat = Math.Clamp(dist / radius, 0, 1);
        _hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
        PushColorFromHsv();
        InvalidateVisual();
    }

    void PushColorFromHsv()
    {
        var color = HsvToRgb(_hue, _sat, Value);
        _suppress = true;
        try
        {
            SelectedColor = color;
        }
        finally
        {
            _suppress = false;
        }

        ColorChanged?.Invoke(this, EventArgs.Empty);
    }

    void RebuildWheel()
    {
        var size = (int)Math.Floor(Math.Min(ActualWidth, ActualHeight));
        if (size < 16)
        {
            _wheel = null;
            return;
        }

        size = Math.Min(size, 360);
        var bmp = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[size * size * 4];
        var cx = (size - 1) / 2.0;
        var cy = (size - 1) / 2.0;
        var radius = size / 2.0 - 1;
        var v = Value;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x - cx;
                var dy = y - cy;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                var i = (y * size + x) * 4;
                if (dist > radius)
                {
                    pixels[i + 3] = 0;
                    continue;
                }

                var sat = dist / radius;
                var hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
                var c = HsvToRgb(hue, sat, v);
                pixels[i] = c.B;
                pixels[i + 1] = c.G;
                pixels[i + 2] = c.R;
                pixels[i + 3] = 255;
            }
        }

        bmp.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
        _wheel = bmp;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size < 8)
            return;

        var left = (ActualWidth - size) / 2;
        var top = (ActualHeight - size) / 2;
        var rect = new Rect(left, top, size, size);

        if (_wheel is not null)
            dc.DrawImage(_wheel, rect);
        else
            dc.DrawEllipse(System.Windows.Media.Brushes.Gray, null, new System.Windows.Point(ActualWidth / 2, ActualHeight / 2), size / 2, size / 2);

        var radius = size / 2 - 4;
        var cx = ActualWidth / 2;
        var cy = ActualHeight / 2;
        var rad = _hue * Math.PI / 180;
        var px = cx + Math.Cos(rad) * _sat * radius;
        var py = cy + Math.Sin(rad) * _sat * radius;

        dc.DrawEllipse(null, new System.Windows.Media.Pen(System.Windows.Media.Brushes.White, 2.5), new System.Windows.Point(px, py), 7, 7);
        dc.DrawEllipse(null, new System.Windows.Media.Pen(System.Windows.Media.Brushes.Black, 1.2), new System.Windows.Point(px, py), 7, 7);
        dc.DrawEllipse(new SolidColorBrush(SelectedColor), null, new System.Windows.Point(px, py), 4, 4);
    }

    public static MediaColor HsvToRgb(double h, double s, double v)
    {
        h = (h % 360 + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        double r1, g1, b1;
        if (h < 60) { r1 = c; g1 = x; b1 = 0; }
        else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
        else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
        else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
        else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
        else { r1 = c; g1 = 0; b1 = x; }

        return MediaColor.FromRgb(
            (byte)Math.Round((r1 + m) * 255),
            (byte)Math.Round((g1 + m) * 255),
            (byte)Math.Round((b1 + m) * 255));
    }

    public static void RgbToHsv(MediaColor color, out double h, out double s, out double v)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        v = max;
        s = max <= 0 ? 0 : delta / max;
        if (delta <= 0.00001)
        {
            h = 0;
            return;
        }

        if (Math.Abs(max - r) < 0.00001)
            h = 60 * (((g - b) / delta) % 6);
        else if (Math.Abs(max - g) < 0.00001)
            h = 60 * (((b - r) / delta) + 2);
        else
            h = 60 * (((r - g) / delta) + 4);

        if (h < 0)
            h += 360;
    }
}
