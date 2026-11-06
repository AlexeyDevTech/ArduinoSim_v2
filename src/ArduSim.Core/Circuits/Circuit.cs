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

    /// <summary>A source behind a resistance: a pull-up, say.</summary>
    Weak,

    /// <summary>An output or a supply rail.</summary>
    Strong,
}

/// <summary>What one source contributes to a net (ADR-017). PWM and tone are state, not edges.</summary>
/// <param name="Resistance">Ohms behind the source. A strong source with none is ideal: it fixes the net's voltage.</param>
/// <param name="Duty">Fraction of time at <paramref name="Voltage"/>, the rest at 0 V (1 = steady).</param>
public readonly record struct NetDrive(DriveStrength Strength, double Voltage, double Resistance, double Duty, int ToneHz)
{
    public static NetDrive None => default;

    public static NetDrive Strong(double voltage, double duty = 1, int toneHz = 0, double resistance = 0) => new(DriveStrength.Strong, voltage, resistance, duty, toneHz);

    public static NetDrive Weak(double voltage, double resistance) => new(DriveStrength.Weak, voltage, resistance, 1, 0);

    internal bool Toggles => Strength != DriveStrength.None && ((Duty > 0 && Duty < 1) || ToneHz > 0);

    internal bool IsIdeal => Strength == DriveStrength.Strong && Resistance <= 0 && !Toggles;

    internal double Conductance => 1 / Math.Max(Resistance, 1);
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

/// <param name="Voltage">The net's voltage; while PWM or a tone moves it, the voltage of its high phase.</param>
/// <param name="Duty">Fraction of time at <paramref name="Voltage"/> (1 = steady).</param>
/// <param name="AverageVoltage">Voltage averaged over the PWM period.</param>
public readonly record struct NetState(NetCondition Condition, double Voltage, double Duty, int ToneHz, double AverageVoltage)
{
    public bool HasVoltage => Condition is NetCondition.Driven or NetCondition.Pulled;
}

/// <summary>A set of pins joined by wires (and closed switches).</summary>
public sealed class Net
{
    internal Net(int index, IReadOnlyList<PinRef> pins, int[] nodes)
    {
        Index = index;
        Pins = pins;
        Nodes = nodes;
    }

    public int Index { get; }

    public IReadOnlyList<PinRef> Pins { get; }

    public NetState State { get; internal set; }

    public string Name => string.Join(", ", Pins.Take(3)) + (Pins.Count > 3 ? ", ..." : "");

    internal int[] Nodes { get; }

    internal List<Driver> Drivers { get; } = [];

    internal List<CircuitElement> Elements { get; } = [];

    // The solver's working state for this net.
    internal int Visit { get; set; }

    /// <summary>The net's unknown in its island, or -1 when its voltage is known.</summary>
    internal int Slot { get; set; }

    internal double Known { get; set; }

    internal bool Reached { get; set; }
}

/// <summary>A component's contribution to the net of one of its pins.</summary>
public sealed class Driver
{
    private readonly Circuit _circuit;

    internal Driver(Circuit circuit, PinRef pin, string owner, double currentLimit, int order)
    {
        _circuit = circuit;
        Pin = pin;
        Owner = owner;
        CurrentLimit = currentLimit;
        Order = order;
    }

    public PinRef Pin { get; }

    /// <summary>Component id, used in warnings.</summary>
    public string Owner { get; }

    public NetDrive Drive { get; private set; }

    /// <summary>Amps above which a warning says the pin is overloaded; 0 for no limit.</summary>
    public double CurrentLimit { get; }

    /// <summary>Amps out of the source into the net, averaged over PWM. Rails: see <see cref="Circuit.Read"/>.</summary>
    public double Current { get; internal set; }

    /// <summary>The amps of largest magnitude in any PWM phase.</summary>
    public double PeakCurrent { get; internal set; }

    internal Net Net { get; set; } = null!;

    /// <summary>Its place among the circuit's drivers: a stable order to choose by.</summary>
    internal int Order { get; }

    /// <summary>The source's voltage in the phase being solved.</summary>
    internal double Source { get; set; }

    public void Set(NetDrive drive)
    {
        if (drive == Drive)
        {
            return;
        }

        Drive = drive;
        _circuit.Invalidate(Pin);
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

/// <summary>A two-terminal part of the solved circuit.</summary>
public abstract class CircuitElement
{
    private protected CircuitElement(PinRef a, PinRef b, string owner)
    {
        A = a;
        B = b;
        Owner = owner;
    }

    public PinRef A { get; }

    public PinRef B { get; }

    public string Owner { get; }

    /// <summary>Amps from <see cref="A"/> to <see cref="B"/>, averaged over PWM.</summary>
    public double Current { get; internal set; }

    /// <summary>The amps of largest magnitude in any PWM phase.</summary>
    public double PeakCurrent { get; internal set; }

    internal Net NetA { get; set; } = null!;

    internal Net NetB { get; set; } = null!;

    internal int Visit { get; set; }

    internal Net Other(Net net) => net == NetA ? NetB : NetA;

    /// <summary>The current with this voltage across it, when both of its nets are known.</summary>
    internal abstract double DirectCurrent(double across);
}

/// <summary>A resistor couples two nets; its value can change at run time (a potentiometer's track).</summary>
public sealed class CircuitResistor : CircuitElement
{
    private readonly Circuit _circuit;

    internal CircuitResistor(Circuit circuit, PinRef a, PinRef b, double ohms, string owner)
        : base(a, b, owner)
    {
        _circuit = circuit;
        Ohms = ohms;
    }

    public double Ohms { get; private set; }

    public void Set(double ohms)
    {
        Assign(ohms);
        _circuit.Invalidate(A);
    }

    internal void Assign(double ohms) => Ohms = Math.Max(ohms, Circuit.MinResistance);

    internal override double DirectCurrent(double across) => across / Ohms;
}

/// <summary>
/// A piecewise-linear diode from A (anode) to B (cathode): nearly open below its knee, a series
/// resistance above it (ADR-017).
/// </summary>
public sealed class CircuitDiode : CircuitElement
{
    internal CircuitDiode(PinRef anode, PinRef cathode, double knee, double seriesResistance, string owner)
        : base(anode, cathode, owner)
    {
        Knee = knee;
        OnConductance = 1 / seriesResistance;
    }

    public double Knee { get; }

    internal double OnConductance { get; }

    /// <summary>Conducting in the phase being solved; kept as the first guess for the next solve.</summary>
    internal bool On { get; set; }

    internal bool EverOn { get; set; }

    internal double OnCurrent(double across) => ((across - Knee) * OnConductance) + (Circuit.DiodeOffConductance * Knee);

    internal override double DirectCurrent(double across) => across > Knee ? OnCurrent(across) : 0;
}

/// <summary>
/// The scene's electrical model: pins joined into nets by wires and closed switches, drivers on
/// pins, and resistors and diodes coupling nets. Every change is solved by nodal analysis around
/// it (ADR-017, the solving is in Circuit.Solve.cs); pin watchers hear about every net whose state
/// changed, in the same host call, and the simulation's time does not move.
/// </summary>
public sealed partial class Circuit
{
    internal const double MinResistance = 0.001;
    internal const double DiodeOffConductance = OffConductance;
    private const double AgreementTolerance = 0.5;
    private const int SettleLimit = 10_000;

    private readonly SimulationLog _log;
    private readonly Dictionary<PinRef, int> _nodes = [];
    private readonly List<PinRef> _nodePins = [];
    private readonly List<List<Action>> _watchers = [];
    private readonly List<(int A, int B, string? Wire)> _wires = [];
    private readonly List<CircuitSwitch> _switches = [];
    private readonly List<Driver> _drivers = [];
    private readonly List<CircuitElement> _elements = [];
    private readonly Queue<int> _dirty = new();
    private readonly HashSet<int> _dirtySet = [];
    private List<Net> _nets = [];
    private int[] _netOfNode = [];
    private bool _built;
    private bool _resolving;
    private bool _repartition;

    public Circuit(SimulationLog log)
    {
        _log = log;
    }

    public IReadOnlyList<Net> Nets => _nets;

    public bool Contains(PinRef pin) => _nodes.ContainsKey(pin);

    public void AddPin(PinRef pin) => Node(pin);

    /// <param name="wire">The scene's id of this wire, when it is one (its current is then reported under it).</param>
    public void Connect(PinRef a, PinRef b, string? wire = null)
    {
        RequireUnbuilt();
        _wires.Add((Node(a), Node(b), wire));
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

    /// <param name="currentLimit">Amps above which a warning says the pin is overloaded; 0 for no limit.</param>
    public Driver AddDriver(PinRef pin, string owner, double currentLimit = 0)
    {
        RequireUnbuilt();
        Node(pin);
        var driver = new Driver(this, pin, owner, currentLimit, _drivers.Count);
        _drivers.Add(driver);
        return driver;
    }

    public CircuitResistor AddResistor(PinRef a, PinRef b, double ohms, string owner)
    {
        RequireUnbuilt();
        Node(a);
        Node(b);
        var resistor = new CircuitResistor(this, a, b, Math.Max(ohms, MinResistance), owner);
        _elements.Add(resistor);
        return resistor;
    }

    /// <param name="knee">Volts across it below which it does not conduct.</param>
    /// <param name="seriesResistance">Ohms above the knee.</param>
    public CircuitDiode AddDiode(PinRef anode, PinRef cathode, double knee, double seriesResistance, string owner)
    {
        RequireUnbuilt();
        Node(anode);
        Node(cathode);
        var diode = new CircuitDiode(anode, cathode, knee, seriesResistance, owner);
        _elements.Add(diode);
        return diode;
    }

    /// <summary>Changes the two halves of a divider at once, so that nothing sees it in between.</summary>
    public void SetDivider(CircuitResistor first, double firstOhms, CircuitResistor second, double secondOhms)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        first.Assign(firstOhms);
        second.Assign(secondOhms);
        Invalidate(second.A);
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

    internal void Invalidate(PinRef pin)
    {
        if (!_built)
        {
            return;
        }

        int net = _netOfNode[_nodes[pin]];
        if (_dirtySet.Add(net))
        {
            _dirty.Enqueue(net);
        }

        Settle();
    }

    internal void Repartition()
    {
        if (!_built)
        {
            return;
        }

        _repartition = true;
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

        foreach ((int a, int b, string? _) in _wires)
        {
            Union(a, b);
        }

        foreach (CircuitSwitch s in _switches.Where(s => s.Closed))
        {
            Union(_nodes[s.A], _nodes[s.B]);
        }

        var netOfRoot = new Dictionary<int, int>();
        var members = new List<List<PinRef>>();
        var nodes = new List<List<int>>();
        _netOfNode = new int[count];
        for (int node = 0; node < count; node++)
        {
            int root = Find(node);
            if (!netOfRoot.TryGetValue(root, out int net))
            {
                net = members.Count;
                netOfRoot[root] = net;
                members.Add([]);
                nodes.Add([]);
            }

            _netOfNode[node] = net;
            members[net].Add(_nodePins[node]);
            nodes[net].Add(node);
        }

        _nets = members.Select((pins, i) => new Net(i, pins, nodes[i].ToArray())).ToList();
        foreach (Driver driver in _drivers)
        {
            driver.Net = _nets[_netOfNode[_nodes[driver.Pin]]];
            driver.Net.Drivers.Add(driver);
        }

        foreach (CircuitElement element in _elements)
        {
            element.NetA = _nets[_netOfNode[_nodes[element.A]]];
            element.NetB = _nets[_netOfNode[_nodes[element.B]]];
            element.Current = element.PeakCurrent = 0;
            if (element.NetA != element.NetB)
            {
                element.NetA.Elements.Add(element);
                element.NetB.Elements.Add(element);
            }
        }

        _dirty.Clear();
        _dirtySet.Clear();
    }

    // The nets changed shape: solve everything, and tell the watchers of what differs.
    private void RebuildNets()
    {
        NetState[] before = _netOfNode.Length == 0 ? [] : Enumerable.Range(0, _nodePins.Count).Select(n => _nets[_netOfNode[n]].State).ToArray();
        Partition();
        ResolveAll();
        for (int node = 0; node < _nodePins.Count; node++)
        {
            if (before.Length == 0 || before[node] != _nets[_netOfNode[node]].State)
            {
                Notify(node);
            }
        }
    }

    // Watchers may change drivers and switches; those changes are queued and solved here until the
    // circuit is stable.
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
            while (true)
            {
                int net;
                if (_repartition)
                {
                    net = -1;
                }
                else if (!_dirty.TryDequeue(out net))
                {
                    break;
                }
                else if (!_dirtySet.Remove(net))
                {
                    continue;
                }

                if (++rounds > SettleLimit)
                {
                    _log.ReportOnce("circuit-settle", SimSeverity.Error, "circuit-unstable", $"The circuit does not settle (components keep changing each other); resolution was stopped.");
                    _repartition = false;
                    _dirty.Clear();
                    _dirtySet.Clear();
                    break;
                }

                if (net < 0)
                {
                    _repartition = false;
                    RebuildNets();
                }
                else
                {
                    ResolveAround(_nets[net]);
                }
            }
        }
        finally
        {
            _resolving = false;
        }
    }

    private void NotifyNet(Net net)
    {
        foreach (int node in net.Nodes)
        {
            Notify(node);
        }
    }

    private void Notify(int node)
    {
        foreach (Action watcher in _watchers[node])
        {
            watcher();
        }
    }
}
