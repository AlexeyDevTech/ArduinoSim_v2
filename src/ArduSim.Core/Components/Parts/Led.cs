using ArduSim.Core.Circuits;
using ArduSim.Core.Diagnostics;

namespace ArduSim.Core.Components.Parts;

/// <summary>An LED as a diode in the circuit, and how its current looks (ADR-017).</summary>
internal static class LedRule
{
    private const double SeriesResistance = 10;
    private const double RatedCurrent = 0.020;
    private const double WarningCurrent = 0.030;
    private const double FullBrightnessCurrent = 0.005;

    public static readonly IReadOnlyList<string> Colors = ["red", "green", "blue", "yellow", "orange", "white"];

    /// <summary>Volts across the LED at its rated current.</summary>
    public static double ForwardVoltage(string color) => color switch
    {
        "red" => 1.8,
        "orange" => 2.0,
        "yellow" => 2.1,
        "green" => 2.2,
        _ => 3.0,
    };

    public static CircuitDiode Add(Circuit circuit, PinRef anode, PinRef cathode, double forwardVoltage, string owner) =>
        circuit.AddDiode(anode, cathode, forwardVoltage - (RatedCurrent * SeriesResistance), SeriesResistance, owner);

    /// <summary>
    /// 0..1: the share of time it is on (PWM) times how bright its current looks, which is full from
    /// 5 mA and falls off as a square root: 1 kΩ is visibly dimmer, 10 kΩ or a pull-up glows faintly.
    /// </summary>
    public static double Brightness(CircuitDiode diode)
    {
        double peak = diode.PeakCurrent;
        return peak <= 0 ? 0 : Math.Round(diode.Current / peak * Math.Sqrt(Math.Min(peak / FullBrightnessCurrent, 1)) * 1e6) / 1e6;
    }

    public static void WarnAboutCurrent(CircuitDiode diode, SimulationLog log)
    {
        if (diode.PeakCurrent > WarningCurrent)
        {
            log.ReportOnce($"led-current-{diode.A}-{diode.B}", SimSeverity.Warning, "led-overcurrent", $"{diode.Owner} carries {diode.PeakCurrent * 1000:0} mA, above the {RatedCurrent * 1000:0} mA an LED is rated for; a real one would burn out. Add a series resistor.", diode.Owner);
        }
    }
}

[SimComponent]
public sealed class Led : SimComponent
{
    private CircuitDiode _diode = null!;

    public Led(string id, ComponentProperties properties)
        : base(id, Spec, properties)
    {
    }

    public static ComponentDefinition Spec { get; } = new(
        "led",
        "Part_Led",
        "output",
        40,
        70,
        [new PinDefinition("A", PinKind.Digital, 10, 70, "+"), new PinDefinition("C", PinKind.Digital, 30, 70, "−")],
        [new PropertyDefinition("color", PropertyKind.Choice, "red", LedRule.Colors)]);

    protected override void OnAttach()
    {
        _diode = LedRule.Add(Circuit, Pin("A"), Pin("C"), LedRule.ForwardVoltage(Properties.Choice("color")), Id);
        WatchPins("A", "C");
    }

    protected override void Refresh()
    {
        SetState("brightness", LedRule.Brightness(_diode));
        LedRule.WarnAboutCurrent(_diode, Context.Log);
    }
}
