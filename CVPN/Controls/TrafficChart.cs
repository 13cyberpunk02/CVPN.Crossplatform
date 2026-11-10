using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CVPN.Core;
using CVPN.Models;

namespace CVPN.Controls;

/// <summary>
/// График скорости за последнюю минуту: приём и отдача - сглаженные линии
/// с градиентной заливкой, справа самая свежая точка. Рисуется вручную,
/// а не библиотекой графиков: точек 60, а тянуть ради них зависимость
/// на три платформы незачем.
/// </summary>
public sealed class TrafficChart : Control
{
    public static readonly StyledProperty<TrafficHistory?> HistoryProperty =
        AvaloniaProperty.Register<TrafficChart, TrafficHistory?>(nameof(History));

    public static readonly StyledProperty<IBrush?> DownloadBrushProperty =
        AvaloniaProperty.Register<TrafficChart, IBrush?>(nameof(DownloadBrush));

    public static readonly StyledProperty<IBrush?> UploadBrushProperty =
        AvaloniaProperty.Register<TrafficChart, IBrush?>(nameof(UploadBrush));

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<TrafficChart, IBrush?>(nameof(GridBrush));

    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<TrafficChart, IBrush?>(nameof(LabelBrush));

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        AvaloniaProperty.Register<TrafficChart, FontFamily>(nameof(FontFamily), FontFamily.Default);

    /// <summary>Место слева под подписи шкалы.</summary>
    private const double AxisWidth = 58;

    /// <summary>Отступ сверху: подпись верхней линии не должна обрезаться.</summary>
    private const double TopPad = 8;

    static TrafficChart()
    {
        AffectsRender<TrafficChart>(HistoryProperty, DownloadBrushProperty, UploadBrushProperty,
            GridBrushProperty, LabelBrushProperty, FontFamilyProperty);
    }

    public TrafficHistory? History
    {
        get => GetValue(HistoryProperty);
        set => SetValue(HistoryProperty, value);
    }

    public IBrush? DownloadBrush
    {
        get => GetValue(DownloadBrushProperty);
        set => SetValue(DownloadBrushProperty, value);
    }

    public IBrush? UploadBrush
    {
        get => GetValue(UploadBrushProperty);
        set => SetValue(UploadBrushProperty, value);
    }

    public IBrush? GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public IBrush? LabelBrush
    {
        get => GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != HistoryProperty) return;

