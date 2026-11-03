using ArduSim.Core.Buses;
using ArduSim.Core.Circuits;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Displays;

namespace ArduSim.Core.Components.Parts;

/// <summary>
/// The Nokia 5110 module: a PCD8544 driving an 84x48 LCD on 3-wire SPI plus D/C (ADR-013). Bytes
/// come whole from the hardware bus while CE is LOW, or bit by bit on the rising edges of CLK. The
/// glass's contrast follows V<sub>LCD</sub> from V<sub>OP</sub>: too low and nothing shows, too high
/// and the background darkens. The LIGHT pin switches the four backlight LEDs (active level a
/// property: HIGH on the common blue boards, LOW on red boards that tie the LEDs to VCC).
/// </summary>
[SimComponent]
public sealed class Pcd8544 : SimComponent, ISpiDevice
{
    public const string TypeName = "pcd8544";

    private const double HighThreshold = 1.5;
    private static readonly string[] Names = ["RST", "CE", "DC", "DIN", "CLK", "VCC", "LIGHT", "GND"];

    private Pcd8544Controller _controller = null!;
    private bool _powered;
    private bool _inReset;
    private bool _clock;
    private int _shift;
    private int _bits;
    private long _version;
    private DisplayFrame? _frame;

    public Pcd8544(string id, ComponentProperties properties)
        : base(id, Spec, properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        LightActiveLow = properties.Choice("light") == "low";
    }

    public static ComponentDefinition Spec { get; } = new(
        TypeName,
        "Part_Pcd8544",
        "display",
        170,
        170,
        [.. Names.Select((name, i) => new PinDefinition(name, name switch { "GND" => PinKind.Ground, "VCC" => PinKind.Power, _ => PinKind.Digital }, 50 + (10 * i), 0))],
        [new PropertyDefinition("light", PropertyKind.Choice, "high", ["high", "low"])]);

    public bool LightActiveLow { get; }

    public Pcd8544Controller Controller => _controller;

    public bool Powered => _powered;

    /// <summary>0 (off) to 1: the backlight now.</summary>
    public double Backlight { get; private set; }

    /// <summary>Pixels at least half dark now.</summary>
    public int DarkPixels
    {
        get
        {
            int dark = 0;
            foreach (byte level in Display.Dots.Span)
            {
                dark += level >= 128 ? 1 : 0;
            }

            return dark;
        }
    }

    public override DisplayFrame Display => _frame is { } frame && frame.Version == _version ? frame : _frame = Render();

    PinRef ISpiDevice.Sck => Pin("CLK");

    PinRef ISpiDevice.Mosi => Pin("DIN");

    PinRef ISpiDevice.ChipSelect => Pin("CE");

    byte ISpiDevice.OnTransfer(byte mosi)
    {
        if (Ready())
        {
            Deliver(mosi);
        }

        return 0xFF; // the PCD8544 has no data output
    }

    /// <summary>
    /// How dark a driven pixel is (on) and how dark an undriven one is (off), from V<sub>LCD</sub>:
    /// the glass needs about 6-7 V for full contrast at 1:48 multiplex (Adafruit's default V<sub>OP</sub>
    /// of 50 gives 6.06 V); far above that the unselected pixels darken too.
    /// </summary>
    public static (double On, double Off) Contrast(int vop)
    {
        double vlcd = 3.06 + (0.06 * vop);
        return (Math.Clamp((vlcd - 4.5) / 2.0, 0, 1), Math.Clamp((vlcd - 7.5) / 2.0, 0, 0.9));
    }

    protected override void OnAttach()
    {
        _controller = new Pcd8544Controller();
        _controller.Changed += Invalidate;
        WatchPins("VCC", "GND", "RST", "LIGHT");
        Context.Spi?.Register(this);
        Circuit.Watch(Pin("CLK"), OnClock);
        Circuit.Watch(Pin("CE"), OnChipSelect);
    }

