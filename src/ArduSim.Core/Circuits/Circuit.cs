using System.Globalization;
using ArduSim.Core.Diagnostics;

namespace ArduSim.Core.Circuits;

/// <summary>A component pin, e.g. <c>board.D13</c> or <c>led1.A</c>.</summary>
public readonly record struct PinRef(string Component, string Pin)
{
    public override string ToString() => Component + "." + Pin;
}

public enum DriveStrength
{
    None,

    /// <summary>A source behind a resistance: pull-ups, a potentiometer wiper.</summary>
    Weak,

    /// <summary>An output or a supply rail.</summary>
    Strong,
}

/// <summary>What one source contributes to a net (ADR-006). PWM and tone are state, not edges.</summary>
/// <param name="Duty">Fraction of time at <paramref name="Voltage"/> (1 = steady).</param>
public readonly record struct NetDrive(DriveStrength Strength, double Voltage, double Resistance, double Duty, int ToneHz)
{
    public static NetDrive None => default;

    public static NetDrive Strong(double voltage, double duty = 1, int toneHz = 0) => new(DriveStrength.Strong, voltage, 0, duty, toneHz);

    public static NetDrive Weak(double voltage, double resistance) => new(DriveStrength.Weak, voltage, resistance, 1, 0);
}

public enum NetCondition
{
    /// <summary>Nothing drives or pulls the net.</summary>
    Floating,

    /// <summary>One or more agreeing strong drivers.</summary>
    Driven,

    /// <summary>Only weak sources (pull-ups, resistors to a driven net, a pot wiper).</summary>
    Pulled,

    /// <summary>Strong drivers disagree: a short circuit.</summary>
    Conflict,
}

/// <param name="SourceResistance">Ohms between the net and its sources (0 for a strong drive).</param>
public readonly record struct NetState(NetCondition Condition, double Voltage, double SourceResistance, double Duty, int ToneHz)
{
    public bool HasVoltage => Condition is NetCondition.Driven or NetCondition.Pulled;

    /// <summary>Voltage averaged over the PWM period.</summary>
    public double AverageVoltage => Voltage * Duty;
}

/// <summary>A set of pins joined by wires (and closed switches).</summary>
public sealed class Net
{
    internal Net(int index, IReadOnlyList<PinRef> pins)
    {
        Index = index;
        Pins = pins;
    }

    public int Index { get; }

    public IReadOnlyList<PinRef> Pins { get; }

    public NetState State { get; internal set; }

    public string Name => string.Join(", ", Pins.Take(3)) + (Pins.Count > 3 ? ", ..." : "");

    internal List<Driver> Drivers { get; } = [];

    internal List<ResistorLink> Resistors { get; } = [];

    internal int Group { get; set; }
}

/// <summary>A component's contribution to the net of one of its pins.</summary>
public sealed class Driver
{
    private readonly Circuit _circuit;

    internal Driver(Circuit circuit, PinRef pin, string owner)
    {
        _circuit = circuit;
        Pin = pin;
        Owner = owner;
    }

    public PinRef Pin { get; }

    /// <summary>Component id, used in warnings.</summary>
    public string Owner { get; }

    public NetDrive Drive { get; private set; }

    public void Set(NetDrive drive)
    {
        if (drive == Drive)
        {
            return;
        }

        Drive = drive;
        _circuit.Invalidate(this);
    }
}

/// <summary>A connection that opens and closes at run time (pushbutton, slide switch).</summary>
public sealed class CircuitSwitch
{
    private readonly Circuit _circuit;

    internal CircuitSwitch(Circuit circuit, PinRef a, PinRef b, bool closed)
    {
        _circuit = circuit;
        A = a;
        B = b;
        Closed = closed;
    }

    public PinRef A { get; }

    public PinRef B { get; }

    public bool Closed { get; private set; }

    public void Set(bool closed)
    {
        if (closed == Closed)
        {
            return;
        }

        Closed = closed;
        _circuit.Repartition();
    }
}

internal sealed record ResistorLink(PinRef A, PinRef B, double Ohms, string Owner);

