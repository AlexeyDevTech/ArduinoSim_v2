using System.Globalization;
using ArduSim.Core.Boards;
using ArduSim.Core.Timing;

namespace ArduSim.Boards;

/// <summary>Arduino Nano v3 (ATmega328P, 16 MHz, 5 V), ADR-007.</summary>
public static class Nano
{
    public const string Id = "arduino-nano-v3";

    // Declared before Profile: static initializers run in textual order.
    private static readonly int[] PwmPins = [3, 5, 6, 9, 10, 11];

    // The board is drawn with the USB connector on the left; two header rows on a 0.1 inch grid.
    private static readonly string[] TopRow = ["D12", "D11", "D10", "D9", "D8", "D7", "D6", "D5", "D4", "D3", "D2", "GND.2", "RST.2", "D0", "D1"];
    private static readonly string[] BottomRow = ["D13", "3V3", "AREF", "A0", "A1", "A2", "A3", "A4", "A5", "A6", "A7", "5V", "RST", "GND", "VIN"];

    /// <summary>Timing table v1 (ADR-005): brief values plus marked estimates, pending calibration.</summary>
    public static TimingTable Timing { get; } = new(
        PinMode: 4 * VirtualTime.Microsecond,
        DigitalWrite: 4 * VirtualTime.Microsecond,
        DigitalRead: 4 * VirtualTime.Microsecond,
        AnalogRead: 112 * VirtualTime.Microsecond,
        AnalogWrite: 5 * VirtualTime.Microsecond,
        ClockRead: 1 * VirtualTime.Microsecond,
        ShortCall: 1 * VirtualTime.Microsecond,
        EepromProgramming: 3_300 * VirtualTime.Microsecond);

    public static BoardProfile Profile { get; } = new(
        Id: Id,
        DisplayName: "Arduino Nano",
        Variant: "nano",
        Defines: ["ARDUINO=10819", "ARDUINO_ARCH_SIM", "SIM_BOARD_NANO", "F_CPU=16000000L"],
        Pins: [.. DigitalPins(), .. AnalogPins()],
        PowerPins:
        [
            Power("5V", "5V", PowerPinKind.Supply, 5.0),
            Power("3V3", "3V3", PowerPinKind.Supply, 3.3),
            Power("GND", "GND", PowerPinKind.Ground, 0),
            Power("GND.2", "GND", PowerPinKind.Ground, 0),
            Power("RST", "RST", PowerPinKind.Reset, 5.0),
            Power("RST.2", "RST", PowerPinKind.Reset, 5.0),
            Power("AREF", "AREF", PowerPinKind.Passive, 0),
            Power("VIN", "VIN", PowerPinKind.Passive, 0),
        ],
        AdcBits: 10,
        LogicVoltage: 5.0,
        EepromSize: 1024,
        ExternalInterrupts: new Dictionary<int, int> { [0] = 2, [1] = 3 },
        SerialPorts: 1,
        BuiltinLed: 13,
        Width: 180,
        Height: 70,
        Timing: Timing,
        I2c: [new I2cPins(Sda: 18, Scl: 19)],
        Spi: [new SpiPins(Mosi: 11, Miso: 12, Sck: 13, Ss: 10)]);

    private static (double X, double Y) Position(string name)
    {
        int top = Array.IndexOf(TopRow, name);
        return top >= 0 ? (20 + (10 * top), 10) : (20 + (10 * Array.IndexOf(BottomRow, name)), 60);
    }

    private static PowerPin Power(string name, string rail, PowerPinKind kind, double voltage)
    {
        (double x, double y) = Position(name);
        return new PowerPin(name, rail, kind, voltage, x, y);
    }

    private static IEnumerable<BoardPin> DigitalPins() =>
        Enumerable.Range(0, 14).Select(n =>
        {
            string name = "D" + n.ToString(CultureInfo.InvariantCulture);
            (double x, double y) = Position(name);
            return new BoardPin(n, name, PwmPins.Contains(n) ? PinCapabilities.Digital | PinCapabilities.Pwm : PinCapabilities.Digital, x, y);
        });

    // A0-A5 double as digital pins 14-19; A6 and A7 are analog-only.
    private static IEnumerable<BoardPin> AnalogPins() =>
        Enumerable.Range(0, 8).Select(n =>
        {
            string name = "A" + n.ToString(CultureInfo.InvariantCulture);
            (double x, double y) = Position(name);
            return new BoardPin(14 + n, name, n < 6 ? PinCapabilities.Digital | PinCapabilities.Analog : PinCapabilities.Analog, x, y);
        });
}

/// <summary>All boards the simulator knows, by project id.</summary>
public static class BoardCatalog
{
    public static IReadOnlyList<BoardProfile> All { get; } = [Nano.Profile, Esp32DevKit.Profile, NodeMcu.Profile, WemosD1Mini.Profile];

    public static BoardProfile? Find(string id) => All.FirstOrDefault(b => string.Equals(b.Id, id, StringComparison.OrdinalIgnoreCase));
}
