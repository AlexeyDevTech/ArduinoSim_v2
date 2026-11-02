using System.Collections.Concurrent;
using System.Globalization;
using ArduSim.Core.Buses;
using ArduSim.Core.Circuits;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Displays;
using ArduSim.Core.Timing;

namespace ArduSim.Core.Components.Parts;

/// <summary>
/// A character LCD module with an HD44780 controller (ADR-006): 16x2 or 20x4, wired in parallel
/// (the 16-pin header; 4-bit wiring hides D0-D3) or through a PCF8574 I2C backpack. The module needs
/// VDD on 5V and VSS on GND. Parallel: the controller latches on the falling edge of E and has
/// pull-ups on RS, RW and D0-D7, so an unconnected RW means "read"; the backlight is the A-K LED;
/// contrast follows V0 when it is wired, else the <c>contrast</c> property. I2C: the backpack has
/// 4.7 kΩ pull-ups on SDA/SCL, its own contrast trimmer (<c>contrast</c>) and a backlight bit.
/// </summary>
[SimComponent]
public sealed class Lcd : SimComponent, II2cDevice
{
    public const string TypeName = "lcd";

    private const double HighThreshold = 2.2;
    private const double ControllerPullUp = 50_000;
    private const double BackpackPullUp = 4_700;
    private const ulong BlinkInterval = 409_600 * VirtualTime.Microsecond;

    private static readonly string[] ParallelHeader = ["VSS", "VDD", "V0", "RS", "RW", "E", "D0", "D1", "D2", "D3", "D4", "D5", "D6", "D7", "A", "K"];
    private static readonly string[] DataPins = ["D0", "D1", "D2", "D3", "D4", "D5", "D6", "D7"];
    private static readonly ConcurrentDictionary<(string Size, string Interface), ComponentDefinition> Layouts = new();

    private static readonly ComponentDefinition Base = new(
        TypeName,
        "Part_Lcd",
        "display",
        320,
        140,
        I2cPins(140),
        [
            new PropertyDefinition("size", PropertyKind.Choice, "16x2", ["16x2", "20x4"]),
            new PropertyDefinition("interface", PropertyKind.Choice, "i2c", ["i2c", "parallel-4", "parallel-8"]),
            new PropertyDefinition("address", PropertyKind.Choice, "0x27", [.. Addresses()]) { ShownWhen = ("interface", "i2c") },
            new PropertyDefinition("backpack", PropertyKind.Choice, "standard", ["standard", "mjkdz"]) { ShownWhen = ("interface", "i2c") },
            new PropertyDefinition("backlight", PropertyKind.Choice, "blue", ["blue", "green", "none"]),
            new PropertyDefinition("contrast", PropertyKind.Number, "0.75", Minimum: 0, Maximum: 1),
        ]);

    private readonly Dictionary<string, Driver> _lineDrivers = new(StringComparer.Ordinal);
    private readonly Pcf8574 _expander = new();
    private Hd44780 _controller = null!;
    private Hd44780Bus _bus = null!;
    private ScheduledEvent? _blink;
    private bool _blinkPhase;
    private bool _powered;
    private bool _enable;
    private long _version;
    private DisplayFrame? _frame;

    public Lcd(string id, ComponentProperties properties)
        : base(id, LayoutFor(properties), properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        string[] size = properties.Choice("size").Split('x');
        Columns = int.Parse(size[0], CultureInfo.InvariantCulture);
        Rows = int.Parse(size[1], CultureInfo.InvariantCulture);
        Interface = properties.Choice("interface");
        Address = Convert.ToInt32(properties.Choice("address"), 16);
        Map = properties.Choice("backpack") == "mjkdz" ? LcdBackpackMap.Mjkdz : LcdBackpackMap.Standard;
        HasBacklight = properties.Choice("backlight") != "none";
    }

