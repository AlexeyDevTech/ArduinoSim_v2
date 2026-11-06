using System.Globalization;
using System.Text.Json;
using ArduSim.Core.Components;
using ArduSim.Core.Projects;

namespace ArduSim.Core.Editing;

/// <summary>A placed part while the scene is being edited.</summary>
public sealed class PartModel(string id, string type)
{
    public string Id { get; set; } = id;

    public string Type { get; } = type;

    public double X { get; set; }

    public double Y { get; set; }

    /// <summary>0, 90, 180 or 270, clockwise.</summary>
    public int Rotation { get; set; }

    public bool Flip { get; set; }

    public Dictionary<string, JsonElement> Properties { get; } = new(StringComparer.Ordinal);

    internal IDictionary<string, JsonElement>? Unknown { get; set; }
}

/// <summary>A wire from pin to pin (ADR-008): through corners set by hand, or laid out by the editor (ADR-020).</summary>
public sealed class WireModel(string id, PinReference from, PinReference to)
{
    public string Id { get; set; } = id;

    public PinReference From { get; set; } = from;

    public PinReference To { get; set; } = to;

    /// <summary>The route's corners, whoever chose them.</summary>
    public List<(double X, double Y)> Waypoints { get; } = [];

    /// <summary>The editor chose the corners and may choose again: they are not saved. False once someone edits the route by hand.</summary>
    public bool IsAuto { get; set; } = true;

    /// <summary><c>auto</c> (by what the net carries) or a swatch name.</summary>
    public string Color { get; set; } = "auto";

    internal IDictionary<string, JsonElement>? Unknown { get; set; }

    public bool Touches(string partId) => From.Component == partId || To.Component == partId;
}

/// <summary>The mutable scene behind the editor; converts to and from <see cref="SceneDocument"/>.</summary>
public sealed class SceneModel
{
    public List<PartModel> Parts { get; } = [];

    public List<WireModel> Wires { get; } = [];

    internal IDictionary<string, JsonElement>? Unknown { get; set; }

    public PartModel? FindPart(string id) => Parts.FirstOrDefault(p => p.Id == id);

    public WireModel? FindWire(string id) => Wires.FirstOrDefault(w => w.Id == id);

    public static SceneModel FromDocument(SceneDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var scene = new SceneModel { Unknown = document.Unknown };
        foreach (ComponentDocument c in document.Components)
        {
            var part = new PartModel(c.Id, c.Type) { X = c.X, Y = c.Y, Rotation = Normalize(c.Rotation), Flip = c.Flip, Unknown = c.Unknown };
            foreach ((string name, JsonElement value) in c.Properties ?? new Dictionary<string, JsonElement>())
            {
                part.Properties[name] = value.Clone();
            }

            scene.Parts.Add(part);
        }

        foreach (WireDocument w in document.Wires)
        {
            var wire = new WireModel(w.Id, w.From, w.To) { Color = w.Color, Unknown = w.Unknown };
            wire.Waypoints.AddRange(w.Waypoints.Where(p => p.Count >= 2).Select(p => (p[0], p[1])));
            wire.IsAuto = w.AutoRouted || wire.Waypoints.Count == 0;
            scene.Wires.Add(wire);
        }

        return scene;
    }

    /// <summary>The document form; property values equal to the definition's default are left out (ADR-008).</summary>
    /// <param name="registry">The parts' definitions.</param>
    /// <param name="keepRoutes">An undo step keeps the laid-out routes too, so that undo brings back what was on screen; a file does not need them.</param>
    public SceneDocument ToDocument(ComponentRegistry registry, bool keepRoutes = false)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return new SceneDocument
        {
            Components = Parts.Select(p => new ComponentDocument
            {
                Id = p.Id,
                Type = p.Type,
                X = p.X,
                Y = p.Y,
                Rotation = p.Rotation,
                Flip = p.Flip,
                Properties = NonDefault(p, registry.Find(p.Type)),
                Unknown = p.Unknown,
            }).ToList(),
            Wires = Wires.Select(w => new WireDocument
            {
                Id = w.Id,
                From = w.From,
                To = w.To,
                Waypoints = w.IsAuto && !keepRoutes ? [] : w.Waypoints.Select(p => (IReadOnlyList<double>)[p.X, p.Y]).ToList(),
                AutoRouted = w.IsAuto && keepRoutes,
                Color = w.Color,
                Unknown = w.Unknown,
            }).ToList(),
            Unknown = Unknown,
        };
    }

    internal static int Normalize(int rotation) => ((rotation % 360) + 360) % 360 / 90 * 90;

    private static Dictionary<string, JsonElement>? NonDefault(PartModel part, ComponentDefinition? definition)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach ((string name, JsonElement value) in part.Properties)
        {
            PropertyDefinition? property = definition?.Properties.FirstOrDefault(d => d.Name == name);
            if (property is null || !IsDefault(property, value))
            {
                result[name] = value;
            }
        }

        return result.Count == 0 ? null : result;
    }

    private static bool IsDefault(PropertyDefinition property, JsonElement value)
    {
        string text = value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText();
        if (property.Kind == PropertyKind.Number &&
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) &&
            double.TryParse(property.Default, NumberStyles.Float, CultureInfo.InvariantCulture, out double fallback))
        {
            return number.Equals(fallback);
        }

        return string.Equals(text, property.Default, StringComparison.Ordinal);
    }
}
