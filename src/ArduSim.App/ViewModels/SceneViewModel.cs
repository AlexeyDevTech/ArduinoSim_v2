using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using ArduSim.App.Localization;
using ArduSim.App.Services;
using ArduSim.Core.Boards;
using ArduSim.Core.Components;
using ArduSim.Core.Components.Parts;
using ArduSim.Core.Editing;
using ArduSim.Core.Projects;
using ArduSim.Ipc;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArduSim.App.ViewModels;

/// <summary>
/// The schematic as the canvas shows and edits it (docs/UI.md §3.6, §4): wraps a
/// <see cref="SceneEditor"/>, keeps one view model per part and wire, and holds the view-only
/// state — selection, hover, the wire being drawn, live simulation state.
/// </summary>
public sealed partial class SceneViewModel : ObservableObject
{
    private readonly Dictionary<string, PartViewModel> _parts = new(StringComparer.Ordinal);
    private readonly Action<string, string, double> _sendInput;
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
    private readonly Dictionary<int, (int Kind, int Value)> _boardPins = [];
    private readonly List<(double X, double Y)> _corners = [];
    private PinReference? _wireStart;
    private PinReference? _hoverPin;
    private string? _hoverWire;
    private (string WireId, List<(double X, double Y)> Corners, int First, bool Vertical)? _segmentDrag;

    public SceneViewModel(SceneEditor editor, BoardProfile board, Action<string, string, double> sendInput)
    {
        Editor = editor;
        Board = board;
        _sendInput = sendInput;
        editor.Changed += Sync;
        Sync();
    }

    public event Action? SelectionChanged;

    public SceneEditor Editor { get; }

    public BoardProfile Board { get; private set; }

    /// <summary>After the project switched boards; the scene itself changes through <see cref="SceneEditor.ChangeBoard"/>.</summary>
    public void UseBoard(BoardProfile board) => Board = board;

    public ObservableCollection<PartViewModel> Parts { get; } = [];

    public ObservableCollection<WireViewModel> Wires { get; } = [];

    public ObservableCollection<PinMarker> PinMarkers { get; } = [];

    public ObservableCollection<CornerHandle> Corners { get; } = [];

    public IReadOnlyCollection<string> SelectedParts => _selected;

    [ObservableProperty]
    public partial string? SelectedWire { get; private set; }

    /// <summary>While a run is active the structure is locked; interactive parts stay live (docs/UI.md §4.4).</summary>
    [ObservableProperty]
    public partial bool IsLocked { get; set; }

    [ObservableProperty]
    public partial PointCollection? Ghost { get; private set; }

    public bool IsWiring => _wireStart is not null;

    public bool VerticalFirst { get; set; }

    public PartViewModel? Find(string id) => _parts.GetValueOrDefault(id);

    /// <summary>Device pixels per canvas unit at the current zoom (set by the canvas); displays draw at this resolution.</summary>
    public double PixelsPerUnit { get; private set; } = 2;

    public void SetPixelsPerUnit(double value)
    {
        if (value <= 0 || Math.Abs(value - PixelsPerUnit) < 1e-9)
        {
            return;
        }

        PixelsPerUnit = value;
        foreach (PartViewModel part in Parts)
        {
            part.RenderScreen();
        }
    }

    public PartViewModel? SinglePart => _selected.Count == 1 ? Find(_selected.First()) : null;

    public Rect ContentBounds()
    {
        if (Parts.Count == 0)
        {
            return new Rect(0, 0, 400, 300);
        }

        Rect bounds = Rect.Empty;
        foreach (PartViewModel part in Parts)
        {
            bounds.Union(part.Bounds);
        }

        foreach (WireViewModel wire in Wires)
        {
            foreach (Point p in wire.Points)
            {
                bounds.Union(p);
            }
        }

        return bounds;
    }

    // ---- hit testing (world coordinates) ---------------------------------------------------

    public PartViewModel? PartAt(Point p) => Parts.LastOrDefault(part => part.Bounds.Contains(p));

    public (PinReference Pin, Point Position)? PinAt(Point p, double radius)
    {
        foreach (PartViewModel part in Parts.Reverse())
        {
            if (part.Definition is not { } definition)
            {
                continue;
            }

            foreach (PinDefinition pin in definition.Pins)
            {
                Point position = part.PinPosition(pin);
                if ((position - p).Length <= radius)
                {
                    return (new PinReference(part.Id, pin.Name), position);
                }
            }
        }

        return null;
    }

    public WireViewModel? WireAt(Point p, double tolerance) =>
        Wires.LastOrDefault(w => Segments(w.Points).Any(s => Distance(p, s.A, s.B) <= tolerance));

    public (WireViewModel Wire, int Segment)? WireSegmentAt(Point p, double tolerance)
    {
        foreach (WireViewModel wire in Wires.Reverse())
        {
            int index = 0;
            foreach ((Point a, Point b) in Segments(wire.Points))
            {
                if (Distance(p, a, b) <= tolerance)
                {
                    return (wire, index);
                }

                index++;
            }
        }

        return null;
    }

    public CornerHandle? CornerAt(Point p, double radius) => Corners.FirstOrDefault(c => (new Point(c.X, c.Y) - p).Length <= radius);

    // ---- selection ---------------------------------------------------------------------------

    public void Select(string id, bool additive)
    {
        if (!additive)
        {
            _selected.Clear();
        }

        if (additive && _selected.Contains(id))
        {
            _selected.Remove(id);
        }
        else
        {
            _selected.Add(id);
        }

        SelectedWire = null;
        RefreshSelection();
    }

    public void SelectWire(string? id)
    {
        _selected.Clear();
        SelectedWire = id;
        RefreshSelection();
    }

    public void SelectMany(IEnumerable<string> ids, bool additive)
    {
        if (!additive)
        {
            _selected.Clear();
        }

        _selected.UnionWith(ids);
        SelectedWire = null;
        RefreshSelection();
    }

    public void SelectInside(Rect area, bool additive) => SelectMany(Parts.Where(p => area.Contains(p.Bounds)).Select(p => p.Id), additive);

    public void SelectAll() => SelectMany(Parts.Select(p => p.Id), additive: false);

    public void ClearSelection()
    {
        _selected.Clear();
        SelectedWire = null;
        RefreshSelection();
    }

    // ---- editing (thin wrappers over SceneEditor) ---------------------------------------------

