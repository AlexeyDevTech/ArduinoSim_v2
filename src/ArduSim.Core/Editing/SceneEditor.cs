using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArduSim.Core.Components;
using ArduSim.Core.Projects;

namespace ArduSim.Core.Editing;

/// <summary>What a wire's net carries, for its automatic color (docs/UI.md §3.1).</summary>
public enum NetKind
{
    Signal,
    Power,
    Ground,
    I2cData,
    I2cClock,
}

/// <summary>
/// Every edit of a scene (docs/UI.md §4): placing, moving, rotating, flipping, deleting, wiring,
/// properties, ids, copy and paste — with undo and redo for all of it. UI-agnostic, so an Avalonia
/// port keeps it. Undo keeps whole-scene snapshots: scenes are small and every edit is covered.
/// </summary>
public sealed partial class SceneEditor
{
    public const double Grid = 10;
    private const int HistoryLimit = 200;
    private const string ClipboardMarker = "ardusim-parts";

    private static readonly Dictionary<string, string> IdPrefixes = new(StringComparer.Ordinal)
    {
        ["led"] = "led",
        ["rgb-led"] = "rgb",
        ["resistor"] = "r",
        ["pushbutton"] = "btn",
        ["slide-switch"] = "sw",
        ["potentiometer"] = "pot",
        ["buzzer"] = "bz",
        ["lcd"] = "lcd",
        ["oled"] = "oled",
    };

    private readonly ComponentRegistry _registry;
    private readonly Stack<SceneDocument> _undo = new();
    private readonly Stack<SceneDocument> _redo = new();
    private readonly WireRouter _router;

    public SceneEditor(SceneModel scene, ComponentRegistry registry)
    {
        Scene = scene;
        _registry = registry;
        _router = new WireRouter(this);
        LayOutWhatHasNoRoute();
    }

    /// <summary>Raised after every change, including undo and redo.</summary>
    public event Action? Changed;

    public SceneModel Scene { get; private set; }

    public ComponentRegistry Registry => _registry;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public static double Snap(double value) => Math.Round(value / Grid) * Grid;

    /// <summary>The part's definition for its property values (the LCD's size and pins depend on them).</summary>
    public ComponentDefinition? Definition(PartModel part)
    {
        ArgumentNullException.ThrowIfNull(part);
        return _registry.Find(part.Type)?.Resolve(part.Properties);
    }

    public bool IsBoard(PartModel part) => Definition(part)?.Category == "board";

    /// <summary>Records the current scene as one undo step; call before a change (or once before a drag).</summary>
    public void Checkpoint()
    {
        _undo.Push(Scene.ToDocument(_registry, keepRoutes: true));
        _redo.Clear();
        Trim(_undo);
    }

    /// <summary>
    /// Wires a file brought without a route are laid out now. So are corners nobody could have set by hand:
    /// off the grid (another tool wrote them), or the one bend the editor used to choose by itself.
    /// </summary>
    private void LayOutWhatHasNoRoute()
    {
        foreach (WireModel wire in Scene.Wires.Where(w => !w.IsAuto))
        {
            bool offGrid = wire.Waypoints.Any(c => c.X != Snap(c.X) || c.Y != Snap(c.Y));
            bool plainBend = wire.Waypoints.Count == 1 && PinPosition(wire.From) is { } a && PinPosition(wire.To) is { } b && wire.Waypoints[0] == (a.X, b.Y);
            wire.IsAuto = offGrid || plainBend;
        }

        _router.Lay([.. Scene.Wires.Where(w => w.IsAuto)], thorough: true);
    }

