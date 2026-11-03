using System.Collections.Concurrent;
using ArduSim.Core.Buses;
using ArduSim.Core.Circuits;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Displays;

namespace ArduSim.Core.Components.Parts;

/// <summary>
/// A color TFT module on 4-wire SPI (ADR-013): the controller, the module's glass (<see cref="TftPanel"/>),
/// its power (3.3-5 V on VCC through the module's regulator), RESET, and the backlight pin, whose
/// level (or PWM duty) dims the LEDs. Bytes come whole from the hardware bus while CS is LOW (always,
/// for modules without CS), or bit by bit on the rising edges of the clock pin (software SPI); D/C is
/// read with each byte. Reads answer on SDO, where the module has one.
/// </summary>
public abstract class TftModule : SimComponent, ISpiDevice
{
    private const double HighThreshold = 1.5;

    private readonly Pins _pins;
    private readonly bool _lightPulledUp;
    private TftController _controller = null!;
    private bool _powered;
    private bool _inReset;
    private bool _clock;
    private int _shift;
    private int _bits;
    private long _version;
    private DisplayFrame? _frame;

    /// <param name="lightPulledUp">The module pulls the backlight pin up (on while it is not connected).</param>
    protected TftModule(string id, ComponentDefinition definition, ComponentProperties properties, Pins pins, bool lightPulledUp, TftPanel panel)
        : base(id, definition, properties)
    {
        ArgumentNullException.ThrowIfNull(pins);
        ArgumentNullException.ThrowIfNull(panel);
        _pins = pins;
        _lightPulledUp = lightPulledUp;
        Panel = panel;
    }

    public TftPanel Panel { get; }

    public TftController Controller => _controller;

    public bool Powered => _powered;

    /// <summary>0 (dark) to 1: the backlight now.</summary>
    public double Backlight { get; private set; }

    /// <summary>Pixels of the glass that are not black now (backlight ignored).</summary>
    public int LitPixels
    {
        get
        {
            ReadOnlySpan<byte> rgb = Display.Dots.Span;
            int lit = 0;
            for (int i = 0; i + 2 < rgb.Length; i += 3)
            {
                lit += (rgb[i] | rgb[i + 1] | rgb[i + 2]) != 0 ? 1 : 0;
            }

            return lit;
        }
    }

    public override DisplayFrame Display => _frame is { } frame && frame.Version == _version ? frame : _frame = Render();

    PinRef ISpiDevice.Sck => Pin(_pins.Clock);

    PinRef ISpiDevice.Mosi => Pin(_pins.Data);

    PinRef ISpiDevice.ChipSelect => Pin(_pins.Select ?? _pins.Clock);

    bool ISpiDevice.HasChipSelect => _pins.Select is not null;

    byte ISpiDevice.OnTransfer(byte mosi)
    {
        if (!Ready())
        {
            return 0xFF;
        }

        if (!High(_pins.Command))
        {
            _controller.Command(mosi);
            return 0xFF;
        }

        byte answer = _controller.Read();
        _controller.Data(mosi);
        return _pins.Out is { } sdo && Circuit.NetOf(Pin(sdo)).Pins.Count > 1 ? answer : (byte)0xFF;
    }

    /// <summary>Pins of a module, by role. <paramref name="Select"/> is null when CS is tied low on the board; <paramref name="Out"/> when there is no SDO.</summary>
    protected sealed record Pins(string Clock, string Data, string Command, string? Select, string Reset, string Light, string? Out);

    protected static PinDefinition[] Header(IReadOnlyList<string> names, double width)
    {
        double left = Math.Round((width / 2 / 10) - ((names.Count - 1) / 2.0)) * 10;
        return [.. names.Select((name, i) => new PinDefinition(
            name,
            name switch
            {
                "GND" => PinKind.Ground,
                "VCC" => PinKind.Power,
                _ => PinKind.Digital,
            },
            left + (10 * i),
            0))];
    }

    protected override void OnAttach()
    {
        _controller = new TftController(Panel.Chip, Panel.RamWidth, Panel.RamHeight);
        _controller.Changed += Invalidate;
        WatchPins("VCC", "GND", _pins.Reset, _pins.Light);
        Context.Spi?.Register(this);
        Circuit.Watch(Pin(_pins.Clock), OnClock);
        if (_pins.Select is { } select)
        {
            Circuit.Watch(Pin(select), OnChipSelect);
        }
    }