    public void DeleteSelection()
    {
        Editor.Delete([.. _selected], SelectedWire is null ? [] : [SelectedWire]);
        _selected.RemoveWhere(id => Find(id) is null);
        SelectedWire = null;
        RefreshSelection();
    }

    public void RotateSelection(bool clockwise) => Editor.Rotate([.. _selected], clockwise);

    public void FlipSelection() => Editor.Flip([.. _selected]);

    public void Nudge(double dx, double dy) => Editor.MoveParts([.. _selected], dx, dy);

    public void DuplicateSelection() => SelectMany(Editor.Duplicate([.. _selected]), additive: false);

    public string? CopySelection() => Editor.Copy([.. _selected]);

    public void Paste(string text, Point at)
    {
        IReadOnlyList<string> ids = Editor.Paste(text, at.X, at.Y);
        if (ids.Count > 0)
        {
            SelectMany(ids, additive: false);
        }
    }

    /// <summary>Places a part from a palette request (<c>type</c> or <c>type#preset</c>) centered on <paramref name="at"/>.</summary>
    public PartViewModel? Place(string request, Point at)
    {
        if (Resolve(request) is not { } resolved)
        {
            return null;
        }

        PartModel part = Editor.AddPart(resolved.Type, at.X - (resolved.Definition.Width / 2), at.Y - (resolved.Definition.Height / 2), resolved.Values);
        Select(part.Id, additive: false);
        return Find(part.Id);
    }

    /// <summary>The type, preset values and resulting layout of a palette request.</summary>
    public (string Type, Dictionary<string, JsonElement>? Values, ComponentDefinition Definition)? Resolve(string request)
    {
        (string type, string? presetId) = PaletteItem.Parse(request);
        if (Editor.Registry.Find(type) is not { } definition)
        {
            return null;
        }

        Dictionary<string, JsonElement>? values = definition.Presets.FirstOrDefault(p => p.Id == presetId) is { } preset ? definition.PresetValues(preset) : null;
        return (type, values, definition.Resolve(values));
    }

    // ---- wiring ------------------------------------------------------------------------------

    public void StartWire(PinReference pin)
    {
        _wireStart = pin;
        _corners.Clear();
        ClearSelection();
        UpdatePinMarkers();
    }

    public void AddCorner(Point p)
    {
        if (_wireStart is null)
        {
            return;
        }

        Point last = _corners.Count > 0 ? new Point(_corners[^1].X, _corners[^1].Y) : PinPoint(_wireStart);
        Point corner = new(SceneEditor.Snap(p.X), SceneEditor.Snap(p.Y));
        if (VerticalFirst && last.X != corner.X && last.Y != corner.Y)
        {
            _corners.Add((last.X, corner.Y));
        }

        _corners.Add((corner.X, corner.Y));
        UpdateGhost(p);
    }

    public void RemoveLastCorner(Point cursor)
    {
        if (_corners.Count > 0)
        {
            _corners.RemoveAt(_corners.Count - 1);
            UpdateGhost(cursor);
        }
    }

    public void UpdateGhost(Point cursor)
    {
        if (_wireStart is null)
        {
            Ghost = null;
            return;
        }

        Point start = PinPoint(_wireStart);
        var end = new Point(SceneEditor.Snap(cursor.X), SceneEditor.Snap(cursor.Y));
        if (PinAt(cursor, 6) is { } target)
        {
            end = target.Position;
        }

        Ghost = ToPoints(SceneEditor.Route((start.X, start.Y), _corners, (end.X, end.Y), VerticalFirst));
    }

    /// <summary>Completes the wire at <paramref name="pin"/>; false when it is the start pin (the wire is cancelled).</summary>
    public bool FinishWire(PinReference pin)
    {
        if (_wireStart is not { } start)
        {
            return false;
        }

        if (start == pin)
        {
            CancelWire();
            return false;
        }

        List<(double X, double Y)> corners = [.. _corners];
        if (VerticalFirst && corners.Count == 0)
        {
            Point a = PinPoint(start);
            Point b = PinPoint(pin);
            if (a.X != b.X && a.Y != b.Y)
            {
                corners.Add((a.X, b.Y));
            }
        }

        CancelWire();
        if (Editor.AddWire(start, pin, corners) is { } wire)
        {
            SelectWire(wire.Id);
        }

        return true;
    }

    public void CancelWire()
    {
        _wireStart = null;
        _corners.Clear();
        Ghost = null;
        UpdatePinMarkers();
    }

    // ---- wire editing ------------------------------------------------------------------------

    /// <summary>Moves one waypoint of a wire, snapped to the grid.</summary>
    public void MoveCorner(string wireId, int index, Point to, bool checkpoint)
    {
        if (Editor.Scene.FindWire(wireId) is not { } wire || index < 0 || index >= wire.Waypoints.Count)
        {
            return;
        }

        var corners = wire.Waypoints.ToList();
        corners[index] = (SceneEditor.Snap(to.X), SceneEditor.Snap(to.Y));
        Editor.SetWaypoints(wireId, corners, checkpoint);
    }

    /// <summary>
    /// Starts dragging segment <paramref name="segment"/> of a wire's route (docs/UI.md §4.3): every bend
    /// becomes an explicit waypoint, and a segment that ends on a pin gets a waypoint there, so the
    /// pin stays put while the segment moves away from it. False for a zero-length segment.
    /// </summary>
    public bool BeginSegmentDrag(string wireId, int segment)
    {
        if (Editor.Scene.FindWire(wireId) is not { } wire)
        {
            return false;
        }

        IReadOnlyList<(double X, double Y)> route = Editor.Route(wire);
        if (segment < 0 || segment + 1 >= route.Count || route[segment] == route[segment + 1])
        {
            return false;
        }

        List<(double X, double Y)> corners = [.. route.Skip(1).Take(route.Count - 2)];
        if (segment == 0)
        {
            corners.Insert(0, route[0]);
        }

        if (segment + 1 == route.Count - 1)
        {
            corners.Add(route[^1]);
        }

        _segmentDrag = (wireId, corners, segment == 0 ? 0 : segment - 1, route[segment].X == route[segment + 1].X);
        return true;
    }

