using ArduSim.Core.Circuits;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Timing;

namespace ArduSim.Core.Components.Parts;

/// <summary>
/// SG90 hobby servo: the horn follows the width of the HIGH pulses on its signal wire, 544 µs for
/// 0° to 2400 µs for 180° (the Arduino Servo library's range). It needs 4-6 V between V+ and GND.
/// A PWM duty on the wire (analogWrite) has no pulse width the part can see, and it says so.
/// </summary>
[SimComponent]
public sealed class ServoMotor : SimComponent
{
    public const double MinPulse = 544;
    public const double MaxPulse = 2400;

    private bool? _level;
    private ulong _riseAt;

    public ServoMotor(string id, ComponentProperties properties)
        : base(id, Spec, properties)
    {
    }

    public static ComponentDefinition Spec { get; } = new(
        "servo",
        "Part_Servo",
        "output",
        80,
        70,
        [
            new PinDefinition("GND", PinKind.Ground, 30, 70),
            new PinDefinition("V+", PinKind.Power, 40, 70),
            new PinDefinition("PWM", PinKind.Digital, 50, 70),
        ],
        []);

    private double Supply => PartSupply.Across(Net("V+"), Net("GND"));

    protected override void OnAttach() => Circuit.Watch(Pin("PWM"), OnSignal);

    public override void OnCircuitReady()
    {
        SetState("angle", 90);
        _level = PartSupply.Level(Net("PWM"), 5);
    }

    private void OnSignal()
    {
        NetState signal = Net("PWM");
        if (signal.HasVoltage && signal.Duty is > 0 and < 1)
        {
            Context.Log.ReportOnce(
                $"servo-pwm-{Id}",
                SimSeverity.Warning,
                "servo-pwm",
                $"{Id}: the signal is a PWM duty (analogWrite), whose pulse width the simulation does not model; drive the servo with the Servo library.",
                Id);
        }

        bool? level = PartSupply.Level(signal, 5);
        if (level == _level)
        {
            return;
        }

        bool? before = _level;
        _level = level;
        ulong now = Context.Engine.Clock.Now;
        if (level == true && before == false)
        {
            _riseAt = now;
        }
        else if (level == false && before == true)
        {
            Pulse((now - _riseAt) / (double)VirtualTime.Microsecond);
        }
    }

    private void Pulse(double microseconds)
    {
        if (microseconds is < 400 or > 2700)
        {
            return;
        }

        if (Supply < 4)
        {
            Context.Log.ReportOnce($"servo-power-{Id}", SimSeverity.Warning, "sensor-power", $"{Id} (servo) has no power: connect V+ to 5 V and GND to GND.", Id, "servo");
            return;
        }

        double angle = Math.Clamp((microseconds - MinPulse) / (MaxPulse - MinPulse) * 180, 0, 180);
        SetState("angle", Math.Round(angle, 1));
    }
}

/// <summary>
/// 5 V relay module (SRD-05VDC with a driver): the coil pulls in while IN is at its trigger level
/// (LOW on the common low-level modules; IN has a pull-up to VCC there, a pull-down otherwise) and the
/// module has 5 V. COM connects to NO while the coil is on and to NC while it is off, 10 ms after
/// the coil changes (5 ms to release), as the datasheet's operate and release times.
/// </summary>
[SimComponent]
public sealed class Relay : SimComponent
{
    private const ulong OperateTime = 10 * VirtualTime.Millisecond;
    private const ulong ReleaseTime = 5 * VirtualTime.Millisecond;

    private CircuitSwitch _no = null!;
    private CircuitSwitch _nc = null!;
    private bool _coil;
    private ScheduledEvent? _move;

    public Relay(string id, ComponentProperties properties)
        : base(id, Spec, properties)
    {
    }

    public static ComponentDefinition Spec { get; } = new(
        "relay",
        "Part_Relay",
        "output",
        100,
        70,
        [
            new PinDefinition("VCC", PinKind.Power, 10, 70),
            new PinDefinition("GND", PinKind.Ground, 20, 70),
            new PinDefinition("IN", PinKind.Digital, 30, 70),
            new PinDefinition("NO", PinKind.Passive, 70, 70),
            new PinDefinition("COM", PinKind.Passive, 80, 70),
            new PinDefinition("NC", PinKind.Passive, 90, 70),
        ],
        [new PropertyDefinition("trigger", PropertyKind.Choice, "low", ["low", "high"])]);

    private bool LowTrigger => Properties.Choice("trigger") == "low";

    protected override void OnAttach()
    {
        _no = Circuit.AddSwitch(Pin("COM"), Pin("NO"), closed: false);
        _nc = Circuit.AddSwitch(Pin("COM"), Pin("NC"), closed: true);
        Circuit.AddResistor(Pin("IN"), Pin(LowTrigger ? "VCC" : "GND"), 10_000, Id);
        WatchPins("VCC", "GND", "IN");
    }

    public override void OnCircuitReady()
    {
        SetState("coil", 0);
        SetState("closed", 0);
        Refresh();
    }

    protected override void Refresh()
    {
        double supply = PartSupply.Across(Net("VCC"), Net("GND"));
        NetState input = Net("IN");
        bool high = input.HasVoltage && input.AverageVoltage >= 2.5;
        bool coil = supply >= 4 && input.HasVoltage && (LowTrigger ? input.AverageVoltage < 1.5 : high);
        if (coil == _coil)
        {
            return;
        }

        _coil = coil;
        SetState("coil", coil ? 1 : 0);
        _move?.Cancel();
        _move = Context.Engine.Clock.Schedule(coil ? OperateTime : ReleaseTime, () =>
        {
            _nc.Set(!coil);
            _no.Set(coil);
            SetState("closed", coil ? 1 : 0);
        });
    }
}
