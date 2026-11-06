using ArduSim.Core.Circuits;

namespace ArduSim.Core.Components.Parts;

/// <summary>An RGB LED with a common cathode (channels are anodes) or a common anode (channels sink).</summary>
[SimComponent]
public sealed class RgbLed : SimComponent
{
    private CircuitDiode _red = null!;
    private CircuitDiode _green = null!;
    private CircuitDiode _blue = null!;

    public RgbLed(string id, ComponentProperties properties)
        : base(id, Spec, properties)
    {
    }

    public static ComponentDefinition Spec { get; } = new(
        "rgb-led",
        "Part_RgbLed",
        "output",
        50,
        80,
        [
            new PinDefinition("R", PinKind.Digital, 10, 80),
            new PinDefinition("COM", PinKind.Digital, 20, 80),
            new PinDefinition("G", PinKind.Digital, 30, 80),
            new PinDefinition("B", PinKind.Digital, 40, 80),
        ],
        [new PropertyDefinition("common", PropertyKind.Choice, "cathode", ["cathode", "anode"])]);

    protected override void OnAttach()
    {
        bool commonAnode = Properties.Choice("common") == "anode";
        CircuitDiode Channel(string pin, double forwardVoltage) => commonAnode
            ? LedRule.Add(Circuit, Pin("COM"), Pin(pin), forwardVoltage, Id)
            : LedRule.Add(Circuit, Pin(pin), Pin("COM"), forwardVoltage, Id);

        _red = Channel("R", 1.8);
        _green = Channel("G", 2.2);
        _blue = Channel("B", 3.0);
        WatchPins("R", "G", "B", "COM");
    }

    protected override void Refresh()
    {
        SetState("r", LedRule.Brightness(_red));
        SetState("g", LedRule.Brightness(_green));
        SetState("b", LedRule.Brightness(_blue));
        LedRule.WarnAboutCurrent(_red, Context.Log);
        LedRule.WarnAboutCurrent(_green, Context.Log);
        LedRule.WarnAboutCurrent(_blue, Context.Log);
    }
}