    /// <summary>Moves the dragged segment perpendicular to itself by the snapped offset from the drag start.</summary>
    public void DragSegment(Vector offset, bool checkpoint)
    {
        if (_segmentDrag is not { } drag)
        {
            return;
        }

        double dx = drag.Vertical ? SceneEditor.Snap(offset.X) : 0;
        double dy = drag.Vertical ? 0 : SceneEditor.Snap(offset.Y);
        List<(double X, double Y)> corners = [.. drag.Corners];
        for (int i = drag.First; i <= drag.First + 1; i++)
        {
            corners[i] = (corners[i].X + dx, corners[i].Y + dy);
        }

        Editor.SetWaypoints(drag.WireId, corners, checkpoint);
    }

    public void EndSegmentDrag() => _segmentDrag = null;

    public void InsertCorner(string wireId, Point at)
    {
        if (Editor.Scene.FindWire(wireId) is not { } wire)
        {
            return;
        }

        var p = (SceneEditor.Snap(at.X), SceneEditor.Snap(at.Y));
        var corners = wire.Waypoints.ToList();
        IReadOnlyList<(double X, double Y)> route = Editor.Route(wire);
        int routeSegment = 0;
        double best = double.MaxValue;
        for (int i = 0; i + 1 < route.Count; i++)
        {
            double d = Distance(at, new Point(route[i].X, route[i].Y), new Point(route[i + 1].X, route[i + 1].Y));
            if (d < best)
            {
                best = d;
                routeSegment = i;
            }
        }

        // Count the corners that lie on the route before this segment to find the insertion index.
        int insertAt = corners.Count(c => route.Take(routeSegment + 1).Contains(c));
        corners.Insert(insertAt, p);
        Editor.SetWaypoints(wireId, corners);
    }

    // ---- hover -------------------------------------------------------------------------------

    public void Hover(PinReference? pin, string? wireId)
    {
        if (pin == _hoverPin && wireId == _hoverWire)
        {
            return;
        }

        _hoverPin = pin;
        _hoverWire = wireId;
        UpdateNetHighlight();
        UpdatePinMarkers();
    }

    public string PinTooltip(PinReference pin)
    {
        if (Find(pin.Component) is not { } part)
        {
            return pin.ToString();
        }

        string text = pin.Pin;
        if (part.Type == "board" && Board.FindPin(pin.Pin) is { } boardPin)
        {
            string kind = boardPin.Has(PinCapabilities.Pwm) ? Strings.Get("Pin_DigitalPwm")
                : boardPin.Has(PinCapabilities.Digital) ? Strings.Get("Pin_Digital") : Strings.Get("Pin_Analog");
            text += " · " + kind;
            if (_boardPins.TryGetValue(boardPin.Number, out var drive))
            {
                text += " · " + drive.Kind switch
                {
                    1 => "LOW",
                    2 => "HIGH",
                    3 => string.Create(CultureInfo.InvariantCulture, $"PWM {drive.Value * 100 / 255}%"),
                    4 => string.Create(CultureInfo.InvariantCulture, $"tone {drive.Value} Hz"),
                    _ => Strings.Get("Pin_Input"),
                };
            }
        }
        else
        {
            text = $"{part.Id}.{pin.Pin}";
        }

        int connections = Editor.Scene.Wires.Count(w => w.From == pin || w.To == pin);
        return connections == 0 ? text : text + " · " + Strings.Format("Pin_Wires", connections);
    }

    // ---- live state --------------------------------------------------------------------------

    public void ApplyComponent(string id, IReadOnlyDictionary<string, double> state, DisplayData? display = null)
    {
        if (Find(id) is { } part)
        {
            part.Apply(state);
            if (display is not null)
            {
                part.ApplyDisplay(display);
            }
        }
    }

    public void ApplyPins(IEnumerable<(int Pin, int Kind, int Value)> pins)
    {
        foreach ((int pin, int kind, int value) in pins)
        {
            _boardPins[pin] = (kind, value);
        }
    }

    /// <summary>Parts back to rest. Warning badges stay after a run ends and clear when the next one starts (docs/UI.md §4.4).</summary>
    public void ResetRuntime(bool clearWarnings)
    {
        _boardPins.Clear();
        foreach (PartViewModel part in Parts)
        {
            part.Apply(new Dictionary<string, double>(), reset: true);
            part.ClearDisplay();
            if (clearWarnings)
            {
                part.HasWarning = false;
            }
        }
    }

    public void Input(string id, string name, double value) => _sendInput(id, name, value);

    // ---- sync with the editor ----------------------------------------------------------------

    private void Sync()
    {
        var alive = new HashSet<string>(StringComparer.Ordinal);
        foreach (PartModel model in Editor.Scene.Parts)
        {
            alive.Add(model.Id);
            if (_parts.TryGetValue(model.Id, out PartViewModel? existing) && existing.Model.Type == model.Type && ReferenceEquals(existing.Definition, Editor.Definition(model)))
            {
                existing.Rebind(model);
            }
            else
            {
                var created = new PartViewModel(model, Editor.Definition(model), this);
                _parts[model.Id] = created;
                Parts.Add(created);
            }
        }

        foreach (PartViewModel stale in Parts.Where(p => !alive.Contains(p.Id) || _parts[p.Id] != p).ToList())
        {
            Parts.Remove(stale);
        }

        foreach (string id in _parts.Keys.Where(id => !alive.Contains(id)).ToList())
        {
            _parts.Remove(id);
        }

        _selected.IntersectWith(alive);
        RebuildWires();
        RefreshSelection();
    }

    /// <summary>Redraws the wires in the current theme's colors (the scene itself is unchanged).</summary>
    public void RecolorWires() => RebuildWires();

    private void RebuildWires()
    {
        var wires = new List<(WireModel Wire, Brush Stroke, string[] Net)>();
        foreach (IReadOnlyList<WireModel> net in Editor.WireNets())
        {
            NetKind kind = SceneEditor.KindOf(net);
            string[] ids = [.. net.Select(w => w.Id)];
            wires.AddRange(net.Select(wire => (wire, WirePalette.Brush(wire.Color, kind), ids)));
        }

        // Dragging changes routes, not the set of wires: update the existing views in place then,
        // so the canvas redraws only the wires that moved instead of regenerating all of them.
        if (wires.Count == Wires.Count && wires.Select(w => w.Wire.Id).SequenceEqual(Wires.Select(w => w.Id), StringComparer.Ordinal))
        {
            for (int i = 0; i < wires.Count; i++)
            {
                Wires[i].Update(Editor.Route(wires[i].Wire), wires[i].Stroke, wires[i].Net);
            }
        }
        else
        {
            Wires.Clear();
            foreach ((WireModel wire, Brush stroke, string[] net) in wires)
            {
                Wires.Add(new WireViewModel(wire.Id, ToPoints(Editor.Route(wire)), stroke, net));
            }
        }

        if (SelectedWire is not null && Editor.Scene.FindWire(SelectedWire) is null)
        {
            SelectedWire = null;
        }

        UpdateNetHighlight();
    }

