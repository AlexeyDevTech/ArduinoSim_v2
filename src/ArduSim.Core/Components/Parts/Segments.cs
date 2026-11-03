using System.Text;
using ArduSim.Core.Circuits;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Displays;
using ArduSim.Core.Timing;

namespace ArduSim.Core.Components.Parts;

/// <summary>The characters seven segments can show, for the displays' text (bit 0 = a … bit 6 = g).</summary>
public static class SegmentFont
{
    private static readonly Dictionary<int, char> Glyphs = new()
    {
        [0x3F] = '0', [0x06] = '1', [0x5B] = '2', [0x4F] = '3', [0x66] = '4', [0x6D] = '5', [0x7D] = '6', [0x07] = '7',
        [0x7F] = '8', [0x6F] = '9', [0x77] = 'A', [0x7C] = 'b', [0x39] = 'C', [0x5E] = 'd', [0x79] = 'E', [0x71] = 'F',
        [0x3D] = 'G', [0x76] = 'H', [0x30] = 'I', [0x1E] = 'J', [0x38] = 'L', [0x54] = 'n', [0x5C] = 'o', [0x73] = 'P',
        [0x50] = 'r', [0x78] = 't', [0x3E] = 'U', [0x1C] = 'u', [0x6E] = 'y', [0x40] = '-', [0x08] = '_', [0x63] = '°',
        [0x27] = '7', [0x00] = ' ',
    };

    /// <summary>The character for a segment pattern (a-g), or '?' for one no font uses.</summary>
    public static char Character(int segments) => Glyphs.TryGetValue(segments & 0x7F, out char c) ? c : '?';
}

/// <summary>
/// One or four seven-segment digits (common cathode or anode), driven directly or multiplexed. Each
/// segment glows with the share of time it is forward-biased over a 20 ms frame, as the eye
/// averages a multiplexed display; the text reads the segments lit at least half as bright as the
/// brightest.
/// </summary>
[SimComponent]
public sealed class SevenSegment : SimComponent
{
    /// <summary>The averaging frame: brightness is published every 20 ms of virtual time.</summary>
    public const ulong Frame = 20 * VirtualTime.Millisecond;

    private static readonly string[] SegmentPins = ["A", "B", "C", "D", "E", "F", "G", "DP"];

    // Declared before Spec: static initializers run in textual order.
    private static readonly ComponentDefinition One = new(
        "seven-segment",
        "Part_SevenSegment",
        "display",
        60,
        80,
        [
            new PinDefinition("G", PinKind.Digital, 10, 0), new PinDefinition("F", PinKind.Digital, 20, 0), new PinDefinition("COM", PinKind.Passive, 30, 0),
            new PinDefinition("A", PinKind.Digital, 40, 0), new PinDefinition("B", PinKind.Digital, 50, 0),
            new PinDefinition("E", PinKind.Digital, 10, 80), new PinDefinition("D", PinKind.Digital, 20, 80), new PinDefinition("COM.2", PinKind.Passive, 30, 80),
            new PinDefinition("C", PinKind.Digital, 40, 80), new PinDefinition("DP", PinKind.Digital, 50, 80),
        ],
        PropertyList)
    {
        Layout = Pick,
    };

    private static readonly ComponentDefinition Four = One with
    {
        Width = 130,
        Height = 70,
        Pins =
        [
            new PinDefinition("D1", PinKind.Passive, 40, 0), new PinDefinition("A", PinKind.Digital, 50, 0), new PinDefinition("F", PinKind.Digital, 60, 0),
            new PinDefinition("D2", PinKind.Passive, 70, 0), new PinDefinition("D3", PinKind.Passive, 80, 0), new PinDefinition("B", PinKind.Digital, 90, 0),
            new PinDefinition("E", PinKind.Digital, 40, 70), new PinDefinition("D", PinKind.Digital, 50, 70), new PinDefinition("DP", PinKind.Digital, 60, 70),
            new PinDefinition("C", PinKind.Digital, 70, 70), new PinDefinition("G", PinKind.Digital, 80, 70), new PinDefinition("D4", PinKind.Passive, 90, 70),
        ],
    };

    private double[] _share = [];
    private double[] _lit = [];
    private byte[] _shown = [];
    private ulong _since;
    private ulong _frameStart;
    private long _version;
    private DisplayFrame? _frame;
    private string _text = "";

    public SevenSegment(string id, ComponentProperties properties)
        : base(id, Pick(properties), properties)
    {
    }

    public static ComponentDefinition Spec => One;

    private static IReadOnlyList<PropertyDefinition> PropertyList =>
    [
        new PropertyDefinition("digits", PropertyKind.Choice, "1", ["1", "4"]),
        new PropertyDefinition("common", PropertyKind.Choice, "cathode", ["cathode", "anode"]),
        new PropertyDefinition("color", PropertyKind.Choice, "red", ["red", "green", "blue", "yellow", "white"]),
    ];