    /// <summary>The definition with the 16x2 I2C layout; other sizes and interfaces come from <see cref="ComponentDefinition.Layout"/>.</summary>
    public static ComponentDefinition Spec { get; } = Base with
    {
        Layout = LayoutFor,
        Presets =
        [
            new ComponentPreset("1602-i2c", "Preset_Lcd1602I2c", new Dictionary<string, string> { ["size"] = "16x2", ["interface"] = "i2c" }),
            new ComponentPreset("2004-i2c", "Preset_Lcd2004I2c", new Dictionary<string, string> { ["size"] = "20x4", ["interface"] = "i2c" }),
            new ComponentPreset("1602-parallel", "Preset_Lcd1602Parallel", new Dictionary<string, string> { ["size"] = "16x2", ["interface"] = "parallel-4" }),
            new ComponentPreset("2004-parallel", "Preset_Lcd2004Parallel", new Dictionary<string, string> { ["size"] = "20x4", ["interface"] = "parallel-4" }),
        ],
    };

    public int Columns { get; }

    public int Rows { get; }

    /// <summary><c>i2c</c>, <c>parallel-4</c> or <c>parallel-8</c>.</summary>
    public string Interface { get; }

    public bool IsI2c => Interface == "i2c";

    public LcdBackpackMap Map { get; }

    public bool HasBacklight { get; }

    public Hd44780 Controller => _controller;

    public bool Powered => _powered;

    /// <summary>The backpack's 7-bit I2C address.</summary>
    public int Address { get; }

    PinRef II2cDevice.Sda => Pin("SDA");

    PinRef II2cDevice.Scl => Pin("SCL");

    /// <summary>The visible rows as text (see <see cref="Hd44780.RowText"/>).</summary>
    public IReadOnlyList<string> Text => [.. Enumerable.Range(0, Rows).Select(r => _powered ? _controller.RowText(r, Columns, Rows) : new string(' ', Columns))];

    public override DisplayFrame Display => _frame is { } frame && frame.Version == _version ? frame : _frame = Render();

    /// <summary>The definition for a size and interface (cached, so each layout is one instance).</summary>
    public static ComponentDefinition LayoutFor(ComponentProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return Layouts.GetOrAdd((properties.Choice("size"), properties.Choice("interface")), key =>
        {
            (double width, double height) = key.Size == "20x4" ? (390.0, 240.0) : (320.0, 140.0);
            IReadOnlyList<PinDefinition> pins = key.Interface switch
            {
                "parallel-8" => [.. ParallelHeader.Select(Header)],
                "parallel-4" => [.. ParallelHeader.Where(p => p is not ("D0" or "D1" or "D2" or "D3")).Select(Header)],
                _ => I2cPins(height),
            };
            return Base with { Width = width, Height = height, Pins = pins, Layout = LayoutFor };
        });
    }

    // ---- I2C (the backpack's PCF8574) ------------------------------------------------------------

    bool II2cDevice.OnStart(bool read)
    {
        if (!_powered)
        {
            ReportNoPower();
            return false;
        }

        return true;
    }

    bool II2cDevice.OnWrite(byte value)
    {
        _expander.Write(value);
        return true;
    }

    byte II2cDevice.OnRead(bool ack) => _expander.Read(_bus.DrivenLines is { } lines ? Map.WithDataDriven(lines) : (byte)0xFF);

    void II2cDevice.OnStop()
    {
    }

    // ---- attach ----------------------------------------------------------------------------------