    public override void OnCircuitReady()
    {
        base.OnCircuitReady();
        if (!_powered)
        {
            Context.Log.ReportOnce($"tft-power-{Id}", SimSeverity.Warning, "tft-power", $"{Id} has no power: connect VCC to 5V (or 3V3) and GND to GND.", Id);
        }
        else if (Backlight == 0 && !Net(_pins.Light).HasVoltage)
        {
            Context.Log.ReportOnce($"tft-light-{Id}", SimSeverity.Info, "tft-light", $"{Id}: the backlight pin {_pins.Light} is not connected, so the screen stays dark; connect it to 3V3 or to an output pin.", Id);
        }
    }

    protected override void Refresh()
    {
        bool powered = Net("VCC") is { HasVoltage: true, Voltage: >= 3.0 } && Net("GND") is { HasVoltage: true, Voltage: <= 0.5 };
        bool reset = Net(_pins.Reset) is { HasVoltage: true } res && res.Voltage < HighThreshold;
        if (powered != _powered || (reset && !_inReset))
        {
            _controller.Reset(); // power-on, or RESET pulled low
            _bits = 0;
        }

        _powered = powered;
        _inReset = reset;
        Backlight = !powered ? 0 : LightLevel(Net(_pins.Light));
        Invalidate();
    }

    private double LightLevel(NetState light)
    {
        if (!light.HasVoltage)
        {
            return _lightPulledUp ? 1 : 0;
        }

        return light.Voltage < HighThreshold ? 0 : Math.Clamp(light.Duty, 0, 1);
    }

    // ---- software SPI: bits on the rising edge of the clock pin -----------------------------------

    private void OnClock()
    {
        bool clock = High(_pins.Clock);
        bool rising = clock && !_clock;
        _clock = clock;
        if (!rising || !Ready() || !Selected())
        {
            return;
        }

        _shift = ((_shift << 1) | (High(_pins.Data) ? 1 : 0)) & 0xFF;
        if (++_bits == 8)
        {
            _bits = 0;
            if (High(_pins.Command))
            {
                _controller.Data((byte)_shift);
            }
            else
            {
                _controller.Command((byte)_shift);
            }
        }
    }

    private void OnChipSelect()
    {
        if (!Selected())
        {
            _bits = 0;
        }
    }

    private void Invalidate()
    {
        _version++;
        NotifyDisplayChanged();
    }

    private DisplayFrame Render()
    {
        bool on = _powered && !_inReset && _controller.DisplayOn && !_controller.Sleeping;
        DisplayStatus status = (_powered ? DisplayStatus.Powered : 0) | (on ? DisplayStatus.On : 0) | (Backlight > 0 ? DisplayStatus.Backlight : 0);
        var rgb = new byte[Panel.Width * Panel.Height * 3];
        if (_powered)
        {
            _controller.Render(Panel, rgb);
        }

        return new DisplayFrame(DisplayKind.ColorTft, Panel.Width, Panel.Height, status, Backlight, rgb, [], _version);
    }

    private bool Ready() => _powered && !_inReset;

    private bool Selected() => _pins.Select is not { } select || (Net(select) is { HasVoltage: true } cs && cs.Voltage < HighThreshold);

    private bool High(string pin) => Net(pin) is { HasVoltage: true } state && state.Voltage >= HighThreshold;
}

/// <summary>ST7735S modules: the 1.8" 128x160 (Adafruit's black tab and green tab), the 1.44" 128x128 and the 0.96" 80x160.</summary>
[SimComponent]
public sealed class St7735 : TftModule
{
    public const string TypeName = "st7735";

    private static readonly string[] Names = ["VCC", "GND", "CS", "RESET", "A0", "SDA", "SCK", "LED"];
    private static readonly ConcurrentDictionary<string, ComponentDefinition> Layouts = new();

    private static readonly ComponentDefinition Base = new(
        TypeName,
        "Part_St7735",
        "display",
        140,
        220,
        Header(Names, 140),
        [
            new PropertyDefinition("module", PropertyKind.Choice, "st7735-128x160", ["st7735-128x160", "st7735-128x160-green", "st7735-128x128", "st7735-80x160"]),
        ]);

    public St7735(string id, ComponentProperties properties)
        : base(id, LayoutFor(properties), properties, new Pins("SCK", "SDA", "A0", "CS", "RESET", "LED", null), lightPulledUp: false, TftPanel.For(properties.Choice("module")))
    {
    }

