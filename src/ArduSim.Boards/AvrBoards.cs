using System.Globalization;
using ArduSim.Core.Boards;
using ArduSim.Core.Timing;

namespace ArduSim.Boards;

/// <summary>What the AVR boards have in common.</summary>
internal static class Avr
{
    public static string Digital(int number) => "D" + number.ToString(CultureInfo.InvariantCulture);

    public static string Analog(int number) => "A" + number.ToString(CultureInfo.InvariantCulture);

    /// <summary>A pin's capabilities on a board: PWM where the board has it, analog as named.</summary>
    public static BoardPin Pin(int number, string name, IReadOnlySet<int> pwm, PinCapabilities kind, (double X, double Y) at) =>
        new(number, name, pwm.Contains(number) ? kind | PinCapabilities.Pwm : kind, at.X, at.Y);

    /// <summary>The table of an AVR at half the clock: every call takes twice as long.</summary>
    public static TimingTable HalfSpeed(TimingTable t) => t with
    {
        PinMode = t.PinMode * 2,
        DigitalWrite = t.DigitalWrite * 2,
        DigitalRead = t.DigitalRead * 2,
        AnalogWrite = t.AnalogWrite * 2,
        ClockRead = t.ClockRead * 2,
        ShortCall = t.ShortCall * 2,
    };
}

/// <summary>Arduino Uno R3 (ATmega328P, 16 MHz, 5 V): the Nano's MCU without A6 and A7, ADR-015.</summary>
public static class Uno
{
    public const string Id = "arduino-uno-r3";

    private static readonly HashSet<int> PwmPins = [3, 5, 6, 9, 10, 11];

    // Declared before Profile: static initializers run in textual order. Headers as on the board, USB on the left.
    private static readonly string[] TopRow = ["AREF", "GND", "D13", "D12", "D11", "D10", "D9", "D8", "", "D7", "D6", "D5", "D4", "D3", "D2", "D1", "D0"];
    private static readonly string[] BottomRow = ["IOREF", "RST", "3V3", "5V", "GND.2", "GND.3", "VIN", "", "A0", "A1", "A2", "A3", "A4", "A5"];

    public static BoardProfile Profile { get; } = new(
        Id: Id,
        DisplayName: "Arduino Uno",
        Variant: "uno",
        Defines: ["ARDUINO=10819", "ARDUINO_ARCH_SIM", "SIM_BOARD_UNO", "ARDUINO_AVR_UNO", "F_CPU=16000000L"],
        Pins: [.. Pins()],
        PowerPins:
        [
            Power("5V", "5V", PowerPinKind.Supply, 5.0),
            Power("3V3", "3V3", PowerPinKind.Supply, 3.3),
            Power("IOREF", "5V", PowerPinKind.Supply, 5.0),
            Power("GND", "GND", PowerPinKind.Ground, 0),
            Power("GND.2", "GND", PowerPinKind.Ground, 0),
            Power("GND.3", "GND", PowerPinKind.Ground, 0),
            Power("RST", "RST", PowerPinKind.Reset, 5.0),
            Power("AREF", "AREF", PowerPinKind.Passive, 0),
            Power("VIN", "VIN", PowerPinKind.Passive, 0),
        ],
        AdcBits: 10,
        LogicVoltage: 5.0,
        EepromSize: 1024,
        ExternalInterrupts: new Dictionary<int, int> { [0] = 2, [1] = 3 },
        SerialPorts: 1,
        BuiltinLed: 13,
        Width: 270,
        Height: 210,
        Timing: Nano.Timing,
        I2c: [new I2cPins(Sda: 18, Scl: 19)],
        Spi: [new SpiPins(Mosi: 11, Miso: 12, Sck: 13, Ss: 10)],
        Uarts: [new UartPins(Rx: 0, Tx: 1)]);

    private static (double X, double Y) Position(string name)
    {
        int top = Array.IndexOf(TopRow, name);
        return top >= 0 ? (90 + (10 * top), 10) : (100 + (10 * Array.IndexOf(BottomRow, name)), 200);
    }

    private static PowerPin Power(string name, string rail, PowerPinKind kind, double voltage)
    {
        (double x, double y) = Position(name);
        return new PowerPin(name, rail, kind, voltage, x, y);
    }

    private static IEnumerable<BoardPin> Pins()
    {
        for (int n = 0; n < 14; n++)
        {
            yield return Avr.Pin(n, Avr.Digital(n), PwmPins, PinCapabilities.Digital, Position(Avr.Digital(n)));
        }

        for (int n = 0; n < 6; n++)
        {
            yield return Avr.Pin(14 + n, Avr.Analog(n), PwmPins, PinCapabilities.Digital | PinCapabilities.Analog, Position(Avr.Analog(n)));
        }
    }
}