    protected override void OnAttach()
    {
        _controller = new Hd44780(() => Context.Engine.Clock.Now);
        _bus = new Hd44780Bus(_controller);
        _controller.Changed += Invalidate;
        _controller.WrittenWhileBusy += () => Context.Log.ReportOnce(
            $"lcd-busy-{Id}",
            SimSeverity.Warning,
            "lcd-busy",
            $"{Id} received a command while the HD44780 was still busy with the previous one; a real display may ignore it (the sketch waits too little between commands).",
            Id);
        if (IsI2c)
        {
            Circuit.AddResistor(Pin("SDA"), Pin("VCC"), BackpackPullUp, Id);
            Circuit.AddResistor(Pin("SCL"), Pin("VCC"), BackpackPullUp, Id);
            if (Context.I2c is { } bus)
            {
                bus.Register(this);
            }

            _expander.Written += OnExpanderWritten;
            WatchPins("VCC", "GND");
        }
        else
        {
            foreach (string pin in DataPins.Where(p => Definition.FindPin(p) is not null).Concat(["RS", "RW"]))
            {
                _lineDrivers[pin] = Circuit.AddDriver(Pin(pin), Id);
            }

            Circuit.Watch(Pin("E"), OnParallelLines);
            Circuit.Watch(Pin("RW"), OnParallelLines);
            WatchPins("VDD", "VSS", "A", "K", "V0");
        }
    }

    public override void OnCircuitReady()
    {
        base.OnCircuitReady();
        if (!_powered)
        {
            ReportNoPower();
        }

        if (IsI2c && Context.I2c is null)
        {
            Context.Log.ReportOnce($"lcd-i2c-{Id}", SimSeverity.Warning, "i2c-unavailable", $"{Id} is an I2C display, but the board has no hardware I2C.", Id, "display");
        }
    }

    protected override void Refresh()
    {
        bool powered = IsI2c
            ? Supplied(Net("VCC")) && Grounded(Net("GND"))
            : Supplied(Net("VDD")) && Grounded(Net("VSS"));
        if (powered != _powered)
        {
            _powered = powered;
            PowerChanged();
        }

        Invalidate();
    }

    // ---- parallel interface ----------------------------------------------------------------------

    private void OnParallelLines()
    {
        if (!_powered)
        {
            return;
        }

        int data = 0;
        for (int bit = 0; bit < 8; bit++)
        {
            string pin = DataPins[bit];
            if (Definition.FindPin(pin) is null || High(Pin(pin)))
            {
                data |= 1 << bit;
            }
        }

        bool enable = High(Pin("E"));
        bool readWrite = High(Pin("RW"));
        if (enable && !_enable && readWrite && Net("RW").Condition != NetCondition.Driven)
        {
            // Nothing drives RW: the controller's pull-up makes every E pulse a read.
            Context.Log.ReportOnce(
                $"lcd-rw-{Id}",
                SimSeverity.Warning,
                "lcd-rw",
                $"{Id}: RW is not connected, so the HD44780's pull-up keeps it in read mode and writes are ignored. Connect RW to GND.",
                Id);
        }

        _enable = enable;
        if (_bus.Update(High(Pin("RS")), readWrite, enable, data))
        {
            DriveDataLines();
        }
    }

    private void DriveDataLines()
    {
        double vdd = Net("VDD").Voltage;
        int? driven = _bus.DrivenLines;
        for (int bit = 0; bit < 8; bit++)
        {
            if (_lineDrivers.TryGetValue(DataPins[bit], out Driver? driver))
            {
                driver.Set(driven is { } lines
                    ? NetDrive.Strong((lines & (1 << bit)) != 0 ? vdd : 0)
                    : _powered ? NetDrive.Weak(vdd, ControllerPullUp) : NetDrive.None);
            }
        }
    }

    // ---- I2C backpack ----------------------------------------------------------------------------

    private void OnExpanderWritten(byte pins)
    {
        if (!_powered)
        {
            return;
        }

        _bus.Update(Map.RegisterSelect(pins), Map.ReadWrite(pins), Map.Enable(pins), Map.DataLines(pins));
        Invalidate();
    }

    // ---- power, view -----------------------------------------------------------------------------

    private void PowerChanged()
    {
        _controller.Reset();
        _bus.Reset();
        _expander.Reset();
        _enable = false;
        if (!IsI2c)
        {
            double vdd = _powered ? Net("VDD").Voltage : 0;
            foreach (Driver driver in _lineDrivers.Values)
            {
                driver.Set(_powered ? NetDrive.Weak(vdd, ControllerPullUp) : NetDrive.None);
            }
        }
    }

