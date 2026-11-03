using ArduSim.Core.Boards;
using ArduSim.Core.Timing;

namespace ArduSim.Boards;

/// <summary>
/// The ESP8266 (ESP-12E/F, 80 MHz, 3.3 V) as the NodeMCU v3 and Wemos D1 mini carry it (ADR-009).
/// Arduino pin numbers are GPIO numbers (A0 is 17); the silkscreen names are D0-D8, A0, RX, TX.
/// </summary>
internal static class Esp8266
{
    /// <summary>Silkscreen name → GPIO, the same on both boards.</summary>
    public static readonly IReadOnlyDictionary<string, int> Gpio = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["D0"] = 16, ["D1"] = 5, ["D2"] = 4, ["D3"] = 0, ["D4"] = 2, ["D5"] = 14, ["D6"] = 12, ["D7"] = 13, ["D8"] = 15,
        ["RX"] = 3, ["TX"] = 1, ["A0"] = 17,
    };

    /// <summary>Timing table v1: estimates for the ESP8266 Arduino core 3.x at 80 MHz, not measured on a board.</summary>
    public static TimingTable Timing { get; } = new(
        PinMode: 2 * VirtualTime.Microsecond,
        DigitalWrite: 500,
        DigitalRead: 500,
        AnalogRead: 70 * VirtualTime.Microsecond,
        AnalogWrite: 5 * VirtualTime.Microsecond,
        ClockRead: 300,
        ShortCall: 1 * VirtualTime.Microsecond, // also the overhead between two loop() calls
        EepromProgramming: 0,
        NetworkCall: 10 * VirtualTime.Microsecond);

    public static BoardPin Pin(string name, double x, double y)
    {
        int gpio = Gpio[name];
        PinCapabilities capabilities = gpio switch
        {
            17 => PinCapabilities.Analog,
            16 => PinCapabilities.Digital | PinCapabilities.Pwm | PinCapabilities.PullDown | PinCapabilities.NoPullUp,
            _ => PinCapabilities.Digital | PinCapabilities.Pwm,
        };
        return new BoardPin(gpio, name, capabilities, x, y);
    }

    public static BoardProfile Profile(string id, string displayName, string boardMacro, IReadOnlyList<BoardPin> pins, IReadOnlyList<PowerPin> power, double width, double height) =>
        new(
            Id: id,
            DisplayName: displayName,
            Variant: "esp8266",
            Defines: ["ARDUINO=10819", "ARDUINO_ARCH_SIM", "SIM_BOARD_ESP8266", "ESP8266", "ARDUINO_ARCH_ESP8266", boardMacro, "F_CPU=80000000L"],
            Pins: pins,
            PowerPins: power,
            AdcBits: 10,
            LogicVoltage: 3.3,
            EepromSize: 4096,
            ExternalInterrupts: pins.Where(p => p.Number <= 15).ToDictionary(p => p.Number, p => p.Number),
            SerialPorts: 2,
            BuiltinLed: 2,
            Width: width,
            Height: height,
            Timing: Timing,
            I2c: [new I2cPins(Sda: 4, Scl: 5)],
            Spi: [new SpiPins(Mosi: 13, Miso: 12, Sck: 14, Ss: 15)],
            CpuHz: 80_000_000,
            BusPinsAnywhere: true,
            PwmConfigurable: true,
            PullResistance: 50_000)
        {
            Core = "esp8266",
            CoreBase = "esp-common",
            SpiAnyDivider = true,
            HasWifi = true,
            InputHighFraction = 0.75,
            InputLowFraction = 0.25,
            InputWriteSetsPullUp = false,
            SoftWatchdog = 3200 * VirtualTime.Millisecond,
            BuiltinLedActiveLow = true,
        };
}

/// <summary>NodeMCU v3 (LoLin, ESP-12E): 2 x 15 pins, drawn with the USB connector on the left.</summary>
public static class NodeMcu
{
    public const string Id = "nodemcu-v3";

