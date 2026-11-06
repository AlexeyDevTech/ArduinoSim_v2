using ArduSim.Core.Components;
using ArduSim.Core.Projects;

namespace ArduSim.Core.Editing;

/// <summary>
/// Lays wires out (docs/UI.md §4.3, ADR-020): orthogonal, straight out of a pin's part, around part
/// bodies, and beside other wires rather than along them. A search over (cell, direction) with prices for
/// length, bends, crossings and what must not happen, so that it always finds something. Wires keep to the
/// 0.1" grid and take the lines halfway between only where it gets crowded: between two rows of pins a
/// tenth of an inch apart, that is what fits a bundle. Wires of one net may share a track: that reads as
/// the connection it is. Wires between two rows of pins that face each other go first, as a bundle (<see cref="WireChannel"/>).
/// </summary>
internal sealed class WireRouter(SceneEditor editor)
{
    private const int Cell = 5;
    private const int Margin = 24;
    private const int StepPrice = 5;
    private const int HalfwayPrice = 2;
    private const int BendPrice = 24;
    private const int BodyPrice = 300;
    private const int OverlapPrice = 150;
    private const int CrossPrice = 30;
    private const int TouchPrice = 60;
    private const int SearchLimit = 300_000;
    private const short Mixed = -1;

    private static readonly (int X, int Y)[] Directions = [(1, 0), (0, 1), (-1, 0), (0, -1)];

    private Dictionary<WireModel, short> _nets = [];
    private int _left;
    private int _top;
    private int _columns;
    private int _rows;
    private bool[] _body = [];
    private short[] _across = [];
    private short[] _down = [];
    private short[] _through = [];
    private int[] _price = [];
    private int[] _cameFrom = [];
    private int[] _seen = [];
    private int _generation;
    private int _haste = 10;

    /// <summary>Gives these wires routes; the others stay as they are and are kept clear of.</summary>
    /// <param name="wires">The wires to route.</param>
    /// <param name="thorough">Also take again the ones that came out crossing or overlapping: for a whole layout, not for a drag.</param>
    public void Lay(IReadOnlyCollection<WireModel> wires, bool thorough)
    {
        if (wires.Count == 0)
        {
            return;
        }

        foreach (WireModel wire in wires)
        {
            wire.Waypoints.Clear();
        }

        // During a drag an answer now beats the very best one: the search leans towards the goal.
        _haste = thorough ? 10 : 15;
        Prepare(wires);
        HashSet<WireModel> bundled = LayBundles(wires);
        List<WireModel> order = [.. wires.Where(w => !bundled.Contains(w)).OrderBy(Span).ThenBy(w => w.Id, StringComparer.Ordinal)];
        foreach (WireModel wire in order)
        {
            LayOne(wire);
        }

        for (int pass = 0; thorough && pass < 3; pass++)
        {
            bool better = false;
            foreach (WireModel wire in order)
            {
                better |= Improve(wire);
            }

            if (!better)
            {
                break;
            }
        }
    }

    /// <summary>The route a wire between these pins would take now, corners only.</summary>
    public IReadOnlyList<(double X, double Y)> Preview(PinReference from, PinReference to)
    {
        var wire = new WireModel("~preview", from, to);
        _haste = 10;
        Prepare([]);

        // It joins the net of whatever is on its pins already.
        short net = _nets.FirstOrDefault(n => n.Key.From == from || n.Key.To == from || n.Key.From == to || n.Key.To == to).Value;
        _nets[wire] = net != 0 ? net : (short)(_nets.Count + 1);
        return Search(wire) ?? [];
    }

    /// <summary>Whether a wire leaves this pin up or down rather than sideways.</summary>
    public bool LeavesVertically(PinReference pin) => ExitOf(pin) is { Direction: 1 or 3 };

