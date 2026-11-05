namespace ArduSim.Core.Boards;

[Flags]
public enum PinCapabilities
{
    None = 0,
    Digital = 1,
    Analog = 2,
    Pwm = 4,

    /// <summary>An 8-bit DAC output (ESP32 GPIO 25 and 26).</summary>
    Dac = 8,

    /// <summary>Has an internal pull-down (ESP32); every digital pin has a pull-up unless <see cref="InputOnly"/>.</summary>
    PullDown = 16,

    /// <summary>Cannot drive and has no internal pulls (ESP32 GPIO 34-39).</summary>
    InputOnly = 32,

    /// <summary>On the ESP32's second ADC, which Wi-Fi takes over while the radio is on.</summary>
    Adc2 = 64,

    /// <summary>No internal pull-up although the pin can drive (ESP8266 GPIO 16).</summary>
    NoPullUp = 128,
}

/// <param name="Number">Arduino pin number as the sketch uses it (Nano: D0-D13 = 0-13, A0-A7 = 14-21).</param>
/// <param name="X">Position on the board drawing, canvas units of 0.01 inch.</param>
public sealed record BoardPin(int Number, string Name, PinCapabilities Capabilities, double X = 0, double Y = 0)
{
    public bool Has(PinCapabilities capability) => (Capabilities & capability) == capability;
}

public enum PowerPinKind
{
    /// <summary>Drives its rail at <see cref="PowerPin.Voltage"/>.</summary>
    Supply,
    Ground,

    /// <summary>Pulled up on the board.</summary>
    Reset,

    /// <summary>Not driven by the board (AREF, VIN when powered over USB).</summary>
    Passive,
}

/// <param name="Rail">Pins with the same rail are connected on the board (GND and GND.2).</param>
public sealed record PowerPin(string Name, string Rail, PowerPinKind Kind, double Voltage, double X, double Y);

/// <summary>Virtual cost of each host call, in nanoseconds (ADR-005).</summary>
public sealed record TimingTable(
    ulong PinMode,
    ulong DigitalWrite,
    ulong DigitalRead,
    ulong AnalogRead,
    ulong AnalogWrite,
    ulong ClockRead,
    ulong ShortCall,
    ulong EepromProgramming,
    ulong KvRead = 0,
    ulong KvWrite = 0,
    ulong NetworkCall = 0);

/// <summary>Default pins of a hardware SPI bus (Nano: MOSI 11, MISO 12, SCK 13, SS 10); -1 where the bus has no default.</summary>
public sealed record SpiPins(int Mosi, int Miso, int Sck, int Ss);

/// <summary>Default pins of a hardware I2C bus (Nano: SDA A4, SCL A5); -1 where the bus has no default.</summary>
public sealed record I2cPins(int Sda, int Scl);

/// <summary>Default pins of a UART (Nano: RX 0, TX 1); -1 where the port has no such pin on the board.</summary>
public sealed record UartPins(int Rx, int Tx);

/// <summary>Everything the simulator and the build need to know about a board (ADR-007).</summary>
/// <param name="Variant">Folder under <c>firmware/shim/variants</c>.</param>
/// <param name="Defines">Compiler macros for the sketch.</param>
/// <param name="ExternalInterrupts">Interrupt number → pin.</param>
/// <param name="Width">Size of the board drawing in canvas units.</param>
/// <param name="I2c">Hardware I2C buses by number (Wire, Wire1) with their default pins; empty when the board has none.</param>
/// <param name="Spi">Hardware SPI buses by number with their default pins; empty when the board has none.</param>
/// <param name="CpuHz">CPU clock; peripheral clocks (SPI) are divided from it.</param>
/// <param name="BusPinsAnywhere">Bus and UART signals can be routed to any GPIO (the ESP32 GPIO matrix); otherwise only the defaults work.</param>
/// <param name="PwmConfigurable">PWM frequency and resolution can be set (ESP32 LEDC); otherwise PWM is fixed at 8 bits.</param>
/// <param name="PullResistance">Internal pull-up (and pull-down) resistance.</param>
/// <param name="Uarts">Default pins of each UART, for the logic analyzer; null when unknown.</param>
public sealed record BoardProfile(
    string Id,
    string DisplayName,
    string Variant,
    IReadOnlyList<string> Defines,
    IReadOnlyList<BoardPin> Pins,
    IReadOnlyList<PowerPin> PowerPins,
    int AdcBits,
    double LogicVoltage,
    int EepromSize,
    IReadOnlyDictionary<int, int> ExternalInterrupts,
    int SerialPorts,
    int? BuiltinLed,
    double Width,
    double Height,
    TimingTable Timing,
    IReadOnlyList<I2cPins> I2c,
    IReadOnlyList<SpiPins> Spi,
    int CpuHz = 16_000_000,
    bool BusPinsAnywhere = false,
    bool PwmConfigurable = false,
    double PullResistance = 35_000,
    IReadOnlyList<UartPins>? Uarts = null)
{
    public BoardPin? FindPin(int number) => Pins.FirstOrDefault(p => p.Number == number);

    public BoardPin? FindPin(string name) => Pins.FirstOrDefault(p => p.Name == name);

    public string PinName(int number) => FindPin(number)?.Name ?? "pin " + number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public int AdcMax => (1 << AdcBits) - 1;

    /// <summary>Input HIGH threshold (ATmega328P: 0.6 Vcc; ESP32: 0.75 Vcc).</summary>
    public double InputHighVoltage => InputHighFraction * LogicVoltage;

    /// <summary>Input LOW threshold (ATmega328P: 0.3 Vcc; ESP32: 0.25 Vcc).</summary>
    public double InputLowVoltage => InputLowFraction * LogicVoltage;

    public double InputHighFraction { get; init; } = 0.6;

    public double InputLowFraction { get; init; } = 0.3;

    /// <summary>The SPI clock is divided from <see cref="CpuHz"/> by any integer (ESP32); otherwise by 2..128 in powers of two (AVR).</summary>
    public bool SpiAnyDivider { get; init; }

    /// <summary>
    /// The ESP8266's software watchdog: a real board resets when loop() runs this long (virtual ns)
    /// without returning, delay() or yield(); the simulation warns. 0 when the board has none.
    /// </summary>
    public ulong SoftWatchdog { get; init; }

    /// <summary>The on-board LED lights while its pin is LOW (ESP8266 modules).</summary>
    public bool BuiltinLedActiveLow { get; init; }

    /// <summary>
    /// GPIOs the chip has but the board does not bring out (the ESP32 DevKit's GPIO 0, on its BOOT
    /// button): <c>pinMode</c> on them is ignored without a warning, as libraries set them up blindly.
    /// </summary>
    public IReadOnlyList<int> UnexposedPins { get; init; } = [];

    /// <summary>The board has a (simulated) Wi-Fi radio (ADR-010).</summary>
    public bool HasWifi { get; init; }

    /// <summary>Folder under <c>firmware/shim/cores</c>: the Arduino core the board's sketches compile against.</summary>
    public string Core { get; init; } = "avr";

    /// <summary>A second core folder searched after <see cref="Core"/>: code shared by a family of cores (<c>esp-common</c>).</summary>
    public string? CoreBase { get; init; }

    /// <summary>digitalWrite on an input switches its pull-up (AVR); other cores leave the pulls alone.</summary>
    public bool InputWriteSetsPullUp { get; init; } = true;
}