/// <summary>
/// The scene's electrical model (ADR-006): pins joined into nets by wires and closed switches,
/// resistors coupling nets, and drivers on pins. Resolution follows simple rules instead of
/// circuit simulation: strong drivers win (and must agree, or it is a short circuit); otherwise
/// weak sources and sources behind resistors combine like a Thevenin equivalent; with no source
/// a net floats. Only the resistor-coupled group around a change is re-resolved, and pin watchers
/// hear about every net whose state changed.
/// </summary>
public sealed class Circuit
{
    private const double AgreementTolerance = 0.5;
    private const double StrongSourceResistance = 25;
    private const int SettleLimit = 10_000;

    private readonly SimulationLog _log;
    private readonly Dictionary<PinRef, int> _nodes = [];
    private readonly List<PinRef> _nodePins = [];
    private readonly List<List<Action>> _watchers = [];
    private readonly List<(int A, int B)> _wires = [];
    private readonly List<CircuitSwitch> _switches = [];
    private readonly List<Driver> _drivers = [];
    private readonly List<ResistorLink> _resistors = [];
    private readonly Queue<int> _dirty = new();
    private readonly HashSet<int> _dirtySet = [];
    private List<Net> _nets = [];
    private int[] _netOfNode = [];
    private List<List<Net>> _groups = [];
    private bool _built;
    private bool _resolving;

    public Circuit(SimulationLog log)
    {
        _log = log;
    }

    public IReadOnlyList<Net> Nets => _nets;

    public bool Contains(PinRef pin) => _nodes.ContainsKey(pin);

    public void AddPin(PinRef pin) => Node(pin);

    public void Connect(PinRef a, PinRef b)
    {
        RequireUnbuilt();
        _wires.Add((Node(a), Node(b)));
    }

    public CircuitSwitch AddSwitch(PinRef a, PinRef b, bool closed)
    {
        RequireUnbuilt();
        Node(a);
        Node(b);
        var connection = new CircuitSwitch(this, a, b, closed);
        _switches.Add(connection);
        return connection;
    }

    public Driver AddDriver(PinRef pin, string owner)
    {
        RequireUnbuilt();
        Node(pin);
        var driver = new Driver(this, pin, owner);
        _drivers.Add(driver);
        return driver;
    }

    public void AddResistor(PinRef a, PinRef b, double ohms, string owner)
    {
        RequireUnbuilt();
        Node(a);
        Node(b);
        _resistors.Add(new ResistorLink(a, b, Math.Max(ohms, 0.001), owner));
    }

    /// <summary>Calls <paramref name="onChange"/> whenever the state of <paramref name="pin"/>'s net changes.</summary>
    public void Watch(PinRef pin, Action onChange) => _watchers[Node(pin)].Add(onChange);

    public void Build()
    {
        _built = true;
        Repartition();
    }

    public Net NetOf(PinRef pin) =>
        _nodes.TryGetValue(pin, out int node) ? _nets[_netOfNode[node]] : throw new KeyNotFoundException($"Pin {pin} is not part of the circuit.");

    public NetState StateOf(PinRef pin) => NetOf(pin).State;

    internal void Invalidate(Driver driver)
    {
        if (!_built)
        {
            return;
        }

        MarkDirty(_nets[_netOfNode[_nodes[driver.Pin]]].Group);
        Settle();
    }

    internal void Repartition()
    {
        if (!_built)
        {
            return;
        }

        NetState[] before = _netOfNode.Length == 0 ? [] : Enumerable.Range(0, _nodePins.Count).Select(n => _nets[_netOfNode[n]].State).ToArray();
        Partition();
        foreach (List<Net> group in _groups)
        {
            Resolve(group, notify: false);
        }

        for (int node = 0; node < _nodePins.Count; node++)
        {
            if (before.Length == 0 || node >= before.Length || before[node] != _nets[_netOfNode[node]].State)
            {
                Notify(node);
            }
        }

        Settle();
    }

    private int Node(PinRef pin)
    {
        if (!_nodes.TryGetValue(pin, out int node))
        {
            RequireUnbuilt();
            node = _nodePins.Count;
            _nodes[pin] = node;
            _nodePins.Add(pin);
            _watchers.Add([]);
        }

        return node;
    }