    // After parts changed place or shape: a wire with one end on them is laid out anew, since no corner
    // of its old route still means anything, and so is a laid-out wire they now lie over.
    private void RerouteAround(IReadOnlyCollection<string> moved, bool bothEndsToo = false)
    {
        var boxes = Scene.Parts.Where(p => moved.Contains(p.Id)).Select(Bounds).ToList();
        var dirty = new List<WireModel>();
        foreach (WireModel wire in Scene.Wires)
        {
            bool from = moved.Contains(wire.From.Component);
            bool to = moved.Contains(wire.To.Component);
            if (from != to || (from && to && bothEndsToo))
            {
                wire.IsAuto = true;
                dirty.Add(wire);
            }
            else if (wire.IsAuto && !(from && to))
            {
                IReadOnlyList<(double X, double Y)> route = Route(wire);
                if (boxes.Any(b => CrossesBox(route, b.Left, b.Top, b.Right, b.Bottom)))
                {
                    dirty.Add(wire);
                }
            }
        }

        _router.Lay(dirty, thorough: false);
    }

    /// <summary>Разложить провода: every wire laid out anew, corners set by hand too. One undo step.</summary>
    public void LayOutWires()
    {
        if (Scene.Wires.Count == 0)
        {
            return;
        }

        Checkpoint();
        foreach (WireModel wire in Scene.Wires)
        {
            wire.IsAuto = true;
        }

        _router.Lay(Scene.Wires, thorough: true);
        Changed?.Invoke();
    }

    /// <summary>Сбросить маршрут: this wire goes back to being laid out by the editor.</summary>
    public void ResetRoute(string wireId)
    {
        if (Scene.FindWire(wireId) is { IsAuto: false } wire)
        {
            Checkpoint();
            wire.IsAuto = true;
            _router.Lay([wire], thorough: false);
            Changed?.Invoke();
        }
    }

    /// <summary>The route a new wire between these pins would take.</summary>
    public IReadOnlyList<(double X, double Y)> PreviewRoute(PinReference from, PinReference to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        return PinPosition(from) is { } a && PinPosition(to) is { } b ? Route(a, _router.Preview(from, to), b) : [];
    }

    public void Undo() => Swap(_undo, _redo);

    public void Redo() => Swap(_redo, _undo);

    /// <summary>Takes back the step just begun, as if it never was: nothing to redo (a drag cancelled by Esc).</summary>
    public void CancelStep()
    {
        if (_undo.Count > 0)
        {
            Scene = SceneModel.FromDocument(_undo.Pop());
            LayOutWhatHasNoRoute();
            Changed?.Invoke();
        }
    }

    public void NotifyChanged() => Changed?.Invoke();

    /// <param name="properties">Initial property values (a palette preset); null for the defaults.</param>
    public PartModel AddPart(string type, double x, double y, IReadOnlyDictionary<string, JsonElement>? properties = null)
    {
        if (_registry.Find(type) is null)
        {
            throw new ArgumentException($"Unknown part type '{type}'.", nameof(type));
        }

        Checkpoint();
        var part = new PartModel(NewPartId(type), type) { X = Snap(x), Y = Snap(y) };
        foreach ((string name, JsonElement value) in properties ?? new Dictionary<string, JsonElement>())
        {
            part.Properties[name] = value.Clone();
        }

        Scene.Parts.Add(part);
        RerouteAround([part.Id]);
        Changed?.Invoke();
        return part;
    }

    /// <summary>Moves parts: wires between them go along, wires with one end on them are laid out anew. Pass checkpoint=false while a drag continues.</summary>
    public void MoveParts(IReadOnlyCollection<string> ids, double dx, double dy, bool checkpoint = true)
    {
        if (ids.Count == 0 || (dx == 0 && dy == 0))
        {
            return;
        }

        if (checkpoint)
        {
            Checkpoint();
        }

        foreach (PartModel part in Scene.Parts.Where(p => ids.Contains(p.Id)))
        {
            part.X += dx;
            part.Y += dy;
        }

        foreach (WireModel wire in Scene.Wires.Where(w => ids.Contains(w.From.Component) && ids.Contains(w.To.Component)))
        {
            for (int i = 0; i < wire.Waypoints.Count; i++)
            {
                wire.Waypoints[i] = (wire.Waypoints[i].X + dx, wire.Waypoints[i].Y + dy);
            }
        }

        RerouteAround(ids);
        Changed?.Invoke();
    }

