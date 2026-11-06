using ArduSim.Core.Buses;
using ArduSim.Core.Circuits;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Displays;

namespace ArduSim.Core.Components.Parts;

/// <summary>
/// MAX7219 8x8 LED matrix modules, one or a chain of four (the FC-16 "4 in 1" board). Each device has
/// a 16-bit shift register clocked on CLK rising (MSB first); chained devices form one long register,
/// so the last 16 bits sent stay in device 0, which is the rightmost module, next to the input pins.
/// LOAD (CS) rising latches every device's word: digits 1-8, decode mode, intensity, scan limit,
/// shutdown and display test, from the datasheet. Devices start in shutdown. How digits and bits map
/// to LEDs depends on the module's wiring, as MD_MAX72XX's hardware types: FC16_HW (digit = row, bit 7 =
/// left column) or GENERIC_HW (digit = column from the right, bit 7 = top row). Hardware SPI (DIN on MOSI,
/// CLK on SCK) and bit-banged clocking (shiftOut, LedControl) reach the same shift register.
/// </summary>
[SimComponent]
public sealed class Max7219Matrix : SimComponent, ISpiDevice
{
    private const double Pitch = 80;

    // The board's ends beyond the LEDs, where the headers and their names are.
    private const double Ends = 40;

    // Declared before Spec: static initializers run in textual order.
    private static readonly ComponentDefinition One = Module(1);
    private static readonly ComponentDefinition Four = Module(4);

    private Device[] _devices = [];
    private Driver _dout = null!;
    private bool _clk;
    private bool _cs = true;
    private long _version;
    private DisplayFrame? _frame;

    public Max7219Matrix(string id, ComponentProperties properties)
        : base(id, Pick(properties), properties)
    {
    }

    public static ComponentDefinition Spec => Four;

    private int Count => _devices.Length;

    private bool Fc16 => Properties.Choice("hardware") == "fc16";

    private bool Powered => PartSupply.Across(Net("VCC"), Net("GND")) >= 4;

    public override DisplayFrame Display => _frame is { } frame && frame.Version == _version ? frame : _frame = Render();

    private static ComponentDefinition Pick(ComponentProperties properties) => properties.Choice("modules") == "4" ? Four : One;

    private static ComponentDefinition Module(int count)
    {
        double width = (Pitch * count) + Ends;
        return new ComponentDefinition(
            "max7219",
            "Part_Max7219",
            "display",
            width,
            90,
            [
                new PinDefinition("VCC", PinKind.Power, width, 20),
                new PinDefinition("GND", PinKind.Ground, width, 30),
                new PinDefinition("DIN", PinKind.Digital, width, 40),
                new PinDefinition("CS", PinKind.Digital, width, 50),
                new PinDefinition("CLK", PinKind.Digital, width, 60),
                new PinDefinition("DOUT", PinKind.Digital, 0, 40),
            ],
            [
                new PropertyDefinition("modules", PropertyKind.Choice, "4", ["4", "1"]),
                new PropertyDefinition("hardware", PropertyKind.Choice, "fc16", ["fc16", "generic"]),
                new PropertyDefinition("color", PropertyKind.Choice, "red", ["red", "green", "blue", "yellow", "white"]),
            ])
        {
            Layout = Pick,
            Presets =
            [
                new ComponentPreset("max7219-4", "Part_Max7219x4", new Dictionary<string, string> { ["modules"] = "4", ["hardware"] = "fc16" }),
                new ComponentPreset("max7219-1", "Part_Max7219x1", new Dictionary<string, string> { ["modules"] = "1", ["hardware"] = "generic" }),
            ],
        };
    }

    protected override void OnAttach()
    {
        _devices = [.. Enumerable.Range(0, Properties.Choice("modules") == "4" ? 4 : 1).Select(_ => new Device())];
        _dout = Circuit.AddDriver(Pin("DOUT"), Id);
        Circuit.Watch(Pin("CLK"), OnBus);
        Circuit.Watch(Pin("CS"), OnBus);
        WatchPins("VCC", "GND");
        Context.Spi?.Register(this);
    }

    PinRef ISpiDevice.Sck => Pin("CLK");

    PinRef ISpiDevice.Mosi => Pin("DIN");

    PinRef ISpiDevice.ChipSelect => Pin("CS");

    byte ISpiDevice.OnTransfer(byte mosi)
    {
        if (Powered)
        {
            for (int bit = 7; bit >= 0; bit--)
            {
                ShiftIn((mosi >> bit) & 1);
            }
        }

        return 0;
    }

    public override void OnCircuitReady()
    {
        _clk = Level("CLK");
        _cs = Level("CS");
        Refresh();
    }