    private double Span(WireModel wire) =>
        editor.PinPosition(wire.From) is { } a && editor.PinPosition(wire.To) is { } b ? Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) : 0;

    private void Prepare(IReadOnlyCollection<WireModel> free)
    {
        _nets = [];
        short net = 0;
        foreach (IReadOnlyList<WireModel> group in editor.WireNets())
        {
            net++;
            foreach (WireModel wire in group)
            {
                _nets[wire] = net;
            }
        }

        MakeRegion();
        foreach (PartModel part in editor.Scene.Parts)
        {
            MarkBody(editor.Bounds(part));
        }

        Occupy(free);
    }

    private void MakeRegion()
    {
        double left = double.MaxValue;
        double top = double.MaxValue;
        double right = double.MinValue;
        double bottom = double.MinValue;
        void Include(double x, double y)
        {
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }

        foreach (PartModel part in editor.Scene.Parts)
        {
            (double l, double t, double r, double b) = editor.Bounds(part);
            Include(l, t);
            Include(r, b);
            foreach (PinDefinition pin in editor.Definition(part)?.Pins ?? [])
            {
                if (editor.PinPosition(new PinReference(part.Id, pin.Name)) is { } at)
                {
                    Include(at.X, at.Y);
                }
            }
        }

        foreach (WireModel wire in editor.Scene.Wires)
        {
            foreach ((double x, double y) in wire.Waypoints)
            {
                Include(x, y);
            }
        }

        if (left > right)
        {
            Include(0, 0);
        }

        _left = ToCell(left) - Margin;
        _top = ToCell(top) - Margin;
        _columns = ToCell(right) + Margin - _left + 1;
        _rows = ToCell(bottom) + Margin - _top + 1;
        int cells = _columns * _rows;
        if (_body.Length < cells)
        {
            _body = new bool[cells];
            _across = new short[cells];
            _down = new short[cells];
            _through = new short[cells];
            _price = new int[cells * 4];
            _cameFrom = new int[cells * 4];
            _seen = new int[cells * 4];
            _generation = 0;
        }
        else
        {
            Array.Clear(_body, 0, cells);
        }
    }

    private static int ToCell(double world) => (int)Math.Round(world / Cell);

    private int Index(int x, int y) => (y * _columns) + x;

    // A part keeps the half cell around it too: a wire one cell from its edge is clear of it.
    private void MarkBody((double Left, double Top, double Right, double Bottom) box)
    {
        const double Clear = 5;
        int fromX = Math.Max(0, (int)Math.Ceiling((box.Left - Clear) / Cell) - _left);
        int toX = Math.Min(_columns - 1, (int)Math.Floor((box.Right + Clear) / Cell) - _left);
        int fromY = Math.Max(0, (int)Math.Ceiling((box.Top - Clear) / Cell) - _top);
        int toY = Math.Min(_rows - 1, (int)Math.Floor((box.Bottom + Clear) / Cell) - _top);
        for (int y = fromY; y <= toY; y++)
        {
            Array.Fill(_body, true, Index(fromX, y), Math.Max(0, toX - fromX + 1));
        }
    }

    // What is taken: every other wire's route and, so that nothing runs along them, the ways out of the pins still to be laid.
    private void Occupy(IReadOnlyCollection<WireModel> free)
    {
        int cells = _columns * _rows;
        Array.Clear(_across, 0, cells);
        Array.Clear(_down, 0, cells);
        Array.Clear(_through, 0, cells);
        foreach (WireModel wire in editor.Scene.Wires)
        {
            if (free.Contains(wire))
            {
                if (Exits(wire) is { } exits)
                {
                    Mark([exits.From.Pin, exits.From.End], _nets[wire]);
                    Mark([exits.To.Pin, exits.To.End], _nets[wire]);
                }
            }
            else
            {
                Mark(editor.Route(wire), _nets.GetValueOrDefault(wire));
            }
        }
    }

    private void Mark(IReadOnlyList<(double X, double Y)> route, short net)
    {
        for (int i = 0; i + 1 < route.Count; i++)
        {
            int x = ToCell(route[i].X) - _left;
            int y = ToCell(route[i].Y) - _top;
            int endX = ToCell(route[i + 1].X) - _left;
            int endY = ToCell(route[i + 1].Y) - _top;
            Put(_through, x, y, net);
            while (x != endX || y != endY)
            {
                int stepX = Math.Sign(endX - x);
                int stepY = stepX != 0 ? 0 : Math.Sign(endY - y);
                if (stepX != 0)
                {
                    Put(_across, Math.Min(x, x + stepX), y, net);
                }
                else
                {
                    Put(_down, x, Math.Min(y, y + stepY), net);
                }

                x += stepX;
                y += stepY;
                Put(_through, x, y, net);
            }
        }
    }

    private void Put(short[] layer, int x, int y, short net)
    {
        if (x >= 0 && y >= 0 && x < _columns && y < _rows)
        {
            int index = Index(x, y);
            layer[index] = layer[index] == 0 || layer[index] == net ? net : Mixed;
        }
    }

    private void LayOne(WireModel wire)
    {
        wire.Waypoints.Clear();
        wire.Waypoints.AddRange(Search(wire) ?? []);
        Mark(editor.Route(wire), _nets.GetValueOrDefault(wire));
    }

    // Takes a wire again with everything else in place; a better route replaces its first one.
    private bool Improve(WireModel wire)
    {
        short net = _nets.GetValueOrDefault(wire);
        Occupy([wire]);
        int was = Trouble(editor.Route(wire), net);
        bool better = false;
        if (was > 0)
        {
            List<(double X, double Y)> before = [.. wire.Waypoints];
            wire.Waypoints.Clear();
            wire.Waypoints.AddRange(Search(wire) ?? []);
            better = Trouble(editor.Route(wire), net) < was;
            if (!better)
            {
                wire.Waypoints.Clear();
                wire.Waypoints.AddRange(before);
            }
        }

        return better;
    }

    // Crossings and overlaps of a route with what is marked now, in the search's own prices.
    private int Trouble(IReadOnlyList<(double X, double Y)> route, short net)
    {
        int trouble = 0;
        for (int i = 0; i + 1 < route.Count; i++)
        {
            int x = ToCell(route[i].X) - _left;
            int y = ToCell(route[i].Y) - _top;
            int endX = ToCell(route[i + 1].X) - _left;
            int endY = ToCell(route[i + 1].Y) - _top;
            while (x != endX || y != endY)
            {
                int stepX = Math.Sign(endX - x);
                int stepY = stepX != 0 ? 0 : Math.Sign(endY - y);
                int nextX = x + stepX;
                int nextY = y + stepY;
                if (nextX < 0 || nextY < 0 || nextX >= _columns || nextY >= _rows)
                {
                    return trouble;
                }

                short edge = stepX != 0 ? _across[Index(Math.Min(x, nextX), y)] : _down[Index(x, Math.Min(y, nextY))];
                trouble += Foreign(edge, net) ? OverlapPrice : Foreign(_through[Index(nextX, nextY)], net) ? CrossPrice : 0;
                x = nextX;
                y = nextY;
            }
        }

        return trouble;
    }

    private static bool Foreign(short occupant, short net) => occupant != 0 && occupant != net;

    // ---- ways out of a pin --------------------------------------------------------------------------

    private readonly record struct Exit((double X, double Y) Pin, (double X, double Y) End, int Direction);

    private (Exit From, Exit To)? Exits(WireModel wire) =>
        ExitOf(wire.From) is { } from && ExitOf(wire.To) is { } to ? (from, to) : null;

    // Straight out through the part's nearest edge, to a grid line clear of it.
    private Exit? ExitOf(PinReference pin)
    {
        if (editor.PinPosition(pin) is not { } at || editor.Scene.FindPart(pin.Component) is not { } part)
        {
            return null;
        }

        (double left, double top, double right, double bottom) = editor.Bounds(part);
        (double Distance, int Direction)[] edges = [(at.Y - top, 3), (bottom - at.Y, 1), (at.X - left, 2), (right - at.X, 0)];
        int direction = edges.MinBy(e => e.Distance).Direction;
        double endX = at.X;
        double endY = at.Y;
        switch (direction)
        {
            case 0:
                endX = Clear(Math.Max(at.X, right), 1);
                break;
            case 1:
                endY = Clear(Math.Max(at.Y, bottom), 1);
                break;
            case 2:
                endX = Clear(Math.Min(at.X, left), -1);
                break;
            default:
                endY = Clear(Math.Min(at.Y, top), -1);
                break;
        }

        return new Exit(at, (endX, endY), direction);
    }

    private static double Clear(double edge, int outward) =>
        (outward > 0 ? Math.Ceiling((edge + SceneEditor.Grid) / SceneEditor.Grid) : Math.Floor((edge - SceneEditor.Grid) / SceneEditor.Grid)) * SceneEditor.Grid;

    // ---- bundles -------------------------------------------------------------------------------------

    private readonly record struct BundleKey(string NearPart, string FarPart, int Direction, double NearRow, double FarRow);

    private readonly record struct BundleWire(WireModel Wire, bool Reversed, double Near, double Far);

    private HashSet<WireModel> LayBundles(IReadOnlyCollection<WireModel> free)
    {
        var groups = new Dictionary<BundleKey, List<BundleWire>>();
        foreach (WireModel wire in free.OrderBy(w => w.Id, StringComparer.Ordinal))
        {
            if (wire.From.Component == wire.To.Component || Exits(wire) is not { } exits)
            {
                continue;
            }

            // Either way round: it is the two parts that make a bundle.
            bool reversed = string.CompareOrdinal(wire.From.Component, wire.To.Component) > 0;
            (Exit near, Exit far) = reversed ? (exits.To, exits.From) : (exits.From, exits.To);
            if (far.Direction != (near.Direction + 2) % 4)
            {
                continue;
            }

            bool vertical = near.Direction % 2 == 1;
            double nearRow = vertical ? near.Pin.Y : near.Pin.X;
            double farRow = vertical ? far.Pin.Y : far.Pin.X;
            if ((farRow - nearRow) * (near.Direction is 0 or 1 ? 1 : -1) <= 0)
            {
                continue;
            }

            var key = new BundleKey(reversed ? wire.To.Component : wire.From.Component, reversed ? wire.From.Component : wire.To.Component, near.Direction, nearRow, farRow);
            if (!groups.TryGetValue(key, out List<BundleWire>? group))
            {
                groups[key] = group = [];
            }

            group.Add(new BundleWire(wire, reversed, vertical ? near.Pin.X : near.Pin.Y, vertical ? far.Pin.X : far.Pin.Y));
        }

        var laid = new HashSet<WireModel>();
        foreach ((BundleKey key, List<BundleWire> group) in groups)
        {
            // A pin with two wires is a net of its own making: the search shares its track.
            List<BundleWire> wires = [.. group.Where(w => group.Count(o => o.Near == w.Near) == 1 && group.Count(o => o.Far == w.Far) == 1)];
            if (wires.Count >= 2)
            {
                LayBundle(key, wires, free, laid);
            }
        }

        return laid;
    }

    private void LayBundle(BundleKey key, List<BundleWire> wires, IReadOnlyCollection<WireModel> free, HashSet<WireModel> laid)
    {
        if (editor.Scene.FindPart(key.NearPart) is not { } nearPart || editor.Scene.FindPart(key.FarPart) is not { } farPart)
        {
            return;
        }

        (double Left, double Top, double Right, double Bottom) near = editor.Bounds(nearPart);
        (double Left, double Top, double Right, double Bottom) far = editor.Bounds(farPart);
        bool vertical = key.Direction % 2 == 1;
        (double first, double last) = key.Direction switch
        {
            0 => (near.Right + Cell, far.Left - Cell),
            1 => (near.Bottom + Cell, far.Top - Cell),
            2 => (near.Left - Cell, far.Right + Cell),
            _ => (near.Top - Cell, far.Bottom + Cell),
        };
        double low = wires.Min(w => Math.Min(w.Near, w.Far)) - Cell;
        double high = wires.Max(w => Math.Max(w.Near, w.Far)) + Cell;
        (double Left, double Top, double Right, double Bottom) channel = vertical
            ? (low, Math.Min(first, last), high, Math.Max(first, last))
            : (Math.Min(first, last), low, Math.Max(first, last), high);
        bool taken = editor.Scene.Parts.Any(p => p != nearPart && p != farPart && Meets(editor.Bounds(p), channel))
            || editor.Scene.Wires.Any(w => (!free.Contains(w) || laid.Contains(w)) && Runs(editor.Route(w), channel));
        if (taken || (last - first) * (key.Direction is 0 or 1 ? 1 : -1) < 0
            || WireChannel.Lay([.. wires.Select(w => new WireChannel.Ends(w.Near, w.Far))], first, last) is not { } corners)
        {
            return;
        }

        for (int i = 0; i < wires.Count; i++)
        {
            WireModel wire = wires[i].Wire;
            IEnumerable<(double X, double Y)> route = corners[i].Select(c => vertical ? (c.Along, c.Across) : (c.Across, c.Along));
            wire.Waypoints.Clear();
            wire.Waypoints.AddRange(wires[i].Reversed ? route.Reverse() : route);
            Mark(editor.Route(wire), _nets.GetValueOrDefault(wire));
            laid.Add(wire);
        }
    }

    private static bool Meets((double Left, double Top, double Right, double Bottom) a, (double Left, double Top, double Right, double Bottom) b) =>
        a.Left <= b.Right && b.Left <= a.Right && a.Top <= b.Bottom && b.Top <= a.Bottom;

    private static bool Runs(IReadOnlyList<(double X, double Y)> route, (double Left, double Top, double Right, double Bottom) box)
    {
        for (int i = 0; i + 1 < route.Count; i++)
        {
            if (Meets((Math.Min(route[i].X, route[i + 1].X), Math.Min(route[i].Y, route[i + 1].Y), Math.Max(route[i].X, route[i + 1].X), Math.Max(route[i].Y, route[i + 1].Y)), box))
            {
                return true;
            }
        }

        return false;
    }

    // ---- the search --------------------------------------------------------------------------------

    private List<(double X, double Y)>? Search(WireModel wire)
    {
        if (Exits(wire) is not { } exits)
        {
            return null;
        }

        (Exit from, Exit to) = exits;
        if (from.Pin == to.Pin)
        {
            return [];
        }

        short net = _nets.GetValueOrDefault(wire);
        int startX = ToCell(from.End.X) - _left;
        int startY = ToCell(from.End.Y) - _top;
        int goalX = ToCell(to.End.X) - _left;
        int goalY = ToCell(to.End.Y) - _top;
        if (startX < 0 || startY < 0 || goalX < 0 || goalY < 0 || startX >= _columns || startY >= _rows || goalX >= _columns || goalY >= _rows)
        {
            return Corners(from.Pin, [from.End, to.End], to.Pin);
        }

        List<(double X, double Y)>? path = Walk(startX, startY, from.Direction, goalX, goalY, to.Direction, net);
        return Corners(from.Pin, path ?? [from.End, to.End], to.Pin);
    }

    private List<(double X, double Y)>? Walk(int startX, int startY, int startDirection, int goalX, int goalY, int goalExit, short net)
    {
        int goal = Index(goalX, goalY);
        int start = Index(startX, startY);
        if (start == goal)
        {
            return [(World(startX, _left), World(startY, _top))];
        }

        int generation = ++_generation;
        var open = new PriorityQueue<int, int>();
        int first = (start * 4) + startDirection;
        _price[first] = 0;
        _seen[first] = generation;
        _cameFrom[first] = -1;
        open.Enqueue(first, Distance(startX, startY, goalX, goalY));
        int arriveStraight = (goalExit + 2) % 4;
        for (int expanded = 0; expanded < SearchLimit && open.TryDequeue(out int state, out int priority); expanded++)
        {
            int cell = state / 4;
            int direction = state % 4;
            int x = cell % _columns;
            int y = cell / _columns;
            if (cell == goal)
            {
                return Path(state);
            }

            if (_price[state] + Distance(x, y, goalX, goalY) < priority)
            {
                continue;
            }

            foreach (int next in (ReadOnlySpan<int>)[direction, (direction + 1) % 4, (direction + 3) % 4])
            {
                int nextX = x + Directions[next].X;
                int nextY = y + Directions[next].Y;
                if (nextX < 0 || nextY < 0 || nextX >= _columns || nextY >= _rows || (nextX == goalX && nextY == goalY && next == goalExit))
                {
                    continue;
                }

                int nextCell = Index(nextX, nextY);
                int price = _price[state] + StepPrice + (OnGridLine(next % 2 == 0 ? y + _top : x + _left) ? 0 : HalfwayPrice);
                if (next != direction)
                {
                    price += BendPrice + (Foreign(_through[cell], net) ? TouchPrice : 0);
                }

                if (_body[nextCell])
                {
                    price += BodyPrice;
                }

                short edge = next % 2 == 0 ? _across[Index(Math.Min(x, nextX), y)] : _down[Index(x, Math.Min(y, nextY))];
                price += Foreign(edge, net) ? OverlapPrice : Foreign(_through[nextCell], net) ? CrossPrice : 0;
                if (nextCell == goal && next != arriveStraight)
                {
                    price += BendPrice;
                }

                int nextState = (nextCell * 4) + next;
                if (_seen[nextState] != generation || price < _price[nextState])
                {
                    _seen[nextState] = generation;
                    _price[nextState] = price;
                    _cameFrom[nextState] = state;
                    open.Enqueue(nextState, price + Distance(nextX, nextY, goalX, goalY));
                }
            }
        }

        return null;
    }

    private static bool OnGridLine(int cell) => cell % 2 == 0;

    private int Distance(int x, int y, int toX, int toY) => StepPrice * (Math.Abs(x - toX) + Math.Abs(y - toY)) * _haste / 10;

    private static double World(int cell, int origin) => (cell + origin) * (double)Cell;

    private List<(double X, double Y)> Path(int state)
    {
        var path = new List<(double X, double Y)>();
        for (int at = state; at >= 0; at = _cameFrom[at])
        {
            int cell = at / 4;
            path.Add((World(cell % _columns, _left), World(cell / _columns, _top)));
        }

        path.Reverse();
        return path;
    }

    // Only the corners stay: what a straight run passes through says nothing.
    private static List<(double X, double Y)> Corners((double X, double Y) from, List<(double X, double Y)> path, (double X, double Y) to)
    {
        var points = new List<(double X, double Y)> { from };
        foreach ((double X, double Y) point in path.Append(to))
        {
            // A pin off the grid, or the fallback's two ends, joins by a right angle.
            (double X, double Y) last = points[^1];
            if (last.X != point.X && last.Y != point.Y)
            {
                points.Add((point.X, last.Y));
            }

            points.Add(point);
        }

        var corners = new List<(double X, double Y)>();
        for (int i = 1; i + 1 < points.Count; i++)
        {
            (double X, double Y) previous = corners.Count > 0 ? corners[^1] : points[0];
            (double X, double Y) point = points[i];
            (double X, double Y) next = points[i + 1];
            bool straight = (previous.X == point.X && point.X == next.X) || (previous.Y == point.Y && point.Y == next.Y);
            if (point != previous && point != next && !straight)
            {
                corners.Add(point);
            }
        }

        return corners;
    }
}
