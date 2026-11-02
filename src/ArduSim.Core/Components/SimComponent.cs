using ArduSim.Core.Buses;
using ArduSim.Core.Circuits;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Displays;
using ArduSim.Core.Simulation;

namespace ArduSim.Core.Components;

/// <summary>What a part needs while it attaches to the circuit.</summary>
/// <param name="i2c">The board's I2C bus, when it has one; devices register on it.</param>
public sealed class ComponentContext(Circuit circuit, SimulationEngine engine, I2cBus? i2c = null)
{
    public Circuit Circuit { get; } = circuit;

    public SimulationEngine Engine { get; } = engine;

    public I2cBus? I2c { get; } = i2c;

    public SimulationLog Log => Engine.Log;
}

/// <summary>
/// A part's behavior (ADR-007): it binds to the nets of its pins, reacts to their changes and
/// exposes its live state (LED brightness, pressed, knob value) for the view.
/// </summary>
public abstract class SimComponent
{
    private readonly Dictionary<string, double> _state = new(StringComparer.Ordinal);

    protected SimComponent(string id, ComponentDefinition definition, ComponentProperties properties)
    {
        Id = id;
        Definition = definition;
        Properties = properties;
    }

    public event Action<SimComponent>? StateChanged;

    public string Id { get; }

    public ComponentDefinition Definition { get; }

    public ComponentProperties Properties { get; }

    public IReadOnlyDictionary<string, double> State => _state;

    /// <summary>The picture of the part's display, for parts that have one (rendered on demand).</summary>
    public virtual DisplayFrame? Display => null;

    protected ComponentContext Context { get; private set; } = null!;

    protected Circuit Circuit => Context.Circuit;

    public void Attach(ComponentContext context)
    {
        Context = context;
        foreach (PinDefinition pin in Definition.Pins)
        {
            context.Circuit.AddPin(Pin(pin.Name));
        }

        OnAttach();
    }

    /// <summary>Called once the circuit is built and resolved.</summary>
    public virtual void OnCircuitReady() => Refresh();

    /// <summary>User input from the view (press a button, turn a knob).</summary>
    public virtual void ApplyInput(string name, double value) =>
        Context.Log.ReportOnce($"input-{Id}-{name}", SimSeverity.Warning, "unknown-input", $"{Id} ({Definition.Type}) has no input '{name}'.", Id);

    public PinRef Pin(string name) => new(Id, name);

    protected abstract void OnAttach();

    /// <summary>Recomputes the visible state from the nets.</summary>
    protected virtual void Refresh()
    {
    }

    protected void WatchPins(params string[] pins)
    {
        foreach (string pin in pins)
        {
            Circuit.Watch(Pin(pin), Refresh);
        }
    }

    protected NetState Net(string pin) => Circuit.StateOf(Pin(pin));

    /// <summary>The display changed; the next snapshot carries a new frame.</summary>
    protected void NotifyDisplayChanged() => StateChanged?.Invoke(this);

    protected void SetState(string key, double value)
    {
        if (_state.TryGetValue(key, out double old) && old.Equals(value))
        {
            return;
        }

        _state[key] = value;
        StateChanged?.Invoke(this);
    }
}
