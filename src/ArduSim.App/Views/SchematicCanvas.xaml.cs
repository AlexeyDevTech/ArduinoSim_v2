using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ArduSim.App.Localization;
using ArduSim.App.Services;
using ArduSim.App.ViewModels;
using ArduSim.Core.Editing;
using ArduSim.Core.Projects;

namespace ArduSim.App.Views;

/// <summary>
/// The infinite schematic canvas (docs/UI.md §3.6, §4.1–§4.4): pan and zoom, selection, moving,
/// wiring, wire corners, drag-and-drop from the palette, context menu, and live input on parts
/// while a run is active. Gestures become <see cref="SceneViewModel"/> calls; hit testing uses the
/// view model's geometry, so the drawing stays passive.
/// </summary>
public partial class SchematicCanvas : UserControl
{
    public const string PartTypeFormat = "ArduSim.PartType";

    private const double PixelsPerUnitAt100 = 2.0;
    private const double MinScale = 0.25 * PixelsPerUnitAt100;
    private const double MaxScale = 8.0 * PixelsPerUnitAt100;
    private const double PinRadiusPx = 9;
    private const double WireTolerancePx = 6;

    private readonly DrawingBrush _gridBrush;
    private readonly GeometryDrawing _gridDotDrawing;
    private readonly EllipseGeometry _gridDot = new(new Point(0, 0), 1, 1);
    private readonly DispatcherTimer _hintTimer = new() { Interval = TimeSpan.FromSeconds(1.6) };
    private double _scale = PixelsPerUnitAt100;
    private Vector _offset = new(40, 40);
    private Gesture _gesture;
    private Point _gestureStartScreen;
    private Point _gestureStartWorld;
    private Vector _applied;
    private bool _checkpointed;
    private PartViewModel? _activePart;
    private CornerHandle? _corner;
    private double _knobStart;
    private bool _spaceDown;
    private bool _fitPending = true;

