using System.Collections.Concurrent;
using ArduSim.Core.Buses;
using ArduSim.Core.Circuits;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Displays;
using ArduSim.Core.Timing;
using System.Numerics;

namespace ArduSim.Core.Components.Parts;

/// <summary>
/// An OLED module (ADR-006, ADR-013): SSD1306 (128x64, 128x32, 64x48, 96x16), SH1106 (128x64),
/// SSD1309 (2.42" 128x64) or the grayscale SSD1327 (1.5" 128x128), over I2C (control byte with Co
/// and D/C) or 4-wire SPI (D0 = SCK, D1 = MOSI, DC, CS, RES). SPI
/// bytes arrive whole from the hardware bus while CS is LOW, or bit by bit on the rising edges of
/// D0 (software SPI, shiftOut). The module needs VCC (3.3-5 V) and GND; I2C modules carry 4.7 kΩ
/// pull-ups, SPI modules a pull-up on RES.
/// </summary>
[SimComponent]
public sealed class Oled : SimComponent, II2cDevice, ISpiDevice
{
    public const string TypeName = "oled";

    private const double HighThreshold = 1.5;
    private const double BackpackPullUp = 4_700;

    private static readonly string[] I2cHeader = ["GND", "VCC", "SCL", "SDA"];
    private static readonly string[] SpiHeader = ["GND", "VCC", "D0", "D1", "RES", "DC", "CS"];
    private static readonly ConcurrentDictionary<(string Chip, string Size, string Interface), ComponentDefinition> Layouts = new();

    private static readonly ComponentDefinition Base = new(
        TypeName,
        "Part_Oled",
        "display",
        110,
        110,
        Header(I2cHeader, 110),
        [
            new PropertyDefinition("controller", PropertyKind.Choice, "ssd1306", ["ssd1306", "sh1106", "ssd1309", "ssd1327"]),
            new PropertyDefinition("size", PropertyKind.Choice, "128x64", ["128x64", "128x32", "64x48", "96x16"]) { ShownWhen = ("controller", "ssd1306") },
            new PropertyDefinition("interface", PropertyKind.Choice, "i2c", ["i2c", "spi"]),
            new PropertyDefinition("address", PropertyKind.Choice, "0x3C", ["0x3C", "0x3D"]) { ShownWhen = ("interface", "i2c") },
            new PropertyDefinition("panel", PropertyKind.Choice, "white", ["white", "blue", "yellow-blue"]),
        ]);

    private IOledController _controller = null!;
    private ScheduledEvent? _scroll;
    private bool _powered;
    private bool _inReset;
    private bool _clock;
    private int _shift;
    private int _bits;
    private bool _expectControl;
    private bool _continuation;
    private bool _dataStream;
    private long _version;
    private DisplayFrame? _frame;

    public Oled(string id, ComponentProperties properties)
        : base(id, LayoutFor(properties), properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        Chip = properties.Choice("controller") switch
        {
            "sh1106" => OledChip.Sh1106,
            "ssd1309" => OledChip.Ssd1309,
            "ssd1327" => OledChip.Ssd1327,
            _ => OledChip.Ssd1306,
        };
        Panel = OledPanel.For(Chip, properties.Choice("size"));
        IsI2c = properties.Choice("interface") == "i2c";
        Address = Convert.ToInt32(properties.Choice("address"), 16);
    }

    public static ComponentDefinition Spec { get; } = Base with
    {
        Layout = LayoutFor,
        Presets =
        [
            new ComponentPreset("ssd1306-128x64-i2c", "Preset_Oled12864I2c", new Dictionary<string, string> { ["interface"] = "i2c" }),
            new ComponentPreset("ssd1306-128x32-i2c", "Preset_Oled12832I2c", new Dictionary<string, string> { ["size"] = "128x32", ["interface"] = "i2c" }),
            new ComponentPreset("ssd1306-128x64-spi", "Preset_Oled12864Spi", new Dictionary<string, string> { ["interface"] = "spi" }),
            new ComponentPreset("sh1106-i2c", "Preset_Sh1106I2c", new Dictionary<string, string> { ["controller"] = "sh1106", ["interface"] = "i2c" }),
            new ComponentPreset("sh1106-spi", "Preset_Sh1106Spi", new Dictionary<string, string> { ["controller"] = "sh1106", ["interface"] = "spi" }),
            new ComponentPreset("ssd1309-spi", "Preset_Ssd1309Spi", new Dictionary<string, string> { ["controller"] = "ssd1309", ["interface"] = "spi" }),
            new ComponentPreset("ssd1327-i2c", "Preset_Ssd1327I2c", new Dictionary<string, string> { ["controller"] = "ssd1327", ["interface"] = "i2c", ["address"] = "0x3D" }),
        ],
    };

