using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using ArduSim.App.Localization;
using ArduSim.App.Services;
using ArduSim.Core.Boards;
using ArduSim.Core.Components;
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
    private bool _applying;
    private DisplayData? _display;
    private Views.LcdScreen? _screen;
    private Views.OledScreen? _oled;

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

    /// <summary>Panel pixels and pixel pitch in canvas units, per module (0.96", 0.91", 0.66", 0.69", 1.3").</summary>
    private (int Columns, int Rows, double Pitch) OledGeometry => IsSh1106 ? (128, 64, 0.9) : Text("size", "128x64") switch
    {
        "128x32" => (128, 32, 0.69),
        "64x48" => (64, 48, 0.82),
        "96x16" => (96, 16, 0.65),
        _ => (128, 64, 0.67),
    };

    /// <summary>The black glass of the module, in part units.</summary>
    public Rect Glass => IsSh1106 ? new Rect(8, 24, 124, 86) : Text("size", "128x64") switch
    {
        "128x32" => new Rect(10, 16, 130, 36),
        "64x48" => new Rect(15, 20, 70, 58),
        "96x16" => new Rect(15, 20, 100, 26),
        _ => new Rect(5, 22, 100, 70),
    };

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