    // Declared before Profile: static initializers run in textual order.
    private static readonly string[] TopRow = ["VIN", "GND", "RST", "EN", "3V3", "GND.2", "CLK", "SD0", "CMD", "SD1", "SD2", "SD3", "RSV", "RSV.2", "A0"];
    private static readonly string[] BottomRow = ["3V3.2", "GND.3", "TX", "RX", "D8", "D7", "D6", "D5", "GND.4", "3V3.3", "D4", "D3", "D2", "D1", "D0"];

    public static BoardProfile Profile { get; } = Esp8266.Profile(
        Id,
        "NodeMCU v3 (ESP8266)",
        "ARDUINO_ESP8266_NODEMCU_ESP12E",
        [.. TopRow.Concat(BottomRow).Where(Esp8266.Gpio.ContainsKey).Select(n => Esp8266.Pin(n, X(n), Y(n)))],
        [
            Power("VIN", "VIN", PowerPinKind.Supply, 5.0),
            Power("3V3", "3V3", PowerPinKind.Supply, 3.3),
            Power("3V3.2", "3V3", PowerPinKind.Supply, 3.3),
            Power("3V3.3", "3V3", PowerPinKind.Supply, 3.3),
            Power("GND", "GND", PowerPinKind.Ground, 0),
            Power("GND.2", "GND", PowerPinKind.Ground, 0),
            Power("GND.3", "GND", PowerPinKind.Ground, 0),
            Power("GND.4", "GND", PowerPinKind.Ground, 0),
            Power("RST", "RST", PowerPinKind.Reset, 3.3),
            Power("EN", "EN", PowerPinKind.Reset, 3.3),
            .. new[] { "CLK", "SD0", "CMD", "SD1", "SD2", "SD3", "RSV", "RSV.2" }.Select(n => Power(n, n, PowerPinKind.Passive, 0)),
        ],
        240,
        120);

    private static double X(string name) => 30 + (10 * (Array.IndexOf(TopRow, name) is >= 0 and var top ? top : Array.IndexOf(BottomRow, name)));

    private static double Y(string name) => Array.IndexOf(TopRow, name) >= 0 ? 10 : 110;

    private static PowerPin Power(string name, string rail, PowerPinKind kind, double voltage) => new(name, rail, kind, voltage, X(name), Y(name));
}

/// <summary>Wemos (LOLIN) D1 mini (ESP-12F): 2 x 8 pins, drawn with the USB connector on the left.</summary>
public static class WemosD1Mini
{
    public const string Id = "wemos-d1-mini";

    // Declared before Profile: static initializers run in textual order.
    private static readonly string[] TopRow = ["3V3", "D8", "D7", "D6", "D5", "D0", "A0", "RST"];
    private static readonly string[] BottomRow = ["5V", "GND", "D4", "D3", "D2", "D1", "RX", "TX"];

    public static BoardProfile Profile { get; } = Esp8266.Profile(
        Id,
        "Wemos D1 mini (ESP8266)",
        "ARDUINO_ESP8266_WEMOS_D1MINI",
        [.. TopRow.Concat(BottomRow).Where(Esp8266.Gpio.ContainsKey).Select(n => Esp8266.Pin(n, X(n), Y(n)))],
        [
            Power("5V", "5V", PowerPinKind.Supply, 5.0),
            Power("3V3", "3V3", PowerPinKind.Supply, 3.3),
            Power("GND", "GND", PowerPinKind.Ground, 0),
            Power("RST", "RST", PowerPinKind.Reset, 3.3),
        ],
        140,
        100);

    private static double X(string name) => 30 + (10 * (Array.IndexOf(TopRow, name) is >= 0 and var top ? top : Array.IndexOf(BottomRow, name)));

    private static double Y(string name) => Array.IndexOf(TopRow, name) >= 0 ? 10 : 90;

    private static PowerPin Power(string name, string rail, PowerPinKind kind, double voltage) => new(name, rail, kind, voltage, X(name), Y(name));
}
