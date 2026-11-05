using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ArduSim.App.Services;
using ArduSim.App.ViewModels;

namespace ArduSim.App.Views;

/// <summary>Draws the Serial Plotter: auto-scaled value axis with grid, one line per series, a legend.</summary>
public sealed class PlotterView : FrameworkElement
{
    public static readonly DependencyProperty PlotterProperty =
        DependencyProperty.Register(nameof(Plotter), typeof(PlotterViewModel), typeof(PlotterView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Bound to <see cref="PlotterViewModel.Version"/>, so new samples redraw.</summary>
    public static readonly DependencyProperty VersionProperty =
        DependencyProperty.Register(nameof(Version), typeof(long), typeof(PlotterView), new FrameworkPropertyMetadata(0L, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly double[] NiceSteps = [1, 2, 2.5, 5, 10, 20];

    // A line per series, by its position. On the light canvas every color has at least 4.5:1.
    private static readonly Brush[] DarkPalette = Palette(0x2BC4A9, 0xF0B44C, 0x3E8BFF, 0xF0605A, 0xA66CFF, 0x3BB273, 0xFF6FAE, 0xB8BEC8);
    private static readonly Brush[] LightPalette = Palette(0x0B7A69, 0x946200, 0x1F66D1, 0xC8332D, 0x7A3FD4, 0x2A7D4F, 0xC22C7A, 0x5F6672);

    private const double AxisWidth = 56;
    private const double Inset = 8;

    public PlotterView()
    {
        Loaded += (_, _) => Theme.Changed += InvalidateVisual;
        Unloaded += (_, _) => Theme.Changed -= InvalidateVisual;
    }

    public PlotterViewModel? Plotter
    {
        get => (PlotterViewModel?)GetValue(PlotterProperty);
        set => SetValue(PlotterProperty, value);
    }

    public long Version
    {
        get => (long)GetValue(VersionProperty);
        set => SetValue(VersionProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        Brush background = Resource("Brush.Canvas", Brushes.Black);
        Brush text = Resource("Brush.Text.Secondary", Brushes.Gray);
        var grid = new Pen(Resource("Brush.Border", Brushes.DimGray), 1);
        grid.Freeze();
        var plotArea = new Rect(AxisWidth, Inset, Math.Max(0, ActualWidth - AxisWidth - Inset), Math.Max(0, ActualHeight - (2 * Inset)));
        drawingContext.DrawRectangle(background, null, new Rect(RenderSize));
        if (Plotter is not { Count: > 0 } plotter || plotArea.Width < 10 || plotArea.Height < 10)
        {
            DrawText(drawingContext, Localization.Strings.Get("Plotter_Empty"), text, new Point(AxisWidth, Inset), 12);
            return;
        }

        (double min, double max, double tick) = Range(plotter);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        int ticks = (int)Math.Round((max - min) / tick);
        for (int i = 0; i <= ticks; i++)
        {
            double value = Math.Round(min + (tick * i), 6);
            double y = Math.Round(plotArea.Bottom - (plotArea.Height * i / ticks)) + 0.5;
            drawingContext.DrawLine(grid, new Point(plotArea.Left, y), new Point(plotArea.Right, y));
            var label = new FormattedText(value.ToString("0.######", CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface(), 11, text, dpi);
            drawingContext.DrawText(label, new Point(AxisWidth - 6 - label.Width, y - (label.Height / 2)));
        }

        drawingContext.PushClip(new RectangleGeometry(plotArea));
        double step = plotArea.Width / Math.Max(1, PlotterViewModel.Capacity - 1);
        double offset = PlotterViewModel.Capacity - plotter.Count; // new samples enter at the right edge
        Brush[] palette = Theme.IsLight ? LightPalette : DarkPalette;
        for (int index = 0; index < plotter.Series.Count; index++)
        {
            PlotSeries series = plotter.Series[index];
            if (!series.Visible)
            {
                continue;
            }

            var geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                bool open = false;
                for (int i = 0; i < series.Values.Count; i++)
                {
                    double v = series.Values[i];
                    if (double.IsNaN(v))
                    {
                        open = false;
                        continue;
                    }

                    var point = new Point(plotArea.Left + ((offset + i) * step), plotArea.Bottom - ((v - min) / (max - min) * plotArea.Height));
                    if (open)
                    {
                        context.LineTo(point, true, true);
                    }
                    else
                    {
                        context.BeginFigure(point, false, false);
                        open = true;
                    }
                }
            }

            geometry.Freeze();
            var pen = new Pen(palette[index % palette.Length], 1.5) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            drawingContext.DrawGeometry(null, pen, geometry);
        }

        drawingContext.Pop();

        // Legend: name and last value of every series, top-left inside the plot.
        double x = plotArea.Left + 8;
        _legend.Clear();
        for (int index = 0; index < plotter.Series.Count; index++)
        {
            PlotSeries series = plotter.Series[index];
            double last = series.Values.LastOrDefault(v => !double.IsNaN(v), double.NaN);
            string caption = series.Name + " " + (double.IsNaN(last) ? "—" : Format(last));
            var label = new FormattedText(caption, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface(), 11, Resource(series.Visible ? "Brush.Text.Primary" : "Brush.Text.Disabled", Brushes.White), dpi);
            if (series.Visible)
            {
                drawingContext.DrawRectangle(palette[index % palette.Length], null, new Rect(x, plotArea.Top + 6, 10, 3));
            }

            drawingContext.DrawText(label, new Point(x + 14, plotArea.Top));
            _legend.Add((new Rect(x, plotArea.Top, 14 + label.Width, label.Height), series));
            x += 14 + label.Width + 16;
        }
    }

    protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonUp(e);
        Point at = e.GetPosition(this);
        foreach ((Rect area, PlotSeries series) in _legend)
        {
            if (area.Contains(at))
            {
                series.Visible = !series.Visible;
                InvalidateVisual();
                e.Handled = true;
                return;
            }
        }
    }

    private readonly List<(Rect Area, PlotSeries Series)> _legend = [];

    private static Brush[] Palette(params int[] colors) =>
    [
        .. colors.Select(rgb =>
        {
            var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            brush.Freeze();
            return (Brush)brush;
        }),
    ];

    /// <summary>The value range rounded out to whole ticks (1, 2, 2.5 or 5 times a power of ten), at most six of them.</summary>
    private static (double Min, double Max, double Tick) Range(PlotterViewModel plotter)
    {
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;
        foreach (double v in plotter.Series.Where(s => s.Visible).SelectMany(s => s.Values).Where(v => !double.IsNaN(v)))
        {
            min = Math.Min(min, v);
            max = Math.Max(max, v);
        }

        if (double.IsInfinity(min))
        {
            (min, max) = (0, 1);
        }
        else if (max == min)
        {
            double pad = Math.Max(1, Math.Abs(max) * 0.1);
            (min, max) = (min - pad, max + pad);
        }

        double magnitude = Math.Pow(10, Math.Floor(Math.Log10((max - min) / 6)));
        foreach (double step in NiceSteps)
        {
            // The smallest tick that fits gives the tightest range; 20x always fits.
            double tick = step * magnitude;
            (double low, double high) = (Math.Floor(min / tick) * tick, Math.Ceiling(max / tick) * tick);
            if ((high - low) / tick <= 6.001 || step == NiceSteps[^1])
            {
                return (low, high, tick);
            }
        }

        return (min, max, max - min);
    }

    private static string Format(double value) => Math.Abs(value) >= 1000 || value == Math.Round(value)
        ? value.ToString("0", CultureInfo.CurrentCulture)
        : value.ToString("0.##", CultureInfo.CurrentCulture);

    private Typeface Typeface() => new((FontFamily)(TryFindResource("Font.Mono") ?? new FontFamily("Consolas")), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private Brush Resource(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    private void DrawText(DrawingContext context, string value, Brush brush, Point at, double size) =>
        context.DrawText(new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface(), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), at);
}
