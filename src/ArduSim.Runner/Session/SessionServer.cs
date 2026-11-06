using ArduSim.Core.Circuits;
using System.Diagnostics;
using ArduSim.Core.Components;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Simulation;
using ArduSim.Core.Tracing;
using ArduSim.Ipc;

namespace ArduSim.Runner.Session;

/// <summary>
/// The Runner side of a session, independent of the transport: turns commands into host actions
/// (applied at the next scheduling point) and publishes snapshots of what changed, at most 60 per
/// second of wall time plus one on every pause and resume (ADR-003).
/// </summary>
internal sealed class SessionServer
{
    private static readonly long Interval = Stopwatch.Frequency / 60;

    private readonly SimulationHost _host;
    private readonly Action<IpcMessage> _send;
    private readonly Dictionary<int, PinUpdate> _pins = [];
    private readonly HashSet<SimComponent> _components = [];
    private readonly List<byte> _serial = [];
    private readonly Dictionary<int, List<byte>> _otherSerial = [];
    private readonly List<LogEntry> _logs = [];
    private readonly Dictionary<string, double> _sentCircuit = new(StringComparer.Ordinal);
    private long _circuitRevision = -1;
    private long _lastSent;
    private bool _first = true;

    public SessionServer(SimulationHost host, Action<IpcMessage> send)
    {
        _host = host;
        _send = send;
        host.Mcu.PinChanged += c => _pins[c.Pin] = new PinUpdate(c.Pin, (byte)c.Drive.Kind, c.Drive.Value);
        foreach (SimComponent component in host.Simulation.Components)
        {
            component.StateChanged += c => _components.Add(c);
        }

        foreach (UartPort uart in host.Mcu.Uarts)
        {
            if (uart.Index == 0)
            {
                uart.ByteTransmitted += (_, b) => _serial.Add(b);
            }
            else
            {
                var bytes = new List<byte>();
                _otherSerial[uart.Index] = bytes;
                uart.ByteTransmitted += (_, b) => bytes.Add(b);
            }
        }

        // The scene was built before anyone could listen, and that is when a wrong part or an LED
        // across the rails is reported.
        foreach (SimDiagnostic diagnostic in host.Engine.Log.Entries)
        {
            _logs.Add(Entry(diagnostic));
        }

        host.Engine.Log.Reported += d => _logs.Add(Entry(d));
        host.Tick = () => Publish(force: false);
        host.RunStateChanged = () => Publish(force: true);
        host.Busy = () =>
        {
            _send(new FirmwareBusy(host.Engine.Clock.Now));
            return false;
        };
    }

    /// <summary>Accepts a command from any thread.</summary>
    public void Handle(IpcMessage command)
    {
        switch (command)
        {
            case PauseRun:
                _host.Post(_host.Pause);
                break;
            case ResumeRun:
                _host.Post(_host.Resume);
                break;
            case StepRun { Kind: StepKind.Loop }:
                _host.Post(_host.StepLoop);
                break;
            case StepRun step:
                _host.Post(() => _host.StepTime(step.Nanoseconds));
                break;
            case SetSpeed speed:
                _host.Post(() => _host.SetSpeed(speed.Speed));
                break;
            case ComponentInput input:
                _host.Post(() => ApplyInput(input));
                break;
            case SerialInput serial when serial.Port >= 0 && serial.Port < _host.Mcu.Uarts.Count:
                _host.Post(() => _host.Mcu.Uarts[serial.Port].Receive(serial.Data));
                break;
            case StopRun:
                _host.RequestStop(StopReason.UserRequest);
                break;
            case SetRecording recording:
                _host.Post(() => _host.Mcu.SetRecording(recording.Enabled));
                break;
        }
    }

    private void ApplyInput(ComponentInput input)
    {
        if (_host.Simulation.FindComponent(input.Component) is { } component)
        {
            component.ApplyInput(input.Name, input.Value);
        }
        else
        {
            _host.Engine.Log.ReportOnce("input-" + input.Component, SimSeverity.Warning, "unknown-input", $"Input for '{input.Component}', which is not in the simulation.", variant: "missing-part");
        }
    }

    private static LogEntry Entry(SimDiagnostic d) => new(d.Time, (byte)d.Severity, d.Code, d.Message, d.ComponentId, d.Key, d.Args);