    public OledChip Chip { get; }

    public OledPanel Panel { get; }

    public bool IsI2c { get; }

    /// <summary>The I2C address (0x3C, or 0x3D with SA0 high).</summary>
    public int Address { get; }

    public IOledController Controller => _controller;

    public bool Powered => _powered;

    PinRef II2cDevice.Sda => Pin("SDA");

    PinRef II2cDevice.Scl => Pin("SCL");

    PinRef ISpiDevice.Sck => Pin("D0");

    PinRef ISpiDevice.Mosi => Pin("D1");

    PinRef ISpiDevice.ChipSelect => Pin("CS");

    /// <summary>Pixels lit on the glass now.</summary>
    public int LitPixels => _controller.Kind == DisplayKind.GrayOled
        ? Display.Dots.ToArray().Count(level => level > 0)
        : Display.Dots.ToArray().Sum(b => BitOperations.PopCount(b));

    public override DisplayFrame Display => _frame is { } frame && frame.Version == _version ? frame : _frame = Render();

    public static ComponentDefinition LayoutFor(ComponentProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        string chip = properties.Choice("controller");
        string size = chip == "ssd1306" ? properties.Choice("size") : "128x64";
        return Layouts.GetOrAdd((chip, size, properties.Choice("interface")), key =>
        {
            (double width, double height) = key.Chip switch
            {
                "sh1106" => (140.0, 130.0),
                "ssd1309" => (240.0, 170.0),
                "ssd1327" => (150.0, 160.0),
                _ => key.Size switch
                {
                    "128x32" => (150.0, 60.0),
                    "64x48" => (100.0, 90.0),
                    "96x16" => (130.0, 60.0),
                    _ => (110.0, 110.0),
                },
            };
            return Base with { Width = width, Height = height, Pins = Header(key.Interface == "spi" ? SpiHeader : I2cHeader, width), Layout = LayoutFor };
        });
    }

    // ---- I2C ---------------------------------------------------------------------------------------

    bool II2cDevice.OnStart(bool read)
    {
        if (!Ready())
        {
            return false;
        }

        _expectControl = true;
        return true;
    }

    bool II2cDevice.OnWrite(byte value)
    {
        if (!Ready())
        {
            return false;
        }

        if (_expectControl)
        {
            // Control byte: Co (bit 7) = one more byte follows, then another control byte; D/C# (bit 6) = data.
            _continuation = (value & 0x80) != 0;
            _dataStream = (value & 0x40) != 0;
            _expectControl = false;
            return true;
        }

        Deliver(_dataStream, value);
        if (_continuation)
        {
            _expectControl = true;
        }

        return true;
    }

    byte II2cDevice.OnRead(bool ack) => _controller.Status;

    void II2cDevice.OnStop() => _expectControl = true;

    // ---- SPI ---------------------------------------------------------------------------------------

    byte ISpiDevice.OnTransfer(byte mosi)
    {
        if (Ready())
        {
            Deliver(High("DC"), mosi);
        }

        return 0xFF; // the controller does not drive MISO
    }

    // ---- attach ------------------------------------------------------------------------------------

    protected override void OnAttach()
    {
        _controller = Chip == OledChip.Ssd1327 ? new Ssd1327Controller() : new OledController(Chip);
        _controller.Changed += Invalidate;
        _controller.UnknownCommand += command => Context.Log.ReportOnce(
            $"oled-command-{Id}-{command}",
            SimSeverity.Info,
            "oled-command",
            $"{Id} ({Chip}) received command 0x{command:X2}, which it does not have; it is ignored (a sketch set up for another controller?).",
            Id);
        WatchPins("VCC", "GND");
        if (IsI2c)
        {
            Circuit.AddResistor(Pin("SDA"), Pin("VCC"), BackpackPullUp, Id);
            Circuit.AddResistor(Pin("SCL"), Pin("VCC"), BackpackPullUp, Id);
            Context.I2c?.Register(this);
        }
        else
        {
            Context.Spi?.Register(this);
            Circuit.Watch(Pin("D0"), OnClock);
            Circuit.Watch(Pin("CS"), OnChipSelect);
            WatchPins("RES");
        }
    }

