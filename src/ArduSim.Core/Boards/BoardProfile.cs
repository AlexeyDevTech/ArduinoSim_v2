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
public sealed record BoardPin(int Number, string Name, PinCapabilities Capabilities)
{
    public bool Has(PinCapabilities capability) => (Capabilities & capability) == capability;
}

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
public sealed record BoardProfile(
    string Id,
    string DisplayName,
    string Variant,
    IReadOnlyList<string> Defines,
    IReadOnlyList<BoardPin> Pins,
    int AdcBits,
    double LogicVoltage,
    int EepromSize,
    IReadOnlyDictionary<int, int> ExternalInterrupts,
    int SerialPorts,
    TimingTable Timing)
{
    public BoardPin? FindPin(int number) => Pins.FirstOrDefault(p => p.Number == number);

    public string PinName(int number) => FindPin(number)?.Name ?? "pin " + number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public int AdcMax => (1 << AdcBits) - 1;
}