    public static ComponentDefinition Spec { get; } = Base with
    {
        Layout = LayoutFor,
        Presets =
        [
            new ComponentPreset("st7735-18", "Preset_St7735_18", new Dictionary<string, string> { ["module"] = "st7735-128x160" }),
            new ComponentPreset("st7735-144", "Preset_St7735_144", new Dictionary<string, string> { ["module"] = "st7735-128x128" }),
            new ComponentPreset("st7735-096", "Preset_St7735_096", new Dictionary<string, string> { ["module"] = "st7735-80x160" }),
        ],
    };

    public static ComponentDefinition LayoutFor(ComponentProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return Layouts.GetOrAdd(properties.Choice("module"), module =>
        {
            (double width, double height) = module switch
            {
                "st7735-128x128" => (130.0, 170.0),
                "st7735-80x160" => (100.0, 150.0),
                _ => (140.0, 220.0),
            };
            return Base with { Width = width, Height = height, Pins = Header(Names, width), Layout = LayoutFor };
        });
    }
}

/// <summary>ST7789V IPS modules: 1.3" 240x240 (no CS pin), 2.0" 240x320 and 1.14" 135x240.</summary>
[SimComponent]
public sealed class St7789 : TftModule
{
    public const string TypeName = "st7789";

    private static readonly string[] Names = ["GND", "VCC", "SCL", "SDA", "RES", "DC", "CS", "BLK"];
    private static readonly string[] NamesWithoutCs = ["GND", "VCC", "SCL", "SDA", "RES", "DC", "BLK"];
    private static readonly ConcurrentDictionary<string, ComponentDefinition> Layouts = new();

    private static readonly ComponentDefinition Base = new(
        TypeName,
        "Part_St7789",
        "display",
        110,
        150,
        Header(NamesWithoutCs, 110),
        [
            new PropertyDefinition("module", PropertyKind.Choice, "st7789-240x240", ["st7789-240x240", "st7789-240x320", "st7789-135x240"]),
        ]);

    public St7789(string id, ComponentProperties properties)
        : base(id, LayoutFor(properties), properties, PinsFor(properties), lightPulledUp: true, TftPanel.For(properties.Choice("module")))
    {
    }

    public static ComponentDefinition Spec { get; } = Base with
    {
        Layout = LayoutFor,
        Presets =
        [
            new ComponentPreset("st7789-130", "Preset_St7789_130", new Dictionary<string, string> { ["module"] = "st7789-240x240" }),
            new ComponentPreset("st7789-200", "Preset_St7789_200", new Dictionary<string, string> { ["module"] = "st7789-240x320" }),
            new ComponentPreset("st7789-114", "Preset_St7789_114", new Dictionary<string, string> { ["module"] = "st7789-135x240" }),
        ],
    };

    public static ComponentDefinition LayoutFor(ComponentProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return Layouts.GetOrAdd(properties.Choice("module"), module =>
        {
            (double width, double height, string[] names) = module switch
            {
                "st7789-240x320" => (140.0, 230.0, Names),
                "st7789-135x240" => (90.0, 150.0, Names),
                _ => (110.0, 150.0, NamesWithoutCs),
            };
            return Base with { Width = width, Height = height, Pins = Header(names, width), Layout = LayoutFor };
        });
    }

    private static Pins PinsFor(ComponentProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return new Pins("SCL", "SDA", "DC", properties.Choice("module") == "st7789-240x240" ? null : "CS", "RES", "BLK", null);
    }
}

/// <summary>The 2.4"/2.8" ILI9341 240x320 module (the SPI header; the touch controller's pins are left out).</summary>
[SimComponent]
public sealed class Ili9341 : TftModule
{
    public const string TypeName = "ili9341";

    private static readonly string[] Names = ["VCC", "GND", "CS", "RESET", "DC", "SDI", "SCK", "LED", "SDO"];

    public Ili9341(string id, ComponentProperties properties)
        : base(id, Spec, properties, new Pins("SCK", "SDI", "DC", "CS", "RESET", "LED", "SDO"), lightPulledUp: false, TftPanel.For("ili9341-240x320"))
    {
    }

    public static ComponentDefinition Spec { get; } = new(
        TypeName,
        "Part_Ili9341",
        "display",
        200,
        340,
        Header(Names, 200),
        []);
}
