using ArduSim.Core.Circuits;

namespace ArduSim.Core.Components.Parts;

/// <summary>When an LED lights, without modelling current (ADR-006: no current, no SPICE).</summary>
internal static class LedRule
{
    /// <summary>Sources weaker than this (an input pull-up, say) cannot light an LED visibly.</summary>
    public const double MaxSourceResistance = 10_000;

    public static readonly IReadOnlyList<string> Colors = ["red", "green", "blue", "yellow", "orange", "white"];

    public static double ForwardVoltage(string color) => color switch
    {
        "red" => 1.8,
        "orange" => 2.0,
        "yellow" => 2.1,
        "green" => 2.2,
        _ => 3.0,
    };

    /// <summary>
    /// 0..1: lit when forward-biased by at least <paramref name="forwardVoltage"/> from sources that
    /// can deliver current; PWM on the anode (or on the cathode, when it sinks) sets the brightness.
    /// </summary>
    public static double Brightness(NetState anode, NetState cathode, double forwardVoltage)
    {
        if (!anode.HasVoltage || !cathode.HasVoltage ||
            anode.SourceResistance > MaxSourceResistance || cathode.SourceResistance > MaxSourceResistance)
        {
            return 0;
        }

        double cathodeLow = cathode.Duty < 1 ? 0 : cathode.Voltage;
        if (anode.Voltage - cathodeLow < forwardVoltage)
        {
            return 0;
        }

        double fromAnode = anode.Duty < 1 ? anode.Duty : 1;
        double fromCathode = cathode.Duty < 1 ? 1 - cathode.Duty : 1;
        return fromAnode * fromCathode;
    }
}

[SimComponent]
public sealed class Led : SimComponent
{
    private double _forwardVoltage;

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
        _forwardVoltage = LedRule.ForwardVoltage(Properties.Choice("color"));
        WatchPins("A", "C");
    }

    protected override void Refresh() => SetState("brightness", LedRule.Brightness(Net("A"), Net("C"), _forwardVoltage));
}
