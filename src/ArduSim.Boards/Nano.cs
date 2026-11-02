using ArduSim.Core.Boards;
using ArduSim.Core.Timing;

namespace ArduSim.Boards;

/// <summary>Arduino Nano v3 (ATmega328P, 16 MHz, 5 V), ADR-007.</summary>
public static class Nano
{
    public const string Id = "arduino-nano-v3";

    // Declared before Profile: static initializers run in textual order.
    private static readonly int[] PwmPins = [3, 5, 6, 9, 10, 11];

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
        AdcBits: 10,
        LogicVoltage: 5.0,
        EepromSize: 1024,
        ExternalInterrupts: new Dictionary<int, int> { [0] = 2, [1] = 3 },
        SerialPorts: 1,
        Timing: Timing);

    private static IEnumerable<BoardPin> DigitalPins() =>
        Enumerable.Range(0, 14).Select(n => new BoardPin(
            n,
            "D" + n.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PwmPins.Contains(n) ? PinCapabilities.Digital | PinCapabilities.Pwm : PinCapabilities.Digital));

    // A0-A5 double as digital pins 14-19; A6 and A7 are analog-only.
    private static IEnumerable<BoardPin> AnalogPins() =>
        Enumerable.Range(0, 8).Select(n => new BoardPin(
            14 + n,
            "A" + n.ToString(System.Globalization.CultureInfo.InvariantCulture),
            n < 6 ? PinCapabilities.Digital | PinCapabilities.Analog : PinCapabilities.Analog));
}

/// <summary>All boards the simulator knows, by project id.</summary>
public static class BoardCatalog
{
    public static IReadOnlyList<BoardProfile> All { get; } = [Nano.Profile];

    public static BoardProfile? Find(string id) => All.FirstOrDefault(b => string.Equals(b.Id, id, StringComparison.OrdinalIgnoreCase));
}
