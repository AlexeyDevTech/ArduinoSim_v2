using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArduSim.App.Services;
using ArduSim.App.ViewModels;
using ArduSim.Core.Screens;

namespace ArduSim.App.Views;

/// <summary>One screen, large: elements are clicked, and dragged by cell or by pixel. Gestures only; the view model does the rest.</summary>
public sealed class ScreenFaceView : FrameworkElement
{
    private const double Margin2 = 24;

    private ScreensViewModel? _model;
    private ScreenElement? _dragged;
    private Point _dragStart;
    private int _dragX;
    private int _dragY;
    private ScreenNode? _cachedFor;
    private BitmapSource? _bitmap;

    public ScreenFaceView()
    {
        Focusable = true;
        FocusVisualStyle = null;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        DataContextChanged += (_, e) =>
        {
            if (_model is not null)
            {
                _model.Changed -= OnModelChanged;
            }

            _model = e.NewValue as ScreensViewModel;
            if (_model is not null)
            {
                _model.Changed += OnModelChanged;
            }

            InvalidateVisual();
        };
        Theme.Changed += InvalidateVisual;
    }

    private void OnModelChanged() => InvalidateVisual();

    private ScreenDisplay? Display => _model?.Document?.Display;

    // The bitmap's dots to this view: as large as fits, in whole steps so that dots stay even.
    private (double Scale, double Left, double Top) Placement(int dotsWide, int dotsHigh)
    {
        double available = Math.Min((ActualWidth - (2 * Margin2)) / Math.Max(dotsWide, 1), (ActualHeight - (2 * Margin2)) / Math.Max(dotsHigh, 1));
        double scale = Math.Max(Math.Floor(available), 1);
        return (scale, Math.Round((ActualWidth - (dotsWide * scale)) / 2), Math.Round((ActualHeight - (dotsHigh * scale)) / 2));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_model?.SelectedScreen is not { } screen || Display is not { } display)
        {
            return;
        }

        if (!ReferenceEquals(_cachedFor, screen))
        {
            _bitmap = ScreenPreview.Render(screen, display);
            _cachedFor = screen;
        }

        (int width, int height) = ScreenPreview.DotSize(display);
        (double scale, double left, double top) = Placement(width, height);
        drawingContext.DrawImage(_bitmap, new Rect(left, top, width * scale, height * scale));

        var accent = (Brush)FindResource("Brush.Accent");
        var selected = new Pen(accent, 2);
        var other = new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 1);
        foreach (ScreenElement element in screen.Elements.Where(e => !e.IsAlternative))
        {
            bool isSelected = element.Id == _model.SelectedElementId;
            ScreenElement shown = isSelected && _dragged is not null ? element with { X = _dragX, Y = _dragY } : element;
            Rect bounds = ScreenPreview.BoundsOf(shown, display);
            var rect = new Rect(left + (bounds.X * scale), top + (bounds.Y * scale), bounds.Width * scale, bounds.Height * scale);
            rect.Inflate(2, 2);
            drawingContext.DrawRectangle(null, isSelected ? selected : other, rect);
        }
    }

    private ScreenElement? ElementAt(Point position, out (double Scale, double Left, double Top) placement)
    {
        placement = default;
        if (_model?.SelectedScreen is not { } screen || Display is not { } display)
        {
            return null;
        }

        (int width, int height) = ScreenPreview.DotSize(display);
        placement = Placement(width, height);
        double dotX = (position.X - placement.Left) / placement.Scale;
        double dotY = (position.Y - placement.Top) / placement.Scale;
        var point = new Point(dotX, dotY);
        return screen.Elements.Where(e => !e.IsAlternative).Reverse().FirstOrDefault(e =>
        {
            Rect bounds = ScreenPreview.BoundsOf(e, display);
            bounds.Inflate(2, 2);
            return bounds.Contains(point);
        });
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (_model is null)
        {
            return;
        }

        ScreenElement? element = ElementAt(e.GetPosition(this), out _);
        _model.SelectElement(element?.Id);
        if (element is not null && _model.CanMove(element))
        {
            _dragged = element;
            _dragStart = e.GetPosition(this);
            _dragX = element.X;
            _dragY = element.Y;
            CaptureMouse();
        }

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);
        if (_dragged is not { } element || Display is not { } display)
        {
            return;
        }

        (int width, int height) = ScreenPreview.DotSize(display);
        (double scale, double _, double _) = Placement(width, height);
        Vector moved = e.GetPosition(this) - _dragStart;
        bool lcd = display.Family == DisplayFamily.CharLcd;
        int x = element.X + (int)Math.Round(moved.X / (scale * (lcd ? 6 : 1)));
        int y = element.Y + (int)Math.Round(moved.Y / (scale * (lcd ? 9 : 1)));
        _dragX = element.XSpan is not null || _model?.IsGenerated == true ? Math.Clamp(x, 0, display.Width - 1) : element.X;
        _dragY = element.YSpan is not null || _model?.IsGenerated == true ? Math.Clamp(y, 0, display.Height - 1) : element.Y;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonUp(e);
        if (_dragged is { } element)
        {
            _dragged = null;
            ReleaseMouseCapture();
            if (_dragX != element.X || _dragY != element.Y)
            {
                _model?.MoveElement(element.Id, _dragX, _dragY);
            }

            InvalidateVisual();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        if (_model?.SelectedElement is not { } element)
        {
            return;
        }

        (int dx, int dy) = e.Key switch
        {
            Key.Left => (-1, 0),
            Key.Right => (1, 0),
            Key.Up => (0, -1),
            Key.Down => (0, 1),
            _ => (0, 0),
        };
        if (dx != 0 || dy != 0)
        {
            _model.MoveElement(element.Id, element.X + dx, element.Y + dy);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && _model.RemoveSelectedCommand.CanExecute(null))
        {
            _model.RemoveSelectedCommand.Execute(null);
            e.Handled = true;
        }
    }
}
