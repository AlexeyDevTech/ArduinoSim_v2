using System.Reflection;
using ArduSim.Core.Boards;
using ArduSim.Core.Components.Parts;

namespace ArduSim.Core.Components;

/// <summary>Known part types, discovered from <see cref="SimComponentAttribute"/> plus one entry per board.</summary>
public sealed class ComponentRegistry
{
    private readonly Dictionary<string, (ComponentDefinition Definition, Func<string, ComponentProperties, SimComponent> Create)> _types =
        new(StringComparer.Ordinal);

    public IEnumerable<ComponentDefinition> Definitions => _types.Values.Select(t => t.Definition);

    /// <summary>The built-in parts of <c>ArduSim.Core</c> and the given boards.</summary>
    public static ComponentRegistry CreateDefault(IEnumerable<BoardProfile> boards)
    {
        var registry = new ComponentRegistry();
        registry.Discover(typeof(ComponentRegistry).Assembly);
        foreach (BoardProfile board in boards)
        {
            registry.Register(BoardComponent.DefinitionFor(board), (id, properties) => new BoardComponent(id, board, properties));
        }

        return registry;
    }

    public void Discover(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        foreach (Type type in assembly.GetTypes().Where(t => t.GetCustomAttribute<SimComponentAttribute>() is not null))
        {
            var definition = (ComponentDefinition)(type.GetProperty("Spec", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                ?? throw new InvalidOperationException($"{type} is marked [SimComponent] but has no static Spec."));
            ConstructorInfo constructor = type.GetConstructor([typeof(string), typeof(ComponentProperties)])
                ?? throw new InvalidOperationException($"{type} needs a constructor (string id, ComponentProperties properties).");
            Register(definition, (id, properties) => (SimComponent)constructor.Invoke([id, properties]));
        }
    }

    public void Register(ComponentDefinition definition, Func<string, ComponentProperties, SimComponent> create)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _types[definition.Type] = (definition, create);
    }

    public ComponentDefinition? Find(string type) => _types.TryGetValue(type, out var entry) ? entry.Definition : null;

    public SimComponent? Create(string type, string id, IReadOnlyDictionary<string, System.Text.Json.JsonElement>? values) =>
        _types.TryGetValue(type, out var entry) ? entry.Create(id, new ComponentProperties(entry.Definition, values)) : null;
}