    public SchematicCanvas()
    {
        InitializeComponent();
        _gridDotDrawing = new GeometryDrawing(null, null, _gridDot);
        var cell = new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 10, 10)));
        _gridBrush = new DrawingBrush(new DrawingGroup { Children = { cell, _gridDotDrawing } })
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 10, 10),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
        };
        GridLayer.Fill = _gridBrush;
        ApplyTheme();
        Loaded += (_, _) => Theme.Changed += ApplyTheme;
        Unloaded += (_, _) => Theme.Changed -= ApplyTheme;
        _hintTimer.Tick += (_, _) =>
        {
            Hint.Visibility = Visibility.Collapsed;
            _hintTimer.Stop();
        };
        DataContextChanged += (_, _) =>
        {
            // A new scene is fitted once the canvas has a size (now, or when it is next shown).
            _fitPending = true;
            _ = Dispatcher.BeginInvoke(Fit, DispatcherPriority.Loaded);
        };
        SizeChanged += (_, _) =>
        {
            if (_fitPending)
            {
                Fit();
            }
        };
        ApplyTransform();
    }

    private enum Gesture
    {
        None,
        Pan,
        MoveParts,
        Marquee,
        Corner,
        Segment,
        PressButton,
        Knob,
    }

    private SceneViewModel? Scene => DataContext as SceneViewModel;

    public void Fit() => Fit(MinScale, 40);

    /// <summary>Fits the content with <paramref name="margin"/> canvas units around it, zooming out no further than <paramref name="minScale"/>.</summary>
    private void Fit(double minScale, double margin)
    {
        if (Scene is null || ActualWidth < 10 || ActualHeight < 10)
        {
            return;
        }

        _fitPending = false;
        Rect bounds = Scene.ContentBounds();
        bounds.Inflate(margin, margin);
        _scale = Math.Clamp(Math.Min(ActualWidth / bounds.Width, ActualHeight / bounds.Height), minScale, 3 * PixelsPerUnitAt100);
        _offset = new Vector(
            (ActualWidth - (bounds.Width * _scale)) / 2 - (bounds.Left * _scale),
            (ActualHeight - (bounds.Height * _scale)) / 2 - (bounds.Top * _scale));
        ApplyTransform();
    }

    /// <summary>Renders a scene, fitted, without the zoom controls (the examples gallery thumbnails).</summary>
    public static BitmapSource Snapshot(SceneViewModel scene, double width, double height, double dpiScale)
    {
        var canvas = new SchematicCanvas { DataContext = scene, Width = width, Height = height };
        canvas.ZoomBar.Visibility = Visibility.Collapsed;
        canvas.Measure(new Size(width, height));
        canvas.Arrange(new Rect(0, 0, width, height));
        canvas.Fit(0.01, 12);
        canvas.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * dpiScale), (int)Math.Ceiling(height * dpiScale), 96 * dpiScale, 96 * dpiScale, PixelFormats.Pbgra32);
        bitmap.Render(canvas);
        bitmap.Freeze();
        return bitmap;
    }

    public void ZoomTo100()
    {
        Point center = new(ActualWidth / 2, ActualHeight / 2);
        ZoomAt(center, PixelsPerUnitAt100 / _scale);
    }

    public void ZoomStep(int steps) => ZoomAt(new Point(ActualWidth / 2, ActualHeight / 2), Math.Pow(1.2, steps));

    private void ApplyTheme() =>
        _gridDotDrawing.Brush = new SolidColorBrush(TryFindResource("Color.CanvasGrid") is Color grid ? grid : Color.FromRgb(0x26, 0x2A, 0x33));

    private Point ToWorld(Point screen) => new((screen.X - _offset.X) / _scale, (screen.Y - _offset.Y) / _scale);

    private void ApplyTransform()
    {
        WorldTransform.Matrix = new Matrix(_scale, 0, 0, _scale, _offset.X, _offset.Y);
        _gridBrush.Transform = new MatrixTransform(WorldTransform.Matrix);
        _gridDot.RadiusX = _gridDot.RadiusY = 1.1 / _scale;
        GridLayer.Opacity = _scale < 0.5 * PixelsPerUnitAt100 ? 0 : 1;
        ZoomButton.Content = (_scale / PixelsPerUnitAt100 * 100).ToString("0", CultureInfo.CurrentCulture) + " %";
        Scene?.SetPixelsPerUnit(_scale * VisualTreeHelper.GetDpi(this).DpiScaleX);
    }

    private void ZoomAt(Point screen, double factor)
    {
        double scale = Math.Clamp(_scale * factor, MinScale, MaxScale);
        Point world = ToWorld(screen);
        _scale = scale;
        _offset = new Vector(screen.X - (world.X * scale), screen.Y - (world.Y * scale));
        ApplyTransform();
    }

    // ---- mouse ---------------------------------------------------------------------------------

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (Scene is not { } scene)
        {
            return;
        }

        Point screen = e.GetPosition(this);
        Point world = ToWorld(screen);
        _gestureStartScreen = screen;
        _gestureStartWorld = world;
        bool additive = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        if (e.ChangedButton == MouseButton.Middle || (e.ChangedButton == MouseButton.Left && _spaceDown))
        {
            Begin(Gesture.Pan);
            return;
        }

        if (e.ChangedButton == MouseButton.Right)
        {
            ShowContextMenu(scene, world);
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (scene.IsLocked)
        {
            PartViewModel? part = scene.PartAt(world);
            switch (part?.Type)
            {
                case "pushbutton":
                    _activePart = part;
                    part.Press();
                    Begin(Gesture.PressButton);
                    break;
                case "slide-switch":
                    part.Toggle();
                    break;
                case "potentiometer":
                    _activePart = part;
                    _knobStart = part.Value;
                    Begin(Gesture.Knob);
                    break;
                case null:
                    Begin(Gesture.Pan);
                    break;
                default:
                    ShowHint(screen);
                    break;
            }

            return;
        }

        if (scene.IsWiring)
        {
            if (scene.PinAt(world, PinRadiusPx / _scale) is { } target)
            {
                scene.FinishWire(target.Pin);
            }
            else
            {
                scene.AddCorner(world);
            }

            return;
        }

        if (e.ClickCount == 2 && scene.WireSegmentAt(world, WireTolerancePx / _scale) is { } segment)
        {
            scene.InsertCorner(segment.Wire.Id, world);
            return;
        }

        if (scene.CornerAt(world, PinRadiusPx / _scale) is { } corner)
        {
            _corner = corner;
            Begin(Gesture.Corner);
            return;
        }

        if (scene.PinAt(world, PinRadiusPx / _scale) is { } pin)
        {
            scene.StartWire(pin.Pin);
            scene.UpdateGhost(world);
            return;
        }

        if (scene.PartAt(world) is { } hit)
        {
            if (!hit.IsSelected || additive)
            {
                scene.Select(hit.Id, additive);
            }

            if (hit.IsSelected)
            {
                Begin(Gesture.MoveParts);
            }

            return;
        }

        if (scene.WireSegmentAt(world, WireTolerancePx / _scale) is { } hitSegment)
        {
            // Click selects the wire; dragging moves the segment under the cursor (docs/UI.md §4.3).
            scene.SelectWire(hitSegment.Wire.Id);
            if (scene.BeginSegmentDrag(hitSegment.Wire.Id, hitSegment.Segment))
            {
                Begin(Gesture.Segment);
            }

            return;
        }

        if (!additive)
        {
            scene.ClearSelection();
        }

        Begin(Gesture.Marquee);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Scene is not { } scene)
        {
            return;
        }

        Point screen = e.GetPosition(this);
        Point world = ToWorld(screen);
        switch (_gesture)
        {
            case Gesture.Pan:
                _offset += screen - _gestureStartScreen;
                _gestureStartScreen = screen;
                ApplyTransform();
                return;
            case Gesture.Knob when _activePart is not null:
                _activePart.Value = Math.Clamp(_knobStart + ((_gestureStartScreen.Y - screen.Y) / 150), 0, 1);
                return;
            case Gesture.MoveParts:
                MoveSelection(scene, world);
                return;
            case Gesture.Corner when _corner is not null:
                if (!_checkpointed)
                {
                    scene.Editor.Checkpoint();
                    _checkpointed = true;
                }

                scene.MoveCorner(_corner.WireId, _corner.Index, world, checkpoint: false);
                return;
            case Gesture.Segment:
                Vector offset = world - _gestureStartWorld;
                if (!_checkpointed && Math.Abs(SceneEditor.Snap(offset.X)) + Math.Abs(SceneEditor.Snap(offset.Y)) > 0)
                {
                    scene.Editor.Checkpoint();
                    _checkpointed = true;
                }

                if (_checkpointed)
                {
                    scene.DragSegment(offset, checkpoint: false);
                }

                return;
            case Gesture.Marquee:
                Rect area = new(_gestureStartScreen, screen);
                Canvas.SetLeft(Marquee, area.Left);
                Canvas.SetTop(Marquee, area.Top);
                Marquee.Width = area.Width;
                Marquee.Height = area.Height;
                Marquee.Visibility = Visibility.Visible;
                return;
        }

        if (scene.IsWiring)
        {
            scene.UpdateGhost(world);
        }

        var pin = scene.PinAt(world, PinRadiusPx / _scale);
        WireViewModel? hoverWire = pin is null ? scene.WireAt(world, WireTolerancePx / _scale) : null;
        scene.Hover(pin?.Pin, hoverWire?.Id);
        if (pin is { } p)
        {
            HoverText.Text = scene.PinTooltip(p.Pin);
            Canvas.SetLeft(HoverTip, screen.X + 14);
            Canvas.SetTop(HoverTip, screen.Y + 16);
            HoverTip.Visibility = Visibility.Visible;
        }
        else
        {
            HoverTip.Visibility = Visibility.Collapsed;
        }

        Cursor = pin is not null && !scene.IsLocked ? Cursors.Cross
            : scene.IsLocked && scene.PartAt(world)?.Type is "pushbutton" or "slide-switch" ? Cursors.Hand
            : scene.IsLocked && scene.PartAt(world)?.Type is "potentiometer" ? Cursors.SizeNS
            : _spaceDown ? Cursors.Hand : null;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (Scene is not { } scene)
        {
            return;
        }

        switch (_gesture)
        {
            case Gesture.PressButton:
                _activePart?.Release();
                break;
            case Gesture.Marquee:
                Marquee.Visibility = Visibility.Collapsed;
                Rect area = new(ToWorld(_gestureStartScreen), ToWorld(e.GetPosition(this)));
                if (area.Width > 2 || area.Height > 2)
                {
                    bool additive = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
                    scene.SelectInside(area, additive);
                }

                break;
        }

        End();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        Point screen = e.GetPosition(this);
        if (Scene is { IsLocked: true } scene && scene.PartAt(ToWorld(screen)) is { Type: "potentiometer" } pot)
        {
            pot.Value = Math.Clamp(pot.Value + (e.Delta > 0 ? 0.02 : -0.02), 0, 1);
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _offset += new Vector(e.Delta / 2.0, 0);
            ApplyTransform();
        }
        else
        {
            ZoomAt(screen, e.Delta > 0 ? 1.2 : 1 / 1.2);
        }

        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        HoverTip.Visibility = Visibility.Collapsed;
        Scene?.Hover(null, null);
    }

    // ---- keyboard ------------------------------------------------------------------------------

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Scene is not { } scene)
        {
            return;
        }

        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        Point cursor = ToWorld(Mouse.GetPosition(this));
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Space when !e.IsRepeat && !scene.IsLocked:
                _spaceDown = true;
                Cursor = Cursors.Hand;
                return;
            case Key.Escape:
                if (scene.IsWiring)
                {
                    scene.CancelWire();
                }
                else
                {
                    scene.ClearSelection();
                }

                return;
            case Key.Tab when scene.IsWiring:
                scene.VerticalFirst = !scene.VerticalFirst;
                scene.UpdateGhost(cursor);
                return;
            case Key.Back when scene.IsWiring:
                scene.RemoveLastCorner(cursor);
                return;
            case Key.D0 when ctrl:
                Fit();
                return;
            case Key.D9 when ctrl:
                ZoomTo100();
                return;
            case Key.OemPlus or Key.Add when ctrl:
                ZoomStep(1);
                return;
            case Key.OemMinus or Key.Subtract when ctrl:
                ZoomStep(-1);
                return;
            case Key.A when ctrl:
                scene.SelectAll();
                return;
            case Key.C when ctrl:
                CopyToClipboard(scene);
                return;
        }

        if (scene.IsLocked && IsStructural(e.Key, ctrl))
        {
            ShowHint(Mouse.GetPosition(this));
            return;
        }

        switch (e.Key)
        {
            case Key.Delete or Key.Back:
                scene.DeleteSelection();
                break;
            case Key.R:
                scene.RotateSelection(clockwise: !shift);
                break;
            case Key.F when !ctrl:
                scene.FlipSelection();
                break;
            case Key.Left or Key.Right or Key.Up or Key.Down:
                double step = (shift ? 10 : 1) * SceneEditor.Grid;
                scene.Nudge(e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0, e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0);
                break;
            case Key.X when ctrl:
                CopyToClipboard(scene);
                scene.DeleteSelection();
                break;
            case Key.V when ctrl:
                PasteFromClipboard(scene, cursor);
                break;
            case Key.D when ctrl:
                scene.DuplicateSelection();
                break;
            default:
                e.Handled = false;
                break;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.Space)
        {
            _spaceDown = false;
            Cursor = null;
        }
    }

    // ---- drag and drop from the palette -----------------------------------------------------------

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        if (Scene is { IsLocked: false } scene && e.Data.GetData(PartTypeFormat) is string request && scene.Resolve(request) is { Definition: var definition })
        {
            e.Effects = DragDropEffects.Copy;
            Point world = ToWorld(e.GetPosition(this));
            Canvas.SetLeft(DropGhost, SceneEditor.Snap(world.X - (definition.Width / 2)));
            Canvas.SetTop(DropGhost, SceneEditor.Snap(world.Y - (definition.Height / 2)));
            DropGhost.Width = definition.Width;
            DropGhost.Height = definition.Height;
            DropGhost.Visibility = Visibility.Visible;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        DropGhost.Visibility = Visibility.Collapsed;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        DropGhost.Visibility = Visibility.Collapsed;
        if (Scene is { IsLocked: false } scene && e.Data.GetData(PartTypeFormat) is string request)
        {
            scene.Place(request, ToWorld(e.GetPosition(this)));
            Focus();
        }
    }

    /// <summary>Places a part at the center of the view (palette double-click); <paramref name="request"/> is <c>type</c> or <c>type#preset</c>.</summary>
    public void PlaceAtCenter(string request)
    {
        if (Scene is { IsLocked: false } scene)
        {
            scene.Place(request, ToWorld(new Point(ActualWidth / 2, ActualHeight / 2)));
            Focus();
        }
    }

    // ---- helpers -------------------------------------------------------------------------------

    private void Begin(Gesture gesture)
    {
        _gesture = gesture;
        _applied = default;
        _checkpointed = false;
        CaptureMouse();
    }

    private void End()
    {
        _gesture = Gesture.None;
        _activePart = null;
        _corner = null;
        Scene?.EndSegmentDrag();
        ReleaseMouseCapture();
    }

    private void MoveSelection(SceneViewModel scene, Point world)
    {
        Vector total = world - _gestureStartWorld;
        bool free = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        Vector target = free ? total : new Vector(SceneEditor.Snap(total.X), SceneEditor.Snap(total.Y));
        Vector delta = target - _applied;
        if (delta.X == 0 && delta.Y == 0)
        {
            return;
        }

        if (!_checkpointed)
        {
            scene.Editor.Checkpoint();
            _checkpointed = true;
        }

        scene.Editor.MoveParts([.. scene.SelectedParts], delta.X, delta.Y, checkpoint: false);
        _applied = target;
    }

    private static bool IsStructural(Key key, bool ctrl) =>
        key is Key.Delete or Key.Back or Key.R or Key.Left or Key.Right or Key.Up or Key.Down || (key == Key.F && !ctrl) ||
        (ctrl && key is Key.X or Key.V or Key.D);

    private void ShowHint(Point screen)
    {
        Canvas.SetLeft(Hint, screen.X + 14);
        Canvas.SetTop(Hint, screen.Y - 30);
        Hint.Visibility = Visibility.Visible;
        _hintTimer.Stop();
        _hintTimer.Start();
    }

    private static void CopyToClipboard(SceneViewModel scene)
    {
        if (scene.CopySelection() is { } text)
        {
            Clipboard.SetText(text);
        }
    }

    private static void PasteFromClipboard(SceneViewModel scene, Point at)
    {
        if (Clipboard.ContainsText())
        {
            scene.Paste(Clipboard.GetText(), at);
        }
    }

    private void ShowContextMenu(SceneViewModel scene, Point world)
    {
        var menu = new ContextMenu();
        MenuItem Item(string key, string gesture, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = Strings.Get(key), InputGestureText = gesture, IsEnabled = enabled };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
            return item;
        }

        bool editable = !scene.IsLocked;
        if (scene.PartAt(world) is { } part)
        {
            if (!part.IsSelected)
            {
                scene.Select(part.Id, additive: false);
            }

            Item("Menu_Rotate", "R", () => scene.RotateSelection(clockwise: true), editable);
            Item("Menu_Flip", "F", scene.FlipSelection, editable);
            Item("Menu_Duplicate", "Ctrl+D", scene.DuplicateSelection, editable && part.Type != "board");
            Item("Menu_Copy", "Ctrl+C", () => CopyToClipboard(scene), part.Type != "board");
            menu.Items.Add(new Separator());
            Item("Menu_Delete", "Del", scene.DeleteSelection, editable && part.Type != "board");
        }
        else if (scene.WireAt(world, WireTolerancePx / _scale) is { } wire)
        {
            scene.SelectWire(wire.Id);
            var colors = new MenuItem { Header = Strings.Get("Menu_WireColor"), IsEnabled = editable };
            foreach (string swatch in WirePalette.Swatches)
            {
                var item = new MenuItem
                {
                    Header = Strings.Get("Color_" + swatch),
                    Icon = new System.Windows.Shapes.Rectangle { Width = 12, Height = 12, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(WirePalette.SwatchColor(swatch)) },
                };
                item.Click += (_, _) => scene.Editor.SetWireColor(wire.Id, swatch);
                colors.Items.Add(item);
            }

            menu.Items.Add(colors);
            menu.Items.Add(new Separator());
            Item("Menu_Delete", "Del", scene.DeleteSelection, editable);
        }
        else
        {
            Item("Menu_Paste", "Ctrl+V", () => PasteFromClipboard(scene, world), editable && Clipboard.ContainsText());
            Item("Menu_Fit", "Ctrl+0", Fit);
        }

        menu.PlacementTarget = this;
        menu.IsOpen = true;
    }

    private void OnZoomReset(object sender, RoutedEventArgs e) => ZoomTo100();

    private void OnFit(object sender, RoutedEventArgs e) => Fit();
}
