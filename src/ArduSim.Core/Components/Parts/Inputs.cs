using ArduSim.Core.Circuits;
using ArduSim.Core.Timing;

namespace ArduSim.Core.Components.Parts;

/// <summary>
/// KY-040 rotary encoder module: CLK and DT are contacts to GND with 10 kΩ pull-ups to +, SW is the
/// shaft button to GND (no pull-up on the module). Each detent is one full quadrature cycle, one
/// transition every 2 ms; clockwise, CLK leads DT. Detents turned faster than that queue up.
/// </summary>
[SimComponent]
public sealed class RotaryEncoder : SimComponent
{
    /// <summary>Time between the four transitions of one detent.</summary>
    public const ulong TransitionTime = 2 * VirtualTime.Millisecond;

    private CircuitSwitch _clk = null!;
    private CircuitSwitch _dt = null!;
    private CircuitSwitch _button = null!;
    private int _queued;
    private int _steps;
    private bool _turning;

    public RotaryEncoder(string id, ComponentProperties properties)
        : base(id, Spec, properties)
    {
    }

    public static ComponentDefinition Spec { get; } = new(
        "rotary-encoder",
        "Part_RotaryEncoder",
        "input",
        60,
        80,
        [
            new PinDefinition("CLK", PinKind.Digital, 10, 80),
            new PinDefinition("DT", PinKind.Digital, 20, 80),
            new PinDefinition("SW", PinKind.Digital, 30, 80),
            new PinDefinition("+", PinKind.Power, 40, 80),
            new PinDefinition("GND", PinKind.Ground, 50, 80),
        ],
        []);

    protected override void OnAttach()
    {
        _clk = Circuit.AddSwitch(Pin("CLK"), Pin("GND"), closed: false);
        _dt = Circuit.AddSwitch(Pin("DT"), Pin("GND"), closed: false);
        _button = Circuit.AddSwitch(Pin("SW"), Pin("GND"), closed: false);
        Circuit.AddResistor(Pin("CLK"), Pin("+"), 10_000, Id);
        Circuit.AddResistor(Pin("DT"), Pin("+"), 10_000, Id);
    }

    public override void OnCircuitReady()
    {
        SetState("step", 0);
        SetState("pressed", 0);
    }

    /// <summary><c>turn</c>: detents to turn (positive = clockwise); <c>pressed</c>: the shaft button.</summary>
    public override void ApplyInput(string name, double value)
    {
        switch (name)
        {
            case "turn":
                _queued += (int)Math.Round(value);
                if (!_turning)
                {
                    NextDetent();
                }

                break;
            case "pressed":
                SetState("pressed", value != 0 ? 1 : 0);
                _button.Set(value != 0);
                break;
            default:
                base.ApplyInput(name, value);
                break;
        }
    }

    private void NextDetent()
    {
        if (_queued == 0)
        {
            _turning = false;
            return;
        }

        _turning = true;
        bool clockwise = _queued > 0;
        _queued -= clockwise ? 1 : -1;
        _steps += clockwise ? 1 : -1;
        SetState("step", _steps);
        (CircuitSwitch lead, CircuitSwitch lag) = clockwise ? (_clk, _dt) : (_dt, _clk);
        SimulationClock clock = Context.Engine.Clock;
        lead.Set(true);
        clock.Schedule(TransitionTime, () => lag.Set(true));
        clock.Schedule(2 * TransitionTime, () => lead.Set(false));
        clock.Schedule(3 * TransitionTime, () => lag.Set(false));
        clock.Schedule(4 * TransitionTime, NextDetent);
    }
}

/// <summary>
/// 4x4 membrane keypad: a pressed key connects its row pin to its column pin. The sketch (or the
/// Keypad library) drives one side and reads the other with pull-ups.
/// </summary>
[SimComponent]
public sealed class MatrixKeypad : SimComponent
{
    /// <summary>Key labels row by row, as printed on the common 4x4 keypad.</summary>
    public const string Labels = "123A456B789C*0#D";

    private readonly CircuitSwitch[] _keys = new CircuitSwitch[16];

    public MatrixKeypad(string id, ComponentProperties properties)
        : base(id, Spec, properties)
    {
    }

