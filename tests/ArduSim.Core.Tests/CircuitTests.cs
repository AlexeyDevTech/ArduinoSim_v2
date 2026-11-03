using System.Text.Json;
using ArduSim.Boards;
using ArduSim.Core.Components;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Projects;
using ArduSim.Core.Simulation;

namespace ArduSim.Core.Tests;

/// <summary>Builds scenes in code for tests.</summary>
internal static class Scenes
{
    public static readonly ComponentRegistry Registry = ComponentRegistry.CreateDefault([Nano.Profile]);

    public static BoardSimulation Create(SceneDocument scene, SimulationEngine engine, EepromMemory eeprom, ArduSim.Core.Components.Parts.IPartFiles? files = null) =>
        BoardSimulation.Create(scene, Nano.Profile, Registry, engine, eeprom, seed: 1, files: files);

    public static SceneDocument Scene(IEnumerable<ComponentDocument> parts, params string[] wires) => new()
    {
        Components = [new ComponentDocument { Id = "board", Type = Nano.Id }, .. parts],
        Wires = wires.Select((w, i) =>
        {
            string[] ends = w.Split(' ');
            return new WireDocument { Id = "w" + i, From = Ref(ends[0]), To = Ref(ends[1]) };
        }).ToList(),
    };

    public static ComponentDocument Part(string id, string type, params (string Name, object Value)[] properties) => new()
    {
        Id = id,
        Type = type,
        Properties = properties.ToDictionary(p => p.Name, p => JsonSerializer.SerializeToElement(p.Value)),
    };

    private static PinReference Ref(string text)
    {
        int dot = text.IndexOf('.', StringComparison.Ordinal);
        return new PinReference(text[..dot], text[(dot + 1)..]);
    }
}

public sealed class CircuitTests : IDisposable
{
    private readonly SimulationEngine _engine = new();
    private readonly EepromMemory _eeprom = EepromMemory.InMemory(1024);

    public void Dispose() => _eeprom.Dispose();

    private BoardSimulation Build(IEnumerable<ComponentDocument> parts, params string[] wires) =>
        Scenes.Create(Scenes.Scene(parts, wires), _engine, _eeprom);

    private IEnumerable<SimDiagnostic> Warnings(string code) => _engine.Log.Entries.Where(e => e.Code == code);

    [Fact]
    public void Button_to_ground_with_input_pullup_reads_low_while_pressed_and_fires_falling()
    {
        BoardSimulation sim = Build([Scenes.Part("btn1", "pushbutton")], "board.D2 btn1.1", "btn1.2 board.GND");
        Mcu mcu = sim.Mcu;
        mcu.PinMode(2, 2);
        mcu.AttachInterrupt(0, 2); // FALLING

        Assert.Equal(1, mcu.DigitalRead(2));
        sim.FindComponent("btn1")!.ApplyInput("pressed", 1);
        Assert.Equal(0, mcu.DigitalRead(2));
        Assert.True(mcu.Interrupts.TryBeginDispatch(out int number) && number == 0);
        mcu.Interrupts.EndDispatch();
        sim.FindComponent("btn1")!.ApplyInput("pressed", 0);
        Assert.Equal(1, mcu.DigitalRead(2));
        Assert.Empty(Warnings("floating-input"));
    }

    [Fact]
    public void External_pull_down_and_button_to_5v()
    {
        BoardSimulation sim = Build(
            [Scenes.Part("btn1", "pushbutton"), Scenes.Part("r1", "resistor", ("resistance", 10_000))],
            "board.D3 btn1.1", "btn1.2 board.5V", "board.D3 r1.1", "r1.2 board.GND");
        sim.Mcu.PinMode(3, 0);

        Assert.Equal(0, sim.Mcu.DigitalRead(3));
        sim.FindComponent("btn1")!.ApplyInput("pressed", 1);
        Assert.Equal(1, sim.Mcu.DigitalRead(3));
    }

    [Fact]
    public void Internal_pullup_against_a_10k_pull_down_reads_low()
    {
        BoardSimulation sim = Build([Scenes.Part("r1", "resistor", ("resistance", 10_000))], "board.D4 r1.1", "r1.2 board.GND");
        sim.Mcu.PinMode(4, 2);

        Assert.Equal(0, sim.Mcu.DigitalRead(4)); // 5 V × 10k / (10k + 35k) ≈ 1.1 V
    }

