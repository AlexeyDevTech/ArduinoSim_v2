using System.Globalization;

namespace ArduSim.Core.Circuits;

/// <summary>What a meter would show on one pin.</summary>
/// <param name="Voltage">The net's average voltage; NaN while it floats.</param>
/// <param name="Current">Amps from the net into the part through this pin (negative: out of the part), averaged over PWM.</param>
/// <param name="PeakCurrent">The largest magnitude in any PWM phase.</param>
public readonly record struct PinReading(double Voltage, double Current, double PeakCurrent);

/// <summary>Every pin's and wire's electrical values at one moment (ADR-017).</summary>
/// <param name="Wires">Amps along each wire by its id, from its first end to its second; NaN for a wire that closes a loop, whose share is not determined.</param>
public sealed record CircuitReading(IReadOnlyDictionary<PinRef, PinReading> Pins, IReadOnlyDictionary<string, double> Wires);

/// <summary>How a <see cref="CircuitReading"/> travels to the view: as the state of a reserved component id, so that the IPC messages stay as they are.</summary>
public static class CircuitState
{
    public const string ComponentId = "~circuit";

    public static string VoltageKey(string component, string pin) => $"v:{component}.{pin}";

    public static string CurrentKey(string component, string pin) => $"i:{component}.{pin}";

    public static string PeakKey(string component, string pin) => $"p:{component}.{pin}";

    public static string WireKey(string wire) => "w:" + wire;

    /// <summary>The reading as state values, rounded to what a person can tell apart (1 mV, 0.1 µA).</summary>
    public static Dictionary<string, double> ToState(CircuitReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        var state = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach ((PinRef pin, PinReading value) in reading.Pins)
        {
            state[VoltageKey(pin.Component, pin.Pin)] = Math.Round(value.Voltage, 3);
            state[CurrentKey(pin.Component, pin.Pin)] = Math.Round(value.Current, 7);
            state[PeakKey(pin.Component, pin.Pin)] = Math.Round(value.PeakCurrent, 7);
        }

        foreach ((string wire, double amps) in reading.Wires)
        {
            state[WireKey(wire)] = Math.Round(amps, 7);
        }

        return state;
    }

    /// <summary>The reading as a table for people: a pin per line, then a wire per line.</summary>
    public static IEnumerable<string> ToLines(CircuitReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        foreach ((PinRef pin, PinReading value) in reading.Pins.OrderBy(p => p.Key.ToString(), StringComparer.Ordinal))
        {
            string voltage = double.IsNaN(value.Voltage) ? "floating" : string.Create(CultureInfo.InvariantCulture, $"{value.Voltage:0.000} V");
            yield return string.Create(CultureInfo.InvariantCulture, $"{pin} {voltage} {value.Current * 1000:0.000} mA peak {value.PeakCurrent * 1000:0.000} mA");
        }

        foreach ((string wire, double amps) in reading.Wires.OrderBy(w => w.Key, StringComparer.Ordinal))
        {
            yield return double.IsNaN(amps)
                ? $"wire {wire} loop"
                : string.Create(CultureInfo.InvariantCulture, $"wire {wire} {amps * 1000:0.000} mA");
        }
    }
}

/// <content>Pin and wire currents for the view, computed on demand from the last solution.</content>
public sealed partial class Circuit
{
    /// <summary>Counts solutions, so that a poller can tell whether anything changed.</summary>
    public long Revision { get; private set; }

    public CircuitReading Read()
    {
        int count = _nodePins.Count;
        var into = new double[count];
        var peak = new double[count];
        foreach (Driver driver in _drivers)
        {
            if (driver.Drive.Strength != DriveStrength.None && !driver.Drive.IsIdeal)
            {
                int node = _nodes[driver.Pin];
                into[node] -= driver.Current;
                peak[node] = Larger(peak[node], -driver.PeakCurrent);
            }
        }

        foreach (CircuitElement element in _elements)
        {
            if (element.NetA != element.NetB)
            {
                int a = _nodes[element.A];
                int b = _nodes[element.B];
                into[a] += element.Current;
                into[b] -= element.Current;
                peak[a] = Larger(peak[a], element.PeakCurrent);
                peak[b] = Larger(peak[b], -element.PeakCurrent);
            }
        }

        // A rail gives what the others on its net take.
        foreach (Net net in _nets)
        {
            Driver? rail = net.Drivers.Find(d => d.Drive.IsIdeal);
            if (rail is not null)
            {
                double taken = 0;
                foreach (int node in net.Nodes)
                {
                    taken += into[node];
                }

                into[_nodes[rail.Pin]] -= taken;
            }
        }

        var wires = new Dictionary<string, double>(StringComparer.Ordinal);
        double[] shown = FlowsThroughNets(into, wires);
        var pins = new Dictionary<PinRef, PinReading>(count);
        for (int node = 0; node < count; node++)
        {
            NetState state = _nets[_netOfNode[node]].State;
            pins[_nodePins[node]] = new PinReading(
                state.Condition == NetCondition.Floating ? double.NaN : state.AverageVoltage,
                shown[node],
                Math.Max(Math.Abs(peak[node]), Math.Abs(shown[node])));
        }

        return new CircuitReading(pins, wires);
    }

    // Wires and closed switches have no resistance, so inside a net the currents follow from where
    // current enters and leaves: walk a spanning tree and sum from its leaves. An edge that closes a
    // loop carries an undetermined share. What passes through a part (a switch, the board's rail)
    // adds to the current of its pins.
    private double[] FlowsThroughNets(double[] into, Dictionary<string, double> wires)
    {
        int count = into.Length;
        var edges = new List<(int A, int B, string? Wire)>(_wires);
        foreach (CircuitSwitch closed in _switches)
        {
            if (closed.Closed)
            {
                edges.Add((_nodes[closed.A], _nodes[closed.B], null));
            }
        }

        var around = new List<int>?[count];
        for (int e = 0; e < edges.Count; e++)
        {
            (around[edges[e].A] ??= []).Add(e);
            (around[edges[e].B] ??= []).Add(e);
        }

        var shown = (double[])into.Clone();
        var subtree = new double[count];
        for (int node = 0; node < count; node++)
        {
            subtree[node] = -into[node];
        }

        var used = new bool[edges.Count];
        var seen = new bool[count];
        var order = new List<(int Node, int Parent, int Edge)>();
        var queue = new Queue<int>();
        for (int root = 0; root < count; root++)
        {
            if (seen[root])
            {
                continue;
            }

            seen[root] = true;
            queue.Enqueue(root);
            while (queue.TryDequeue(out int node))
            {
                foreach (int e in (IEnumerable<int>?)around[node] ?? [])
                {
                    int other = edges[e].A == node ? edges[e].B : edges[e].A;
                    if (!seen[other])
                    {
                        seen[other] = true;
                        used[e] = true;
                        order.Add((other, node, e));
                        queue.Enqueue(other);
                    }
                }
            }
        }

        for (int i = order.Count - 1; i >= 0; i--)
        {
            (int node, int parent, int e) = order[i];
            double flow = subtree[node];
            subtree[parent] += flow;
            if (edges[e].Wire is { } wire)
            {
                wires[wire] = edges[e].A == node ? flow : -flow;
            }
            else
            {
                shown[node] += flow;
                shown[parent] -= flow;
            }
        }

        for (int e = 0; e < edges.Count; e++)
        {
            if (!used[e] && edges[e].Wire is { } wire)
            {
                wires[wire] = double.NaN;
            }
        }

        return shown;
    }
}
