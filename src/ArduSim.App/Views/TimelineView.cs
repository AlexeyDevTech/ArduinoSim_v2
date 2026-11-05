using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ArduSim.App.Services;
using ArduSim.App.ViewModels;
using ArduSim.Core.Tracing;

namespace ArduSim.App.Views;

/// <summary>
/// Draws the Диаграмма tab (ADR-014): the names on the left, a time ruler, one waveform per pin and
/// one row of labeled frames per bus. Wheel zooms about the pointer, a drag pans, a click sets the
/// marker Δ is measured from, the ✕ at a row's name removes it.
/// </summary>
public sealed class TimelineView : FrameworkElement
{
    public static readonly DependencyProperty TimelineProperty =
        DependencyProperty.Register(nameof(Timeline), typeof(TimelineViewModel), typeof(TimelineView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Bound to <see cref="TimelineViewModel.Version"/>, so new data redraws.</summary>
    public static readonly DependencyProperty VersionProperty =
        DependencyProperty.Register(nameof(Version), typeof(long), typeof(TimelineView), new FrameworkPropertyMetadata(0L, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double NameWidth = 150;
    private const double RulerHeight = 22;
    private const double RowHeight = 28;
    private const double ClickSlop = 3;

    private Point? _dragFrom;
    private double _dragged;

    public TimelineView()
    {
        ClipToBounds = true;
        Loaded += (_, _) => Theme.Changed += InvalidateVisual;
        Unloaded += (_, _) => Theme.Changed -= InvalidateVisual;
    }

    public TimelineViewModel? Timeline
    {
        get => (TimelineViewModel?)GetValue(TimelineProperty);
        set => SetValue(TimelineProperty, value);
    }

    public long Version
    {
        get => (long)GetValue(VersionProperty);
        set => SetValue(VersionProperty, value);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Timeline?.SetViewWidth(ActualWidth - NameWidth);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        Brush background = Resource("Brush.Canvas", Brushes.Black);
        Brush text = Resource("Brush.Text.Secondary", Brushes.Gray);
        drawingContext.DrawRectangle(background, null, new Rect(RenderSize));
        if (Timeline is not { } timeline || ActualWidth <= NameWidth + 10)
        {
            return;
        }

        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (timeline.IsEmpty)
        {
            drawingContext.DrawText(Label(Localization.Strings.Get("Timeline_Empty"), text, 12, dip), new Point(12, 12));
            return;
        }

        var grid = new Pen(Resource("Brush.Border", Brushes.DimGray), 1);
        grid.Freeze();
        double plotWidth = ActualWidth - NameWidth;
        DrawRuler(drawingContext, timeline, grid, text, plotWidth, dip);
        Brush accent = Resource("Brush.Accent", Brushes.Teal);
        var wave = new Pen(accent, 1);
        wave.Freeze();
        var columns = new ColumnLevel[(int)plotWidth];
        double y = RulerHeight;
        foreach (TimelineRow row in timeline.Rows)
        {
            drawingContext.DrawLine(grid, new Point(0, y + RowHeight - 0.5), new Point(ActualWidth, y + RowHeight - 0.5));
            drawingContext.PushClip(new RectangleGeometry(new Rect(0, y, NameWidth - 18, RowHeight)));
            drawingContext.DrawText(Label(row.Title, Resource("Brush.Text.Primary", Brushes.White), 12, dip), new Point(8, y + 6));
            drawingContext.Pop();
            drawingContext.DrawText(Label("✕", text, 10, dip), new Point(NameWidth - 16, y + 8));
            drawingContext.PushClip(new RectangleGeometry(new Rect(NameWidth, y, plotWidth, RowHeight)));
            if (row.Decoder is { } decoder)
            {
                DrawFrames(drawingContext, timeline, decoder, y, plotWidth, accent, dip);
            }
            else
            {
                timeline.Store.Render(row.Pin, timeline.ViewStart, timeline.NsPerPixel, columns);
                DrawWave(drawingContext, timeline, row.Pin, columns, y, wave, accent, text, dip);
            }

            drawingContext.Pop();
            y += RowHeight;
        }

        var markerPen = new Pen(Resource("Brush.Warning", Brushes.Orange), 1);
        markerPen.Freeze();
        var cursorPen = new Pen(text, 1) { DashStyle = DashStyles.Dot };
        cursorPen.Freeze();
        DrawVertical(drawingContext, timeline, timeline.Marker, markerPen, y);
        DrawVertical(drawingContext, timeline, timeline.Cursor, cursorPen, y);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseWheel(e);
        Timeline?.ZoomAt(Math.Max(0, e.GetPosition(this).X - NameWidth), e.Delta > 0 ? 1 / 1.25 : 1.25);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonDown(e);
        Point at = e.GetPosition(this);
        if (Timeline is not { } timeline)
        {
            return;
        }

        if (at.X < NameWidth)
        {
            int index = (int)((at.Y - RulerHeight) / RowHeight);
            if (at.X >= NameWidth - 20 && index >= 0 && index < timeline.Rows.Count)
            {
                timeline.RemoveRow(timeline.Rows[index]);
            }

            return;
        }

        _dragFrom = at;
        _dragged = 0;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);
        Point at = e.GetPosition(this);
        if (Timeline is not { } timeline)
        {
            return;
        }

        if (_dragFrom is { } from)
        {
            _dragged += Math.Abs(at.X - from.X);
            timeline.PanBy(from.X - at.X);
            _dragFrom = at;
        }

        timeline.SetCursor(at.X >= NameWidth ? at.X - NameWidth : null);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonUp(e);
        if (_dragFrom is not null)
        {
            ReleaseMouseCapture();
            if (_dragged < ClickSlop)
            {
                Timeline?.SetMarker(e.GetPosition(this).X - NameWidth);
            }

            _dragFrom = null;
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Timeline?.SetCursor(null);
    }

    private void DrawRuler(DrawingContext context, TimelineViewModel timeline, Pen grid, Brush text, double plotWidth, double dip)
    {
        ulong step = TimelineViewModel.TickStep(timeline.NsPerPixel);
        ulong tick = timeline.ViewStart / step * step;
        ulong end = timeline.ViewStart + (ulong)(plotWidth * timeline.NsPerPixel);
        for (; tick <= end; tick += step)
        {
            if (tick < timeline.ViewStart)
            {
                continue;
            }

            double x = NameWidth + Math.Round((tick - timeline.ViewStart) / timeline.NsPerPixel) + 0.5;
            context.DrawLine(grid, new Point(x, RulerHeight - 6), new Point(x, ActualHeight));
            context.DrawText(Label(TimelineViewModel.FormatTime(tick), text, 10, dip), new Point(x + 3, 3));
        }

        context.DrawLine(grid, new Point(0, RulerHeight - 0.5), new Point(ActualWidth, RulerHeight - 0.5));
        context.DrawLine(grid, new Point(NameWidth - 0.5, 0), new Point(NameWidth - 0.5, ActualHeight));
    }

    private static void DrawWave(DrawingContext context, TimelineViewModel timeline, int pin, ColumnLevel[] columns, double y, Pen wave, Brush accent, Brush text, double dip)
    {
        double high = y + 5;
        double low = y + RowHeight - 6;
        double middle = (high + low) / 2;
        var geometry = new StreamGeometry();
        using (StreamGeometryContext path = geometry.Open())
        {
            double? last = null;
            for (int column = 0; column < columns.Length; column++)
            {
                double x = NameWidth + column + 0.5;
                ColumnLevel level = columns[column];
                if (level is ColumnLevel.None || (level & (ColumnLevel.Pwm | ColumnLevel.Tone)) != 0)
                {
                    last = null;
                    continue;
                }

                if ((level & ColumnLevel.Floating) != 0 && (level & (ColumnLevel.Low | ColumnLevel.High)) == 0)
                {
                    // A dotted middle line: nothing drives the net.
                    if (column % 4 == 0)
                    {
                        path.BeginFigure(new Point(x, middle), false, false);
                        path.LineTo(new Point(x + 2, middle), true, false);
                    }

                    last = null;
                    continue;
                }

                bool both = (level & ColumnLevel.Low) != 0 && (level & ColumnLevel.High) != 0;
                double to = (level & ColumnLevel.High) != 0 ? high : low;
                if (both)
                {
                    path.BeginFigure(new Point(x, high), false, false);
                    path.LineTo(new Point(x, low), true, false);
                    last = null;
                    continue;
                }

                path.BeginFigure(new Point(x, last ?? to), false, false);
                path.LineTo(new Point(x, to), true, false);
                path.LineTo(new Point(x + 1, to), true, false);
                last = to;
            }
        }

        geometry.Freeze();
        context.DrawGeometry(null, wave, geometry);
        DrawBands(context, timeline, pin, columns, y, accent, text, dip);
    }

    // PWM and tones are not waveforms in the simulator: a band with the duty or the frequency.
    private static void DrawBands(DrawingContext context, TimelineViewModel timeline, int pin, ColumnLevel[] columns, double y, Brush accent, Brush text, double dip)
    {
        int column = 0;
        while (column < columns.Length)
        {
            if ((columns[column] & (ColumnLevel.Pwm | ColumnLevel.Tone)) == 0)
            {
                column++;
                continue;
            }

            int first = column;
            while (column < columns.Length && (columns[column] & (ColumnLevel.Pwm | ColumnLevel.Tone)) != 0)
            {
                column++;
            }

            var band = new Rect(NameWidth + first, y + 5, column - first, RowHeight - 11);
            context.PushOpacity(0.3);
            context.DrawRectangle(accent, null, band);
            context.Pop();
            ulong at = timeline.ViewStart + (ulong)((first + 1) * timeline.NsPerPixel);
            if (timeline.Store.SampleAt(pin, at) is { } sample && band.Width > 40)
            {
                string label = sample.Level == SignalLevel.Tone
                    ? Localization.Strings.Format("Timeline_Tone", sample.Value)
                    : Localization.Strings.Format("Timeline_Pwm", (sample.Value / 10.0).ToString("0.#", CultureInfo.CurrentCulture));
                context.DrawText(Label(label, text, 10, dip), new Point(band.Left + 4, y + 7));
            }
        }
    }

    private void DrawFrames(DrawingContext context, TimelineViewModel timeline, BusDecoder decoder, double y, double plotWidth, Brush accent, double dip)
    {
        ulong start = timeline.ViewStart;
        ulong end = start + (ulong)(plotWidth * timeline.NsPerPixel);
        (int first, int count) = TraceStore.FramesIn(decoder, start, end);
        Brush error = Resource("Brush.Error", Brushes.IndianRed);
        Brush text = Resource("Brush.Text.Primary", Brushes.White);
        double lastRight = double.NegativeInfinity;
        for (int i = first; i < first + count; i++)
        {
            DecodedFrame frame = decoder.Frames[i];
            double left = NameWidth + (((double)frame.Start - start) / timeline.NsPerPixel);
            double right = NameWidth + (((double)frame.End - start) / timeline.NsPerPixel);
            if (right - lastRight < 0.5)
            {
                continue; // too fine to see: the band already covers this pixel
            }

            lastRight = right;
            Brush fill = frame.Kind == FrameKind.Error ? error : accent;
            var box = new Rect(left, y + 4, Math.Max(1, right - left), RowHeight - 9);
            context.PushOpacity(0.35);
            context.DrawRoundedRectangle(fill, null, box, 2, 2);
            context.Pop();
            FormattedText label = Label(frame.Text, text, 11, dip);
            if (label.Width + 4 < box.Width)
            {
                context.DrawText(label, new Point(left + ((box.Width - label.Width) / 2), y + 6));
            }
        }
    }

    private void DrawVertical(DrawingContext context, TimelineViewModel timeline, ulong? time, Pen pen, double bottom)
    {
        if (time is { } at && at >= timeline.ViewStart)
        {
            double x = NameWidth + Math.Round((at - timeline.ViewStart) / timeline.NsPerPixel) + 0.5;
            if (x < ActualWidth)
            {
                context.DrawLine(pen, new Point(x, 0), new Point(x, bottom));
            }
        }
    }

    private static FormattedText Label(string text, Brush brush, double size, double dip) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, dip);

    private Brush Resource(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
}
