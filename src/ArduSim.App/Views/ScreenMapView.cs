using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArduSim.App.Localization;
using ArduSim.App.Services;
using ArduSim.App.ViewModels;
using ArduSim.Core.Screens;

namespace ArduSim.App.Views;

/// <summary>
/// The map: each screen a card with its thumbnail, each transition a labelled arrow. Drag a card to
/// place it, the background to pan, the wheel to zoom; drag from a card's right edge to another card to
/// connect them. Gestures only; the view model does the rest.
/// </summary>
public sealed class ScreenMapView : FrameworkElement
{
    private const double CardWidth = 200;
    private const double HeaderHeight = 24;
    private const double HandleRadius = 6;
    private const double AnywhereX = -240;

    private readonly Dictionary<string, (ScreenNode Node, BitmapSource Bitmap)> _thumbnails = new(StringComparer.Ordinal);
    private readonly List<(Rect Box, string? From, int Index)> _labels = [];
    private readonly List<(Rect Box, FormattedText Text)> _labelTexts = [];
    private ScreensViewModel? _model;
    private double _zoom = 0.8;
    private Vector _pan = new(260, 40);
    private string? _dragging;
    private Vector _dragOffset;
    private string? _connecting;
    private Point _connectTo;
    private bool _dragMoved;
    private bool _panning;
    private Point _panFrom;

    public ScreenMapView()
    {
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        DataContextChanged += (_, e) =>
        {
            if (_model is not null)
            {
                _model.Changed -= InvalidateVisual;
            }

            _model = e.NewValue as ScreensViewModel;
            if (_model is not null)
            {
                _model.Changed += InvalidateVisual;
            }

            _fitted = false;
            InvalidateVisual();
        };
        Theme.Changed += InvalidateVisual;
    }

    private bool _fitted;

    private ScreenDocument? Document => _model?.Document;

    private static double CardHeight(ScreenDisplay display)
    {
        (int width, int height) = ScreenPreview.DotSize(display);
        return HeaderHeight + (CardWidth * height / width) + 8;
    }

    private static Rect CardRect(ScreenNode screen, ScreenDisplay display) => new(screen.MapX, screen.MapY, CardWidth, CardHeight(display));

    private static Rect AnywhereRect() => new(AnywhereX, 0, 150, 40);

    private Point ToWorld(Point view) => new((view.X - _pan.X) / _zoom, (view.Y - _pan.Y) / _zoom);

    private BitmapSource Thumbnail(ScreenNode screen, ScreenDisplay display)
    {
        if (_thumbnails.TryGetValue(screen.Id, out var known) && ReferenceEquals(known.Node, screen))
        {
            return known.Bitmap;
        }

        BitmapSource bitmap = ScreenPreview.Render(screen, display);
        _thumbnails[screen.Id] = (screen, bitmap);
        return bitmap;
    }

    private Brush Token(string key) => (Brush)FindResource(key);

