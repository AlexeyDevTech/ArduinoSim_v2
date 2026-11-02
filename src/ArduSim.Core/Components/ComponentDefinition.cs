using System.Globalization;
using System.Text.Json;

namespace ArduSim.Core.Components;

public enum PinKind
{
    Digital,
    Analog,
    Power,
    Ground,
    Passive,
}

/// <param name="X">Position in the unrotated part, canvas units of 0.01 inch.</param>
public sealed record PinDefinition(string Name, PinKind Kind, double X, double Y, string? Label = null);

public enum PropertyKind
{
    Number,
    Choice,
    Text,
}

/// <param name="Options">Allowed values for <see cref="PropertyKind.Choice"/>.</param>
public sealed record PropertyDefinition(
    string Name,
    PropertyKind Kind,
    string Default,
    IReadOnlyList<string>? Options = null,
    double Minimum = double.NegativeInfinity,
    double Maximum = double.PositiveInfinity,
    string? Unit = null);

/// <summary>What a part is, as data (ADR-007): pins with positions, editable properties, size.</summary>
/// <param name="NameKey">Resource key of the display name in the app.</param>
public sealed record ComponentDefinition(
    string Type,
    string NameKey,
    string Category,
    double Width,
    double Height,
    IReadOnlyList<PinDefinition> Pins,
    IReadOnlyList<PropertyDefinition> Properties)
{
    public PinDefinition? FindPin(string name) => Pins.FirstOrDefault(p => p.Name == name);
}

/// <summary>Marks a <see cref="SimComponent"/> for discovery; the class exposes <c>public static ComponentDefinition Spec</c>.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SimComponentAttribute : Attribute
{
}

/// <summary>A part's property values: the project file's values over the definition's defaults.</summary>
public sealed class ComponentProperties
{
    private readonly ComponentDefinition _definition;
    private readonly IReadOnlyDictionary<string, JsonElement> _values;

    public ComponentProperties(ComponentDefinition definition, IReadOnlyDictionary<string, JsonElement>? values)
    {
        _definition = definition;
        _values = values ?? new Dictionary<string, JsonElement>();
    }

    public string Text(string name)
    {
        if (_values.TryGetValue(name, out JsonElement value))
        {
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText();
        }

        return Definition(name).Default;
    }

    public double Number(string name)
    {
        PropertyDefinition definition = Definition(name);
        double value = _values.TryGetValue(name, out JsonElement element) && element.ValueKind == JsonValueKind.Number
            ? element.GetDouble()
            : double.Parse(Text(name), CultureInfo.InvariantCulture);
        return Math.Clamp(value, definition.Minimum, definition.Maximum);
    }

    public string Choice(string name)
    {
        string value = Text(name);
        PropertyDefinition definition = Definition(name);
        return definition.Options is { } options && !options.Contains(value) ? definition.Default : value;
    }

    private PropertyDefinition Definition(string name) =>
        _definition.Properties.FirstOrDefault(p => p.Name == name) ?? throw new KeyNotFoundException($"{_definition.Type} has no property {name}.");
}