    // Power: off, the chips forget everything and come back in shutdown.
    protected override void Refresh()
    {
        if (!Powered)
        {
            _dout.Set(NetDrive.None);
            if (_devices.Any(d => !d.IsReset))
            {
                foreach (Device device in _devices)
                {
                    device.Reset();
                }

                Changed();
            }
        }
    }

    private bool Level(string pin) => Net(pin) is { HasVoltage: true } net && net.Voltage >= 2.5;

    private void OnBus()
    {
        bool clk = Level("CLK");
        bool cs = Level("CS");
        bool rising = clk && !_clk;
        bool load = cs && !_cs;
        _clk = clk;
        _cs = cs;
        if (!Powered)
        {
            return;
        }

        if (rising)
        {
            ShiftIn(Level("DIN") ? 1 : 0);
        }

        if (load)
        {
            bool changed = false;
            foreach (Device device in _devices)
            {
                changed |= device.Latch(this);
            }

            if (changed)
            {
                Changed();
            }
        }
    }

    // One clock: a bit into device 0, every device's top bit on to the next, the last one's to DOUT.
    private void ShiftIn(int bit)
    {
        int carry = bit;
        foreach (Device device in _devices)
        {
            int next = device.Shift >> 15;
            device.Shift = (ushort)((device.Shift << 1) | carry);
            carry = next;
        }

        _dout.Set(NetDrive.Strong((_devices[^1].Shift >> 15) != 0 ? Net("VCC").Voltage : 0));
    }

    private void Changed()
    {
        _version++;
        NotifyDisplayChanged();
    }

    private DisplayFrame Render()
    {
        int columns = 8 * Count;
        int stride = (columns + 7) / 8;
        var dots = new byte[8 * stride];
        for (int device = 0; device < Count; device++)
        {
            int left = 8 * (Count - 1 - device);
            for (int row = 0; row < 8; row++)
            {
                for (int column = 0; column < 8; column++)
                {
                    if (_devices[device].Lit(Fc16 ? row : 7 - column, Fc16 ? 7 - column : 7 - row))
                    {
                        int x = left + column;
                        dots[(row * stride) + (x / 8)] |= (byte)(0x80 >> (x % 8));
                    }
                }
            }
        }

        double intensity = (_devices.Max(d => d.Intensity) + 1) / 16.0;
        DisplayStatus status = (Powered ? DisplayStatus.Powered : 0) | (_devices.Any(d => !d.Shutdown) ? DisplayStatus.On : 0);
        return new DisplayFrame(DisplayKind.LedMatrix, columns, 8, status, intensity, dots, [], _version);
    }

    private sealed class Device
    {
        private readonly byte[] _digits = new byte[8];
        private int _decode;
        private int _scanLimit;
        private bool _test;

        public ushort Shift { get; set; }

        public int Intensity { get; private set; }

        public bool Shutdown { get; private set; } = true;

        public bool IsReset => Shutdown && !_test && _decode == 0 && _scanLimit == 0 && Intensity == 0 && _digits.All(d => d == 0);

        public void Reset()
        {
            Array.Clear(_digits);
            Shift = 0;
            _decode = 0;
            _scanLimit = 0;
            _test = false;
            Intensity = 0;
            Shutdown = true;
        }

        /// <summary>LED of digit register <paramref name="digit"/> (0-7) and segment bit <paramref name="bit"/>.</summary>
        public bool Lit(int digit, int bit) =>
            _test || (!Shutdown && digit <= _scanLimit && (_digits[digit] & (1 << bit)) != 0);

        /// <summary>LOAD rose: the word in the shift register goes to its register. True when the picture may change.</summary>
        public bool Latch(Max7219Matrix part)
        {
            int address = (Shift >> 8) & 0x0F;
            byte data = (byte)Shift;
            switch (address)
            {
                case >= 1 and <= 8:
                    _digits[address - 1] = data;
                    return true;
                case 0x09:
                    _decode = data;
                    if (data != 0)
                    {
                        part.Context.Log.ReportOnce($"max7219-decode-{part.Id}", SimSeverity.Warning, "max7219-decode", $"{part.Id}: decode mode 0x{data:X2} is for 7-segment digits; on an LED matrix the data bits are shown as they are.", part.Id);
                    }

                    return false;
                case 0x0A:
                    Intensity = data & 0x0F;
                    return true;
                case 0x0B:
                    _scanLimit = data & 0x07;
                    return true;
                case 0x0C:
                    Shutdown = (data & 0x01) == 0;
                    return true;
                case 0x0F:
                    _test = (data & 0x01) != 0;
                    return true;
                default:
                    return false;
            }
        }
    }
}
