using ArduSim.Core.Circuits;

namespace ArduSim.Core.Components.Parts;

/// <summary>A momentary pushbutton: pins 1 and 2 are connected while it is pressed.</summary>
[SimComponent]
public sealed class PushButton : SimComponent
{
    private CircuitSwitch _contact = null!;

    public PushButton(string id, ComponentProperties properties)
        : base(id, Spec, properties)
    {
    }

    public static ComponentDefinition Spec { get; } = new(
        "pushbutton",
        "Part_PushButton",
        "input",
        60,
        60,
        [new PinDefinition("1", PinKind.Passive, 10, 60), new PinDefinition("2", PinKind.Passive, 50, 60)],
        [
            new PropertyDefinition("color", PropertyKind.Choice, "red", ["red", "green", "blue", "yellow", "black", "white"]),
            new PropertyDefinition("key", PropertyKind.Text, ""),
        ]);

    protected override void OnAttach() => _contact = Circuit.AddSwitch(Pin("1"), Pin("2"), closed: false);

    public override void OnCircuitReady() => SetState("pressed", 0);

    public override void ApplyInput(string name, double value)
    {
        if (name != "pressed")
        {
            base.ApplyInput(name, value);
            return;
        }

        SetState("pressed", value != 0 ? 1 : 0);
        _contact.Set(value != 0);
    }
}

/// <summary>A slide switch: the middle pin 2 connects to pin 1 (position 0) or pin 3 (position 1).</summary>
[SimComponent]
public sealed class SlideSwitch : SimComponent
{
    private CircuitSwitch _left = null!;
    private CircuitSwitch _right = null!;

    public SlideSwitch(string id, ComponentProperties properties)
        : base(id, Spec, properties)
    {
    }

    public static ComponentDefinition Spec { get; } = new(
        "slide-switch",
        "Part_SlideSwitch",
        "input",
        60,
        40,
        [new PinDefinition("1", PinKind.Passive, 10, 40), new PinDefinition("2", PinKind.Passive, 30, 40), new PinDefinition("3", PinKind.Passive, 50, 40)],
        [new PropertyDefinition("position", PropertyKind.Choice, "0", ["0", "1"])]);

    protected override void OnAttach()
    {
        bool right = Properties.Choice("position") == "1";
        _left = Circuit.AddSwitch(Pin("1"), Pin("2"), closed: !right);
        _right = Circuit.AddSwitch(Pin("2"), Pin("3"), closed: right);
    }

    public override void OnCircuitReady() => SetState("position", _right.Closed ? 1 : 0);

    public override void ApplyInput(string name, double value)
    {
        if (name != "position")
        {
            base.ApplyInput(name, value);
            return;
        }

        bool right = value != 0;
        SetState("position", right ? 1 : 0);
        _left.Set(!right);
        _right.Set(right);
    }
}
