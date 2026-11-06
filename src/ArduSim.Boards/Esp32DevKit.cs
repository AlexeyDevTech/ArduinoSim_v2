using System.Globalization;
using ArduSim.Core.Boards;
using ArduSim.Core.Timing;

namespace ArduSim.Boards;

/// <summary>
/// ESP32 DevKit v1 (DOIT, 30 pins, ESP32-WROOM-32, 240 MHz, 3.3 V), ADR-009. Arduino pin numbers are
/// GPIO numbers; the pin names are the board's silkscreen (D13, RX2, VP, …).
/// </summary>
public static class Esp32DevKit
{
    public const string Id = "esp32-devkit-v1";

    // Declared before Profile: static initializers run in textual order.
    // Drawn with the USB connector on the left and the antenna on the right, as the Nano.
    private static readonly string[] TopRow = ["VIN", "GND", "D13", "D12", "D14", "D27", "D26", "D25", "D33", "D32", "D35", "D34", "VN", "VP", "EN"];
    private static readonly string[] BottomRow = ["3V3", "GND.2", "D15", "D2", "D4", "RX2", "TX2", "D5", "D18", "D19", "D21", "RX0", "TX0", "D22", "D23"];

    private static readonly Dictionary<string, int> Gpio = new(StringComparer.Ordinal)
    {
        ["VP"] = 36, ["VN"] = 39, ["RX0"] = 3, ["TX0"] = 1, ["RX2"] = 16, ["TX2"] = 17,
    };

    private static readonly int[] InputOnlyPins = [34, 35, 36, 39];

    // ADC1 (32-39) and ADC2 (0, 2, 4, 12-15, 25-27); ADC2 is not usable while Wi-Fi is on.
    private static readonly int[] AnalogPins = [32, 33, 34, 35, 36, 39, 2, 4, 12, 13, 14, 15, 25, 26, 27];

    /// <summary>Timing table v1: estimates for the Arduino-ESP32 core 3.x at 240 MHz, not measured on a board.</summary>
    public static TimingTable Timing { get; } = new(
        PinMode: 2 * VirtualTime.Microsecond,
        DigitalWrite: 200,
        DigitalRead: 200,
        AnalogRead: 25 * VirtualTime.Microsecond,
        AnalogWrite: 3 * VirtualTime.Microsecond,
        ClockRead: 100,
        ShortCall: 500, // also the loopTask overhead between two loop() calls
        EepromProgramming: 0,
        KvRead: 50 * VirtualTime.Microsecond,
        KvWrite: 2 * VirtualTime.Millisecond,
        NetworkCall: 5 * VirtualTime.Microsecond);

    private static readonly BoardPin[] IoPins = [.. TopRow.Concat(BottomRow).Where(n => GpioOf(n) >= 0).Select(Pin)];

    public static BoardProfile Profile { get; } = new(
        Id: Id,
        DisplayName: "ESP32 DevKit v1",
        Variant: "esp32-devkit",
        Defines: ["ARDUINO=10819", "ARDUINO_ARCH_SIM", "SIM_BOARD_ESP32_DEVKIT", "ESP32", "ARDUINO_ARCH_ESP32", "ARDUINO_ESP32_DEV", "F_CPU=240000000L"],
        Pins: IoPins,
        PowerPins:
        [
            Power("VIN", "VIN", PowerPinKind.Supply, 5.0),
            Power("3V3", "3V3", PowerPinKind.Supply, 3.3),
            Power("GND", "GND", PowerPinKind.Ground, 0),
            Power("GND.2", "GND", PowerPinKind.Ground, 0),
            Power("EN", "EN", PowerPinKind.Reset, 3.3),
        ],
        AdcBits: 12,
        LogicVoltage: 3.3,
        EepromSize: 4096,
        ExternalInterrupts: IoPins.ToDictionary(p => p.Number, p => p.Number),
        SerialPorts: 3,
        BuiltinLed: 2,
        Width: 230,
        Height: 110,
        Timing: Timing,
        I2c: [new I2cPins(Sda: 21, Scl: 22), new I2cPins(Sda: -1, Scl: -1)],
        Spi: [new SpiPins(Mosi: 23, Miso: 19, Sck: 18, Ss: 5), new SpiPins(Mosi: 13, Miso: 12, Sck: 14, Ss: 15)],
        CpuHz: 80_000_000, // the APB clock the SPI peripheral divides
        BusPinsAnywhere: true,
        PwmConfigurable: true,
        PullResistance: 45_000,
        PinOutputResistance: 20,
        Uarts: [new UartPins(Rx: 3, Tx: 1), new UartPins(Rx: -1, Tx: -1), new UartPins(Rx: 16, Tx: 17)])
    {
        Core = "esp32",
        CoreBase = "esp-common",
        SpiAnyDivider = true,
        HasWifi = true,
        UnexposedPins = [0],
        InputHighFraction = 0.75,
        InputLowFraction = 0.25,
        InputWriteSetsPullUp = false,
    };

    private static int GpioOf(string name) => Gpio.TryGetValue(name, out int gpio) ? gpio
        : name.StartsWith('D') && int.TryParse(name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : -1;

    private static (double X, double Y) Position(string name)
    {
        int top = Array.IndexOf(TopRow, name);
        return top >= 0 ? (30 + (10 * top), 10) : (30 + (10 * Array.IndexOf(BottomRow, name)), 100);
    }

    private static BoardPin Pin(string name)
    {
        int gpio = GpioOf(name);
        PinCapabilities capabilities = InputOnlyPins.Contains(gpio)
            ? PinCapabilities.Digital | PinCapabilities.InputOnly
            : PinCapabilities.Digital | PinCapabilities.Pwm | PinCapabilities.PullDown;
        if (AnalogPins.Contains(gpio))
        {
            capabilities |= gpio is >= 32 and <= 39 ? PinCapabilities.Analog : PinCapabilities.Analog | PinCapabilities.Adc2;
        }

        if (gpio is 25 or 26)
        {
            capabilities |= PinCapabilities.Dac;
        }

        (double x, double y) = Position(name);
        return new BoardPin(gpio, name, capabilities, x, y);
    }

    private static PowerPin Power(string name, string rail, PowerPinKind kind, double voltage)
    {
        (double x, double y) = Position(name);
        return new PowerPin(name, rail, kind, voltage, x, y);
    }
}
