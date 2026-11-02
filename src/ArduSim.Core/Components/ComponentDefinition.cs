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
    string? Unit = null)
{
    /// <summary>Shown in the editor only while another property has a value (the LCD's I2C address with interface = i2c).</summary>
    public (string Property, string Value)? ShownWhen { get; init; }
}

/// <summary>A palette entry that places a part with some properties preset (e.g. "LCD 1602 I2C", ADR-006).</summary>
/// <param name="Properties">Property values as their text.</param>
public sealed record ComponentPreset(string Id, string NameKey, IReadOnlyDictionary<string, string> Properties);

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
    /// <summary>
    /// For parts whose size and pins depend on their properties (the LCD's size and interface): the
    /// definition for given values. It must return the same instance for the same layout.
    /// </summary>
    public Func<ComponentProperties, ComponentDefinition>? Layout { get; init; }

    /// <summary>Palette entries with preset properties; none means one plain entry.</summary>
    public IReadOnlyList<ComponentPreset> Presets { get; init; } = [];

    public PinDefinition? FindPin(string name) => Pins.FirstOrDefault(p => p.Name == name);

    /// <summary>The definition for a part with these property values (itself unless <see cref="Layout"/> is set).</summary>
    public ComponentDefinition Resolve(IReadOnlyDictionary<string, JsonElement>? values) =>
        Layout is null ? this : Layout(new ComponentProperties(this, values));

    /// <summary>A preset's values as JSON, typed by the property kinds.</summary>
    public Dictionary<string, JsonElement> PresetValues(ComponentPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return preset.Properties.ToDictionary(
            p => p.Key,
            p => Properties.FirstOrDefault(d => d.Name == p.Key)?.Kind == PropertyKind.Number
                ? JsonSerializer.SerializeToElement(double.Parse(p.Value, CultureInfo.InvariantCulture))
                : JsonSerializer.SerializeToElement(p.Value),
            StringComparer.Ordinal);
    }
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