    private void Invalidate()
    {
        _version++;
        UpdateBlink();
        NotifyDisplayChanged();
    }

    private void UpdateBlink()
    {
        bool wanted = _powered && _controller.DisplayOn && _controller.BlinkOn;
        if (wanted && _blink is null)
        {
            _blinkPhase = true;
            _blink = Context.Engine.Clock.Schedule(BlinkInterval, Blink);
        }
        else if (!wanted && _blink is not null)
        {
            _blink.Cancel();
            _blink = null;
        }
    }

    private void Blink()
    {
        _blink = null;
        _blinkPhase = !_blinkPhase;
        if (_powered && _controller.DisplayOn && _controller.BlinkOn)
        {
            _blink = Context.Engine.Clock.Schedule(BlinkInterval, Blink);
            _version++;
            NotifyDisplayChanged();
        }
    }

    private DisplayFrame Render()
    {
        bool backlight = HasBacklight && _powered && (IsI2c ? Map.BacklightOn(_expander.Latch) : Net("A").Voltage - Net("K").Voltage >= 2.5 && Net("A").HasVoltage);
        DisplayStatus flags = (_powered ? DisplayStatus.Powered : 0) | (_powered && _controller.DisplayOn ? DisplayStatus.On : 0) | (backlight ? DisplayStatus.Backlight : 0);
        byte[] dots = _powered ? _controller.Render(Columns, Rows, _blinkPhase) : new byte[Rows * 8 * Columns];
        return new DisplayFrame(DisplayKind.CharacterLcd, Columns, Rows, flags, Contrast(), dots, Text, _version);
    }

    /// <summary>V0 near GND gives the darkest picture, about 2 V and up nothing (VDD = 5 V); unwired V0 uses the property.</summary>
    private double Contrast()
    {
        if (!IsI2c && Net("V0") is { HasVoltage: true } v0)
        {
            return Math.Clamp(1 - (v0.AverageVoltage / 2.0), 0, 1);
        }

        return Properties.Number("contrast");
    }

    private void ReportNoPower() => Context.Log.ReportOnce(
        $"lcd-power-{Id}",
        SimSeverity.Warning,
        "lcd-power",
        $"{Id} has no power: connect {(IsI2c ? "VCC" : "VDD")} to 5V and {(IsI2c ? "GND" : "VSS")} to GND.",
        Id);

    private bool High(PinRef pin) => Circuit.StateOf(pin) is { HasVoltage: true } state && state.Voltage >= HighThreshold;

    private static bool Supplied(NetState state) => state.HasVoltage && state.Voltage >= 4.0;

    private static bool Grounded(NetState state) => state.HasVoltage && state.Voltage <= 0.5;

    private static PinDefinition Header(string name) => new(
        name,
        name switch
        {
            "VSS" or "K" => PinKind.Ground,
            "VDD" or "A" => PinKind.Power,
            "V0" => PinKind.Analog,
            _ => PinKind.Digital,
        },
        40 + (10 * Array.IndexOf(ParallelHeader, name)),
        0);

    private static IReadOnlyList<PinDefinition> I2cPins(double height)
    {
        double top = (Math.Round(height / 20) * 10) - 20;
        return
        [
            new PinDefinition("GND", PinKind.Ground, 0, top),
            new PinDefinition("VCC", PinKind.Power, 0, top + 10),
            new PinDefinition("SDA", PinKind.Digital, 0, top + 20),
            new PinDefinition("SCL", PinKind.Digital, 0, top + 30),
        ];
    }

    private static IEnumerable<string> Addresses() =>
        Enumerable.Range(0x20, 8).Concat(Enumerable.Range(0x38, 8)).Select(a => "0x" + a.ToString("X2", CultureInfo.InvariantCulture));
}