    private void RefreshSelection()
    {
        foreach (PartViewModel part in Parts)
        {
            part.IsSelected = _selected.Contains(part.Id);
        }

        foreach (WireViewModel wire in Wires)
        {
            wire.IsSelected = wire.Id == SelectedWire;
        }

        Corners.Clear();
        if (SelectedWire is not null && Editor.Scene.FindWire(SelectedWire) is { } selected)
        {
            for (int i = 0; i < selected.Waypoints.Count; i++)
            {
                Corners.Add(new CornerHandle(selected.Id, i, selected.Waypoints[i].X, selected.Waypoints[i].Y));
            }
        }

        UpdatePinMarkers();
        SelectionChanged?.Invoke();
    }

    private void UpdateNetHighlight()
    {
        string[]? net = null;
        if (_hoverWire is not null)
        {
            net = Wires.FirstOrDefault(w => w.Id == _hoverWire)?.Net;
        }
        else if (_hoverPin is { } pin)
        {
            WireModel? touching = Editor.Scene.Wires.FirstOrDefault(w => w.From == pin || w.To == pin);
            net = touching is null ? null : Wires.FirstOrDefault(w => w.Id == touching.Id)?.Net;
        }

        foreach (WireViewModel wire in Wires)
        {
            wire.Dimmed = net is not null && !net.Contains(wire.Id);
        }
    }

    private void UpdatePinMarkers()
    {
        PinMarkers.Clear();
        IEnumerable<PartViewModel> parts = IsWiring || IsLocked ? Parts.Where(p => IsWiring) : Parts.Where(p => p.IsSelected || (_hoverPin?.Component == p.Id));
        foreach (PartViewModel part in parts.Where(p => p.Type != "board"))
        {
            foreach (PinDefinition pin in part.Definition?.Pins ?? [])
            {
                Point at = part.PinPosition(pin);
                PinMarkers.Add(new PinMarker(at.X, at.Y, _hoverPin == new PinReference(part.Id, pin.Name)));
            }
        }

        if (_hoverPin is { } hovered && Find(hovered.Component) is { Type: "board" } board && board.Definition?.FindPin(hovered.Pin) is { } boardPin)
        {
            Point at = board.PinPosition(boardPin);
            PinMarkers.Add(new PinMarker(at.X, at.Y, true));
        }
    }

    private Point PinPoint(PinReference pin) =>
        Editor.PinPosition(pin) is { } p ? new Point(p.X, p.Y) : default;

    private static PointCollection ToPoints(IEnumerable<(double X, double Y)> points)
    {
        var collection = new PointCollection(points.Select(p => new Point(p.X, p.Y)));
        collection.Freeze();
        return collection;
    }

    private static IEnumerable<(Point A, Point B)> Segments(PointCollection points)
    {
        for (int i = 0; i + 1 < points.Count; i++)
        {
            yield return (points[i], points[i + 1]);
        }
    }

    private static double Distance(Point p, Point a, Point b)
    {
        Vector ab = b - a;
        double t = ab.LengthSquared == 0 ? 0 : Math.Clamp(Vector.Multiply(p - a, ab) / ab.LengthSquared, 0, 1);
        return (p - (a + (t * ab))).Length;
    }
}

/// <summary>One part on the canvas. The view picks its template by <see cref="Type"/>.</summary>
public sealed partial class PartViewModel : ObservableObject
{
    private readonly SceneViewModel _scene;
    private readonly Dictionary<string, double> _live = new(StringComparer.Ordinal);
    private IReadOnlyList<KeyCapViewModel>? _keyCaps;
    private bool _applying;
    private DisplayData? _display;
    private Views.LcdScreen? _screen;
    private static readonly SolidColorBrush RedBoard = Board(0xB0, 0x1E, 0x2A);
    private static readonly SolidColorBrush BlueBoard = Board(0x1B, 0x4C, 0x9C);

    private Views.OledScreen? _oled;
    private Views.TftScreen? _tft;

    public PartViewModel(PartModel model, ComponentDefinition? definition, SceneViewModel scene)
    {
        Model = model;
        _scene = scene;
        Definition = definition;
        _applying = true;
        Value = Number("value", 0.5);
        SwitchRight = Number("position", 0) != 0;
        _applying = false;
        ClearDisplay();
    }

    public PartModel Model { get; private set; }

    public ComponentDefinition? Definition { get; }

    public string Id => Model.Id;

    /// <summary>Template key; boards share one ("board") and unknown types show a placeholder.</summary>
    public string Type => Definition is null ? "unknown" : Definition.Category == "board" ? "board" : Model.Type;

    public string DisplayName => Definition is null ? Model.Type : Strings.Get(Definition.NameKey);

    public double X => Model.X;

    public double Y => Model.Y;

    public double Width => Definition?.Width ?? 60;

    public double Height => Definition?.Height ?? 40;

    public double Rotation => Model.Rotation;

    public double FlipScale => Model.Flip ? -1 : 1;

    public Rect Bounds
    {
        get
        {
            (double left, double top, double right, double bottom) = _scene.Editor.Bounds(Model);
            return new Rect(left, top, right - left, bottom - top);
        }
    }

    public double BoundsLeft => Bounds.Left;

    public double BoundsTop => Bounds.Top;

    public double BoundsWidth => Bounds.Width;

    public double BoundsHeight => Bounds.Height;

    public double OutlineWidth => Bounds.Width + 6;

    public double OutlineHeight => Bounds.Height + 6;

    public double BadgeLeft => Bounds.Width - 6;

    public string Key => Text("key", "");

    public string KeyLabel => Key.Length == 0 ? "" : Key == "Space" ? Strings.Get("Key_Space") : Key;

    public Color LedColor => WirePalette.PartColor(Text("color", "red"));

