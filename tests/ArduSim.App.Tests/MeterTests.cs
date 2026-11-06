using System.Globalization;
using ArduSim.App.ViewModels;
using ArduSim.Boards;
using ArduSim.Core.Circuits;
using ArduSim.Core.Components;
using ArduSim.Core.Editing;
using ArduSim.Core.Projects;

namespace ArduSim.App.Tests;

/// <summary>What the view says about voltages and currents (ADR-017).</summary>
public sealed class MeterTests
{
    private static readonly PinReference BoardD9 = new("board", "D9");
    private static readonly PinReference BoardGnd = new("board", "GND");
    private static readonly PinReference LedAnode = new("led1", "A");

    // Wires ask the theme for their colors, and its static fields are pack URIs, which need the scheme registered.
    static MeterTests() => _ = System.IO.Packaging.PackUriHelper.UriSchemePack;

    private static SceneViewModel Scene()
    {
        var document = new SceneDocument
        {
            Components =
            [
                new ComponentDocument { Id = "board", Type = Nano.Id },
                new ComponentDocument { Id = "r1", Type = "resistor" },
                new ComponentDocument { Id = "led1", Type = "led" },
            ],
            Wires =
            [
                new WireDocument { Id = "w1", From = BoardD9, To = new PinReference("r1", "1") },
                new WireDocument { Id = "w2", From = new PinReference("r1", "2"), To = LedAnode },
                new WireDocument { Id = "w3", From = new PinReference("led1", "C"), To = BoardGnd },
            ],
        };
        var editor = new SceneEditor(SceneModel.FromDocument(document), ComponentRegistry.CreateDefault([Nano.Profile]));
        return new SceneViewModel(editor, Nano.Profile, (_, _, _) => { });
    }

    private static void Running(SceneViewModel scene)
    {
        scene.ApplyPins([(9, 2, 0)]);
        scene.ApplyComponent(CircuitState.ComponentId, new Dictionary<string, double>
        {
            [CircuitState.VoltageKey("board", "D9")] = 4.706,
            [CircuitState.CurrentKey("board", "D9")] = -0.0118,
            [CircuitState.PeakKey("board", "D9")] = 0.0118,
            [CircuitState.VoltageKey("r1", "1")] = 4.706,
            [CircuitState.VoltageKey("r1", "2")] = 2.118,
            [CircuitState.CurrentKey("r1", "1")] = 0.0118,
            [CircuitState.VoltageKey("led1", "A")] = 2.118,
            [CircuitState.CurrentKey("led1", "A")] = 0.0118,
            [CircuitState.PeakKey("led1", "A")] = 0.0118,
            [CircuitState.VoltageKey("led1", "C")] = 0,
            [CircuitState.WireKey("w1")] = 0.0118,
            [CircuitState.WireKey("w2")] = double.NaN,
        });
    }

    private static T InRussian<T>(Func<T> body)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
        try
        {
            return body();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void A_board_pin_says_its_level_voltage_and_which_way_the_current_goes() =>
        InRussian(() =>
        {
            SceneViewModel scene = Scene();
            Running(scene);

            Assert.Equal("HIGH · 4,71 В · отдаёт 11,8 мА", scene.LevelText(BoardD9));
            return 0;
        });

    [Fact]
    public void A_part_pin_behind_a_resistor_says_its_voltage_and_current() =>
        InRussian(() =>
        {
            SceneViewModel scene = Scene();
            Running(scene);

            Assert.Equal("2,12 В · 11,8 мА", scene.LevelText(LedAnode));
            return 0;
        });

    [Fact]
    public void Pwm_shows_the_average_and_the_peak() =>
        InRussian(() =>
        {
            SceneViewModel scene = Scene();
            scene.ApplyComponent(CircuitState.ComponentId, new Dictionary<string, double>
            {
                [CircuitState.VoltageKey("led1", "A")] = 1.2,
                [CircuitState.CurrentKey("led1", "A")] = 0.003,
                [CircuitState.PeakKey("led1", "A")] = 0.012,
            });

            Assert.Equal("1,20 В · 3,0 мА в среднем, 12,0 мА в импульсе", scene.LevelText(LedAnode));
            return 0;
        });

    [Fact]
    public void Small_currents_are_in_microamps_and_leakage_is_none() =>
        InRussian(() =>
        {
            SceneViewModel scene = Scene();
            scene.ApplyComponent(CircuitState.ComponentId, new Dictionary<string, double>
            {
                [CircuitState.VoltageKey("led1", "A")] = 1.7,
                [CircuitState.CurrentKey("led1", "A")] = 0.000140,
                [CircuitState.VoltageKey("led1", "C")] = 0.1,
                [CircuitState.CurrentKey("led1", "C")] = 0.0000001,
            });

            Assert.Equal("1,70 В · 140 мкА", scene.LevelText(LedAnode));
            Assert.Equal("0,10 В", scene.LevelText(new PinReference("led1", "C")));
            return 0;
        });

    [Fact]
    public void A_floating_pin_says_so()
    {
        SceneViewModel scene = Scene();
        scene.ApplyComponent(CircuitState.ComponentId, new Dictionary<string, double> { [CircuitState.VoltageKey("led1", "A")] = double.NaN });

        Assert.Equal("напряжение не задано", scene.LevelText(LedAnode));
    }

    [Fact]
    public void A_wire_says_its_own_current_unless_it_closes_a_loop() =>
        InRussian(() =>
        {
            SceneViewModel scene = Scene();
            Running(scene);

            Assert.EndsWith("HIGH · 4,71 В · 11,8 мА", scene.WireTooltip(scene.Wires.Single(w => w.Id == "w1")), StringComparison.Ordinal);
            Assert.DoesNotContain("мА", scene.WireTooltip(scene.Wires.Single(w => w.Id == "w2")), StringComparison.Ordinal);
            return 0;
        });

    [Fact]
    public void Leds_and_resistors_carry_a_readout_that_parameters_can_hide_and_a_stop_clears() =>
        InRussian(() =>
        {
            SceneViewModel scene = Scene();
            Running(scene);
            PartViewModel led = scene.Parts.Single(p => p.Id == "led1");
            PartViewModel resistor = scene.Parts.Single(p => p.Id == "r1");

            Assert.Equal("11,8 мА", led.Readout);
            Assert.Equal("2,59 В · 11,8 мА", resistor.Readout);

            scene.CurrentsShown = false;
            Assert.Equal("", led.Readout);
            scene.CurrentsShown = true;
            Assert.Equal("11,8 мА", led.Readout);

            scene.ResetRuntime(clearWarnings: true);
            Assert.Equal("", led.Readout);
            Assert.Equal("", scene.LevelText(LedAnode));
            return 0;
        });

    [Fact]
    public void The_reserved_id_is_not_a_part_and_breaks_nothing()
    {
        SceneViewModel scene = Scene();

        scene.ApplyComponent(CircuitState.ComponentId, new Dictionary<string, double>());

        Assert.DoesNotContain(scene.Parts, p => p.Id == CircuitState.ComponentId);
    }
}
