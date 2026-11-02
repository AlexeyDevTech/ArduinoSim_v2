using System.Diagnostics;
using ArduSim.Core.Components;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Simulation;
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
    private readonly List<LogEntry> _logs = [];
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

        if (host.Mcu.Uarts.Count > 0)
        {
            host.Mcu.Uarts[0].ByteTransmitted += (_, b) => _serial.Add(b);
        }

        host.Engine.Log.Reported += d => _logs.Add(new LogEntry(d.Time, (byte)d.Severity, d.Code, d.Message, d.ComponentId, d.Key, d.Args));
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

    private void Publish(bool force)
    {
        long now = Stopwatch.GetTimestamp();
        if (!force && !_first && now - _lastSent < Interval)
        {
            return;
        }

        bool empty = _pins.Count == 0 && _components.Count == 0 && _serial.Count == 0 && _logs.Count == 0;
        if (!force && !_first && empty && now - _lastSent < 4 * Interval)
        {
            return; // nothing new: keep the clock readout fresh at 15 Hz only
        }

        IEnumerable<PinUpdate> pins = _first
            ? _host.Mcu.Board.Pins.Select(p => _host.Mcu.DriveOf(p.Number)).Zip(_host.Mcu.Board.Pins, (d, p) => new PinUpdate(p.Number, (byte)d.Kind, d.Value))
            : _pins.Values;
        IEnumerable<SimComponent> components = _first ? _host.Simulation.Components : _components;
        var snapshot = new Snapshot(
            _host.Engine.Clock.Now,
            _host.WallTime.TotalSeconds,
            _host.IsPaused ? RunState.Paused : RunState.Running,
            pins.ToList(),
            components.Select(c => new ComponentUpdate(c.Id, new Dictionary<string, double>(c.State))).ToList(),
            [.. _serial],
            [.. _logs]);
        _pins.Clear();
        _components.Clear();
        _serial.Clear();
        _logs.Clear();
        _first = false;
        _lastSent = now;
        _send(snapshot);
    }
}