    public string ResistanceText
    {
        get
        {
            double ohms = Number("resistance", 220);
            return ohms >= 1e6 ? (ohms / 1e6).ToString("0.##", CultureInfo.CurrentCulture) + " МΩ"
                : ohms >= 1e3 ? (ohms / 1e3).ToString("0.##", CultureInfo.CurrentCulture) + " кΩ"
                : ohms.ToString("0.##", CultureInfo.CurrentCulture) + " Ω";
        }
    }

    public IEnumerable<PinViewModel> Pins => Definition?.Pins.Select(p => new PinViewModel(p.Name, p.X, p.Y)) ?? [];

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool HasWarning { get; set; }

    [ObservableProperty]
    public partial bool Highlighted { get; set; }

    [ObservableProperty]
    public partial double Brightness { get; set; }

    [ObservableProperty]
    public partial double OnboardLed { get; set; }

    /// <summary>The board's Wi-Fi indicator: 0 off, 1 connecting, 2 connected, 3 access point.</summary>
    [ObservableProperty]
    public partial double WifiState { get; set; }

    /// <summary>Template key: every board has its own drawing (<c>Part.board.&lt;id&gt;</c>), falling back to <c>Part.board</c>.</summary>
    public string TemplateKey => Type == "board" ? "board." + Model.Type : Type;

    [ObservableProperty]
    public partial Color RgbColor { get; set; } = Colors.Black;

    [ObservableProperty]
    public partial double RgbIntensity { get; set; }

    [ObservableProperty]
    public partial bool Pressed { get; set; }

    [ObservableProperty]
    public partial bool SwitchRight { get; set; }

    [ObservableProperty]
    public partial double Frequency { get; set; }

    [ObservableProperty]
    public partial double Value { get; set; }

    public double KnobAngle => -135 + (270 * Value);

    // ---- M8 inputs and sensors (ADR-011) ---------------------------------------------------------

    /// <summary>Encoder detents turned in this run; the knob turns 18° per detent.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EncoderAngle))]
    public partial double Step { get; set; }

    public double EncoderAngle => Step * 18;

    /// <summary>The keypad key or remote button held down, or -1.</summary>
    [ObservableProperty]
    public partial int KeyIndex { get; set; } = -1;

    private static readonly string[] RemoteLabels = ["CH−", "CH", "CH+", "|◀◀", "▶▶|", "▶‖", "−", "+", "EQ", "0", "100+", "200+", "1", "2", "3", "4", "5", "6", "7", "8", "9"];

    /// <summary>The keypad's keys or the remote's buttons, row by row.</summary>
    public IReadOnlyList<KeyCapViewModel> KeyCaps => _keyCaps ??= Type switch
    {
        "keypad" => [.. MatrixKeypad.Labels.Select(c => new KeyCapViewModel(c.ToString(), char.IsLetter(c)))],
        "ir-remote" => [.. RemoteLabels.Select((label, i) => new KeyCapViewModel(label, i < 3))],
        _ => [],
    };

