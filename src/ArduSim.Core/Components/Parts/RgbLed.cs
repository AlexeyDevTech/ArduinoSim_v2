namespace ArduSim.Core.Components.Parts;

/// <summary>An RGB LED with a common cathode (channels are anodes) or a common anode (channels sink).</summary>
[SimComponent]
public sealed class RgbLed : SimComponent
{
    private bool _commonAnode;

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
        _commonAnode = Properties.Choice("common") == "anode";
        WatchPins("R", "G", "B", "COM");
    }

    protected override void Refresh()
    {
        SetState("r", Channel("R", 1.8));
        SetState("g", Channel("G", 2.2));
        SetState("b", Channel("B", 3.0));
    }

    private double Channel(string pin, double forwardVoltage) => _commonAnode
        ? LedRule.Brightness(Net("COM"), Net(pin), forwardVoltage)
        : LedRule.Brightness(Net(pin), Net("COM"), forwardVoltage);
}
