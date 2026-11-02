using ArduSim.Core.Circuits;

namespace ArduSim.Core.Components.Parts;

/// <summary>A resistor couples two nets: a pull-up, a pull-down, or a series resistor.</summary>
[SimComponent]
public sealed class Resistor : SimComponent
{
    public Resistor(string id, ComponentProperties properties)
        : base(id, Spec, properties)
    {
    }

    public static ComponentDefinition Spec { get; } = new(
        "resistor",
        "Part_Resistor",
        "passive",
        60,
        20,
        [new PinDefinition("1", PinKind.Passive, 0, 10), new PinDefinition("2", PinKind.Passive, 60, 10)],
        [new PropertyDefinition("resistance", PropertyKind.Number, "220", Minimum: 0.1, Maximum: 1e9, Unit: "Ω")]);

    protected override void OnAttach() => Circuit.AddResistor(Pin("1"), Pin("2"), Properties.Number("resistance"), Id);
}

/// <summary>
/// A potentiometer: the wiper W sits at <c>value</c> (0..1) of the way from pin 1 to pin 2 and
/// acts as a source with the divider's Thevenin resistance.
/// </summary>
[SimComponent]
public sealed class Potentiometer : SimComponent
{
    private Driver _wiper = null!;
    private double _resistance;
    private double _value;

    public Potentiometer(string id, ComponentProperties properties)
        : base(id, Spec, properties)
    {
    }

    public static ComponentDefinition Spec { get; } = new(
        "potentiometer",
        "Part_Potentiometer",
        "input",
        60,
        60,
        [new PinDefinition("1", PinKind.Passive, 10, 60), new PinDefinition("W", PinKind.Analog, 30, 60), new PinDefinition("2", PinKind.Passive, 50, 60)],
        [
            new PropertyDefinition("resistance", PropertyKind.Number, "10000", Minimum: 1, Maximum: 1e9, Unit: "Ω"),
            new PropertyDefinition("value", PropertyKind.Number, "0.5", Minimum: 0, Maximum: 1),
        ]);

    protected override void OnAttach()
    {
        _resistance = Properties.Number("resistance");
        _value = Properties.Number("value");
        _wiper = Circuit.AddDriver(Pin("W"), Id);
        WatchPins("1", "2");
    }

    public override void ApplyInput(string name, double value)
    {
        if (name != "value")
        {
            base.ApplyInput(name, value);
            return;
        }

        _value = Math.Clamp(value, 0, 1);
        Refresh();
    }

    protected override void Refresh()
    {
        SetState("value", _value);
        NetState end1 = Net("1");
        NetState end2 = Net("2");
        double toEnd1 = Math.Max(_resistance * _value, 1);
        double toEnd2 = Math.Max(_resistance * (1 - _value), 1);
        if (end1.HasVoltage && end2.HasVoltage)
        {
            double voltage = end1.AverageVoltage + ((end2.AverageVoltage - end1.AverageVoltage) * _value);
            _wiper.Set(NetDrive.Weak(voltage, Math.Max(toEnd1 * toEnd2 / (toEnd1 + toEnd2), 1)));
        }
        else if (end1.HasVoltage || end2.HasVoltage)
        {
            _wiper.Set(end1.HasVoltage ? NetDrive.Weak(end1.AverageVoltage, toEnd1) : NetDrive.Weak(end2.AverageVoltage, toEnd2));
        }
        else
        {
            _wiper.Set(NetDrive.None);
        }
    }
}

/// <summary>A passive piezo buzzer: shows the tone frequency on pin 1 while pin 2 is grounded.</summary>
[SimComponent]
public sealed class Buzzer : SimComponent
{
    public Buzzer(string id, ComponentProperties properties)
        : base(id, Spec, properties)
    {
    }

    public static ComponentDefinition Spec { get; } = new(
        "buzzer",
        "Part_Buzzer",
        "output",
        50,
        50,
        [new PinDefinition("1", PinKind.Digital, 15, 50, "+"), new PinDefinition("2", PinKind.Digital, 35, 50, "−")],
        []);

    protected override void OnAttach() => WatchPins("1", "2");

    protected override void Refresh()
    {
        NetState positive = Net("1");
        NetState negative = Net("2");
        bool grounded = negative.HasVoltage && negative.Voltage < 1;
        SetState("frequency", grounded && positive.HasVoltage ? positive.ToneHz : 0);
    }
}