    /// <summary>Rotates the parts by 90° about the center of their bounds; pins stay on the grid.</summary>
    public void Rotate(IReadOnlyCollection<string> ids, bool clockwise)
    {
        List<PartModel> parts = Scene.Parts.Where(p => ids.Contains(p.Id)).ToList();
        if (parts.Count == 0)
        {
            return;
        }

        Checkpoint();
        (double left, double top, double right, double bottom) = Union(parts);
        double px = (left + right) / 2;
        double py = (top + bottom) / 2;
        foreach (PartModel part in parts)
        {
            ComponentDefinition? definition = Definition(part);
            double w = definition?.Width ?? 0;
            double h = definition?.Height ?? 0;
            double cx = part.X + (w / 2) - px;
            double cy = part.Y + (h / 2) - py;
            (cx, cy) = clockwise ? (-cy, cx) : (cy, -cx);
            part.X = px + cx - (w / 2);
            part.Y = py + cy - (h / 2);
            part.Rotation = SceneModel.Normalize(part.Rotation + (clockwise ? 90 : 270));
            AlignToGrid(part);
        }

        RerouteAround(ids, bothEndsToo: true);
        Changed?.Invoke();
    }

    public void Flip(IReadOnlyCollection<string> ids)
    {
        List<PartModel> parts = Scene.Parts.Where(p => ids.Contains(p.Id)).ToList();
        if (parts.Count == 0)
        {
            return;
        }

        Checkpoint();
        foreach (PartModel part in parts)
        {
            part.Flip = !part.Flip;
            AlignToGrid(part);
        }

        RerouteAround(ids, bothEndsToo: true);
        Changed?.Invoke();
    }

    /// <summary>Deletes parts (never the board) with their wires, and wires.</summary>
    public void Delete(IReadOnlyCollection<string> partIds, IReadOnlyCollection<string> wireIds)
    {
        var parts = Scene.Parts.Where(p => partIds.Contains(p.Id) && !IsBoard(p)).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        if (parts.Count == 0 && !Scene.Wires.Any(w => wireIds.Contains(w.Id)))
        {
            return;
        }

        Checkpoint();
        Scene.Parts.RemoveAll(p => parts.Contains(p.Id));
        Scene.Wires.RemoveAll(w => wireIds.Contains(w.Id) || parts.Contains(w.From.Component) || parts.Contains(w.To.Component));
        Changed?.Invoke();
    }

    public WireModel? AddWire(PinReference from, PinReference to, IEnumerable<(double X, double Y)> waypoints)
    {
        if (from == to || PinPosition(from) is null || PinPosition(to) is null)
        {
            return null;
        }

        Checkpoint();
        var wire = new WireModel(NewWireId(), from, to);
        wire.Waypoints.AddRange(waypoints);
        wire.IsAuto = wire.Waypoints.Count == 0;
        Scene.Wires.Add(wire);
        if (wire.IsAuto)
        {
            _router.Lay([wire], thorough: false);
        }

        Changed?.Invoke();
        return wire;
    }

    public void SetWaypoints(string wireId, IEnumerable<(double X, double Y)> waypoints, bool checkpoint = true)
    {
        if (Scene.FindWire(wireId) is not { } wire)
        {
            return;
        }

        if (checkpoint)
        {
            Checkpoint();
        }

        wire.Waypoints.Clear();
        wire.Waypoints.AddRange(waypoints.Select(c => (Snap(c.X), Snap(c.Y))));
        wire.IsAuto = false;
        Changed?.Invoke();
    }