        if (change.OldValue is TrafficHistory old) old.Changed -= OnHistoryChanged;
        if (change.NewValue is TrafficHistory fresh && IsAttached) fresh.Changed += OnHistoryChanged;
    }

    private bool IsAttached => VisualRoot is not null;

    // Подписка только пока график на экране: ушли со страницы - не держим её в памяти
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (History is { } history) history.Changed += OnHistoryChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (History is { } history) history.Changed -= OnHistoryChanged;
    }

    private void OnHistoryChanged() => Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateVisual);

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= AxisWidth + 10 || height <= TopPad + 10) return;

        var plot = new Rect(AxisWidth, TopPad, width - AxisWidth, height - TopPad - 1);
        var history = History;
        var top = TrafficHistory.NiceCeiling(history?.Max ?? 0);

        DrawGrid(context, plot, top);

        if (history is null || history.Count < 2) return;

        // Отдача под приёмом: приём обычно больше и важнее, он рисуется поверх
        DrawSeries(context, plot, history, top, history.Up, UploadBrush);
        DrawSeries(context, plot, history, top, history.Down, DownloadBrush);
    }

    /// <summary>Три пунктирные линии: ноль, половина и верх шкалы с подписями.</summary>
    private void DrawGrid(DrawingContext context, Rect plot, long top)
    {
        var line = new Pen(GridBrush ?? Brushes.Gray, 1, new DashStyle([3, 4], 0));
        var solid = new Pen(GridBrush ?? Brushes.Gray, 1);
        var typeface = new Typeface(FontFamily);

        for (var step = 0; step <= 2; step++)
        {
            var y = Math.Round(plot.Bottom - plot.Height * step / 2) + 0.5;

            context.DrawLine(step == 0 ? solid : line, new Point(plot.Left, y), new Point(plot.Right, y));

            var (value, unit) = ByteFormat.Rate(top * step / 2);
            var text = new FormattedText(step == 0 ? "0" : $"{Compact(value)} {unit}", CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, typeface, 10, LabelBrush ?? Brushes.Gray);

            var labelY = Math.Clamp(y - text.Height / 2, 0, plot.Bottom - text.Height);
            context.DrawText(text, new Point(plot.Left - text.Width - 8, labelY));
        }
    }

    /// <summary>«10,0» на шкале - лишний шум: целые значения без дробной части.</summary>
    private static string Compact(string value) =>
        value.EndsWith(",0", StringComparison.Ordinal) || value.EndsWith(".0", StringComparison.Ordinal) ? value[..^2] : value;

    private static void DrawSeries(DrawingContext context, Rect plot, TrafficHistory history, long top,
        Func<int, long> value, IBrush? brush)
    {
        var color = (brush as ISolidColorBrush)?.Color ?? Colors.DeepSkyBlue;

        // Точки прижаты вправо: пока минута не набралась, график растёт от правого края
        var stepX = plot.Width / (history.Capacity - 1);
        var offset = history.Capacity - history.Count;
        var points = new Point[history.Count];

        for (var i = 0; i < history.Count; i++)
        {
            var x = plot.Left + (offset + i) * stepX;
            var y = plot.Bottom - plot.Height * Math.Min(1, (double)value(i) / top);
            points[i] = new Point(x, y);
        }

        var line = Smooth(points, close: false, plot.Bottom);
        var area = Smooth(points, close: true, plot.Bottom);

        var fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, plot.Top, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(0, plot.Bottom, RelativeUnit.Absolute),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x66, color.R, color.G, color.B), 0),
                new GradientStop(Color.FromArgb(0x00, color.R, color.G, color.B), 1)
            }
        };

        context.DrawGeometry(fill, null, area);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(color), 2, lineCap: PenLineCap.Round,
            lineJoin: PenLineJoin.Round), line);

        // Свежая точка: ореол и сама точка - видно, что график живой
        var last = points[^1];
        context.DrawEllipse(new SolidColorBrush(color, 0.25), null, last, 6, 6);
        context.DrawEllipse(new SolidColorBrush(color), null, last, 3, 3);
    }

    /// <summary>
    /// Сглаживание монотонными кубическими кривыми: линия плавная, но не
    /// «выстреливает» ниже нуля и выше пиков, как сплайн Катмулла - Рома.
    /// </summary>
    private static StreamGeometry Smooth(Point[] p, bool close, double bottom)
    {
        var n = p.Length;
        var tangents = new double[n];
        var slopes = new double[n - 1];

        for (var i = 0; i < n - 1; i++)
            slopes[i] = (p[i + 1].Y - p[i].Y) / (p[i + 1].X - p[i].X);

        tangents[0] = slopes[0];
        tangents[n - 1] = slopes[n - 2];

        for (var i = 1; i < n - 1; i++)
            tangents[i] = slopes[i - 1] * slopes[i] <= 0 ? 0 : (slopes[i - 1] + slopes[i]) / 2;

        // Ограничение Фрича - Карлсона: без него кривая перелетает соседние точки
        for (var i = 0; i < n - 1; i++)
        {
            if (slopes[i] == 0)
            {
                tangents[i] = tangents[i + 1] = 0;
                continue;
            }

            var a = tangents[i] / slopes[i];
            var b = tangents[i + 1] / slopes[i];
            var h = a * a + b * b;
            if (h <= 9) continue;

            var tau = 3 / Math.Sqrt(h);
            tangents[i] = tau * a * slopes[i];
            tangents[i + 1] = tau * b * slopes[i];
        }

        var geometry = new StreamGeometry();

        using var ctx = geometry.Open();

        if (close)
        {
            ctx.BeginFigure(new Point(p[0].X, bottom), true);
            ctx.LineTo(p[0]);
        }
        else
        {
            ctx.BeginFigure(p[0], false);
        }

        for (var i = 0; i < n - 1; i++)
        {
            var dx = (p[i + 1].X - p[i].X) / 3;
            ctx.CubicBezierTo(
                new Point(p[i].X + dx, p[i].Y + tangents[i] * dx),
                new Point(p[i + 1].X - dx, p[i + 1].Y - tangents[i + 1] * dx),
                p[i + 1]);
        }

        if (close)
        {
            ctx.LineTo(new Point(p[^1].X, bottom));
            ctx.EndFigure(true);
        }
        else
        {
            ctx.EndFigure(false);
        }

        return geometry;
    }
}