    private int Digits => Definition.Width > 100 ? 4 : 1;

    private bool CommonAnode => Properties.Choice("common") == "anode";

    public override DisplayFrame Display => _frame is { } frame && frame.Version == _version
        ? frame
        : _frame = new DisplayFrame(DisplayKind.Segments, Digits, 1, DisplayStatus.Powered | DisplayStatus.On, 1, _shown.ToArray(), [_text], _version);

    private static ComponentDefinition Pick(ComponentProperties properties) => properties.Choice("digits") == "4" ? Four : One;

    private string Common(int digit) => Digits == 1 ? "COM" : "D" + (digit + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    protected override void OnAttach()
    {
        int segments = Digits * 8;
        _share = new double[segments];
        _lit = new double[segments];
        _shown = new byte[segments];
        if (Digits == 1)
        {
            Circuit.Connect(Pin("COM"), Pin("COM.2"));
        }

        foreach (PinDefinition pin in Definition.Pins)
        {
            Circuit.Watch(Pin(pin.Name), Integrate);
        }
    }

    public override void OnCircuitReady()
    {
        _since = _frameStart = Context.Engine.Clock.Now;
        Integrate();
        Context.Engine.Clock.Schedule(Frame, EndFrame);
    }

    // The share of time each segment glows, accumulated since the last change.
    private void Integrate()
    {
        ulong now = Context.Engine.Clock.Now;
        double elapsed = now - _since;
        for (int i = 0; i < _share.Length; i++)
        {
            _lit[i] += _share[i] * elapsed;
        }

        _since = now;
        for (int digit = 0; digit < Digits; digit++)
        {
            double common = High(Net(Common(digit)));
            for (int segment = 0; segment < 8; segment++)
            {
                double line = High(Net(SegmentPins[segment]));
                _share[(digit * 8) + segment] = CommonAnode ? common * (1 - line) : line * (1 - common);
            }
        }
    }

    // Fraction of time the net sits at a logic HIGH (its PWM duty); 0 while it floats or is low.
    private static double High(NetState net) => net.HasVoltage && net.Voltage >= 2.5 ? net.Duty : 0;

    private void EndFrame()
    {
        Integrate();
        ulong now = Context.Engine.Clock.Now;
        double length = Math.Max(1, now - _frameStart);
        bool changed = false;
        for (int i = 0; i < _shown.Length; i++)
        {
            byte level = (byte)Math.Round(Math.Clamp(_lit[i] / length, 0, 1) * 255);
            changed |= level != _shown[i];
            _shown[i] = level;
            _lit[i] = 0;
        }

        _frameStart = now;
        if (changed)
        {
            _text = Read(_shown, Digits);
            _version++;
            NotifyDisplayChanged();
        }

        Context.Engine.Clock.Schedule(Frame, EndFrame);
    }

    /// <summary>The digits as text: segments at least half as bright as the brightest count as lit.</summary>
    public static string Read(byte[] levels, int digits)
    {
        ArgumentNullException.ThrowIfNull(levels);
        byte brightest = levels.Length == 0 ? (byte)0 : levels.Max();
        int threshold = Math.Max(16, brightest / 2);
        var text = new StringBuilder();
        for (int digit = 0; digit < digits; digit++)
        {
            int mask = 0;
            for (int segment = 0; segment < 7; segment++)
            {
                mask |= levels[(digit * 8) + segment] >= threshold ? 1 << segment : 0;
            }

            text.Append(SegmentFont.Character(mask));
            if (levels[(digit * 8) + 7] >= threshold)
            {
                text.Append('.');
            }
        }

        return text.ToString();
    }
}

/// <summary>
/// TM1637 four-digit display module with a colon: a two-wire bus (CLK, DIO with 10 kΩ pull-ups on
/// the module), START and STOP like I2C, bytes LSB first latched on CLK rising, an ACK pulled LOW
/// from the eighth falling edge to the ninth. Commands: data (0x40 auto-increment, 0x44 fixed
/// address), address (0xC0-0xC5) followed by digit bytes, display control (0x80 | on | brightness).
/// The colon is bit 7 of the second digit. Key scanning (0x42) is not simulated.
/// </summary>
[SimComponent]
public sealed class Tm1637 : SimComponent
{
    /// <summary>Pulse widths of the eight brightness levels, in sixteenths (datasheet).</summary>
    private static readonly int[] Widths = [1, 2, 4, 10, 11, 12, 13, 14];

    private readonly byte[] _digits = new byte[6];
    private Driver _ack = null!;
    private bool _clk = true;
    private bool _dio = true;
    private bool _active;
    private int _bits;
    private int _shift;
    private bool _acking;
    private int _bytes;
    private int _address;
    private bool _fixed;
    private bool _on;
    private int _brightness;
    private long _version;
    private DisplayFrame? _frame;

