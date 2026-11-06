using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArduSim.App.Services;
using ArduSim.App.ViewModels;
using ArduSim.Core.Screens;

namespace ArduSim.App.Views;

/// <summary>
/// One screen, large, as a construction surface (docs/UI.md §2.10): elements are clicked, dragged by cell or
/// by pixel, resized by their handle and edited in place, and new ones are dropped from the toolbar. Gestures
/// only; the view model does the rest.
/// </summary>
public sealed class ScreenFaceView : FrameworkElement
{
    /// <summary>The format of what the toolbar's element buttons drag: the element's kind.</summary>
    public const string ElementFormat = "ardusim-screen-element";

    private const double Edge = 24;
    private const double HandleSize = 8;

    private ScreensViewModel? _model;
    private ScreenElement? _moving;
    private ScreenElement? _sizing;
    private Point _dragStart;
    private int _dragX;
    private int _dragY;
    private int _dragWidth;
    private int _dragHeight;
    private ScreenNode? _cachedFor;
    private BitmapSource? _bitmap;

    public ScreenFaceView()
    {
        Focusable = true;
        FocusVisualStyle = null;
        AllowDrop = true;
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

    /// <summary>A double-click, F2 or Enter on a text or value: its text, in place, over this rectangle.</summary>
    public event Action<ScreenElement, Rect>? EditTextRequested;

    private void OnModelChanged() => InvalidateVisual();

    private ScreenDisplay? Display => _model?.Document?.Display;

    // The bitmap's dots to this view: as large as fits, in whole steps so that dots stay even.
    private (double Scale, double Left, double Top) Placement(ScreenDisplay display)
    {
        (int dotsWide, int dotsHigh) = ScreenPreview.DotSize(display);
        double available = Math.Min((ActualWidth - (2 * Edge)) / Math.Max(dotsWide, 1), (ActualHeight - (2 * Edge)) / Math.Max(dotsHigh, 1));
        double scale = Math.Max(Math.Floor(available), 1);
        return (scale, Math.Round((ActualWidth - (dotsWide * scale)) / 2), Math.Round((ActualHeight - (dotsHigh * scale)) / 2));
    }

    private ScreenElement Shown(ScreenElement element) =>
        ReferenceEquals(element, _moving) ? element with { X = _dragX, Y = _dragY }
        : ReferenceEquals(element, _sizing) ? element with { Width = _dragWidth, Height = _dragHeight }
        : element;

    // What a drag has done so far is drawn as it will be, not as an outline.
    private ScreenNode Live(ScreenNode screen) =>
        _moving is null && _sizing is null ? screen : screen with { Elements = [.. screen.Elements.Select(Shown)] };

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (_model?.SelectedScreen is not { } screen || Display is not { } display)
        {
            return;
        }

        if (_moving is not null || _sizing is not null)
        {
            _bitmap = ScreenPreview.Render(Live(screen), display);
            _cachedFor = null;
        }
        else if (!ReferenceEquals(_cachedFor, screen))
        {
            _bitmap = ScreenPreview.Render(screen, display);
            _cachedFor = screen;
        }

        (int width, int height) = ScreenPreview.DotSize(display);
        (double scale, double left, double top) = Placement(display);
        drawingContext.DrawImage(_bitmap, new Rect(left, top, width * scale, height * scale));
        if (display.Family == DisplayFamily.Oled && scale >= 3)
        {
            DrawGrid(drawingContext, width, height, scale, left, top);
        }

        var accent = (Brush)FindResource("Brush.Accent");
        var selected = new Pen(accent, 2);
        var other = new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 1);
        foreach (ScreenElement element in ScreenPreview.Shown(screen, display))
        {
            bool isSelected = element.Id == _model.SelectedElementId;
            Rect rect = ViewRect(Shown(element), display, scale, left, top);
            rect.Inflate(2, 2);
            drawingContext.DrawRectangle(null, isSelected ? selected : other, rect);
            if (isSelected && HandleAt(Shown(element), display) is { } handle)
            {
                drawingContext.DrawRectangle(accent, null, HandleRect(handle, scale, left, top));
            }
        }
    }

    // Faint lines every eight pixels: text rows on an OLED.
    private static void DrawGrid(DrawingContext dc, int width, int height, double scale, double left, double top)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)), 1);
        pen.Freeze();
        for (int x = 8; x < width; x += 8)
        {
            dc.DrawLine(pen, new Point(left + (x * scale), top), new Point(left + (x * scale), top + (height * scale)));
        }

        for (int y = 8; y < height; y += 8)
        {
            dc.DrawLine(pen, new Point(left, top + (y * scale)), new Point(left + (width * scale), top + (y * scale)));
        }
    }

    private static Rect ViewRect(ScreenElement element, ScreenDisplay display, double scale, double left, double top)
    {
        Rect bounds = ScreenPreview.BoundsOf(element, display);
        return new Rect(left + (bounds.X * scale), top + (bounds.Y * scale), bounds.Width * scale, bounds.Height * scale);
    }

    // The point a handle drags, in dots: a shape's corner, a line's far end, a value's right edge in a generated project.
    private Point? HandleAt(ScreenElement element, ScreenDisplay display)
    {
        if (_model is null)
        {
            return null;
        }

        Rect bounds = ScreenPreview.BoundsOf(element, display);
        if (element.Kind == ElementKind.Line && _model.CanResize(element))
        {
            return new Point(element.Width, element.Height);
        }

        if (_model.CanResize(element))
        {
            return bounds.BottomRight;
        }

        return element.Kind == ElementKind.Value && _model.IsGenerated ? new Point(bounds.Right, bounds.Y + (bounds.Height / 2)) : null;
    }

    private static Rect HandleRect(Point handle, double scale, double left, double top) =>
        new(left + (handle.X * scale) - (HandleSize / 2), top + (handle.Y * scale) - (HandleSize / 2), HandleSize, HandleSize);

    private ScreenElement? ElementAt(Point position)
    {
        if (_model?.SelectedScreen is not { } screen || Display is not { } display)
        {
            return null;
        }

        (double scale, double left, double top) = Placement(display);
        var point = new Point((position.X - left) / scale, (position.Y - top) / scale);
        return ScreenPreview.Shown(screen, display).Reverse().FirstOrDefault(e =>
        {
            Rect bounds = ScreenPreview.BoundsOf(e, display);
            bounds.Inflate(2, 2);
            return bounds.Contains(point);
        });
    }

    // Where on the display a point of this view is, as an element's position.
    private (int X, int Y) DisplayPoint(Point position, ScreenDisplay display)
    {
        (double scale, double left, double top) = Placement(display);
        double dotX = (position.X - left) / scale;
        double dotY = (position.Y - top) / scale;
        return display.Family == DisplayFamily.CharLcd
            ? ((int)Math.Floor((dotX - 1) / 6), (int)Math.Floor((dotY - 1) / 9))
            : ((int)Math.Round(dotX), (int)Math.Round(dotY) + (display.TextFromBaseline ? 7 : 0));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (_model is null || Display is not { } display)
        {
            return;
        }

        Point position = e.GetPosition(this);
        (double scale, double left, double top) = Placement(display);
        if (_model.SelectedElement is { } current && HandleAt(current, display) is { } handle && HandleRect(handle, scale, left, top).Contains(position))
        {
            _sizing = current;
            _dragStart = position;
            _dragWidth = current.Width;
            _dragHeight = current.Height;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        ScreenElement? element = ElementAt(position);
        if (element?.Id != _model.SelectedElementId || element is null)
        {
            _model.SelectElement(element?.Id);
        }

        element = _model.SelectedElement;
        if (element is not null && e.ClickCount == 2)
        {
            RequestTextEdit(element, display);
        }
        else if (element is not null && _model.CanMove(element))
        {
            _moving = element;
            _dragStart = position;
            _dragX = element.X;
            _dragY = element.Y;
            CaptureMouse();
        }

        e.Handled = true;
    }

    private void RequestTextEdit(ScreenElement element, ScreenDisplay display)
    {
        bool editable = element.Kind == ElementKind.Text ? _model?.IsGenerated == true || element.TextSpan is not null
            : element.Kind == ElementKind.Value && (_model?.IsGenerated == true || element.ExpressionSpan is not null);
        if (editable)
        {
            (double scale, double left, double top) = Placement(display);
            EditTextRequested?.Invoke(element, ViewRect(element, display, scale, left, top));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);
        if (Display is not { } display)
        {
            return;
        }

        (double scale, double left, double top) = Placement(display);
        Point position = e.GetPosition(this);
        Vector moved = position - _dragStart;
        bool lcd = display.Family == DisplayFamily.CharLcd;
        if (_moving is { } element)
        {
            int x = element.X + (int)Math.Round(moved.X / (scale * (lcd ? 6 : 1)));
            int y = element.Y + (int)Math.Round(moved.Y / (scale * (lcd ? 9 : 1)));
            _dragX = _model?.CanMoveAcross(element) == true ? Math.Clamp(x, 0, display.Width - 1) : element.X;
            _dragY = _model?.CanMoveDown(element) == true ? Math.Clamp(y, 0, display.Height - 1) : element.Y;
            InvalidateVisual();
        }
        else if (_sizing is { } sized)
        {
            int across = (int)Math.Round(moved.X / (scale * (lcd ? 6 : 1)));
            int down = (int)Math.Round(moved.Y / scale);
            _dragWidth = sized.Kind == ElementKind.Value ? Math.Max(1, sized.Width + across)
                : sized.Kind == ElementKind.Line ? Math.Clamp(sized.Width + across, 0, display.Width - 1)
                : Math.Max(1, sized.Width + across);
            _dragHeight = sized.Kind == ElementKind.Value ? sized.Height
                : sized.Kind == ElementKind.Line ? Math.Clamp(sized.Height + down, 0, display.Height - 1)
                : Math.Max(1, sized.Height + down);
            InvalidateVisual();
        }
        else if (_model?.SelectedElement is { } current && HandleAt(current, display) is { } handle && HandleRect(handle, scale, left, top).Contains(position))
        {
            Cursor = Cursors.SizeNWSE;
        }
        else
        {
            Cursor = ElementAt(position) is { } over && _model?.CanMove(over) == true ? Cursors.SizeAll : Cursors.Arrow;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonUp(e);
        if (_moving is { } element)
        {
            _moving = null;
            ReleaseMouseCapture();
            if (_dragX != element.X || _dragY != element.Y)
            {
                _model?.MoveElement(element.Id, _dragX, _dragY);
            }

            InvalidateVisual();
        }
        else if (_sizing is { } sized)
        {
            _sizing = null;
            ReleaseMouseCapture();
            if (_dragWidth != sized.Width || _dragHeight != sized.Height)
            {
                _model?.ResizeElement(sized.Id, _dragWidth, _dragHeight, finished: true);
            }

            InvalidateVisual();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        if (_model is null)
        {
            return;
        }

        if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control && _model.DuplicateSelectedCommand.CanExecute(null))
        {
            _model.DuplicateSelectedCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (_model.SelectedElement is not { } element)
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
        else if (e.Key is Key.F2 or Key.Enter && Display is { } display)
        {
            RequestTextEdit(element, display);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _model.SelectElement(null);
            e.Handled = true;
        }
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnDragOver(e);
        e.Effects = e.Data.GetDataPresent(ElementFormat) && _model?.CanAddElements == true ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnDrop(e);
        if (_model is not null && Display is { } display && e.Data.GetData(ElementFormat) is string kind && Enum.TryParse(kind, out ElementKind parsed))
        {
            (int x, int y) = DisplayPoint(e.GetPosition(this), display);
            _model.AddElementAt(parsed, x, y);
            Focus();
            e.Handled = true;
        }
    }
}