/// <summary>Arduino Mega 2560 (ATmega2560, 16 MHz, 5 V): 54 digital pins, 16 analog, four UARTs, ADR-015.</summary>
public static class Mega2560
{
    public const string Id = "arduino-mega-2560";

    private static readonly HashSet<int> PwmPins = [2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 44, 45, 46];

    // Declared before Profile: static initializers run in textual order. USB on the left; D22-D53 on the double header at the right.
    private static readonly string[] TopRow = ["AREF", "GND", "D13", "D12", "D11", "D10", "D9", "D8", "", "D7", "D6", "D5", "D4", "D3", "D2", "D1", "D0", "", "D14", "D15", "D16", "D17", "D18", "D19", "D20", "D21"];
    private static readonly string[] BottomRow = ["IOREF", "RST", "3V3", "5V", "GND.2", "GND.3", "VIN", "", "A0", "A1", "A2", "A3", "A4", "A5", "A6", "A7", "", "A8", "A9", "A10", "A11", "A12", "A13", "A14", "A15"];

    public static BoardProfile Profile { get; } = new(
        Id: Id,
        DisplayName: "Arduino Mega 2560",
        Variant: "mega",
        Defines: ["ARDUINO=10819", "ARDUINO_ARCH_SIM", "SIM_BOARD_MEGA", "ARDUINO_AVR_MEGA2560", "F_CPU=16000000L"],
        Pins: [.. Pins()],
        PowerPins:
        [
            Power("5V", "5V", PowerPinKind.Supply, 5.0),
            Power("5V.2", "5V", PowerPinKind.Supply, 5.0),
            Power("5V.3", "5V", PowerPinKind.Supply, 5.0),
            Power("3V3", "3V3", PowerPinKind.Supply, 3.3),
            Power("IOREF", "5V", PowerPinKind.Supply, 5.0),
            Power("GND", "GND", PowerPinKind.Ground, 0),
            Power("GND.2", "GND", PowerPinKind.Ground, 0),
            Power("GND.3", "GND", PowerPinKind.Ground, 0),
            Power("GND.4", "GND", PowerPinKind.Ground, 0),
            Power("GND.5", "GND", PowerPinKind.Ground, 0),
            Power("RST", "RST", PowerPinKind.Reset, 5.0),
            Power("AREF", "AREF", PowerPinKind.Passive, 0),
            Power("VIN", "VIN", PowerPinKind.Passive, 0),
        ],
        AdcBits: 10,
        LogicVoltage: 5.0,
        EepromSize: 4096,
        ExternalInterrupts: new Dictionary<int, int> { [0] = 2, [1] = 3, [2] = 21, [3] = 20, [4] = 19, [5] = 18 },
        SerialPorts: 4,
        BuiltinLed: 13,
        Width: 400,
        Height: 210,
        Timing: Nano.Timing,
        I2c: [new I2cPins(Sda: 20, Scl: 21)],
        Spi: [new SpiPins(Mosi: 51, Miso: 50, Sck: 52, Ss: 53)],
        Uarts: [new UartPins(Rx: 0, Tx: 1), new UartPins(Rx: 19, Tx: 18), new UartPins(Rx: 17, Tx: 16), new UartPins(Rx: 15, Tx: 14)]);

    private static (double X, double Y) Position(string name)
    {
        // The double header: 5V at the top, D22/D23 ... D52/D53, GND at the bottom.
        switch (name)
        {
            case "5V.2":
                return (370, 20);
            case "5V.3":
                return (380, 20);
            case "GND.4":
                return (370, 190);
            case "GND.5":
                return (380, 190);
        }

        if (name.Length == 3 && name[0] == 'D' && int.Parse(name.AsSpan(1), CultureInfo.InvariantCulture) is >= 22 and var number)
        {
            return (370 + (10 * (number % 2)), 30 + (10 * ((number - 22) / 2)));
        }

        int top = Array.IndexOf(TopRow, name);
        return top >= 0 ? (90 + (10 * top), 10) : (100 + (10 * Array.IndexOf(BottomRow, name)), 200);
    }

    private static PowerPin Power(string name, string rail, PowerPinKind kind, double voltage)
    {
        (double x, double y) = Position(name);
        return new PowerPin(name, rail, kind, voltage, x, y);
    }