    partial void OnKeyIndexChanged(int value)
    {
        for (int i = 0; i < KeyCaps.Count; i++)
        {
            KeyCaps[i].IsDown = i == value;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CapLeft))]
    public partial double StickX { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CapTop))]
    public partial double StickY { get; set; }

    /// <summary>The joystick cap, which leans 10 units at full travel.</summary>
    public double CapLeft => 20 + (10 * StickX);

    public double CapTop => 17 - (10 * StickY);

    /// <summary>The IR receiver is taking in a frame.</summary>
    [ObservableProperty]
    public partial bool Receiving { get; set; }

    public bool IsDht11 => Text("model", "dht22") == "dht11";

    // ---- M9 outputs and modules (ADR-012) --------------------------------------------------------

    /// <summary>The servo horn's angle from the middle position (-90° to 90°).</summary>
    [ObservableProperty]
    public partial double HornAngle { get; set; }

    [ObservableProperty]
    public partial bool RelayOn { get; set; }

    /// <summary>The relay's COM-NO contact is closed.</summary>
    [ObservableProperty]
    public partial bool RelayClosed { get; set; }

    public bool LowTrigger => Text("trigger", "low") == "low";

    public bool IsRing => Text("layout", "strip") == "ring";

    public bool IsMatrixLayout => Text("layout", "strip") == "matrix";

    /// <summary>The ring's board: a circle the part's width across.</summary>
    public double RingSize => Width;

    public double RingHole => Width - 44;

    public double RingHoleOffset => (Width - RingHole) / 2;

    public string SdFolder => Text("folder", "sd");

    public bool CardInserted => Text("inserted", "yes") == "yes";

    /// <summary>The seven-segment body (it leaves the pins at the top and bottom edges free).</summary>
    public double BodyHeight => Height - 8;

    /// <summary>The LED area of the MAX7219 modules: 80 units per module.</summary>
    public double MatrixWidth => Width - 8;

    /// <summary>A strip's board; the matrix panel fills the part.</summary>
    public double BoardHeight => IsMatrixLayout ? Height : 40;

    /// <summary>Raised when a live sensor value changes, from the sim or from a gesture.</summary>
    public event Action<string, double>? LiveChanged;

    /// <summary>A live sensor reading: the run's value while it runs, the saved one otherwise.</summary>
    public double LiveValue(string name) => _live.TryGetValue(name, out double v) ? v : Number(name, 0);

    /// <summary>The live readings on the part's label (°C, %, cm).</summary>
    public string Readout => Type switch
    {
        "dht" => string.Create(CultureInfo.CurrentCulture, $"{LiveValue("temperature"):0.0} °C  {LiveValue("humidity"):0} %"),
        "ds18b20" or "ds3231" => string.Create(CultureInfo.CurrentCulture, $"{LiveValue("temperature"):0.0#} °C"),
        "hc-sr04" => string.Create(CultureInfo.CurrentCulture, $"{LiveValue("distance"):0} {Strings.Get("Unit_cm")}"),
        _ => "",
    };

    /// <summary>The property a vertical drag on the part changes (Shift: the second one, DHT humidity).</summary>
    public string? LiveDragProperty(bool second) =>
        Definition?.Properties.Where(p => p.Live).Select(p => p.Name).ElementAtOrDefault(second ? 1 : 0) ?? (second ? LiveDragProperty(false) : null);

    /// <summary>How much one pixel of drag (or a tenth of a wheel notch) changes a live value.</summary>
    public static double LiveStep(string name) => name switch
    {
        "humidity" => 0.5,
        "distance" => 1,
        _ => 0.1,
    };

    /// <summary>Sets a live sensor value for the running simulation: clamped, rounded, not saved, not undoable.</summary>
    public void SetLive(string name, double value)
    {
        if (Definition?.Properties.FirstOrDefault(p => p.Name == name && p.Live) is not { } definition)
        {
            return;
        }

        double resolution = name == "temperature" ? 0.01 : LiveStep(name);
        value = Math.Round(Math.Clamp(value, definition.Minimum, definition.Maximum) / resolution) * resolution;
        _live[name] = value;
        OnPropertyChanged(nameof(Readout));
        LiveChanged?.Invoke(name, value);
        _scene.Input(Id, name, value);
    }

    public void Turn(int detents)
    {
        if (detents != 0)
        {
            _scene.Input(Id, "turn", detents);
        }
    }

    /// <summary>Keypad key <c>k0</c>…<c>k15</c> or remote button <c>b0</c>…<c>b20</c>.</summary>
    public void PressKey(int index)
    {
        KeyIndex = index;
        _scene.Input(Id, (Type == "ir-remote" ? "b" : "k") + index.ToString(CultureInfo.InvariantCulture), 1);
    }

    public void ReleaseKey()
    {
        if (KeyIndex >= 0)
        {
            _scene.Input(Id, (Type == "ir-remote" ? "b" : "k") + KeyIndex.ToString(CultureInfo.InvariantCulture), 0);
            KeyIndex = -1;
        }
    }

    public void MoveStick(double x, double y)
    {
        x = Math.Clamp(x, -1, 1);
        y = Math.Clamp(y, -1, 1);
        if (x != StickX)
        {
            StickX = x;
            _scene.Input(Id, "x", x);
        }

        if (y != StickY)
        {
            StickY = y;
            _scene.Input(Id, "y", y);
        }
    }

    /// <summary>The key or button under a point in part coordinates (the drawing's grid), or null.</summary>
    public int? KeyAt(Point local)
    {
        (double left, double top, double width, double height, int columns, int rows) = Type == "ir-remote"
            ? (8.0, 28.0, 64.0, 133.0, 3, 7)
            : (10.0, 8.0, 100.0, 116.0, 4, 4);
        int column = (int)Math.Floor((local.X - left) / (width / columns));
        int row = (int)Math.Floor((local.Y - top) / (height / rows));
        return column >= 0 && column < columns && row >= 0 && row < rows ? (row * columns) + column : null;
    }

    /// <summary>A canvas point in the part's own unrotated, unflipped coordinates.</summary>
    public Point ToLocal(Point world)
    {
        double cx = Width / 2;
        double cy = Height / 2;
        double dx = world.X - (X + cx);
        double dy = world.Y - (Y + cy);
        (dx, dy) = ((((int)Rotation % 360) + 360) % 360) switch
        {
            90 => (dy, -dx),
            180 => (-dx, -dy),
            270 => (-dy, dx),
            _ => (dx, dy),
        };
        return new Point(Model.Flip ? Width - (cx + dx) : cx + dx, cy + dy);
    }

    // ---- LCD -----------------------------------------------------------------------------------

    public bool IsI2c => Text("interface", "i2c") == "i2c";

    public int LcdColumns => Text("size", "16x2") == "20x4" ? 20 : 16;

    public int LcdRows => Text("size", "16x2") == "20x4" ? 4 : 2;

    /// <summary>The black frame around the glass, in part units (below the header, inside the PCB).</summary>
    public Rect Bezel => LcdRows == 4 ? new Rect(16, 30, Width - 32, Height - 64) : new Rect(16, 22, Width - 32, Height - 44);

    public double BezelLeft => Bezel.Left;

    public double BezelTop => Bezel.Top;

    public double BezelWidth => Bezel.Width;

    public double BezelHeight => Bezel.Height;

    public double HoleRight => Width - 14;

    public double HoleBottom => Height - 14;

    /// <summary>The backpack board behind the module, around its four pins at the left edge.</summary>
    public double BackpackTop => (Definition?.FindPin("GND")?.Y ?? 50) - 10;

    [ObservableProperty]
    public partial ImageSource? Screen { get; private set; }

    /// <summary>Size of the drawn glass in part units (it follows the zoom by whole pixels).</summary>
    [ObservableProperty]
    public partial double ScreenWidth { get; private set; }

    [ObservableProperty]
    public partial double ScreenHeight { get; private set; }

    /// <summary>A display frame from the run; identical frames (the sketch rewrote the same text) are not redrawn.</summary>
    public void ApplyDisplay(DisplayData display)
    {
        if (_display is { } old && old.Status == display.Status && old.Contrast.Equals(display.Contrast) &&
            old.Columns == display.Columns && old.Rows == display.Rows && old.Dots.AsSpan().SequenceEqual(display.Dots))
        {
            return;
        }

        _display = display;
        RenderScreen();
    }

    /// <summary>Back to the look of a module without power (before and after a run).</summary>
    public void ClearDisplay()
    {
        _display = null;
        RenderScreen();
    }

    // ---- OLED ----------------------------------------------------------------------------------

    public bool IsSh1106 => Text("controller", "ssd1306") == "sh1106";

    /// <summary>Panel pixels and pixel pitch in canvas units, per module (0.96", 0.91", 0.66", 0.69", 1.3", 2.42", 1.5").</summary>
    private (int Columns, int Rows, double Pitch) OledGeometry => Text("controller", "ssd1306") switch
    {
        "sh1106" => (128, 64, 0.9),
        "ssd1309" => (128, 64, 1.69),
        "ssd1327" => (128, 128, 0.82),
        _ => Text("size", "128x64") switch
        {
            "128x32" => (128, 32, 0.69),
            "64x48" => (64, 48, 0.82),
            "96x16" => (96, 16, 0.65),
            _ => (128, 64, 0.67),
        },
    };

    /// <summary>The glass of the module (OLED, TFT or graphic LCD), in part units.</summary>
    public Rect Glass => Type switch
    {
        "st7735" or "st7789" or "ili9341" => TftGlass.Glass,
        "pcd8544" => new Rect(26, 50, 118, 68),
        _ => Text("controller", "ssd1306") switch
        {
            "sh1106" => new Rect(8, 24, 124, 86),
            "ssd1309" => new Rect(8, 22, 224, 128),
            "ssd1327" => new Rect(16, 24, 118, 118),
            _ => Text("size", "128x64") switch
            {
                "128x32" => new Rect(10, 16, 130, 36),
                "64x48" => new Rect(15, 20, 70, 58),
                "96x16" => new Rect(15, 20, 100, 26),
                _ => new Rect(5, 22, 100, 70),
            },
        },
    };

    /// <summary>A TFT module's glass (its active area, after the module's size) and its pixel count.</summary>
    private (Rect Glass, int Columns, int Rows) TftGlass => Type == "ili9341" ? (new Rect(15, 55, 170, 227), 240, 320) : Text("module", "") switch
    {
        "st7735-128x128" => (new Rect(15, 45, 100, 100), 128, 128),
        "st7735-80x160" => (new Rect(28.75, 40, 42.5, 85), 80, 160),
        "st7789-240x320" => (new Rect(10, 45, 120, 160), 240, 320),
        "st7789-135x240" => (new Rect(18.2, 35, 53.6, 95.3), 135, 240),
        "st7789-240x240" => (new Rect(9, 40, 92, 92), 240, 240),
        _ => (new Rect(15, 40, 110, 137.5), 128, 160),
    };

    /// <summary>The module's circuit board: TFTs and the LIGHT-low Nokia board are red, the rest blue.</summary>
    public Brush BoardBrush => Type switch
    {
        "st7735" or "ili9341" => RedBoard,
        "pcd8544" when Text("light", "high") == "low" => RedBoard,
        _ => BlueBoard,
    };

    private static SolidColorBrush Board(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public double GlassLeft => Glass.Left;

    public double GlassTop => Glass.Top;

    public double GlassWidth => Glass.Width;

    public double GlassHeight => Glass.Height;

    /// <summary>The color of the pixels' glow.</summary>
    public Color GlowColor => Text("panel", "white") == "white" ? Color.FromRgb(0xF0, 0xF5, 0xFF) : Color.FromRgb(0x48, 0xAE, 0xFF);

    /// <summary>Redraws the glass for the current frame, style and zoom.</summary>
    public void RenderScreen()
    {
        if (Type == "lcd")
        {
            _screen ??= new Views.LcdScreen();
            if (_screen.Render(_display, LcdColumns, LcdRows, Text("backlight", "blue"), _scene.PixelsPerUnit))
            {
                Screen = _screen.Source;
            }

            ScreenWidth = _screen.Width;
            ScreenHeight = _screen.Height;
        }
        else if (Type is "seven-segment" or "tm1637" or "max7219" or "ws2812")
        {
            var part = new Size(Width, Height);
            Color color = WirePalette.PartColor(Text("color", "red"));
            Screen = Type switch
            {
                "seven-segment" => Views.LedScreens.Segments(_display, Text("digits", "1") == "4" ? 4 : 1, color, colon: false, part, Text("digits", "1") == "4" ? new Rect(12, 12, 106, 46) : new Rect(10, 14, 40, 52)),
                "tm1637" => Views.LedScreens.Segments(_display, 4, color, colon: true, part, new Rect(12, 8, 96, 36)),
                "max7219" => Views.LedScreens.Matrix(_display, (int)Math.Round((Width - 10) / 80) * 8, color, part),
                _ => Views.LedScreens.Rgb(_display, Views.LedScreens.LedCenters(Text("layout", "strip"), (int)Number("count", 8), part), 7, part),
            };
            ScreenWidth = Width;
            ScreenHeight = Height;
        }
        else if (Type is "st7735" or "st7789" or "ili9341" or "pcd8544")
        {
            _tft ??= new Views.TftScreen();
            (Rect glass, int columns, int rows) = Type == "pcd8544" ? (Glass, 84, 48) : TftGlass;
            if (_tft.Render(_display, columns, rows, Type == "pcd8544", _scene.PixelsPerUnit, glass.Width / columns))
            {
                Screen = _tft.Source;
            }

            ScreenWidth = glass.Width;
            ScreenHeight = glass.Height;
        }
        else if (Type == "oled")
        {
            _oled ??= new Views.OledScreen();
            (int columns, int rows, double pitch) = OledGeometry;
            if (_oled.Render(_display, columns, rows, Text("panel", "white"), _scene.PixelsPerUnit, pitch))
            {
                Screen = _oled.Source;
            }

            ScreenWidth = _oled.Width;
            ScreenHeight = _oled.Height;
        }
    }

    public Point PinPosition(PinDefinition pin)
    {
        (double x, double y) = ComponentGeometry.PinPosition(Definition!, pin, Model.X, Model.Y, Model.Rotation, Model.Flip);
        return new Point(x, y);
    }

    public string Text(string name, string fallback) =>
        Model.Properties.TryGetValue(name, out JsonElement e)
            ? e.ValueKind == JsonValueKind.String ? e.GetString() ?? fallback : e.GetRawText()
            : Definition?.Properties.FirstOrDefault(d => d.Name == name)?.Default ?? fallback;

    public double Number(string name, double fallback) =>
        double.TryParse(Text(name, fallback.ToString(CultureInfo.InvariantCulture)), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

    /// <summary>The editor replaced or changed the model (move, property, undo): refresh everything derived from it.</summary>
    public void Rebind(PartModel model)
    {
        Model = model;
        OnPropertyChanged(string.Empty);
        RenderScreen();
    }

    public void Apply(IReadOnlyDictionary<string, double> state, bool reset = false)
    {
        _applying = true;
        try
        {
            double Get(string key, double fallback) => state.TryGetValue(key, out double v) ? v : reset ? fallback : double.NaN;
            void Set(string key, double fallback, Action<double> apply)
            {
                double v = Get(key, fallback);
                if (!double.IsNaN(v))
                {
                    apply(v);
                }
            }

            Set("brightness", 0, v => Brightness = v);
            Set("L", 0, v => OnboardLed = v);
        Set("wifi", 0, v => WifiState = v);
            Set("pressed", 0, v => Pressed = v != 0);
            Set("position", Number("position", 0), v => SwitchRight = v != 0);
            Set("frequency", 0, v => Frequency = v);
            Set("value", Number("value", 0.5), v => Value = v);
            Set("step", 0, v => Step = v);
            Set("key", -1, v => KeyIndex = (int)v);
            Set("x", 0, v => StickX = v);
            Set("y", 0, v => StickY = v);
            Set("receiving", 0, v => Receiving = v != 0);
            Set("angle", 90, v => HornAngle = v - 90);
            Set("coil", 0, v => RelayOn = v != 0);
            Set("closed", 0, v => RelayClosed = v != 0);
            if (reset)
            {
                _live.Clear();
                OnPropertyChanged(nameof(Readout));
            }

            foreach (string name in (string[])["temperature", "humidity", "distance"])
            {
                if (state.TryGetValue(name, out double reading) && (!_live.TryGetValue(name, out double known) || known != reading))
                {
                    _live[name] = reading;
                    OnPropertyChanged(nameof(Readout));
                    LiveChanged?.Invoke(name, reading);
                }
            }
            if (state.ContainsKey("r") || state.ContainsKey("g") || state.ContainsKey("b") || reset)
            {
                double r = Math.Max(0, Get("r", 0)), g = Math.Max(0, Get("g", 0)), b = Math.Max(0, Get("b", 0));
                r = double.IsNaN(r) ? 0 : r;
                g = double.IsNaN(g) ? 0 : g;
                b = double.IsNaN(b) ? 0 : b;
                double max = Math.Max(r, Math.Max(g, b));
                RgbIntensity = max;
                RgbColor = max > 0 ? Color.FromRgb((byte)(255 * r / max), (byte)(255 * g / max), (byte)(255 * b / max)) : Colors.Black;
            }
        }
        finally
        {
            _applying = false;
        }
    }

    public void Press()
    {
        Pressed = true;
        _scene.Input(Id, "pressed", 1);
    }

    public void Release()
    {
        if (Pressed)
        {
            Pressed = false;
            _scene.Input(Id, "pressed", 0);
        }
    }

    public void Toggle()
    {
        SwitchRight = !SwitchRight;
        _scene.Input(Id, "position", SwitchRight ? 1 : 0);
    }

    partial void OnValueChanged(double value)
    {
        OnPropertyChanged(nameof(KnobAngle));
        if (!_applying)
        {
            _scene.Input(Id, "value", value);
        }
    }
}

/// <summary>One key of the keypad or the IR remote, as drawn.</summary>
public sealed partial class KeyCapViewModel(string label, bool isAccent) : ObservableObject
{
    public string Label { get; } = label;

    /// <summary>Drawn in the accent color (the keypad's letters, the remote's top row).</summary>
    public bool IsAccent { get; } = isAccent;

    [ObservableProperty]
    public partial bool IsDown { get; set; }
}

public sealed record PinViewModel(string Name, double X, double Y)
{
    /// <summary>Duplicate pins (GND.2, RST.2) are labelled like their twin.</summary>
    public string Label => Name.EndsWith(".2", StringComparison.Ordinal) ? Name[..^2] : Name;

    /// <summary>Label offset from the pin: top-row labels go below it, bottom-row labels above.</summary>
    public double LabelOffset => Y < 35 ? 4 : -10;
}

public sealed record PinMarker(double X, double Y, bool IsHot);

public sealed record CornerHandle(string WireId, int Index, double X, double Y);

/// <summary>A wire's polyline, color and live view state.</summary>
public sealed partial class WireViewModel(string id, PointCollection points, Brush stroke, string[] net) : ObservableObject
{
    public string Id { get; } = id;

    [ObservableProperty]
    public partial PointCollection Points { get; private set; } = points;

    [ObservableProperty]
    public partial Brush Stroke { get; private set; } = stroke;

    /// <summary>Ids of every wire in the same net.</summary>
    public string[] Net { get; private set; } = net;

    /// <summary>Takes a new route, color and net; unchanged values do not notify.</summary>
    public void Update(IReadOnlyList<(double X, double Y)> route, Brush stroke, string[] net)
    {
        if (route.Count != Points.Count || route.Where((p, i) => p.X != Points[i].X || p.Y != Points[i].Y).Any())
        {
            var points = new PointCollection(route.Select(p => new Point(p.X, p.Y)));
            points.Freeze();
            Points = points;
        }

        Stroke = stroke;
        Net = net;
    }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool Dimmed { get; set; }
}

/// <summary>Wire and part colors (docs/UI.md §3.1).</summary>
public static class WirePalette
{
    public static readonly IReadOnlyList<string> Swatches = ["auto", "green", "blue", "yellow", "orange", "purple", "pink", "white", "grey"];

    // One frozen brush per color, so unchanged wires keep the same instance (no redraw).
    private static readonly Dictionary<Color, SolidColorBrush> Brushes = [];

    public static Brush Brush(string color, NetKind kind)
    {
        Color c = color switch
        {
            "green" => Rgb(0x3BB273),
            "blue" => Rgb(0x3E8BFF),
            "yellow" => Rgb(0xF2C94C),
            "orange" => Rgb(0xFF962E),
            "purple" => Rgb(0xA66CFF),
            "pink" => Rgb(0xFF6FAE),
            "white" => Rgb(0xE7E9EE),
            "grey" => Rgb(0x8A93A6),
            _ => kind switch
            {
                NetKind.Power => Rgb(0xE54848),
                NetKind.Ground => Theme.IsLight ? Rgb(0x2D3138) : Rgb(0x5A606E),
                NetKind.I2cData => Rgb(0x3E8BFF),
                NetKind.I2cClock => Rgb(0xF2C94C),
                _ => Rgb(0x3BB273),
            },
        };
        if (!Brushes.TryGetValue(c, out SolidColorBrush? brush))
        {
            brush = new SolidColorBrush(c);
            brush.Freeze();
            Brushes[c] = brush;
        }

        return brush;
    }

    public static Color PartColor(string color) => color switch
    {
        "green" => Rgb(0x3AE06A),
        "blue" => Rgb(0x3E8BFF),
        "yellow" => Rgb(0xFFD83A),
        "orange" => Rgb(0xFF962E),
        "white" => Rgb(0xF2F4FF),
        "black" => Rgb(0x303034),
        _ => Rgb(0xFF3B3B),
    };

    public static Color SwatchColor(string swatch) => swatch == "auto" ? Rgb(0x3BB273) : ((SolidColorBrush)Brush(swatch, NetKind.Signal)).Color;

    private static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