    public Tm1637(string id, ComponentProperties properties)
        : base(id, Spec, properties)
    {
    }

    public static ComponentDefinition Spec { get; } = new(
        "tm1637",
        "Part_Tm1637",
        "display",
        120,
        60,
        [
            new PinDefinition("CLK", PinKind.Digital, 30, 60),
            new PinDefinition("DIO", PinKind.Digital, 40, 60),
            new PinDefinition("VCC", PinKind.Power, 50, 60),
            new PinDefinition("GND", PinKind.Ground, 60, 60),
        ],
        [new PropertyDefinition("color", PropertyKind.Choice, "red", ["red", "green", "blue", "yellow", "white"])]);

    private bool Powered => PartSupply.Across(Net("VCC"), Net("GND")) >= 3;

    public override DisplayFrame Display => _frame is { } frame && frame.Version == _version ? frame : _frame = Render();

    protected override void OnAttach()
    {
        _ack = Circuit.AddDriver(Pin("DIO"), Id);
        Circuit.AddResistor(Pin("CLK"), Pin("VCC"), 10_000, Id);
        Circuit.AddResistor(Pin("DIO"), Pin("VCC"), 10_000, Id);
        Circuit.Watch(Pin("CLK"), OnBus);
        Circuit.Watch(Pin("DIO"), OnBus);
        WatchPins("VCC", "GND");
    }

    public override void OnCircuitReady() => Refresh();

    // Power: without it the module shows nothing and forgets its digits.
    protected override void Refresh()
    {
        if (!Powered && (_on || _digits.Any(d => d != 0)))
        {
            Array.Clear(_digits);
            _on = false;
            Changed();
        }
    }

    private void OnBus()
    {
        bool clk = Level("CLK");
        bool dio = Level("DIO");
        bool clkBefore = _clk;
        bool dioBefore = _dio;
        _clk = clk;
        _dio = dio;
        if (!Powered)
        {
            return;
        }

        if (clk && clkBefore && dio != dioBefore)
        {
            // DIO moving while CLK is high: START (falling) or STOP (rising).
            _active = !dio;
            _bits = 0;
            _shift = 0;
            _bytes = 0;
            Ack(false);
            return;
        }

        if (!_active || clk == clkBefore)
        {
            return;
        }

        if (clk && _bits < 8)
        {
            _shift |= (dio ? 1 : 0) << _bits++;
        }
        else if (!clk && _bits == 8 && !_acking)
        {
            Ack(true);
        }
        else if (!clk && _acking)
        {
            Ack(false);
            Received((byte)_shift);
            _bits = 0;
            _shift = 0;
        }
    }

    private bool Level(string pin) => Net(pin) is { HasVoltage: true } net && net.Voltage >= 1.5;

    private void Ack(bool pull)
    {
        _acking = pull;
        _ack.Set(pull ? NetDrive.Strong(0) : NetDrive.None);
    }

    private void Received(byte value)
    {
        if (_bytes++ == 0)
        {
            switch (value & 0xC0)
            {
                case 0x40:
                    _fixed = (value & 0x04) != 0;
                    if ((value & 0x02) != 0)
                    {
                        Context.Log.ReportOnce($"tm1637-keys-{Id}", SimSeverity.Warning, "tm1637-keys", $"{Id}: the TM1637's key scan is not simulated; reading keys returns no key.", Id);
                        _active = false;
                    }

                    return;
                case 0xC0:
                    _address = value & 0x07;
                    return;
                case 0x80:
                    _on = (value & 0x08) != 0;
                    _brightness = value & 0x07;
                    Changed();
                    return;
                default:
                    return;
            }
        }

        if (_address < _digits.Length)
        {
            _digits[_address] = value;
            Changed();
        }

        if (!_fixed)
        {
            _address++;
        }
    }

    private void Changed()
    {
        _version++;
        NotifyDisplayChanged();
    }

    private DisplayFrame Render()
    {
        var levels = new byte[4 * 8];
        byte level = _on && Powered ? (byte)(Widths[_brightness] * 255 / 14) : (byte)0;
        for (int digit = 0; digit < 4; digit++)
        {
            for (int segment = 0; segment < 8; segment++)
            {
                levels[(digit * 8) + segment] = (_digits[digit] & (1 << segment)) != 0 ? level : (byte)0;
            }
        }

        string text = level == 0 ? "    " : string.Concat(
            SegmentFont.Character(_digits[0]),
            SegmentFont.Character(_digits[1]),
            (_digits[1] & 0x80) != 0 ? ":" : "",
            SegmentFont.Character(_digits[2]),
            SegmentFont.Character(_digits[3]));
        DisplayStatus status = (Powered ? DisplayStatus.Powered : 0) | (_on ? DisplayStatus.On : 0);
        return new DisplayFrame(DisplayKind.Segments, 4, 1, status, level / 255.0, levels, [text], _version);
    }
}