    private static IEnumerable<BoardPin> Pins()
    {
        for (int n = 0; n < 54; n++)
        {
            yield return Avr.Pin(n, Avr.Digital(n), PwmPins, PinCapabilities.Digital, Position(Avr.Digital(n)));
        }

        for (int n = 0; n < 16; n++)
        {
            yield return Avr.Pin(54 + n, Avr.Analog(n), PwmPins, PinCapabilities.Digital | PinCapabilities.Analog, Position(Avr.Analog(n)));
        }
    }
}

/// <summary>Arduino Pro Mini (ATmega328P, no USB): 5 V at 16 MHz or 3.3 V at 8 MHz, the Nano's pin map, ADR-015.</summary>
public static class ProMini
{
    public const string Id5V = "arduino-pro-mini-5v";
    public const string Id3V3 = "arduino-pro-mini-3v3";

    private static readonly HashSet<int> PwmPins = [3, 5, 6, 9, 10, 11];

    // Declared before the profiles: static initializers run in textual order. The FTDI header on the left, A4-A7 on inner pads.
    private static readonly string[] TopRow = ["D1", "D0", "RST.2", "GND.2", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9"];
    private static readonly string[] BottomRow = ["RAW", "GND", "RST", "VCC", "A3", "A2", "A1", "A0", "D13", "D12", "D11", "D10"];
    private static readonly string[] InnerPads = ["A4", "A5", "A6", "A7"];

    public static BoardProfile Profile5V { get; } = Profile(Id5V, "Arduino Pro Mini 5V", 5.0, 16_000_000, Nano.Timing);

    public static BoardProfile Profile3V3 { get; } = Profile(Id3V3, "Arduino Pro Mini 3.3V", 3.3, 8_000_000, Avr.HalfSpeed(Nano.Timing));

    private static BoardProfile Profile(string id, string name, double volts, int cpuHz, TimingTable timing) => new(
        Id: id,
        DisplayName: name,
        Variant: "nano",
        Defines: ["ARDUINO=10819", "ARDUINO_ARCH_SIM", "SIM_BOARD_PRO_MINI", "ARDUINO_AVR_PRO", "F_CPU=" + cpuHz.ToString(CultureInfo.InvariantCulture) + "L"],
        Pins: [.. Pins()],
        PowerPins:
        [
            Power("VCC", "VCC", PowerPinKind.Supply, volts),
            Power("GND", "GND", PowerPinKind.Ground, 0),
            Power("GND.2", "GND", PowerPinKind.Ground, 0),
            Power("RST", "RST", PowerPinKind.Reset, volts),
            Power("RST.2", "RST", PowerPinKind.Reset, volts),
            Power("RAW", "RAW", PowerPinKind.Passive, 0),
        ],
        AdcBits: 10,
        LogicVoltage: volts,
        EepromSize: 1024,
        ExternalInterrupts: new Dictionary<int, int> { [0] = 2, [1] = 3 },
        SerialPorts: 1,
        BuiltinLed: 13,
        Width: 150,
        Height: 70,
        Timing: timing,
        I2c: [new I2cPins(Sda: 18, Scl: 19)],
        Spi: [new SpiPins(Mosi: 11, Miso: 12, Sck: 13, Ss: 10)],
        CpuHz: cpuHz,
        Uarts: [new UartPins(Rx: 0, Tx: 1)],
        PinOutputResistance: volts < 4 ? 40 : 25);

    private static (double X, double Y) Position(string name)
    {
        int inner = Array.IndexOf(InnerPads, name);
        if (inner >= 0)
        {
            return (140, 20 + (10 * inner));
        }

        int top = Array.IndexOf(TopRow, name);
        return top >= 0 ? (20 + (10 * top), 10) : (20 + (10 * Array.IndexOf(BottomRow, name)), 60);
    }

    private static PowerPin Power(string name, string rail, PowerPinKind kind, double voltage)
    {
        (double x, double y) = Position(name);
        return new PowerPin(name, rail, kind, voltage, x, y);
    }

    private static IEnumerable<BoardPin> Pins()
    {
        for (int n = 0; n < 14; n++)
        {
            yield return Avr.Pin(n, Avr.Digital(n), PwmPins, PinCapabilities.Digital, Position(Avr.Digital(n)));
        }

        for (int n = 0; n < 8; n++)
        {
            yield return Avr.Pin(14 + n, Avr.Analog(n), PwmPins, n < 6 ? PinCapabilities.Digital | PinCapabilities.Analog : PinCapabilities.Analog, Position(Avr.Analog(n)));
        }
    }
}
