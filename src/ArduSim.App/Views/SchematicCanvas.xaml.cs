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

    // A press on one pin released over another finishes the wire only past this distance: at far zoom
    // the pins are 5 px apart, and a shaky click must not join two neighbours.
    private const double WireDragPx = 10;

    // Pin labels stay readable at the zoom a scene opens with: this many screen pixels, between these canvas units.
    private const double PinLabelPixels = 7;
    private const double PinLabelMinimum = 4.2;
    private const double PinLabelMaximum = 5.4;

    private readonly DrawingBrush _gridBrush;
    private readonly GeometryDrawing _gridDotDrawing;
    private readonly EllipseGeometry _gridDot = new(new Point(0, 0), 1, 1);
    private readonly DispatcherTimer _hintTimer = new() { Interval = TimeSpan.FromSeconds(1.6) };
    private readonly DispatcherTimer _holdTimer = new() { Interval = HoldDelay };
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
    private string? _liveName;
    private double _encoderAngle;
    private bool _moved;
    private bool _pressHeld;
    private bool _spaceDown;
    private bool _fitPending = true;
    private bool _userMovedView;
    private bool _wireByPress;
    private bool _lockedEmptyClick;
    private bool _lockedPartPress;
    private WireEndHandle? _endHandle;
    private Point _pressedAt;
    private bool _rightPress;
    private bool _endDragged;

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
        _holdTimer.Tick += OnHold;
        _hintTimer.Tick += (_, _) =>
        {
            Hint.Visibility = Visibility.Collapsed;
            _hintTimer.Stop();
        };
        DataContextChanged += (_, _) =>
        {
            // A new scene is fitted once the canvas has a size (now, or when it is next shown).
            _fitPending = true;
            _userMovedView = false;
            _ = Dispatcher.BeginInvoke(Refit, DispatcherPriority.Loaded);
        };
        SizeChanged += (_, _) =>
        {
            // The scene follows the canvas until the user has panned or zoomed it.
            if (_fitPending || !_userMovedView)
            {
                FitNew();
            }
        };
        Resources["Font.PinLabel"] = PinLabelMinimum;
        ApplyTransform();
    }

    /// <summary>A key on the canvas asks for the Детали search (A) .</summary>
    public event EventHandler? AddPartRequested;

    /// <summary>The keys and gestures sheet (F1 on the canvas, its menu).</summary>
    public event EventHandler? ShowHelpRequested;

    /// <summary>Enter on a selected part: the keyboard goes to its properties.</summary>
    public event EventHandler? OpenPropertiesRequested;

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

        /// <summary>Turning an encoder by dragging around its knob; holding still presses its shaft.</summary>
        Encoder,

        /// <summary>A keypad key or remote button held down.</summary>
        PressKey,

        /// <summary>Leaning a joystick; it springs back on release, and holding still presses it.</summary>
        Stick,

        /// <summary>A vertical drag on a sensor changes its reading (ADR-011).</summary>
        Live,

        /// <summary>A press on a part that is not an input while running: a drag shows why it does not move.</summary>
        LockedPart,

        /// <summary>Dragging an end of the selected wire to another pin.</summary>
        WireEnd,
    }

    // A click on an encoder or joystick that does not move presses it for this long; holding still
    // for HoldDelay presses it until the mouse is released.
    private static readonly TimeSpan ClickPress = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan HoldDelay = TimeSpan.FromMilliseconds(250);
    private const double MoveThresholdPx = 4;

    private SceneViewModel? Scene => DataContext as SceneViewModel;

    public void Fit() => Fit(MinScale, 40);

    /// <summary>Fits a scene just opened; a bare board stays at 100 % in the upper part, with free room below it.</summary>
    private void FitNew()
    {
        Fit();
        if (Scene is { Parts.Count: 1 } && ActualHeight > 10)
        {
            Rect bounds = Scene.ContentBounds();
            _scale = Math.Min(_scale, PixelsPerUnitAt100);
            _offset = new Vector(
                ((ActualWidth - (bounds.Width * _scale)) / 2) - (bounds.Left * _scale),
                (ActualHeight * 0.12) - (bounds.Top * _scale));
            ApplyTransform();
        }
    }

    /// <summary>The canvas changed size for another reason than the user's wish (the Рядом mode).</summary>
    public void Refit()
    {
        if (!_userMovedView)
        {
            FitNew();
        }
    }

    /// <summary>Pans just enough to bring a part into view.</summary>
    public void BringIntoView(PartViewModel part)
    {
        ArgumentNullException.ThrowIfNull(part);
        if (ActualWidth < 10)
        {
            return;
        }

        Rect bounds = part.Bounds;
        bounds.Inflate(20, 20);
        Point topLeft = FromWorld(bounds.TopLeft);
        Point bottomRight = FromWorld(bounds.BottomRight);
        double dx = topLeft.X < 0 ? -topLeft.X : bottomRight.X > ActualWidth ? ActualWidth - bottomRight.X : 0;
        double dy = topLeft.Y < 0 ? -topLeft.Y : bottomRight.Y > ActualHeight ? ActualHeight - bottomRight.Y : 0;
        if (dx != 0 || dy != 0)
        {
            _offset += new Vector(dx, dy);
            _userMovedView = true;
            ApplyTransform();
        }
    }

    private Point FromWorld(Point world) => new((world.X * _scale) + _offset.X, (world.Y * _scale) + _offset.Y);

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
        canvas._userMovedView = true;
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
        double label = Math.Round(Math.Clamp(PinLabelPixels / _scale, PinLabelMinimum, PinLabelMaximum), 1);
        if (Resources["Font.PinLabel"] is not double known || known != label)
        {
            Resources["Font.PinLabel"] = label;
        }

        if (Scene is { } scene)
        {
            scene.SetPixelsPerUnit(_scale * VisualTreeHelper.GetDpi(this).DpiScaleX);
            scene.WireSnapRadius = Math.Max(PinRadiusPx / _scale, SceneEditor.Grid * 0.71);
        }
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

        // A second button during a drag or a held press would orphan it: the first gesture finishes first.
        if (_gesture != Gesture.None)
        {
            return;
        }

        Point screen = e.GetPosition(this);
        Point world = ToWorld(screen);
        _gestureStartScreen = screen;
        _gestureStartWorld = world;
        _pressedAt = screen;
        _endDragged = false;
        bool additive = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        _wireByPress = false;
        _lockedEmptyClick = false;
        _lockedPartPress = false;

        if (e.ChangedButton == MouseButton.Middle || (e.ChangedButton == MouseButton.Left && _spaceDown))
        {
            Begin(Gesture.Pan);
            return;
        }

        if (e.ChangedButton == MouseButton.Right)
        {
            // A wire in progress is cancelled, as in the other schematic tools; it opens no menu.
            if (scene.IsWiring)
            {
                scene.CancelWire();
            }
            else
            {
                _rightPress = true;
                Begin(Gesture.Pan);
            }

            return;
        }

        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (scene.BadgeAt(world) is { } warned && !scene.IsWiring)
        {
            scene.Warn(warned);
            return;
        }

        if (scene.IsLocked)
        {
            PartViewModel? part = scene.PartAt(world);
            if (part is null && scene.WireAt(world, WireTolerancePx / _scale) is { } clickedWire)
            {
                scene.ClickWire(clickedWire);
            }
            else if (part is null)
            {
                _lockedEmptyClick = true;
            }

            switch (part?.Type)
            {
                case "pushbutton" when Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                    if (part.Pressed)
                    {
                        part.Release();
                    }
                    else
                    {
                        part.Press();
                    }

                    break;
                case "pushbutton":
                    _activePart = part;
                    part.Press();
                    Begin(Gesture.PressButton);
                    break;
                case "keypad" or "ir-remote" when Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && part.KeyAt(part.ToLocal(world)) is int latched:
                    if (part.KeyIndex == latched)
                    {
                        part.ReleaseKey();
                    }
                    else
                    {
                        part.ReleaseKey();
                        part.PressKey(latched);
                    }

                    break;
                case "slide-switch":
                    part.Toggle();
                    break;
                case "potentiometer":
                    _activePart = part;
                    _knobStart = part.Value;
                    Begin(Gesture.Knob);
                    break;
                case "rotary-encoder":
                    _activePart = part;
                    _encoderAngle = EncoderAngle(part, world);
                    BeginHold(Gesture.Encoder);
                    break;
                case "joystick":
                    _activePart = part;
                    BeginHold(Gesture.Stick);
                    break;
                case "keypad" or "ir-remote" when part.KeyAt(part.ToLocal(world)) is int key:
                    _activePart = part;
                    part.PressKey(key);
                    Begin(Gesture.PressKey);
                    break;
                case "keypad" or "ir-remote":
                    SelectLocked(scene, part);
                    break;
                case "dht" or "ds18b20" or "hc-sr04" or "ds3231":
                    // A click selects the sensor, whose slider is in Properties; a drag changes the reading.
                    SelectLocked(scene, part);
                    _activePart = part;
                    _liveName = part.LiveDragProperty(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                    _knobStart = _liveName is null ? 0 : part.LiveValue(_liveName);
                    Begin(Gesture.Live);
                    break;
                case null:
                    Begin(Gesture.Pan);
                    break;
                default:
                    // Nothing happens on this part while it runs except looking at it: a drag says why.
                    SelectLocked(scene, part);
                    _lockedPartPress = true;
                    Begin(Gesture.LockedPart);
                    break;
            }

            return;
        }

        if (scene.IsWiring)
        {
            if (scene.PinAt(world, scene.WireSnapRadius) is { } target)
            {
                // Near its own start only because of the snap: not the deliberate click that cancels.
                if (!scene.IsWireStart(target.Pin) || (target.Position - world).Length <= PinRadiusPx / _scale)
                {
                    scene.FinishWire(target.Pin);
                }
            }
            else
            {
                scene.AddCorner(world);
            }

            return;
        }

        if (e.ClickCount == 2 && scene.CornerAt(world, PinRadiusPx / _scale) is { } doomed)
        {
            scene.RemoveCorner(doomed);
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

        if (scene.WireEndAt(world, PinRadiusPx / _scale) is { } end)
        {
            _endHandle = end;
            Begin(Gesture.WireEnd);
            return;
        }

        if (scene.PinAt(world, PinRadiusPx / _scale) is { } pin)
        {
            scene.StartWire(pin.Pin);
            scene.UpdateGhost(world);
            _wireByPress = true;
            return;
        }

        if (scene.WireOverPart(world, WireTolerancePx / _scale) is { } crossing)
        {
            scene.SelectWire(crossing.Wire.Id);
            if (scene.BeginSegmentDrag(crossing.Wire.Id, crossing.Segment))
            {
                Begin(Gesture.Segment);
            }

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
                if ((screen - _pressedAt).Length > MoveThresholdPx)
                {
                    _lockedEmptyClick = false;
                    _userMovedView = true;
                }

                _offset += screen - _gestureStartScreen;
                _gestureStartScreen = screen;
                ApplyTransform();
                return;
            case Gesture.LockedPart:
                if (_lockedPartPress && (screen - _gestureStartScreen).Length > MoveThresholdPx)
                {
                    _lockedPartPress = false;
                    ShowHint(screen);
                }

                return;
            case Gesture.WireEnd when _endHandle is not null:
                if (_endDragged || (screen - _gestureStartScreen).Length > MoveThresholdPx)
                {
                    _endDragged = true;
                    scene.GhostReconnect(_endHandle, world);
                    Cursor = scene.PinAt(world, PinRadiusPx / _scale) is not null ? Cursors.Cross : Cursors.No;
                }

                return;
            case Gesture.Knob when _activePart is not null:
                _activePart.Value = Math.Clamp(_knobStart + ((_gestureStartScreen.Y - screen.Y) / 150), 0, 1);
                return;
            case Gesture.Live when _activePart is not null && _liveName is not null:
                _activePart.SetLive(_liveName, _knobStart + ((_gestureStartScreen.Y - screen.Y) * PartViewModel.LiveStep(_liveName)));
                return;
            case Gesture.Encoder when _activePart is not null:
                if (Moved(screen))
                {
                    // One detent per 18° around the knob, clockwise positive.
                    double angle = EncoderAngle(_activePart, world);
                    double delta = Math.IEEERemainder(angle - _encoderAngle, 360);
                    int detents = (int)(delta / 18);
                    if (detents != 0)
                    {
                        _activePart.Turn(detents);
                        _encoderAngle += detents * 18;
                    }
                }

                return;
            case Gesture.Stick when _activePart is not null:
                if (Moved(screen))
                {
                    Vector lean = _activePart.ToLocal(world) - _activePart.ToLocal(_gestureStartWorld);
                    _activePart.MoveStick(lean.X / 20, -lean.Y / 20);
                }

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
        PartViewModel? badge = pin is null ? scene.BadgeAt(world) : null;
        scene.Hover(pin?.Pin, hoverWire?.Id);
        string? tip = pin is { } p ? scene.PinTooltip(p.Pin)
            : badge is not null ? badge.WarningText
            : hoverWire is not null ? scene.WireTooltip(hoverWire)
            : null;
        if (tip is not null)
        {
            HoverText.Text = tip;
            Canvas.SetLeft(HoverTip, Math.Min(screen.X + 14, Math.Max(0, ActualWidth - 320)));
            Canvas.SetTop(HoverTip, screen.Y + 16);
            HoverTip.Visibility = Visibility.Visible;
        }
        else
        {
            HoverTip.Visibility = Visibility.Collapsed;
        }

        Cursor = badge is not null ? Cursors.Hand
            : pin is not null && !scene.IsLocked ? Cursors.Cross
            : !scene.IsLocked && scene.CornerAt(world, PinRadiusPx / _scale) is not null ? Cursors.SizeAll
            : !scene.IsLocked && hoverWire is not null && scene.SelectedWire == hoverWire.Id ? Cursors.SizeAll
            : scene.IsLocked && scene.PartAt(world)?.Type is "pushbutton" or "slide-switch" or "rotary-encoder" or "joystick" or "keypad" or "ir-remote" ? Cursors.Hand
            : scene.IsLocked && scene.PartAt(world)?.Type is "potentiometer" or "dht" or "ds18b20" or "hc-sr04" or "ds3231" ? Cursors.SizeNS
            : _spaceDown ? Cursors.Hand : null;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (Scene is not { } scene)
        {
            return;
        }

        Point releasedAt = e.GetPosition(this);
        Point releasedWorld = ToWorld(releasedAt);

        // The right button pans; released where it was pressed, it opens the menu.
        if (e.ChangedButton == MouseButton.Right && _rightPress)
        {
            _rightPress = false;
            bool click = (releasedAt - _pressedAt).Length <= MoveThresholdPx;
            End();
            if (click)
            {
                ShowContextMenu(scene, releasedWorld);
            }

            return;
        }

        // A press on a pin released over another pin finishes the wire too (docs/UI.md §4.3).
        if (_wireByPress && e.ChangedButton == MouseButton.Left && scene.IsWiring)
        {
            _wireByPress = false;
            if ((releasedAt - _gestureStartScreen).Length >= WireDragPx && scene.PinAt(releasedWorld, scene.WireSnapRadius) is { } target && !scene.IsWireStart(target.Pin))
            {
                scene.FinishWire(target.Pin);
            }
        }

        if (_lockedEmptyClick && e.ChangedButton == MouseButton.Left)
        {
            scene.ClearSelection();
        }

        _lockedEmptyClick = false;
        // Parts that now meet exactly are connected, in the move's own undo step.
        if (_gesture == Gesture.MoveParts && _checkpointed)
        {
            scene.Editor.JoinCoincidingPins([.. scene.SelectedParts]);
            scene.Editor.SettleWires();
        }

        switch (_gesture)
        {
            case Gesture.WireEnd when _endHandle is { } end:
                scene.ClearGhost();
                Cursor = null;
                if (!_endDragged)
                {
                    // Only a click: the pin starts a new wire, as any pin does.
                    if (scene.PinAt(releasedWorld, PinRadiusPx / _scale) is { } clicked)
                    {
                        scene.StartWire(clicked.Pin);
                        scene.UpdateGhost(releasedWorld);
                    }
                }
                else if (scene.PinAt(releasedWorld, scene.WireSnapRadius) is { } dropped)
                {
                    scene.ReconnectWire(end, dropped.Pin);
                }

                break;
            case Gesture.PressButton:
                _activePart?.Release();
                break;
            case Gesture.PressKey:
                _activePart?.ReleaseKey();
                break;
            case Gesture.Encoder or Gesture.Stick when _activePart is { } held:
                if (_gesture == Gesture.Stick)
                {
                    held.MoveStick(0, 0);
                }

                if (_pressHeld)
                {
                    held.Release();
                }
                else if (!_moved)
                {
                    held.Press();
                    var release = new DispatcherTimer { Interval = ClickPress };
                    release.Tick += (_, _) =>
                    {
                        release.Stop();
                        held.Release();
                    };
                    release.Start();
                }

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

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_gesture == Gesture.None)
        {
            return;
        }

        switch (_gesture)
        {
            case Gesture.PressButton:
                _activePart?.Release();
                break;
            case Gesture.PressKey:
                _activePart?.ReleaseKey();
                break;
            case Gesture.Stick:
                _activePart?.MoveStick(0, 0);
                _activePart?.Release();
                break;
            case Gesture.Encoder:
                _activePart?.Release();
                break;
            case Gesture.Marquee:
                Marquee.Visibility = Visibility.Collapsed;
                break;
            case Gesture.WireEnd:
                Scene?.ClearGhost();
                Cursor = null;
                break;
        }

        End();
    }

    /// <summary>A drag or a held press is under way; Esc belongs to it.</summary>
    public bool InGesture => _gesture != Gesture.None;

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        Point screen = e.GetPosition(this);
        if (Scene is { IsLocked: true } scene && scene.PartAt(ToWorld(screen)) is { Type: "potentiometer" } pot)
        {
            pot.Value = Math.Clamp(pot.Value + (e.Delta > 0 ? 0.02 : -0.02), 0, 1);
        }
        else if (Scene is { IsLocked: true } && Scene.PartAt(ToWorld(screen)) is { Type: "rotary-encoder" } encoder)
        {
            encoder.Turn(e.Delta > 0 ? 1 : -1);
        }
        else if (Scene is { IsLocked: true } && Scene.PartAt(ToWorld(screen)) is { Type: "dht" or "ds18b20" or "hc-sr04" or "ds3231" } sensor &&
                 sensor.LiveDragProperty(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) is { } name)
        {
            double step = name == "temperature" ? 0.5 : PartViewModel.LiveStep(name) * (name == "humidity" ? 2 : 1);
            sensor.SetLive(name, sensor.LiveValue(name) + (e.Delta > 0 ? step : -step));
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _offset += new Vector(e.Delta / 2.0, 0);
            _userMovedView = true;
            ApplyTransform();
        }
        else if (Scene is { WheelPans: true } && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _offset += new Vector(0, e.Delta / 2.0);
            _userMovedView = true;
            ApplyTransform();
        }
        else
        {
            // By the wheel's delta: a touchpad reports small steps, and a notch of a mouse is 120.
            _userMovedView = true;
            ZoomAt(screen, Math.Pow(1.2, e.Delta / 120.0));
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
        HandleKey(e);
    }

    /// <summary>Canvas commands that also work while another panel holds the focus (the window routes them here).</summary>
    public bool HandlesKey(Key key, ModifierKeys modifiers)
    {
        if (Scene is not { } scene)
        {
            return false;
        }

        bool ctrl = modifiers == ModifierKeys.Control;
        bool plain = modifiers is ModifierKeys.None or ModifierKeys.Shift;
        bool hasSelection = scene.SelectedParts.Count > 0 || scene.SelectedWire is not null;
        return (plain && hasSelection && key is Key.Delete or Key.R or Key.F)
            || (plain && key == Key.A)
            || (ctrl && key is Key.D0 or Key.D9 or Key.OemPlus or Key.Add or Key.OemMinus or Key.Subtract)
            || (ctrl && hasSelection && key is Key.D or Key.X)
            || (ctrl && key == Key.V);
    }

    /// <summary>The keyboard's commands on the canvas (docs/UI.md §4.2).</summary>
    public void HandleKey(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (Scene is not { } scene)
        {
            return;
        }

        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        Point pointer = Mouse.GetPosition(this);

        if (!ctrl && scene.IsLocked && scene.SinglePart is { } live && HandleLiveKey(live, e.Key, shift))
        {
            e.Handled = true;
            return;
        }

        // With the mouse elsewhere (a key after a click in a panel), the view's center stands in for it.
        Point cursor = ToWorld(IsMouseOver ? pointer : new Point(ActualWidth / 2, ActualHeight / 2));
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Space when !e.IsRepeat && !scene.IsLocked:
                _spaceDown = true;
                Cursor = Cursors.Hand;
                return;
            case Key.Escape:
                if (_gesture is Gesture.MoveParts or Gesture.Corner or Gesture.Segment or Gesture.WireEnd)
                {
                    // The drag never happened: back to where it started, with no redo entry.
                    if (_checkpointed)
                    {
                        scene.Editor.CancelStep();
                    }

                    scene.ClearGhost();
                    End();
                }
                else if (scene.IsWiring)
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
            case Key.Tab when !ctrl:
                if (scene.SelectNext(shift) is { } next)
                {
                    BringIntoView(next);
                }
                else
                {
                    e.Handled = false;
                }

                return;
            case Key.Left or Key.Right or Key.Up or Key.Down when scene.SelectedParts.Count == 0 && scene.SelectedWire is null && !scene.IsWiring:
                // Nothing to nudge: the arrows move the view.
                double pan = shift ? 200 : 40;
                _offset += new Vector(e.Key == Key.Left ? pan : e.Key == Key.Right ? -pan : 0, e.Key == Key.Up ? pan : e.Key == Key.Down ? -pan : 0);
                _userMovedView = true;
                ApplyTransform();
                return;
            case Key.Enter when !scene.IsWiring:
                if (scene.SelectedParts.Count > 0)
                {
                    OpenPropertiesRequested?.Invoke(this, EventArgs.Empty);
                }

                return;
            case Key.A when !ctrl && !shift && !scene.IsWiring:
                if (scene.IsLocked)
                {
                    ShowHint(pointer);
                }
                else
                {
                    AddPartRequested?.Invoke(this, EventArgs.Empty);
                }

                return;
            case Key.Back when scene.IsWiring:
                scene.RemoveLastCorner(cursor);
                return;
            case Key.D0 when ctrl:
                _userMovedView = false;
                Fit();
                return;
            case Key.D9 when ctrl:
                _userMovedView = true;
                ZoomTo100();
                return;
            case Key.OemPlus or Key.Add when ctrl:
                _userMovedView = true;
                ZoomStep(1);
                return;
            case Key.OemMinus or Key.Subtract when ctrl:
                _userMovedView = true;
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
            case Key.R when !ctrl:
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

        // What a key holds down while running lets go with it.
        if (Scene is { IsLocked: true, SinglePart: { } live })
        {
            switch (live.Type)
            {
                case "joystick" when e.Key is Key.Left or Key.Right or Key.Up or Key.Down:
                    live.MoveStick(0, 0);
                    break;
                case "joystick" or "rotary-encoder" when e.Key == Key.Space:
                    live.Release();
                    break;
                case "keypad" when KeypadIndex(e.Key, shift: false) is not null:
                    live.ReleaseKey();
                    break;
            }
        }
    }

    /// <summary>The selected input part while running (Tab reaches it): arrows, Page keys, Home and End, Space, and a keypad's own keys.</summary>
    private static bool HandleLiveKey(PartViewModel part, Key key, bool shift)
    {
        int direction = key is Key.Up or Key.Right ? 1 : key is Key.Down or Key.Left ? -1 : 0;
        switch (part.Type)
        {
            case "potentiometer":
                double next = key switch
                {
                    Key.Home => 0,
                    Key.End => 1,
                    Key.PageUp => part.Value + 0.1,
                    Key.PageDown => part.Value - 0.1,
                    _ when direction != 0 => part.Value + (direction * (shift ? 0.1 : 0.01)),
                    _ => double.NaN,
                };
                if (double.IsNaN(next))
                {
                    return false;
                }

                part.Value = Math.Clamp(next, 0, 1);
                return true;
            case "rotary-encoder" when direction != 0:
                part.Turn(direction);
                return true;
            case "rotary-encoder" or "joystick" when key == Key.Space:
                part.Press();
                return true;
            case "joystick" when direction != 0:
                part.MoveStick(key is Key.Left or Key.Right ? direction : part.StickX, key is Key.Up or Key.Down ? direction : part.StickY);
                return true;
            case "dht" or "ds18b20" or "hc-sr04" or "ds3231" when direction != 0 && part.LiveDragProperty(shift) is { } name:
                part.SetLive(name, part.LiveValue(name) + (direction * (name == "temperature" ? 0.5 : PartViewModel.LiveStep(name))));
                return true;
            case "keypad" when KeypadIndex(key, shift) is int index:
                if (part.KeyIndex != index)
                {
                    part.PressKey(index);
                }

                return true;
            default:
                return false;
        }
    }

    /// <summary>A keypad's key by its label: digits, A-D, * and #.</summary>
    private static int? KeypadIndex(Key key, bool shift)
    {
        char label = key switch
        {
            >= Key.D0 and <= Key.D9 when shift && key == Key.D8 => '*',
            >= Key.D0 and <= Key.D9 when shift && key == Key.D3 => '#',
            >= Key.D0 and <= Key.D9 => (char)('0' + (key - Key.D0)),
            >= Key.NumPad0 and <= Key.NumPad9 => (char)('0' + (key - Key.NumPad0)),
            >= Key.A and <= Key.D => (char)('A' + (key - Key.A)),
            Key.Multiply => '*',
            _ => '\0',
        };
        int index = ArduSim.Core.Components.Parts.MatrixKeypad.Labels.IndexOf(label, StringComparison.Ordinal);
        return index < 0 ? null : index;
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
            var view = new Rect(ToWorld(new Point(0, 0)), ToWorld(new Point(ActualWidth, ActualHeight)));
            if (scene.PlaceFree(request, ToWorld(new Point(ActualWidth / 2, ActualHeight / 2)), view) is { } placed)
            {
                BringIntoView(placed);
            }

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

    /// <summary>An encoder or joystick gesture: a drag turns or leans it, holding still presses it.</summary>
    private void BeginHold(Gesture gesture)
    {
        _moved = false;
        _pressHeld = false;
        Begin(gesture);
        _holdTimer.Stop();
        _holdTimer.Start();
    }

    private void OnHold(object? sender, EventArgs e)
    {
        _holdTimer.Stop();
        if (_gesture is Gesture.Encoder or Gesture.Stick && !_moved && _activePart is { } part)
        {
            part.Press();
            _pressHeld = true;
        }
    }

    /// <summary>Whether this gesture has become a drag (not once the hold pressed the part).</summary>
    private bool Moved(Point screen)
    {
        if (!_moved && !_pressHeld && (screen - _gestureStartScreen).Length > MoveThresholdPx)
        {
            _moved = true;
            _holdTimer.Stop();
        }

        return _moved;
    }

    /// <summary>Degrees clockwise around the encoder's knob (drawn at 30, 30 in the part).</summary>
    private static double EncoderAngle(PartViewModel part, Point world)
    {
        Point local = part.ToLocal(world);
        return Math.Atan2(local.Y - 30, local.X - 30) * 180 / Math.PI;
    }

    private void End()
    {
        _holdTimer.Stop();
        _gesture = Gesture.None;
        _activePart = null;
        _corner = null;
        _endHandle = null;
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
        key is Key.Delete or Key.Back or Key.Left or Key.Right or Key.Up or Key.Down || (key is Key.F or Key.R && !ctrl) ||
        (ctrl && key is Key.X or Key.V or Key.D);

    /// <summary>A click on a part while running selects it, leaving an unchanged selection alone (the panel would rebuild).</summary>
    private static void SelectLocked(SceneViewModel scene, PartViewModel part)
    {
        if (!part.IsSelected)
        {
            scene.Select(part.Id, additive: false);
        }
    }

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
        if (scene.WireOverPart(world, WireTolerancePx / _scale) is null && scene.PartAt(world) is { } part)
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
            menu.Items.Add(new Separator());
            Item("Menu_Properties", "Enter", () => OpenPropertiesRequested?.Invoke(this, EventArgs.Empty));
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
            bool byHand = scene.Editor.Scene.FindWire(wire.Id) is { IsAuto: false };
            Item("Menu_ResetRoute", "", () => scene.ResetRoute(wire.Id), editable && byHand);
            Item("Menu_LayOutWires", "", scene.Editor.LayOutWires, editable);
            menu.Items.Add(new Separator());
            Item("Menu_Delete", "Del", scene.DeleteSelection, editable);
        }
        else
        {
            Item("Menu_AddPart", "A", () => AddPartRequested?.Invoke(this, EventArgs.Empty), editable);
            Item("Menu_Paste", "Ctrl+V", () => PasteFromClipboard(scene, world), editable && Clipboard.ContainsText());
            Item("Menu_LayOutWires", "", scene.Editor.LayOutWires, editable && scene.Editor.Scene.Wires.Count > 0);
            Item("Menu_Fit", "Ctrl+0", Fit);
            menu.Items.Add(new Separator());
            Item("Menu_Help", "F1", () => ShowHelpRequested?.Invoke(this, EventArgs.Empty));
        }

        menu.PlacementTarget = this;
        menu.IsOpen = true;
    }

    private void OnLayOutWires(object sender, RoutedEventArgs e)
    {
        if (DataContext is SceneViewModel { IsLocked: false } scene)
        {
            scene.Editor.LayOutWires();
        }
    }

    private void OnZoomReset(object sender, RoutedEventArgs e)
    {
        _userMovedView = true;
        ZoomTo100();
    }

    private void OnFit(object sender, RoutedEventArgs e)
    {
        _userMovedView = false;
        Fit();
    }
}