    [Fact]
    public void Button_without_a_pull_resistor_floats_and_the_warning_points_at_it()
    {
        BoardSimulation sim = Build([Scenes.Part("btn1", "pushbutton")], "board.D2 btn1.1", "btn1.2 board.5V");
        sim.Mcu.PinMode(2, 0);

        sim.Mcu.DigitalRead(2);
        sim.Mcu.DigitalRead(2);

        SimDiagnostic warning = Assert.Single(Warnings("floating-input"));
        Assert.Equal("btn1", warning.ComponentId);
        Assert.Contains("board.D2, btn1.1", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Output_wired_to_ground_is_a_short_circuit()
    {
        BoardSimulation sim = Build([], "board.D13 board.GND.2");
        sim.Mcu.PinMode(13, 1);
        sim.Mcu.DigitalWrite(13, 1);

        Assert.Single(Warnings("short-circuit"));
    }

    [Fact]
    public void Led_behind_a_resistor_follows_the_pin_and_its_pwm_duty()
    {
        BoardSimulation sim = Build(
            [Scenes.Part("r1", "resistor", ("resistance", 220)), Scenes.Part("led1", "led", ("color", "green"))],
            "board.D9 r1.1", "r1.2 led1.A", "led1.C board.GND");
        SimComponent led = sim.FindComponent("led1")!;
        sim.Mcu.PinMode(9, 1);

        Assert.Equal(0, led.State["brightness"]);
        sim.Mcu.DigitalWrite(9, 1);
        Assert.Equal(1, led.State["brightness"]);
        sim.Mcu.AnalogWrite(9, 64);
        Assert.Equal(64 / 255.0, led.State["brightness"], 3);
        sim.Mcu.DigitalWrite(9, 0);
        Assert.Equal(0, led.State["brightness"]);
    }

    [Fact]
    public void Led_with_a_floating_cathode_stays_dark()
    {
        BoardSimulation sim = Build([Scenes.Part("led1", "led")], "board.D9 led1.A");
        sim.Mcu.PinMode(9, 1);
        sim.Mcu.DigitalWrite(9, 1);

        Assert.Equal(0, sim.FindComponent("led1")!.State["brightness"]);
    }

    [Fact]
    public void Potentiometer_sets_the_analog_reading()
    {
        BoardSimulation sim = Build([Scenes.Part("pot1", "potentiometer")], "pot1.1 board.GND", "pot1.2 board.5V", "pot1.W board.A0");

        Assert.InRange(sim.Mcu.AnalogRead(14), 510, 513);
        sim.FindComponent("pot1")!.ApplyInput("value", 1);
        Assert.Equal(1023, sim.Mcu.AnalogRead(14));
        sim.FindComponent("pot1")!.ApplyInput("value", 0.25);
        Assert.InRange(sim.Mcu.AnalogRead(14), 254, 257);
        Assert.Empty(Warnings("floating-input"));
    }

    [Fact]
    public void Slide_switch_selects_between_rails()
    {
        BoardSimulation sim = Build([Scenes.Part("sw1", "slide-switch")], "sw1.1 board.GND", "sw1.3 board.5V", "sw1.2 board.D4");
        sim.Mcu.PinMode(4, 0);

        Assert.Equal(0, sim.Mcu.DigitalRead(4));
        sim.FindComponent("sw1")!.ApplyInput("position", 1);
        Assert.Equal(1, sim.Mcu.DigitalRead(4));
    }

    [Fact]
    public void Rgb_led_channels_light_independently()
    {
        BoardSimulation sim = Build([Scenes.Part("rgb1", "rgb-led")], "rgb1.R board.D9", "rgb1.G board.D10", "rgb1.B board.D11", "rgb1.COM board.GND");
        SimComponent rgb = sim.FindComponent("rgb1")!;
        sim.Mcu.AnalogWrite(9, 255);
        sim.Mcu.AnalogWrite(11, 128);

        Assert.Equal((1.0, 0.0), (rgb.State["r"], rgb.State["g"]));
        Assert.Equal(128 / 255.0, rgb.State["b"], 3);
    }

    [Fact]
    public void Buzzer_shows_the_tone_frequency()
    {
        BoardSimulation sim = Build([Scenes.Part("bz1", "buzzer")], "bz1.1 board.D8", "bz1.2 board.GND");
        SimComponent buzzer = sim.FindComponent("bz1")!;

        sim.Mcu.Tone(8, 440, 0);
        Assert.Equal(440, buzzer.State["frequency"]);
        sim.Mcu.NoTone(8);
        Assert.Equal(0, buzzer.State["frequency"]);
    }

    [Fact]
    public void Onboard_led_follows_D13()
    {
        BoardSimulation sim = Build([]);
        sim.Mcu.PinMode(13, 1);
        sim.Mcu.DigitalWrite(13, 1);

        Assert.Equal(1, sim.Board.State["L"]);
        sim.Mcu.DigitalWrite(13, 0);
        Assert.Equal(0, sim.Board.State["L"]);
    }

    [Fact]
    public void Unknown_parts_and_bad_wires_are_reported_and_left_out()
    {
        BoardSimulation sim = Build([Scenes.Part("x1", "flux-capacitor")], "board.D2 ghost.1", "board.D2 board.Q9");

        Assert.Null(sim.FindComponent("x1"));
        Assert.Equal(3, Warnings("scene").Count());
    }

    [Fact]
    public void Board_part_is_created_when_the_scene_has_none()
    {
        BoardSimulation sim = Scenes.Create(new SceneDocument(), _engine, _eeprom);

        Assert.Equal("board", sim.Board.Id);
        Assert.True(sim.Circuit.StateOf(sim.Board.Pin("5V")).HasVoltage);
    }

    [Fact]
    public void Pin_geometry_rotates_about_the_part_center()
    {
        ComponentDefinition resistor = Scenes.Registry.Find("resistor")!;
        PinDefinition pin2 = resistor.FindPin("2")!;

        Assert.Equal((160.0, 110.0), ComponentGeometry.PinPosition(resistor, pin2, 100, 100, 0, false));
        Assert.Equal((130.0, 140.0), ComponentGeometry.PinPosition(resistor, pin2, 100, 100, 90, false));
        Assert.Equal((100.0, 110.0), ComponentGeometry.PinPosition(resistor, pin2, 100, 100, 0, true));
    }
}