    public static ComponentDefinition Spec { get; } = new(
        "keypad",
        "Part_Keypad",
        "input",
        120,
        150,
        [
            .. Enumerable.Range(0, 4).Select(r => new PinDefinition($"R{r + 1}", PinKind.Digital, 20 + (10 * r), 150)),
            .. Enumerable.Range(0, 4).Select(c => new PinDefinition($"C{c + 1}", PinKind.Digital, 60 + (10 * c), 150)),
        ],
        []);

    protected override void OnAttach()
    {
        for (int key = 0; key < 16; key++)
        {
            _keys[key] = Circuit.AddSwitch(Pin($"R{(key / 4) + 1}"), Pin($"C{(key % 4) + 1}"), closed: false);
        }
    }

    public override void OnCircuitReady() => SetState("key", -1);

    /// <summary><c>k0</c>…<c>k15</c> (row by row, see <see cref="Labels"/>): 1 pressed, 0 released.</summary>
    public override void ApplyInput(string name, double value)
    {
        if (name.Length < 2 || name[0] != 'k' || !int.TryParse(name.AsSpan(1), System.Globalization.CultureInfo.InvariantCulture, out int key) || key is < 0 or > 15)
        {
            base.ApplyInput(name, value);
            return;
        }

        _keys[key].Set(value != 0);
        int pressed = Array.FindIndex(_keys, k => k.Closed);
        SetState("key", pressed);
    }
}

/// <summary>
/// Analog joystick module (KY-023): two 10 kΩ potentiometers between VCC and GND whose wipers are
/// VRX and VRY, and a push button from SW to GND. Centred, both read half the supply; right and up
/// raise the voltage.
/// </summary>
[SimComponent]
public sealed class Joystick : SimComponent
{
    private const double Track = 10_000;

    private Driver _x = null!;
    private Driver _y = null!;
    private CircuitSwitch _button = null!;
    private double _horizontal;
    private double _vertical;

    public Joystick(string id, ComponentProperties properties)
        : base(id, Spec, properties)
    {
    }

    public static ComponentDefinition Spec { get; } = new(
        "joystick",
        "Part_Joystick",
        "input",
        70,
        80,
        [
            new PinDefinition("GND", PinKind.Ground, 10, 80),
            new PinDefinition("VCC", PinKind.Power, 20, 80),
            new PinDefinition("VRX", PinKind.Analog, 30, 80),
            new PinDefinition("VRY", PinKind.Analog, 40, 80),
            new PinDefinition("SW", PinKind.Digital, 50, 80),
        ],
        []);

    protected override void OnAttach()
    {
        _x = Circuit.AddDriver(Pin("VRX"), Id);
        _y = Circuit.AddDriver(Pin("VRY"), Id);
        _button = Circuit.AddSwitch(Pin("SW"), Pin("GND"), closed: false);
        WatchPins("VCC", "GND");
    }

    public override void OnCircuitReady()
    {
        SetState("x", 0);
        SetState("y", 0);
        SetState("pressed", 0);
        Refresh();
    }

    /// <summary><c>x</c> and <c>y</c>: -1…1 (0 = centre); <c>pressed</c>: the stick's button.</summary>
    public override void ApplyInput(string name, double value)
    {
        switch (name)
        {
            case "x":
                _horizontal = Math.Clamp(value, -1, 1);
                SetState("x", _horizontal);
                Refresh();
                break;
            case "y":
                _vertical = Math.Clamp(value, -1, 1);
                SetState("y", _vertical);
                Refresh();
                break;
            case "pressed":
                SetState("pressed", value != 0 ? 1 : 0);
                _button.Set(value != 0);
                break;
            default:
                base.ApplyInput(name, value);
                break;
        }
    }

    protected override void Refresh()
    {
        NetState vcc = Net("VCC");
        NetState gnd = Net("GND");
        _x.Set(Wiper(vcc, gnd, (1 + _horizontal) / 2));
        _y.Set(Wiper(vcc, gnd, (1 + _vertical) / 2));
    }

    // As the potentiometer part: the wiper is a source behind the divider's Thevenin resistance.
    private static NetDrive Wiper(NetState high, NetState low, double fraction)
    {
        if (!high.HasVoltage || !low.HasVoltage)
        {
            return NetDrive.None;
        }

        double toLow = Math.Max(Track * fraction, 1);
        double toHigh = Math.Max(Track * (1 - fraction), 1);
        double voltage = low.AverageVoltage + ((high.AverageVoltage - low.AverageVoltage) * fraction);
        return NetDrive.Weak(voltage, Math.Max(toLow * toHigh / (toLow + toHigh), 1));
    }
}