    public override void OnCircuitReady()
    {
        base.OnCircuitReady();
        if (!_powered)
        {
            Context.Log.ReportOnce($"lcd5110-power-{Id}", SimSeverity.Warning, "lcd5110-power", $"{Id} has no power: connect VCC to 3V3 and GND to GND.", Id);
        }
        else if (Net("VCC").Voltage > 3.6)
        {
            Context.Log.ReportOnce($"lcd5110-supply-{Id}", SimSeverity.Info, "lcd5110-supply", $"{Id}: VCC is {Net("VCC").Voltage:0.0} V, above the PCD8544's 2.7-3.3 V operating range; the simulation runs it anyway.", Id);
        }
    }

    protected override void Refresh()
    {
        bool powered = Net("VCC") is { HasVoltage: true, Voltage: >= 2.7 } && Net("GND") is { HasVoltage: true, Voltage: <= 0.5 };
        bool reset = Net("RST") is { HasVoltage: true } res && res.Voltage < HighThreshold;
        if (powered != _powered || (reset && !_inReset))
        {
            _controller.Reset(); // power-on, or RST pulled low
            _bits = 0;
        }

        _powered = powered;
        _inReset = reset;
        Backlight = powered ? LightLevel(Net("LIGHT")) : 0;
        Invalidate();
    }

    private double LightLevel(NetState light)
    {
        if (!light.HasVoltage)
        {
            return 0;
        }

        bool high = light.Voltage >= HighThreshold;
        if (LightActiveLow)
        {
            return high ? (light.Duty < 1 ? 1 - light.Duty : 0) : 1;
        }

        return high ? Math.Clamp(light.Duty, 0, 1) : 0;
    }

    private void OnClock()
    {
        bool clock = High("CLK");
        bool rising = clock && !_clock;
        _clock = clock;
        if (!rising || !Ready() || !Selected())
        {
            return;
        }

        _shift = ((_shift << 1) | (High("DIN") ? 1 : 0)) & 0xFF;
        if (++_bits == 8)
        {
            _bits = 0;
            Deliver((byte)_shift);
        }
    }

    private void OnChipSelect()
    {
        if (!Selected())
        {
            _bits = 0;
        }
    }

    private void Deliver(byte value)
    {
        if (High("DC"))
        {
            _controller.Data(value);
        }
        else
        {
            _controller.Command(value);
        }
    }

    private void Invalidate()
    {
        _version++;
        NotifyDisplayChanged();
    }

    private DisplayFrame Render()
    {
        bool on = _powered && !_inReset && !_controller.PowerDown && _controller.Mode != 0;
        DisplayStatus status = (_powered ? DisplayStatus.Powered : 0) | (on ? DisplayStatus.On : 0) | (Backlight > 0 ? DisplayStatus.Backlight : 0);
        var dots = new byte[Pcd8544Controller.Columns * 48];
        if (_powered && !_inReset)
        {
            (double onLevel, double offLevel) = Contrast(_controller.Vop);
            byte darkOn = (byte)Math.Round(onLevel * 255);
            byte darkOff = (byte)Math.Round(offLevel * 255);
            for (int y = 0; y < 48; y++)
            {
                for (int x = 0; x < Pcd8544Controller.Columns; x++)
                {
                    dots[(y * Pcd8544Controller.Columns) + x] = _controller.PowerDown ? (byte)0 : _controller.Dark(x, y) ? darkOn : darkOff;
                }
            }
        }

        return new DisplayFrame(DisplayKind.GraphicLcd, Pcd8544Controller.Columns, 48, status, Backlight, dots, [], _version);
    }

    private bool Ready() => _powered && !_inReset;

    private bool Selected() => Net("CE") is { HasVoltage: true } ce && ce.Voltage < HighThreshold;

    private bool High(string pin) => Net(pin) is { HasVoltage: true } state && state.Voltage >= HighThreshold;
}