    private static Typeface Ui => new(new FontFamily("Segoe UI Variable, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private FormattedText Label(string text, double size, Brush brush) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Ui, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    /// <summary>The whole map in the view.</summary>
    public void Fit()
    {
        _fitted = false;
        InvalidateVisual();
    }

    private void FitOnce(ScreenDocument document)
    {
        if (_fitted || ActualWidth <= 0 || document.Screens.Count == 0)
        {
            return;
        }

        _fitted = true;
        double right = document.Screens.Max(s => s.MapX) + CardWidth;
        double bottom = document.Screens.Max(s => s.MapY + CardHeight(document.Display));
        double left = document.FromAnywhere.Count > 0 ? AnywhereX : document.Screens.Min(s => s.MapX);
        double top = document.Screens.Min(s => s.MapY);
        _zoom = Math.Clamp(Math.Min((ActualWidth - 40) / Math.Max(right - left, 1), (ActualHeight - 40) / Math.Max(bottom - top, 1)), 0.25, 1);
        _pan = new Vector(20 - (left * _zoom), 20 - (top * _zoom));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (Document is not { } document)
        {
            return;
        }

        FitOnce(document);
        drawingContext.PushTransform(new MatrixTransform(_zoom, 0, 0, _zoom, _pan.X, _pan.Y));
        DrawArrows(drawingContext, document);
        foreach (ScreenNode screen in document.Screens)
        {
            DrawCard(drawingContext, document, screen);
        }

        // Over the cards, so that a label wider than the gap can still be read.
        foreach ((Rect box, FormattedText text) in _labelTexts)
        {
            drawingContext.DrawRoundedRectangle(Token("Brush.Canvas"), null, box, 3, 3);
            drawingContext.DrawText(text, new Point(box.X + 3, box.Y + 1));
        }

        if (document.FromAnywhere.Count > 0)
        {
            Rect rect = AnywhereRect();
            drawingContext.DrawRoundedRectangle(Token("Brush.SurfaceRaised"), new Pen(Token("Brush.Border"), 1), rect, 6, 6);
            FormattedText text = Label(Strings.Get("Screens_Anywhere"), 12, Token("Brush.Text.Secondary"));
            drawingContext.DrawText(text, new Point(rect.X + ((rect.Width - text.Width) / 2), rect.Y + ((rect.Height - text.Height) / 2)));
        }

        if (_connecting is { } from && document.Find(from) is { } source)
        {
            Rect rect = CardRect(source, document.Display);
            drawingContext.DrawLine(new Pen(Token("Brush.Accent"), 2), new Point(rect.Right, rect.Y + (rect.Height / 2)), _connectTo);
        }

        drawingContext.Pop();
    }

    private void DrawCard(DrawingContext dc, ScreenDocument document, ScreenNode screen)
    {
        Rect rect = CardRect(screen, document.Display);
        bool selected = screen.Id == _model?.SelectedScreenId;
        dc.DrawRoundedRectangle(Token("Brush.SurfaceRaised"), new Pen(selected ? Token("Brush.Accent") : Token("Brush.Border"), selected ? 2 : 1), rect, 6, 6);
        string title = screen.Title.Length > 0 ? screen.Title : screen.Id;
        var header = Label(document.Start == screen.Id ? "▶ " + title : title, 12, Token("Brush.Text.Primary"));
        header.MaxTextWidth = CardWidth - 16;
        header.MaxLineCount = 1;
        header.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(header, new Point(rect.X + 8, rect.Y + 4));
        var image = new Rect(rect.X + 4, rect.Y + HeaderHeight, CardWidth - 8, rect.Height - HeaderHeight - 4);
        dc.DrawImage(Thumbnail(screen, document.Display), image);
        if (screen.DrawnElsewhere)
        {
            FormattedText note = Label(Strings.Get("Screens_DrawnElsewhere"), 11, Token("Brush.Text.Hint"));
            note.MaxTextWidth = image.Width - 8;
            dc.DrawText(note, new Point(image.X + 4, image.Y + 4));
        }

        if (_model?.CanConnect == true)
        {
            dc.DrawEllipse(Token("Brush.Accent"), null, new Point(rect.Right, rect.Y + (rect.Height / 2)), HandleRadius, HandleRadius);
        }
    }

    private void DrawArrows(DrawingContext dc, ScreenDocument document)
    {
        _labels.Clear();
        _labelTexts.Clear();
        foreach (ScreenNode screen in document.Screens)
        {
            foreach (IGrouping<string, (string Target, string Label, int Index)> group in ScreensViewModel.LinksOf(screen).GroupBy(l => l.Target, StringComparer.Ordinal))
            {
                if (group.Key != screen.Id && document.Find(group.Key) is { } target)
                {
                    bool selected = _model?.SelectedTransition is { } picked && picked.From == screen.Id && group.Any(l => l.Index == picked.Index);
                    Arrow(dc, CardRect(screen, document.Display), CardRect(target, document.Display), string.Join(", ", group.Select(l => l.Label).Distinct(StringComparer.Ordinal).Take(3)), selected, screen.Id == _model?.SelectedScreenId || group.Key == _model?.SelectedScreenId, screen.Id, group.First().Index);
                }
            }
        }

        foreach (IGrouping<string, (ScreenTransition Transition, int Index)> group in document.FromAnywhere.Select((t, i) => (t, i)).GroupBy(p => p.t.Target, StringComparer.Ordinal))
        {
            if (document.Find(group.Key) is { } target)
            {
                Arrow(dc, AnywhereRect(), CardRect(target, document.Display), string.Join(", ", group.Select(p => ScreensViewModel.TriggerText(p.Transition.Trigger, null)).Distinct(StringComparer.Ordinal).Take(3)), _model?.SelectedTransition is { From: null } picked && group.Any(p => p.Index == picked.Index), group.Key == _model?.SelectedScreenId, null, group.First().Index);
            }
        }
    }

    private void Arrow(DrawingContext dc, Rect from, Rect to, string label, bool selected, bool near, string? source, int index)
    {
        (Point start, Point one, Point two, Point end) = Route(from, to);
        var figure = new PathFigure { StartPoint = start, IsFilled = false };
        figure.Segments.Add(new BezierSegment(one, two, end, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        Brush brush = selected || near ? Token("Brush.Accent") : Token("Brush.Text.Hint");
        dc.DrawGeometry(null, new Pen(brush, selected ? 2.5 : near ? 1.5 : 1), geometry);

        // The head along the curve's last direction.
        Vector direction = end - two;
        direction.Normalize();
        var side = new Vector(-direction.Y, direction.X);
        var head = new StreamGeometry();
        using (StreamGeometryContext context = head.Open())
        {
            context.BeginFigure(end, true, true);
            context.LineTo(end - (direction * 8) + (side * 4), true, false);
            context.LineTo(end - (direction * 8) - (side * 4), true, false);
        }

        head.Freeze();
        dc.DrawGeometry(brush, null, head);

        if (label.Length > 0)
        {
            double t = 0.5;
            var middle = new Point(
                (Math.Pow(1 - t, 3) * start.X) + (3 * Math.Pow(1 - t, 2) * t * one.X) + (3 * (1 - t) * t * t * two.X) + (t * t * t * end.X),
                (Math.Pow(1 - t, 3) * start.Y) + (3 * Math.Pow(1 - t, 2) * t * one.Y) + (3 * (1 - t) * t * t * two.Y) + (t * t * t * end.Y));
            FormattedText text = Label(label, 10, near || selected ? Token("Brush.Text.Primary") : Token("Brush.Text.Secondary"));
            text.MaxTextWidth = 150;
            text.MaxLineCount = 1;
            text.Trimming = TextTrimming.CharacterEllipsis;
            var box = new Rect(middle.X - (text.Width / 2) - 3, middle.Y - (text.Height / 2) - 1, text.Width + 6, text.Height + 2);
            _labelTexts.Add((box, text));
            _labels.Add((box, source, index));
        }
    }

    // A forward arrow leaves to the right and arrives from the left, a backward one bows below both
    // cards, and one within a column goes between their facing edges: none crosses its own cards.
    private static (Point Start, Point One, Point Two, Point End) Route(Rect from, Rect to)
    {
        if (to.X >= from.Right - 1)
        {
            var start = new Point(from.Right, from.Y + (from.Height / 2));
            var end = new Point(to.X, to.Y + (to.Height / 2));
            double reach = Math.Max(40, (end.X - start.X) / 2);
            return (start, new Point(start.X + reach, start.Y), new Point(end.X - reach, end.Y), end);
        }

        double fromCenter = from.X + (from.Width / 2);
        double toCenter = to.X + (to.Width / 2);
        if (to.Right <= from.X + 1)
        {
            var start = new Point(fromCenter, from.Bottom);
            var end = new Point(toCenter, to.Bottom);
            double bow = 40 + (Math.Abs(fromCenter - toCenter) * 0.12);
            return (start, new Point(start.X, start.Y + bow), new Point(end.X, end.Y + bow), end);
        }

        if (to.Y >= from.Y)
        {
            var start = new Point(fromCenter, from.Bottom);
            var end = new Point(toCenter, to.Y);
            double reach = Math.Max(20, (end.Y - start.Y) / 2);
            return (start, new Point(start.X, start.Y + reach), new Point(end.X, end.Y - reach), end);
        }

        var up = new Point(fromCenter, from.Y);
        var arrive = new Point(toCenter, to.Bottom);
        double rise = Math.Max(20, (up.Y - arrive.Y) / 2);
        return (up, new Point(up.X, up.Y - rise), new Point(arrive.X, arrive.Y + rise), arrive);
    }

    private ScreenNode? CardAt(Point world)
    {
        if (Document is not { } document)
        {
            return null;
        }

        return document.Screens.Reverse().FirstOrDefault(s => CardRect(s, document.Display).Contains(world));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonDown(e);
        Focus();
        Point world = ToWorld(e.GetPosition(this));
        if (Document is { } document && _model?.CanConnect == true)
        {
            foreach (ScreenNode screen in document.Screens)
            {
                Rect rect = CardRect(screen, document.Display);
                var handle = new Point(rect.Right, rect.Y + (rect.Height / 2));
                if ((world - handle).Length <= HandleRadius + 4)
                {
                    _connecting = screen.Id;
                    _connectTo = world;
                    CaptureMouse();
                    e.Handled = true;
                    return;
                }
            }
        }

        foreach ((Rect box, string? from, int index) in _labels)
        {
            if (box.Contains(world))
            {
                // A menu item's arrow has its row in the panel of its screen.
                if (index >= 0)
                {
                    _model?.SelectTransition(from, index);
                }
                else
                {
                    _model?.SelectScreen(from);
                }

                e.Handled = true;
                return;
            }
        }

        if (CardAt(world) is { } card)
        {
            _model?.SelectScreen(card.Id);
            if (e.ClickCount == 2 && _model is not null)
            {
                _model.IsFace = true;
                e.Handled = true;
                return;
            }

            _dragging = card.Id;
            _dragMoved = false;
            _dragOffset = new Point(card.MapX, card.MapY) - world;
        }
        else if (e.ClickCount == 2 && _model?.CanAddScreens == true)
        {
            _model.AddScreenAt(Math.Round(world.X / 10) * 10, Math.Round(world.Y / 10) * 10);
            e.Handled = true;
            return;
        }
        else
        {
            _panning = true;
            _panFrom = e.GetPosition(this);
        }

        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseRightButtonUp(e);
        if (_model is not { } model)
        {
            return;
        }

        Point world = ToWorld(e.GetPosition(this));
        var menu = new ContextMenu { PlacementTarget = this };
        void Item(string key, System.Windows.Input.ICommand command) =>
            menu.Items.Add(new MenuItem { Header = Strings.Get(key), Command = command, Visibility = command.CanExecute(null) ? Visibility.Visible : Visibility.Collapsed });
        void Do(string key, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = Strings.Get(key), Visibility = enabled ? Visibility.Visible : Visibility.Collapsed };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        if (CardAt(world) is { } card)
        {
            model.SelectScreen(card.Id);
            Do("Screens_MenuOpen", () => model.IsFace = true);
            Item("Screens_GoToCode", model.GoToCodeCommand);
            Item("Screens_SetStart", model.SetStartCommand);
            Item("Screens_Duplicate", model.DuplicateSelectedCommand);
            menu.Items.Add(new Separator());
            Item("Screens_Remove", model.RemoveSelectedCommand);
        }
        else
        {
            double x = Math.Round(world.X / 10) * 10;
            double y = Math.Round(world.Y / 10) * 10;
            Do("Screens_MenuNew", () => model.AddScreenAt(x, y), model.CanAddScreens);
            Do("Screens_MenuNewMenu", () => model.AddMenuScreenAt(x, y), model.IsGenerated);
            Do("Screens_FitTip", Fit);
        }

        menu.IsOpen = true;
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);
        Point view = e.GetPosition(this);
        Point world = ToWorld(view);
        if (_dragging is { } id)
        {
            Point place = world + _dragOffset;
            double x = Math.Round(place.X / 10) * 10;
            double y = Math.Round(place.Y / 10) * 10;
            if (Document?.Find(id) is { } card && (card.MapX != x || card.MapY != y))
            {
                _dragMoved = true;
                _model?.MoveOnMap(id, x, y, finished: false);
            }
        }
        else if (_connecting is not null)
        {
            _connectTo = world;
            InvalidateVisual();
        }
        else if (_panning)
        {
            _pan += view - _panFrom;
            _panFrom = view;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonUp(e);
        Point world = ToWorld(e.GetPosition(this));
        if (_dragging is { } id && _dragMoved && Document?.Find(id) is { } moved)
        {
            _model?.MoveOnMap(id, moved.MapX, moved.MapY, finished: true);
        }
        else if (_connecting is { } from && CardAt(world) is { } to && to.Id != from)
        {
            _model?.Connect(from, to.Id);
        }

        _dragging = null;
        _connecting = null;
        _panning = false;
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseWheel(e);
        Point view = e.GetPosition(this);
        Point world = ToWorld(view);
        _zoom = Math.Clamp(_zoom * Math.Pow(1.2, e.Delta / 120.0), 0.2, 2.5);
        _pan = new Vector(view.X - (world.X * _zoom), view.Y - (world.Y * _zoom));
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        if (e.Key == Key.Delete && _model?.RemoveSelectedCommand.CanExecute(null) == true)
        {
            _model.RemoveSelectedCommand.Execute(null);
            e.Handled = true;
        }
    }
}