    public override void OnCircuitReady()
    {
        base.OnCircuitReady();
        if (!_powered)
        {
            ReportNoPower();
        }
    }

    protected override void Refresh()
    {
        bool powered = Net("VCC") is { HasVoltage: true, Voltage: >= 3.0 } && Net("GND") is { HasVoltage: true, Voltage: <= 0.5 };
        bool reset = !IsI2c && Net("RES") is { HasVoltage: true } res && res.Voltage < HighThreshold;
        if (powered != _powered || (reset && !_inReset))
        {
            _controller.Reset(); // power-on, or RES pulled low
            _bits = 0;
        }

        _powered = powered;
        _inReset = reset;
        Invalidate();
    }

    // ---- software SPI: bits on the rising edge of D0 --------------------------------------------

    private void OnClock()
    {
        bool clock = High("D0");
        bool rising = clock && !_clock;
        _clock = clock;
        if (!rising || !Ready() || !Selected())
        {
            return;
        }

        _shift = ((_shift << 1) | (High("D1") ? 1 : 0)) & 0xFF;
        if (++_bits == 8)
        {
            _bits = 0;
            Deliver(High("DC"), (byte)_shift);
        }
    }

    private void OnChipSelect()
    {
        if (!Selected())
        {
            _bits = 0;
        }
    }

    // ---- controller and view -----------------------------------------------------------------------

    private void Deliver(bool data, byte value)
    {
        if (data)
        {
            _controller.Data(value);
            return;
        }

        bool wasOn = _controller.DisplayOn;
        _controller.Command(value);
        if (_controller.DisplayOn && !wasOn && !_controller.PanelSupply)
        {
            if (Chip == OledChip.Sh1106)
            {
                Context.Log.ReportOnce($"oled-supply-{Id}", SimSeverity.Warning, "oled-supply", $"{Id}: the display is switched on, but the DC-DC converter is off (0xAD 0x8B not sent); a module without external VCC stays dark.", Id, "sh1106");
            }
            else
            {
                Context.Log.ReportOnce($"oled-supply-{Id}", SimSeverity.Warning, "oled-supply", $"{Id}: the display is switched on, but the charge pump is off (0x8D 0x14 not sent); a module without external VCC stays dark.", Id);
            }
        }
    }

    private void Invalidate()
    {
        _version++;
        UpdateScroll();
        NotifyDisplayChanged();
    }

    private void UpdateScroll()
    {
        bool wanted = _powered && _controller.ScrollActive;
        if (wanted && _scroll is null)
        {
            _scroll = Context.Engine.Clock.Schedule(_controller.ScrollStepInterval, ScrollStep);
        }
        else if (!wanted && _scroll is not null)
        {
            _scroll.Cancel();
            _scroll = null;
        }
    }

    private void ScrollStep()
    {
        _scroll = null;
        _controller.ScrollStep(); // raises Changed, which schedules the next step while scrolling
    }

    private DisplayFrame Render()
    {
        bool on = _powered && !_inReset && _controller.DisplayOn && _controller.PanelSupply;
        DisplayStatus status = (_powered ? DisplayStatus.Powered : 0) | (on ? DisplayStatus.On : 0);
        byte[] pixels = on ? _controller.Render(Panel) : new byte[(_controller.Kind == DisplayKind.GrayOled ? Panel.Width : (Panel.Width + 7) / 8) * Panel.Height];
        return new DisplayFrame(_controller.Kind, Panel.Width, Panel.Height, status, _controller.Contrast / 255.0, pixels, [], _version);
    }

    private bool Ready() => _powered && !_inReset;

    private bool Selected() => Net("CS") is { HasVoltage: true } cs && cs.Voltage < HighThreshold;

    private bool High(string pin) => Net(pin) is { HasVoltage: true } state && state.Voltage >= HighThreshold;

    private void ReportNoPower() => Context.Log.ReportOnce(
        $"oled-power-{Id}",
        SimSeverity.Warning,
        "oled-power",
        $"{Id} has no power: connect VCC to 5V (or 3V3) and GND to GND.",
        Id);

    private static PinDefinition[] Header(string[] names, double width)
    {
        double left = Math.Round((width / 2 / 10) - ((names.Length - 1) / 2.0)) * 10;
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
}
