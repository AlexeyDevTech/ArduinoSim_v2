using ArduSim.Core.Boards;
using ArduSim.Core.Buses;
using ArduSim.Core.Circuits;
using ArduSim.Core.Components;
using ArduSim.Core.Components.Parts;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Projects;

namespace ArduSim.Core.Simulation;

/// <summary>
/// One project's simulated hardware: the circuit built from the scene, its parts, and the MCU.
/// Unknown part types and wires to missing pins are reported and left out, never silently dropped.
/// </summary>
public sealed class BoardSimulation
{
    private readonly Dictionary<string, SimComponent> _components;

    private BoardSimulation(Circuit circuit, IReadOnlyList<SimComponent> components, BoardComponent board, Mcu mcu)
    {
        Circuit = circuit;
        Components = components;
        Board = board;
        Mcu = mcu;
        _components = components.ToDictionary(c => c.Id, StringComparer.Ordinal);
    }

    public Circuit Circuit { get; }

    public IReadOnlyList<SimComponent> Components { get; }

    public BoardComponent Board { get; }

    public Mcu Mcu { get; }

    public SimComponent? FindComponent(string id) => _components.GetValueOrDefault(id);

    public static BoardSimulation Create(SceneDocument scene, BoardProfile board, ComponentRegistry registry, SimulationEngine engine, EepromMemory eeprom, int seed, KeyValueStore? kv = null, WifiSettings? wifi = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(engine);
        SimulationLog log = engine.Log;
        var circuit = new Circuit(log);
        var components = new List<SimComponent>();
        BoardComponent? boardComponent = null;

        foreach (ComponentDocument document in scene.Components)
        {
            if (components.Any(c => c.Id == document.Id))
            {
                log.Report(SimSeverity.Warning, "scene", $"Two parts share the id '{document.Id}'; the second is left out.", document.Id, "duplicate-id");
                continue;
            }

            SimComponent? component = registry.Create(document.Type, document.Id, document.Properties);
            if (component is null)
            {
                log.Report(SimSeverity.Warning, "scene", $"Unknown part type '{document.Type}' ({document.Id}) is left out of the simulation.", document.Id, "unknown-type");
                continue;
            }

            if (component is BoardComponent b)
            {
                if (b.Board.Id != board.Id || boardComponent is not null)
                {
                    log.Report(SimSeverity.Warning, "scene", $"Board part {document.Id} is not the project's board ({board.Id}) and is left out.", document.Id, "wrong-board");
                    continue;
                }

                boardComponent = b;
            }

            components.Add(component);
        }

        if (boardComponent is null)
        {
            boardComponent = (BoardComponent)registry.Create(board.Id, "board", null)!;
            components.Insert(0, boardComponent);
        }

        // Bus 0 on its default pins; devices register on it and other buses (or other pins) reach them too.
        I2cBus? i2c = board.I2c.Count > 0 && board.I2c[0] is { Sda: >= 0, Scl: >= 0 } i2cPins
            ? new I2cBus(circuit, log, boardComponent.Pin(board.PinName(i2cPins.Sda)), boardComponent.Pin(board.PinName(i2cPins.Scl)))
            : null;
        SpiBus? spi = board.Spi.Count > 0 && board.Spi[0] is { Sck: >= 0, Mosi: >= 0, Miso: >= 0 } pins
            ? new SpiBus(circuit, log, boardComponent.Pin(board.PinName(pins.Sck)), boardComponent.Pin(board.PinName(pins.Mosi)), boardComponent.Pin(board.PinName(pins.Miso)))
            : null;
        var context = new ComponentContext(circuit, engine, i2c, spi);
        foreach (SimComponent component in components)
        {
            component.Attach(context);
        }

        var byId = components.ToDictionary(c => c.Id, StringComparer.Ordinal);
        foreach (WireDocument wire in scene.Wires)
        {
            if (Resolve(wire.From, byId, wire.Id, log) is { } from && Resolve(wire.To, byId, wire.Id, log) is { } to)
            {
                circuit.Connect(from, to);
            }
        }

        var mcu = new Mcu(board, engine, eeprom, seed, circuit, boardComponent.Id, i2c, spi, kv);
        if (board.HasWifi)
        {
            var radio = new WifiRadio(engine, wifi);
            radio.Changed += () => boardComponent.ShowWifi(radio.Indicator);
            mcu.Wifi = radio;
        }
        circuit.Build();
        foreach (SimComponent component in components)
        {
            component.OnCircuitReady();
        }

        return new BoardSimulation(circuit, components, boardComponent, mcu);
    }

    private static PinRef? Resolve(PinReference end, Dictionary<string, SimComponent> components, string wireId, SimulationLog log)
    {
        if (!components.TryGetValue(end.Component, out SimComponent? component))
        {
            log.Report(SimSeverity.Warning, "scene", $"Wire {wireId} ends at part '{end.Component}', which is not in the scene; the wire is left out.", wireId, "missing-part");
            return null;
        }

        if (component.Definition.FindPin(end.Pin) is null)
        {
            log.Report(SimSeverity.Warning, "scene", $"Wire {wireId} ends at {end.Component}.{end.Pin}, but {component.Definition.Type} has no pin {end.Pin}; the wire is left out.", end.Component, "missing-pin");
            return null;
        }

        return component.Pin(end.Pin);
    }
}