    private static DisplayData? Display(SimComponent component) =>
        component.Display is { } frame
            ? new DisplayData((byte)frame.Kind, frame.Columns, frame.Rows, (byte)frame.Status, (float)frame.Contrast, frame.Dots.ToArray())
            : null;

    private TraceBlock? DrainTrace()
    {
        SignalRecorder recorder = _host.Mcu.Recorder;
        if (!recorder.HasData)
        {
            return null;
        }

        (PinSample[] pins, BusEvent[] bus, bool overflowed) = recorder.Drain();
        var pinTimes = new ulong[pins.Length];
        var pinData = new byte[pins.Length * 2];
        var pinValues = new int[pins.Length];
        for (int i = 0; i < pins.Length; i++)
        {
            pinTimes[i] = pins[i].Time;
            pinData[2 * i] = pins[i].Pin;
            pinData[(2 * i) + 1] = (byte)pins[i].Level;
            pinValues[i] = pins[i].Value;
        }

        var busTimes = new ulong[bus.Length];
        var busDurations = new uint[bus.Length];
        var busData = new byte[bus.Length * 6];
        for (int i = 0; i < bus.Length; i++)
        {
            BusEvent e = bus[i];
            busTimes[i] = e.Time;
            busDurations[i] = e.Duration;
            busData[6 * i] = (byte)e.Kind;
            busData[(6 * i) + 1] = e.Value;
            busData[(6 * i) + 2] = e.Extra;
            busData[(6 * i) + 3] = e.PinA;
            busData[(6 * i) + 4] = e.PinB;
            busData[(6 * i) + 5] = e.PinC;
        }

        return new TraceBlock(pinTimes, pinData, pinValues, busTimes, busDurations, busData, overflowed);
    }

    // Only what changed since the last snapshot; between snapshots the view costs the simulation nothing.
    private ComponentUpdate? DrainCircuit()
    {
        Circuit circuit = _host.Simulation.Circuit;
        if (circuit.Revision == _circuitRevision)
        {
            return null;
        }

        _circuitRevision = circuit.Revision;
        var changed = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach ((string key, double value) in CircuitState.ToState(circuit.Read()))
        {
            if (!_sentCircuit.TryGetValue(key, out double sent) || !sent.Equals(value))
            {
                _sentCircuit[key] = value;
                changed[key] = value;
            }
        }

        return changed.Count == 0 ? null : new ComponentUpdate(CircuitState.ComponentId, changed);
    }

    private void Publish(bool force)
    {
        long now = Stopwatch.GetTimestamp();
        if (!force && !_first && now - _lastSent < Interval)
        {
            return;
        }

        bool empty = _pins.Count == 0 && _components.Count == 0 && _serial.Count == 0 && _logs.Count == 0 && !_host.Mcu.Recorder.HasData && _otherSerial.Values.All(b => b.Count == 0)
            && _host.Simulation.Circuit.Revision == _circuitRevision;
        if (!force && !_first && empty && now - _lastSent < 4 * Interval)
        {
            return; // nothing new: keep the clock readout fresh at 15 Hz only
        }

        IEnumerable<PinUpdate> pins = _first
            ? _host.Mcu.Board.Pins.Select(p => _host.Mcu.DriveOf(p.Number)).Zip(_host.Mcu.Board.Pins, (d, p) => new PinUpdate(p.Number, (byte)d.Kind, d.Value))
            : _pins.Values;
        IEnumerable<SimComponent> components = _first ? _host.Simulation.Components : _components;
        List<ComponentUpdate> updates = components.Select(c => new ComponentUpdate(c.Id, new Dictionary<string, double>(c.State), Display(c))).ToList();
        if (DrainCircuit() is { } circuitUpdate)
        {
            updates.Add(circuitUpdate);
        }

        var snapshot = new Snapshot(
            _host.Engine.Clock.Now,
            _host.WallTime.TotalSeconds,
            _host.IsPaused ? RunState.Paused : RunState.Running,
            pins.ToList(),
            updates,
            [.. _serial],
            [.. _logs],
            _otherSerial.Where(p => p.Value.Count > 0).Select(p => new SerialOutput(p.Key, [.. p.Value])).ToList(),
            DrainTrace());
        _pins.Clear();
        _components.Clear();
        _serial.Clear();
        foreach (List<byte> bytes in _otherSerial.Values)
        {
            bytes.Clear();
        }

        _logs.Clear();
        _first = false;
        _lastSent = now;
        _send(snapshot);
    }
}