    private void RequireUnbuilt()
    {
        if (_built)
        {
            throw new InvalidOperationException("The circuit is built; its topology can no longer change.");
        }
    }

    private void Partition()
    {
        int count = _nodePins.Count;
        int[] parent = Enumerable.Range(0, count).ToArray();
        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }

            return x;
        }

        void Union(int a, int b) => parent[Find(a)] = Find(b);

        foreach ((int a, int b) in _wires)
        {
            Union(a, b);
        }

        foreach (CircuitSwitch s in _switches.Where(s => s.Closed))
        {
            Union(_nodes[s.A], _nodes[s.B]);
        }

        var netOfRoot = new Dictionary<int, int>();
        var members = new List<List<PinRef>>();
        _netOfNode = new int[count];
        for (int node = 0; node < count; node++)
        {
            int root = Find(node);
            if (!netOfRoot.TryGetValue(root, out int net))
            {
                net = members.Count;
                netOfRoot[root] = net;
                members.Add([]);
            }

            _netOfNode[node] = net;
            members[net].Add(_nodePins[node]);
        }

        _nets = members.Select((pins, i) => new Net(i, pins)).ToList();
        foreach (Driver driver in _drivers)
        {
            _nets[_netOfNode[_nodes[driver.Pin]]].Drivers.Add(driver);
        }

        int[] groupParent = Enumerable.Range(0, _nets.Count).ToArray();
        int FindGroup(int x)
        {
            while (groupParent[x] != x)
            {
                x = groupParent[x] = groupParent[groupParent[x]];
            }

            return x;
        }

        foreach (ResistorLink r in _resistors)
        {
            Net a = _nets[_netOfNode[_nodes[r.A]]];
            Net b = _nets[_netOfNode[_nodes[r.B]]];
            if (a != b)
            {
                a.Resistors.Add(r);
                b.Resistors.Add(r);
                groupParent[FindGroup(a.Index)] = FindGroup(b.Index);
            }
        }

        var groupOfRoot = new Dictionary<int, int>();
        _groups = [];
        foreach (Net net in _nets)
        {
            int root = FindGroup(net.Index);
            if (!groupOfRoot.TryGetValue(root, out int group))
            {
                group = _groups.Count;
                groupOfRoot[root] = group;
                _groups.Add([]);
            }

            net.Group = group;
            _groups[group].Add(net);
        }

        _dirty.Clear();
        _dirtySet.Clear();
    }

    private void MarkDirty(int group)
    {
        if (_dirtySet.Add(group))
        {
            _dirty.Enqueue(group);
        }
    }

    // Watchers may change drivers (a pot wiper follows its end nets); those changes are queued and
    // resolved here until the circuit is stable.
    private void Settle()
    {
        if (_resolving)
        {
            return;
        }

        _resolving = true;
        try
        {
            int rounds = 0;
            while (_dirty.TryDequeue(out int group))
            {
                _dirtySet.Remove(group);
                if (++rounds > SettleLimit)
                {
                    _log.ReportOnce("circuit-settle", SimSeverity.Error, "circuit-unstable", "The circuit does not settle (components keep changing each other); resolution was stopped.");
                    _dirty.Clear();
                    _dirtySet.Clear();
                    break;
                }

                if (group < _groups.Count)
                {
                    Resolve(_groups[group], notify: true);
                }
            }
        }
        finally
        {
            _resolving = false;
        }
    }

    private void Resolve(List<Net> group, bool notify)
    {
        var states = new NetState[group.Count];
        for (int i = 0; i < group.Count; i++)
        {
            states[i] = ResolveNet(group[i]);
        }

        for (int i = 0; i < group.Count; i++)
        {
            Net net = group[i];
            if (net.State == states[i])
            {
                continue;
            }

            net.State = states[i];
            if (notify)
            {
                foreach (PinRef pin in net.Pins)
                {
                    Notify(_nodes[pin]);
                }
            }
        }
    }

    private void Notify(int node)
    {
        foreach (Action watcher in _watchers[node])
        {
            watcher();
        }
    }

    private readonly record struct Source(double Voltage, double Resistance, double Duty, int ToneHz);

    private NetState ResolveNet(Net net)
    {
        List<Driver> strong = net.Drivers.Where(d => d.Drive.Strength == DriveStrength.Strong).ToList();
        if (strong.Count > 0)
        {
            NetDrive first = strong[0].Drive;
            Driver? disagreeing = strong.FirstOrDefault(d => Math.Abs(d.Drive.Voltage - first.Voltage) > AgreementTolerance);
            if (disagreeing is not null)
            {
                ReportShort(net, strong[0], disagreeing);
                return new NetState(NetCondition.Conflict, (first.Voltage + disagreeing.Drive.Voltage) / 2, 0, 1, 0);
            }

            Driver steady = strong.FirstOrDefault(d => d.Drive.Duty >= 1 && d.Drive.ToneHz == 0) ?? strong[0];
            return new NetState(NetCondition.Driven, steady.Drive.Voltage, 0, steady.Drive.Duty, steady.Drive.ToneHz);
        }

        List<Source> sources = OwnSources(net, includeStrong: false).ToList();
        sources.AddRange(SourcesThroughResistors(net));
        if (sources.Count == 0)
        {
            return default;
        }

        double conductance = sources.Sum(s => 1 / s.Resistance);
        double voltage = sources.Sum(s => s.Voltage / s.Resistance) / conductance;
        Source dominant = sources.MinBy(s => s.Resistance);
        return new NetState(NetCondition.Pulled, voltage, 1 / conductance, dominant.Duty, dominant.ToneHz);
    }

    private static IEnumerable<Source> OwnSources(Net net, bool includeStrong) =>
        net.Drivers
            .Select(d => d.Drive)
            .Where(d => d.Strength == DriveStrength.Weak || (includeStrong && d.Strength == DriveStrength.Strong))
            .Select(d => new Source(d.Voltage, d.Strength == DriveStrength.Strong ? StrongSourceResistance : Math.Max(d.Resistance, 1), d.Duty, d.ToneHz));

    // Shortest resistor path from this net to every other net with sources; a strongly driven net
    // is a source and stops the search (it clamps whatever lies behind it).
    private IEnumerable<Source> SourcesThroughResistors(Net start)
    {
        if (start.Resistors.Count == 0)
        {
            yield break;
        }

        var distance = new Dictionary<Net, double> { [start] = 0 };
        var queue = new PriorityQueue<Net, double>();
        queue.Enqueue(start, 0);
        while (queue.TryDequeue(out Net? net, out double path))
        {
            if (path > distance[net])
            {
                continue;
            }

            if (net != start)
            {
                bool clamped = net.Drivers.Any(d => d.Drive.Strength == DriveStrength.Strong);
                foreach (Source s in OwnSources(net, includeStrong: true))
                {
                    yield return s with { Resistance = s.Resistance + path };
                }

                if (clamped)
                {
                    continue;
                }
            }

            foreach (ResistorLink r in net.Resistors)
            {
                Net other = _nets[_netOfNode[_nodes[r.A]]] == net ? _nets[_netOfNode[_nodes[r.B]]] : _nets[_netOfNode[_nodes[r.A]]];
                double next = path + r.Ohms;
                if (!distance.TryGetValue(other, out double known) || next < known)
                {
                    distance[other] = next;
                    queue.Enqueue(other, next);
                }
            }
        }
    }

    private void ReportShort(Net net, Driver a, Driver b)
    {
        string Level(Driver d) => d.Drive.Voltage.ToString("0.#", CultureInfo.InvariantCulture) + " V";
        string component = a.Owner != b.Owner ? b.Owner : a.Owner;
        _log.ReportOnce(
            "short-" + net.Pins[0],
            SimSeverity.Warning,
            "short-circuit",
            $"Short circuit on net {net.Name}: {a.Owner} drives {Level(a)} while {b.Owner} drives {Level(b)}.",
            component);
    }
}