    /// <summary>Moves one end of a wire to another pin, in one undo step; the route is drawn anew. False for the wire's other end or a pin that is not there.</summary>
    public bool ReconnectWire(string wireId, bool fromEnd, PinReference pin)
    {
        if (Scene.FindWire(wireId) is not { } wire || PinPosition(pin) is null || pin == wire.To || pin == wire.From)
        {
            return false;
        }

        Checkpoint();
        if (fromEnd)
        {
            wire.From = pin;
        }
        else
        {
            wire.To = pin;
        }

        wire.IsAuto = true;
        _router.Lay([wire], thorough: false);
        Changed?.Invoke();
        return true;
    }

    private static bool CrossesBox(IReadOnlyList<(double X, double Y)> route, double left, double top, double right, double bottom)
    {
        for (int i = 0; i + 1 < route.Count; i++)
        {
            (double ax, double ay) = route[i];
            (double bx, double by) = route[i + 1];
            if (Math.Max(ax, bx) > left && Math.Min(ax, bx) < right && Math.Max(ay, by) > top && Math.Min(ay, by) < bottom)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Where a part of this size goes near a point without covering another part: on the grid, spiralling
    /// out, inside <paramref name="view"/> when it can be. The answer is the spot's center.
    /// </summary>
    public (double X, double Y) FreeSpot(double width, double height, double centerX, double centerY, (double Left, double Top, double Right, double Bottom)? view = null)
    {
        const int Rings = 200;
        double margin = 2 * Grid;
        double startX = Snap(centerX - (width / 2));
        double startY = Snap(centerY - (height / 2));
        foreach (bool insideView in view is null ? (bool[])[false] : [true, false])
        {
            for (int ring = 0; ring <= Rings; ring++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                {
                    for (int dy = -ring; dy <= ring; dy++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring)
                        {
                            continue;
                        }

                        double x = startX + (dx * 2 * Grid);
                        double y = startY + (dy * 2 * Grid);
                        bool fits = !insideView || (x >= view!.Value.Left && y >= view.Value.Top && x + width <= view.Value.Right && y + height <= view.Value.Bottom);
                        if (fits && !Scene.Parts.Any(p => Overlaps(Bounds(p), x, y, width, height, margin)))
                        {
                            return (x + (width / 2), y + (height / 2));
                        }
                    }
                }
            }
        }

        return (centerX, centerY);
    }

    private static bool Overlaps((double Left, double Top, double Right, double Bottom) box, double x, double y, double width, double height, double margin) =>
        x < box.Right + margin && x + width > box.Left - margin && y < box.Bottom + margin && y + height > box.Top - margin;

    /// <summary>
    /// A part's pin that lands exactly on another part's pin is connected: a zero-length wire joins them,
    /// in the undo step of the move or placement that made them meet. Answers how many were added.
    /// </summary>
    public int JoinCoincidingPins(IReadOnlyCollection<string> partIds)
    {
        ArgumentNullException.ThrowIfNull(partIds);
        const double Same = 0.01;
        int added = 0;
        foreach (string id in partIds)
        {
            if (Scene.FindPart(id) is not { } part || Definition(part) is not { } definition)
            {
                continue;
            }

            foreach (PinDefinition pin in definition.Pins)
            {
                var from = new PinReference(id, pin.Name);
                if (PinPosition(from) is not { } at)
                {
                    continue;
                }

                foreach (PartModel other in Scene.Parts.Where(p => p.Id != id))
                {
                    foreach (PinDefinition otherPin in Definition(other)?.Pins ?? [])
                    {
                        var to = new PinReference(other.Id, otherPin.Name);
                        bool meets = PinPosition(to) is { } there && Math.Abs(there.X - at.X) < Same && Math.Abs(there.Y - at.Y) < Same;
                        if (meets && !Scene.Wires.Any(w => (w.From == from && w.To == to) || (w.From == to && w.To == from)))
                        {
                            Scene.Wires.Add(new WireModel(NewWireId(), from, to));
                            added++;
                        }
                    }
                }
            }
        }

        if (added > 0)
        {
            Changed?.Invoke();
        }

        return added;
    }

    public void SetWireColor(string wireId, string color)
    {
        if (Scene.FindWire(wireId) is not { } wire || wire.Color == color)
        {
            return;
        }

        Checkpoint();
        wire.Color = color;
        Changed?.Invoke();
    }

    public void SetProperty(string partId, string name, JsonElement value, bool checkpoint = true)
    {
        if (Scene.FindPart(partId) is not { } part ||
            (part.Properties.TryGetValue(name, out JsonElement old) && old.GetRawText() == value.GetRawText()))
        {
            return;
        }

        if (checkpoint)
        {
            Checkpoint();
        }

        (double Left, double Top, double Right, double Bottom) box = Bounds(part);
        part.Properties[name] = value.Clone();

        // A property can change the pins (the LCD's interface): wires to pins that are gone go too.
        if (Definition(part) is { } definition)
        {
            Scene.Wires.RemoveAll(w => (w.From.Component == partId && definition.FindPin(w.From.Pin) is null) ||
                                       (w.To.Component == partId && definition.FindPin(w.To.Pin) is null));
        }

        // And the size (the LCD's rows): what ends there no longer ends where it did.
        if (Bounds(part) != box)
        {
            RerouteAround([partId], bothEndsToo: true);
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Puts another board in the board's place (same id, position and orientation). Wires to pins the
    /// new board lacks are removed; wires to pins with the same name stay. One undo step.
    /// </summary>
    public bool ChangeBoard(string boardType)
    {
        PartModel? board = Scene.Parts.FirstOrDefault(IsBoard);
        if (board is null || board.Type == boardType || _registry.Find(boardType) is not { Category: "board" } definition)
        {
            return false;
        }

        Checkpoint();
        Scene.Parts[Scene.Parts.IndexOf(board)] = new PartModel(board.Id, boardType) { X = board.X, Y = board.Y, Rotation = board.Rotation, Flip = board.Flip };
        Scene.Wires.RemoveAll(w => (w.From.Component == board.Id && definition.FindPin(w.From.Pin) is null) ||
                                   (w.To.Component == board.Id && definition.FindPin(w.To.Pin) is null));
        RerouteAround([board.Id]);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Renames a part and the wire ends that refer to it; ids are lowercase letters, digits and dashes.</summary>
    public bool RenamePart(string oldId, string newId)
    {
        if (oldId == newId || !IdPattern().IsMatch(newId) || Scene.FindPart(newId) is not null || Scene.FindPart(oldId) is not { } part)
        {
            return false;
        }

        Checkpoint();
        part.Id = newId;
        foreach (WireModel wire in Scene.Wires)
        {
            if (wire.From.Component == oldId)
            {
                wire.From = wire.From with { Component = newId };
            }

            if (wire.To.Component == oldId)
            {
                wire.To = wire.To with { Component = newId };
            }
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>The parts (without the board) and the wires between them, as clipboard text.</summary>
    public string? Copy(IReadOnlyCollection<string> ids)
    {
        var parts = Scene.Parts.Where(p => ids.Contains(p.Id) && !IsBoard(p)).ToList();
        if (parts.Count == 0)
        {
            return null;
        }

        var selected = parts.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var fragment = new SceneModel();
        fragment.Parts.AddRange(parts);
        fragment.Wires.AddRange(Scene.Wires.Where(w => selected.Contains(w.From.Component) && selected.Contains(w.To.Component)));
        var document = new Dictionary<string, object> { [ClipboardMarker] = fragment.ToDocument(_registry) };
        return JsonSerializer.Serialize(document, ProjectFile.JsonOptions);
    }

    /// <summary>Pastes clipboard text with its top-left corner at (x, y); returns the new part ids, or none if the text is not ArduSim parts.</summary>
    public IReadOnlyList<string> Paste(string text, double x, double y)
    {
        SceneDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<Dictionary<string, SceneDocument>>(text, ProjectFile.JsonOptions)?.GetValueOrDefault(ClipboardMarker);
        }
        catch (JsonException)
        {
            return [];
        }

        if (document is null || document.Components.Count == 0)
        {
            return [];
        }

        SceneModel fragment = SceneModel.FromDocument(document);
        fragment.Parts.RemoveAll(p => _registry.Find(p.Type) is null || IsBoard(p));
        if (fragment.Parts.Count == 0)
        {
            return [];
        }

        Checkpoint();
        double dx = Snap(x) - Snap(fragment.Parts.Min(p => p.X));
        double dy = Snap(y) - Snap(fragment.Parts.Min(p => p.Y));
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (PartModel part in fragment.Parts)
        {
            string id = NewPartId(part.Type);
            ids[part.Id] = id;
            part.Id = id;
            part.X += dx;
            part.Y += dy;
            Scene.Parts.Add(part);
        }

        foreach (WireModel wire in fragment.Wires.Where(w => ids.ContainsKey(w.From.Component) && ids.ContainsKey(w.To.Component)))
        {
            wire.Id = NewWireId();
            wire.From = wire.From with { Component = ids[wire.From.Component] };
            wire.To = wire.To with { Component = ids[wire.To.Component] };
            for (int i = 0; i < wire.Waypoints.Count; i++)
            {
                wire.Waypoints[i] = (wire.Waypoints[i].X + dx, wire.Waypoints[i].Y + dy);
            }

            Scene.Wires.Add(wire);
        }

        _router.Lay([.. Scene.Wires.Where(w => w.IsAuto && w.Waypoints.Count == 0 && ids.ContainsValue(w.From.Component))], thorough: true);
        RerouteAround([.. ids.Values]);
        Changed?.Invoke();
        return [.. ids.Values];
    }

    public IReadOnlyList<string> Duplicate(IReadOnlyCollection<string> ids)
    {
        if (Copy(ids) is not { } text)
        {
            return [];
        }

        var parts = Scene.Parts.Where(p => ids.Contains(p.Id) && !IsBoard(p)).ToList();
        return Paste(text, parts.Min(p => p.X) + (2 * Grid), parts.Min(p => p.Y) + (2 * Grid));
    }

    public (double X, double Y)? PinPosition(PinReference pin)
    {
        if (Scene.FindPart(pin.Component) is not { } part || Definition(part) is not { } definition || definition.FindPin(pin.Pin) is not { } pinDefinition)
        {
            return null;
        }

        return ComponentGeometry.PinPosition(definition, pinDefinition, part.X, part.Y, part.Rotation, part.Flip);
    }

    /// <summary>The part's box on the canvas after rotation.</summary>
    public (double Left, double Top, double Right, double Bottom) Bounds(PartModel part)
    {
        ArgumentNullException.ThrowIfNull(part);
        ComponentDefinition? definition = Definition(part);
        double w = definition?.Width ?? 40;
        double h = definition?.Height ?? 40;
        double cx = part.X + (w / 2);
        double cy = part.Y + (h / 2);
        (double hw, double hh) = part.Rotation is 90 or 270 ? (h / 2, w / 2) : (w / 2, h / 2);
        return (cx - hw, cy - hh, cx + hw, cy + hh);
    }

    /// <summary>The orthogonal polyline from pin through the corners to pin (horizontal leg first).</summary>
    public IReadOnlyList<(double X, double Y)> Route(WireModel wire)
    {
        ArgumentNullException.ThrowIfNull(wire);
        if (PinPosition(wire.From) is not { } from || PinPosition(wire.To) is not { } to)
        {
            return [];
        }

        return Route(from, wire.Waypoints, to);
    }

    public static IReadOnlyList<(double X, double Y)> Route((double X, double Y) from, IEnumerable<(double X, double Y)> corners, (double X, double Y) to, bool verticalFirst = false)
    {
        var anchors = new List<(double X, double Y)> { from };
        anchors.AddRange(corners);
        anchors.Add(to);
        var points = new List<(double X, double Y)> { anchors[0] };
        for (int i = 1; i < anchors.Count; i++)
        {
            (double X, double Y) a = anchors[i - 1];
            (double X, double Y) b = anchors[i];
            if (a.X != b.X && a.Y != b.Y)
            {
                points.Add(verticalFirst ? (a.X, b.Y) : (b.X, a.Y));
            }

            points.Add(b);
        }

        return points;
    }

    /// <summary>Wires grouped by the nets they form through shared pins.</summary>
    public IReadOnlyList<IReadOnlyList<WireModel>> WireNets()
    {
        var parent = Scene.Wires.ToDictionary(w => w, w => w);
        WireModel Find(WireModel w)
        {
            while (parent[w] != w)
            {
                w = parent[w] = parent[parent[w]];
            }

            return w;
        }

        foreach (IGrouping<PinReference, WireModel> shared in Scene.Wires.SelectMany(w => new[] { (w.From, w), (w.To, w) }).GroupBy(e => e.Item1, e => e.w))
        {
            WireModel first = shared.First();
            foreach (WireModel other in shared.Skip(1))
            {
                parent[Find(other)] = Find(first);
            }
        }

        return Scene.Wires.GroupBy(Find).Select(g => (IReadOnlyList<WireModel>)g.ToList()).ToList();
    }

    public static NetKind KindOf(IEnumerable<WireModel> net)
    {
        string[] pins = net.SelectMany(w => new[] { w.From.Pin, w.To.Pin }).ToArray();
        if (pins.Any(p => p.StartsWith("GND", StringComparison.Ordinal)))
        {
            return NetKind.Ground;
        }

        if (pins.Any(p => p is "5V" or "3V3" or "VIN" or "VCC"))
        {
            return NetKind.Power;
        }

        if (pins.Any(p => p is "SDA" or "A4"))
        {
            return NetKind.I2cData;
        }

        return pins.Any(p => p is "SCL" or "A5") ? NetKind.I2cClock : NetKind.Signal;
    }

    private void AlignToGrid(PartModel part)
    {
        if (Definition(part) is not { Pins.Count: > 0 } definition)
        {
            part.X = Snap(part.X);
            part.Y = Snap(part.Y);
            return;
        }

        (double x, double y) = ComponentGeometry.PinPosition(definition, definition.Pins[0], part.X, part.Y, part.Rotation, part.Flip);
        part.X += Snap(x) - x;
        part.Y += Snap(y) - y;
    }

    private (double Left, double Top, double Right, double Bottom) Union(List<PartModel> parts)
    {
        var boxes = parts.Select(Bounds).ToList();
        return (boxes.Min(b => b.Left), boxes.Min(b => b.Top), boxes.Max(b => b.Right), boxes.Max(b => b.Bottom));
    }

    private string NewPartId(string type)
    {
        string prefix = IdPrefixes.GetValueOrDefault(type, type);
        for (int n = 1; ; n++)
        {
            string id = prefix + n.ToString(CultureInfo.InvariantCulture);
            if (Scene.FindPart(id) is null)
            {
                return id;
            }
        }
    }

    private string NewWireId()
    {
        for (int n = Scene.Wires.Count + 1; ; n++)
        {
            string id = "w" + n.ToString(CultureInfo.InvariantCulture);
            if (Scene.FindWire(id) is null)
            {
                return id;
            }
        }
    }

    private void Swap(Stack<SceneDocument> from, Stack<SceneDocument> to)
    {
        if (from.Count == 0)
        {
            return;
        }

        to.Push(Scene.ToDocument(_registry, keepRoutes: true));
        Scene = SceneModel.FromDocument(from.Pop());
        LayOutWhatHasNoRoute();
        Changed?.Invoke();
    }

    private static void Trim(Stack<SceneDocument> stack)
    {
        if (stack.Count <= HistoryLimit)
        {
            return;
        }

        SceneDocument[] keep = stack.Take(HistoryLimit).Reverse().ToArray();
        stack.Clear();
        foreach (SceneDocument document in keep)
        {
            stack.Push(document);
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{0,31}$")]
    private static partial Regex IdPattern();
}
