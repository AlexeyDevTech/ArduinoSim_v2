namespace ArduSim.Core.Boards;

[Flags]
public enum PinCapabilities
{
    None = 0,
    Digital = 1,
    Analog = 2,
    Pwm = 4,
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
    ulong EepromProgramming);

/// <summary>Everything the simulator and the build need to know about a board (ADR-007).</summary>
/// <param name="Variant">Folder under <c>firmware/shim/variants</c>.</param>
/// <param name="Defines">Compiler macros for the sketch.</param>
/// <param name="ExternalInterrupts">Interrupt number → pin.</param>
/// <param name="Width">Size of the board drawing in canvas units.</param>
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
    TimingTable Timing)
{
    public BoardPin? FindPin(int number) => Pins.FirstOrDefault(p => p.Number == number);

    public BoardPin? FindPin(string name) => Pins.FirstOrDefault(p => p.Name == name);

    public string PinName(int number) => FindPin(number)?.Name ?? "pin " + number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public int AdcMax => (1 << AdcBits) - 1;

    /// <summary>Input HIGH threshold (ATmega328P: 0.6 Vcc).</summary>
    public double InputHighVoltage => 0.6 * LogicVoltage;

    /// <summary>Input LOW threshold (ATmega328P: 0.3 Vcc).</summary>
    public double InputLowVoltage => 0.3 * LogicVoltage;
}
