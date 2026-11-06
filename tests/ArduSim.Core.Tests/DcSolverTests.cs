using ArduSim.Core.Circuits;
using ArduSim.Core.Components;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Projects;
using ArduSim.Core.Simulation;

namespace ArduSim.Core.Tests;

/// <summary>Voltages and currents against what a bench calculation gives (ADR-017).</summary>
public sealed class DcSolverTests : IDisposable
{
    private const double OutputOhms = 25;
    private const double LedOhms = 10;

    private readonly SimulationEngine _engine = new();
    private readonly EepromMemory _eeprom = EepromMemory.InMemory(1024);

    public void Dispose() => _eeprom.Dispose();

    private BoardSimulation Build(IEnumerable<ComponentDocument> parts, params string[] wires) =>
        Scenes.Create(Scenes.Scene(parts, wires), _engine, _eeprom);

    private IEnumerable<SimDiagnostic> Warnings(string code) => _engine.Log.Entries.Where(e => e.Code == code);

    private static double Milliamps(BoardSimulation sim, string component, string pin) =>
        sim.Circuit.Read().Pins[new PinRef(component, pin)].Current * 1000;

    private static double Volts(BoardSimulation sim, string component, string pin) =>
        sim.Circuit.Read().Pins[new PinRef(component, pin)].Voltage;

    private static ComponentDocument Resistor(string id, double ohms) => Scenes.Part(id, "resistor", ("resistance", ohms));

    private static ComponentDocument Led(string id, string color = "red") => Scenes.Part(id, "led", ("color", color));

    private static BoardSimulation High(BoardSimulation sim, int pin)
    {
        sim.Mcu.PinMode(pin, 1);
        sim.Mcu.DigitalWrite(pin, 1);
        return sim;
    }

    [Theory]
    [InlineData("red", 1.8)]
    [InlineData("green", 2.2)]
    [InlineData("white", 3.0)]
    public void Led_current_is_what_the_series_circuit_gives(string color, double forward)
    {
        BoardSimulation sim = High(Build([Resistor("r1", 220), Led("led1", color)], "board.D9 r1.1", "r1.2 led1.A", "led1.C board.GND"), 9);

        double expected = (5 - (forward - 0.2)) / (220 + OutputOhms + LedOhms) * 1000;
        Assert.Equal(expected, Milliamps(sim, "led1", "A"), 6);
        Assert.Equal(-expected, Milliamps(sim, "led1", "C"), 6);
        Assert.Equal(5 - (expected / 1000 * OutputOhms), Volts(sim, "board", "D9"), 4);
        Assert.Equal(expected, Milliamps(sim, "r1", "1"), 6);
        Assert.Empty(Warnings("pin-overload"));
        Assert.Empty(Warnings("led-overcurrent"));
    }

    [Theory]
    [InlineData(220, 1)]
    [InlineData(1000, 0.81)]
    [InlineData(10_000, 0.26)]
    public void Led_brightness_falls_with_its_current(double ohms, double brightness)
    {
        BoardSimulation sim = High(Build([Resistor("r1", ohms), Led("led1")], "board.D9 r1.1", "r1.2 led1.A", "led1.C board.GND"), 9);

        Assert.Equal(brightness, sim.FindComponent("led1")!.State["brightness"], 2);
    }

    [Fact]
    public void Led_on_an_input_pullup_glows_faintly()
    {
        BoardSimulation sim = Build([Led("led1")], "board.D9 led1.A", "led1.C board.GND");
        sim.Mcu.PinMode(9, 2);

        double brightness = sim.FindComponent("led1")!.State["brightness"];
        Assert.InRange(brightness, 0.1, 0.2);
        Assert.Equal((5 - 1.6) / (35_000 + LedOhms) * 1000, Milliamps(sim, "led1", "A"), 4);
    }

    [Fact]
    public void Led_without_a_resistor_overloads_the_pin_and_warns_about_both()
    {
        BoardSimulation sim = High(Build([Led("led1")], "board.D9 led1.A", "led1.C board.GND"), 9);

        Assert.Equal((5 - 1.6) / (OutputOhms + LedOhms) * 1000, Milliamps(sim, "led1", "A"), 5);
        SimDiagnostic pin = Assert.Single(Warnings("pin-overload"));
        Assert.Equal("led1", pin.ComponentId);
        Assert.Equal(["D9", "97", "40"], pin.Args);
        SimDiagnostic led = Assert.Single(Warnings("led-overcurrent"));
        Assert.Equal("led1", led.ComponentId);
        Assert.Equal(1, sim.FindComponent("led1")!.State["brightness"]);
    }

    [Fact]
    public void Led_across_the_rails_warns_though_no_pin_is_involved()
    {
        BoardSimulation sim = Build([Led("led1")], "board.5V led1.A", "led1.C board.GND");

        Assert.Equal((5 - 1.6) / LedOhms * 1000, Milliamps(sim, "led1", "A"), 5);
        Assert.Single(Warnings("led-overcurrent"));
        Assert.Empty(Warnings("pin-overload"));
    }

    [Fact]
    public void Pin_limit_is_where_the_current_passes_forty_milliamps()
    {
        BoardSimulation under = High(Build([Resistor("r1", 110)], "board.D9 r1.1", "r1.2 board.GND"), 9);
        Assert.Equal(5.0 / 135 * 1000, Milliamps(under, "r1", "1"), 3);
        Assert.Empty(Warnings("pin-overload"));

        BoardSimulation over = High(Build([Resistor("r1", 90)], "board.D9 r1.1", "r1.2 board.GND"), 9);
        Assert.Single(Warnings("pin-overload"));
        Assert.True(Milliamps(over, "r1", "1") > 40);
    }

    [Fact]
    public void Sinking_counts_toward_the_limit_too()
    {
        BoardSimulation sim = Build([Resistor("r1", 50)], "board.5V r1.1", "r1.2 board.D9");
        sim.Mcu.PinMode(9, 1);
        sim.Mcu.DigitalWrite(9, 0);

        Assert.Equal(5.0 / 75 * 1000, Milliamps(sim, "r1", "1"), 3);
        Assert.Single(Warnings("pin-overload"));
    }

    [Fact]
    public void Short_circuit_keeps_its_own_warning_and_shows_its_current()
    {
        BoardSimulation sim = High(Build([], "board.D13 board.GND.2"), 13);

        Assert.Single(Warnings("short-circuit"));
        Assert.Empty(Warnings("pin-overload"));
        Assert.Equal(NetCondition.Conflict, sim.Circuit.StateOf(new PinRef("board", "D13")).Condition);
        Assert.Equal(-5.0 / OutputOhms * 1000, Milliamps(sim, "board", "D13"), 3);
    }

    [Fact]
    public void Divider_reads_the_ratio_and_a_load_pulls_it_down()
    {
        BoardSimulation open = Build([Resistor("r1", 10_000), Resistor("r2", 10_000)], "board.5V r1.1", "r1.2 board.A0", "board.A0 r2.1", "r2.2 board.GND");
        Assert.Equal(512, open.Mcu.AnalogRead(14));
        Assert.Equal(5000, open.Circuit.SourceResistanceOf(new PinRef("board", "A0")), 3);

        BoardSimulation loaded = Build(
            [Resistor("r1", 10_000), Resistor("r2", 10_000), Resistor("r3", 10_000)],
            "board.5V r1.1", "r1.2 board.A0", "board.A0 r2.1", "r2.2 board.GND", "board.A0 r3.1", "r3.2 board.GND");
        Assert.Equal(Math.Round(5.0 / 3 / 5 * 1023), loaded.Mcu.AnalogRead(14));
    }

    [Fact]
    public void A_load_on_a_potentiometer_pulls_its_wiper_down()
    {
        BoardSimulation sim = Build(
            [Scenes.Part("pot1", "potentiometer"), Resistor("r1", 1000)],
            "pot1.1 board.GND", "pot1.2 board.5V", "pot1.W board.A0", "pot1.W r1.1", "r1.2 board.GND");

        // 5 kΩ to 5 V over 5 kΩ ∥ 1 kΩ to ground: far below the unloaded 2.5 V.
        double lower = 5000.0 * 1000 / 6000;
        Assert.Equal(5 * lower / (lower + 5000), Volts(sim, "board", "A0"), 4);
    }

    [Fact]
    public void Potentiometer_with_one_end_connected_passes_it_on_and_with_none_floats()
    {
        BoardSimulation oneEnd = Build([Scenes.Part("pot1", "potentiometer")], "pot1.2 board.5V", "pot1.W board.A0");
        Assert.Equal(1023, oneEnd.Mcu.AnalogRead(14));

        BoardSimulation noEnd = Build([Scenes.Part("pot1", "potentiometer")], "pot1.W board.A0");
        Assert.Equal(NetCondition.Floating, noEnd.Circuit.StateOf(new PinRef("board", "A0")).Condition);
    }

    [Fact]
    public void Two_leds_in_series_share_the_drop()
    {
        BoardSimulation sim = High(
            Build([Resistor("r1", 220), Led("a"), Led("b")], "board.D9 r1.1", "r1.2 a.A", "a.C b.A", "b.C board.GND"),
            9);

        double expected = (5 - 3.2) / (220 + OutputOhms + (2 * LedOhms)) * 1000;
        Assert.Equal(expected, Milliamps(sim, "a", "A"), 5);
        Assert.Equal(expected, Milliamps(sim, "b", "A"), 5);
    }

    [Fact]
    public void Three_white_leds_in_series_stay_dark_on_five_volts()
    {
        BoardSimulation sim = High(
            Build([Resistor("r1", 220), Led("a", "white"), Led("b", "white"), Led("c", "white")], "board.D9 r1.1", "r1.2 a.A", "a.C b.A", "b.C c.A", "c.C board.GND"),
            9);

        Assert.Equal(0, sim.FindComponent("a")!.State["brightness"]);
        Assert.Equal(0, sim.FindComponent("c")!.State["brightness"]);
    }

    [Fact]
    public void In_parallel_the_lower_forward_voltage_takes_the_current()
    {
        BoardSimulation sim = High(
            Build([Resistor("r1", 220), Led("red"), Led("blue", "blue")], "board.D9 r1.1", "r1.2 red.A", "r1.2 blue.A", "red.C board.GND", "blue.C board.GND"),
            9);

        Assert.Equal(1, sim.FindComponent("red")!.State["brightness"]);
        Assert.Equal(0, sim.FindComponent("blue")!.State["brightness"]);
        Assert.Equal(0, Milliamps(sim, "blue", "A"));
    }

    [Fact]
    public void Reversed_led_carries_nothing_and_leaves_the_voltage_where_it_is()
    {
        BoardSimulation sim = High(Build([Resistor("r1", 220), Led("led1")], "board.D9 r1.1", "r1.2 led1.C", "led1.A board.GND"), 9);

        Assert.Equal(0, sim.FindComponent("led1")!.State["brightness"]);
        Assert.Equal(0, Milliamps(sim, "led1", "A"));
        Assert.Equal(5, Volts(sim, "led1", "C"), 3);
    }

    [Fact]
    public void A_pin_behind_a_dark_led_still_floats()
    {
        BoardSimulation sim = Build([Led("led1")], "board.5V led1.A", "led1.C board.D2");
        sim.Mcu.PinMode(2, 0);
        sim.Mcu.DigitalRead(2);

        Assert.Single(Warnings("floating-input"));
    }

    [Fact]
    public void Pwm_on_the_anode_scales_the_average_and_not_the_peak()
    {
        BoardSimulation sim = Build([Resistor("r1", 220), Led("led1", "green")], "board.D9 r1.1", "r1.2 led1.A", "led1.C board.GND");
        sim.Mcu.AnalogWrite(9, 51);

        PinReading led = sim.Circuit.Read().Pins[new PinRef("led1", "A")];
        double on = (5 - 2.0) / (220 + OutputOhms + LedOhms);
        Assert.Equal(on, led.PeakCurrent, 8);
        Assert.Equal(on * 51 / 255, led.Current, 8);
        Assert.Equal(51 / 255.0, sim.FindComponent("led1")!.State["brightness"], 5);
    }

    [Fact]
    public void Pwm_on_the_cathode_lights_while_it_is_low()
    {
        BoardSimulation sim = Build([Resistor("r1", 220), Led("led1")], "board.5V r1.1", "r1.2 led1.A", "led1.C board.D9");
        sim.Mcu.AnalogWrite(9, 51);

        Assert.Equal(1 - (51 / 255.0), sim.FindComponent("led1")!.State["brightness"], 5);
    }

    [Fact]
    public void Pwm_on_both_ends_lights_while_the_anode_is_high_and_the_cathode_low()
    {
        BoardSimulation sim = Build([Resistor("r1", 220), Led("led1")], "board.D9 r1.1", "r1.2 led1.A", "led1.C board.D10");
        sim.Mcu.AnalogWrite(9, 128);
        sim.Mcu.AnalogWrite(10, 64);

        Assert.Equal(128 / 255.0 * (1 - (64 / 255.0)), sim.FindComponent("led1")!.State["brightness"], 5);
    }

    [Fact]
    public void Rgb_channels_behind_one_shared_resistor_are_each_their_own_duty()
    {
        BoardSimulation sim = Build(
            [Scenes.Part("rgb1", "rgb-led"), Resistor("r1", 220)],
            "rgb1.R board.D9", "rgb1.G board.D10", "rgb1.B board.D11", "rgb1.COM r1.1", "r1.2 board.GND");
        sim.Mcu.AnalogWrite(9, 128);

        SimComponent rgb = sim.FindComponent("rgb1")!;
        Assert.Equal(128 / 255.0, rgb.State["r"], 3);
        Assert.Equal(0, rgb.State["g"]);
    }

    [Fact]
    public void A_steady_output_stays_steady_though_pwm_at_the_far_end_of_its_load_makes_it_droop()
    {
        BoardSimulation sim = High(Build([Resistor("r1", 220), Led("led1")], "board.D9 r1.1", "r1.2 led1.A", "led1.C board.D10"), 9);
        sim.Mcu.AnalogWrite(10, 64);

        NetState pin = sim.Circuit.StateOf(new PinRef("board", "D9"));
        Assert.Equal(1, pin.Duty);
        Assert.Equal(pin.Voltage, pin.AverageVoltage);
        Assert.InRange(pin.Voltage, 4.5, 5);
        Assert.Equal(1, sim.Mcu.DigitalRead(9));
        Assert.Equal(1 - (64 / 255.0), sim.FindComponent("led1")!.State["brightness"], 3);
    }

    [Fact]
    public void A_low_output_does_not_pick_up_a_tone_through_a_resistor()
    {
        BoardSimulation sim = Build([Resistor("r1", 220)], "board.D8 r1.1", "r1.2 board.D9");
        sim.Mcu.PinMode(9, 1);
        sim.Mcu.DigitalWrite(9, 0);
        sim.Mcu.Tone(8, 440, 0);

        NetState low = sim.Circuit.StateOf(new PinRef("board", "D9"));
        Assert.Equal(0, low.ToneHz);
        Assert.Equal(1, low.Duty);
        Assert.Equal(440, sim.Circuit.StateOf(new PinRef("board", "D8")).ToneHz);
    }

    [Fact]
    public void A_net_that_only_dips_counts_as_steady_at_its_average()
    {
        BoardSimulation sim = Build(
            [Resistor("r1", 1000), Resistor("r2", 10_000)],
            "board.5V r1.1", "r1.2 board.A0", "board.D9 r2.1", "r2.2 board.A0");
        sim.Mcu.AnalogWrite(9, 128);

        NetState input = sim.Circuit.StateOf(new PinRef("board", "A0"));
        Assert.Equal(1, input.Duty);
        Assert.InRange(input.Voltage, 4.5, 5);
        Assert.Equal(input.Voltage, input.AverageVoltage);
    }

    [Fact]
    public void With_many_pwm_sources_the_result_does_not_depend_on_which_was_written_last()
    {
        int[] pins = [3, 5, 6, 9, 10, 11];
        BoardSimulation Scene(IEnumerable<int> order)
        {
            BoardSimulation sim = Scenes.Create(
                Scenes.Scene(
                    [Resistor("r1", 100), .. pins.Select(p => Led("led" + p))],
                    [.. pins.Select(p => $"board.D{p} led{p}.A"), .. pins.Select(p => $"led{p}.C r1.1"), "r1.2 board.GND"]),
                new SimulationEngine(),
                _eeprom);
            foreach (int pin in order)
            {
                sim.Mcu.AnalogWrite(pin, 64);
            }

            return sim;
        }

        BoardSimulation forward = Scene(pins);
        BoardSimulation backward = Scene(pins.Reverse());
        foreach (int pin in pins)
        {
            string led = "led" + pin;
            Assert.Equal(forward.FindComponent(led)!.State["brightness"], backward.FindComponent(led)!.State["brightness"]);
        }


    }

    [Fact]
    public void A_tie_between_phases_is_decided_the_same_way_every_time()
    {
        double Duty(bool toneFirst)
        {
            BoardSimulation sim = Scenes.Create(
                Scenes.Scene([Resistor("r1", 1000), Resistor("r2", 1000)], "board.D9 r1.1", "board.D8 r2.1", "r1.2 board.A0", "r2.2 board.A0"),
                new SimulationEngine(),
                _eeprom);
            if (toneFirst)
            {
                sim.Mcu.Tone(8, 440, 0);
                sim.Mcu.AnalogWrite(9, 64);
            }
            else
            {
                sim.Mcu.AnalogWrite(9, 64);
                sim.Mcu.Tone(8, 440, 0);
            }

            return sim.Circuit.StateOf(new PinRef("board", "A0")).Duty;
        }

        Assert.Equal(Duty(toneFirst: true), Duty(toneFirst: false));
    }

    [Fact]
    public void A_net_behind_two_opposed_diodes_floats_whatever_came_before()
    {
        var circuit = new Circuit(_engine.Log);
        Driver left = circuit.AddDriver(new PinRef("a", "1"), "a");
        Driver right = circuit.AddDriver(new PinRef("b", "1"), "b");
        CircuitDiode first = circuit.AddDiode(new PinRef("a", "1"), new PinRef("m", "1"), 1, 10, "d1");
        circuit.AddDiode(new PinRef("b", "1"), new PinRef("m", "1"), 1, 10, "d2");
        circuit.Build();
        left.Set(NetDrive.Strong(0));
        right.Set(NetDrive.Strong(5));

        Assert.Equal(0, first.Current);
        left.Set(NetDrive.Strong(5));
        right.Set(NetDrive.Strong(0));
        Assert.Equal(NetCondition.Floating, circuit.StateOf(new PinRef("m", "1")).Condition);
    }

    [Fact]
    public void Pwm_average_reaches_an_input_through_a_resistor()
    {
        BoardSimulation sim = Build([Resistor("r1", 1000)], "board.D9 r1.1", "r1.2 board.A0");
        sim.Mcu.AnalogWrite(9, 128);

        Assert.InRange(sim.Mcu.AnalogRead(14), 510, 516);
        NetState state = sim.Circuit.StateOf(new PinRef("board", "A0"));
        Assert.Equal(NetCondition.Pulled, state.Condition);
        Assert.Equal(128 / 255.0, state.Duty, 5);
    }

    [Fact]
    public void A_tone_passes_through_a_resistor_to_the_buzzer()
    {
        BoardSimulation sim = Build([Resistor("r1", 220), Scenes.Part("bz1", "buzzer")], "board.D8 r1.1", "r1.2 bz1.1", "bz1.2 board.GND");
        sim.Mcu.Tone(8, 440, 0);

        Assert.Equal(440, sim.FindComponent("bz1")!.State["frequency"]);
    }

    [Fact]
    public void Wire_and_switch_currents_follow_the_path()
    {
        BoardSimulation sim = Build(
            [Scenes.Part("btn1", "pushbutton"), Resistor("r1", 1000)],
            "board.5V btn1.1", "btn1.2 r1.1", "r1.2 board.GND");
        sim.FindComponent("btn1")!.ApplyInput("pressed", 1);

        CircuitReading reading = sim.Circuit.Read();
        Assert.Equal(5, Math.Abs(reading.Wires["w0"]) * 1000, 3);
        Assert.Equal(5, Math.Abs(reading.Wires["w1"]) * 1000, 3);
        Assert.Equal(5, Math.Abs(reading.Wires["w2"]) * 1000, 3);
        Assert.Equal(5, reading.Pins[new PinRef("btn1", "1")].Current * 1000, 3);
        Assert.Equal(-5, reading.Pins[new PinRef("btn1", "2")].Current * 1000, 3);
        Assert.Equal(-5, reading.Pins[new PinRef("board", "5V")].Current * 1000, 3);
        Assert.Equal(5, reading.Pins[new PinRef("board", "GND")].Current * 1000, 3);

        sim.FindComponent("btn1")!.ApplyInput("pressed", 0);
        reading = sim.Circuit.Read();
        Assert.Equal(0, reading.Pins[new PinRef("r1", "1")].Current, 6);
    }

    [Fact]
    public void A_branching_net_splits_its_current_among_the_wires()
    {
        BoardSimulation sim = High(
            Build([Resistor("r1", 1000), Resistor("r2", 1000)], "board.D9 r1.1", "r1.1 r2.1", "r1.2 board.GND", "r2.2 board.GND"),
            9);

        CircuitReading reading = sim.Circuit.Read();
        double total = 5.0 / (500 + OutputOhms);
        Assert.Equal(total, Math.Abs(reading.Wires["w0"]), 6);
        Assert.Equal(total / 2, Math.Abs(reading.Wires["w1"]), 6);
    }

    [Fact]
    public void A_wire_that_closes_a_loop_has_no_determined_current()
    {
        BoardSimulation sim = High(
            Build([Resistor("r1", 1000), Resistor("r2", 1000)], "board.D9 r1.1", "r1.1 r2.1", "r2.1 board.D9", "r1.2 board.GND", "r2.2 board.GND"),
            9);

        Assert.Single(sim.Circuit.Read().Wires.Values, double.IsNaN);
    }

    [Fact]
    public void Floating_pin_reads_as_not_a_number()
    {
        BoardSimulation sim = Build([Led("led1")], "board.D9 led1.A");

        Assert.True(double.IsNaN(Volts(sim, "led1", "A")));
        Assert.True(double.IsNaN(Volts(sim, "led1", "C")));
    }

    [Fact]
    public void Changing_only_a_part_of_the_circuit_leaves_other_islands_alone()
    {
        BoardSimulation sim = High(
            Build([Resistor("r1", 220), Led("a"), Resistor("r2", 220), Led("b")], "board.D9 r1.1", "r1.2 a.A", "a.C board.GND", "board.D10 r2.1", "r2.2 b.A", "b.C board.GND"),
            9);
        int notified = 0;
        sim.Circuit.Watch(new PinRef("b", "A"), () => notified++);

        sim.Mcu.DigitalWrite(9, 0);
        sim.Mcu.DigitalWrite(9, 1);

        Assert.Equal(0, notified);
    }

    [Fact]
    public void Random_networks_obey_kirchhoff_and_the_diode_law()
    {
#pragma warning disable CA5394 // a test needs a reproducible generator, not a secure one
        var random = new Random(20261106);
        for (int round = 0; round < 300; round++)
        {
            var circuit = new Circuit(_engine.Log);
            int nets = random.Next(2, 9);
            PinRef Node(int i, int element) => new("n" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), "p" + element.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var diodes = new List<CircuitDiode>();
            int next = 0;
            var pins = new List<PinRef>[nets];
            for (int i = 0; i < nets; i++)
            {
                pins[i] = [];
            }

            PinRef Attach(int net)
            {
                PinRef pin = Node(net, next++);
                if (pins[net].Count > 0)
                {
                    circuit.Connect(pins[net][0], pin);
                }

                pins[net].Add(pin);
                return pin;
            }

            for (int e = random.Next(2, 16); e > 0; e--)
            {
                int a = random.Next(nets);
                int b = random.Next(nets);
                PinRef from = Attach(a);
                PinRef to = Attach(b);
                if (random.Next(2) == 0)
                {
                    circuit.AddResistor(from, to, Math.Pow(10, 1 + (random.NextDouble() * 5)), "r");
                }
                else
                {
                    diodes.Add(circuit.AddDiode(from, to, 1 + (random.NextDouble() * 2), 10, "d"));
                }
            }

            for (int s = random.Next(1, 4); s > 0; s--)
            {
                PinRef pin = Attach(random.Next(nets));
                double volts = random.NextDouble() * 5;
                circuit.AddDriver(pin, "s" + s.ToString(System.Globalization.CultureInfo.InvariantCulture)).Set(random.Next(3) switch
                {
                    0 => NetDrive.Weak(volts, 1000),
                    1 => NetDrive.Strong(volts, resistance: 25),
                    _ => NetDrive.Strong(0),
                });
            }

            circuit.Build();
            CircuitReading reading = circuit.Read();
            foreach (Net net in circuit.Nets)
            {
                double sum = net.Pins.Sum(p => reading.Pins[p].Current);
                Assert.True(Math.Abs(sum) < 1e-6, $"round {round}: {sum} A does not balance on {net.Name}");
            }

            foreach (CircuitDiode diode in diodes)
            {
                double anode = circuit.StateOf(diode.A).AverageVoltage;
                double cathode = circuit.StateOf(diode.B).AverageVoltage;
                bool bothFixed = circuit.StateOf(diode.A).Condition != NetCondition.Floating && circuit.StateOf(diode.B).Condition != NetCondition.Floating;
                Assert.True(diode.Current >= 0, $"round {round}: reverse current through a diode");
                if (bothFixed && circuit.NetOf(diode.A) != circuit.NetOf(diode.B) && anode - cathode > diode.Knee + 1e-3)
                {
                    Assert.True(diode.Current > 0, $"round {round}: {anode - cathode} V across a diode that does not conduct");
                }
            }
        }
#pragma warning restore CA5394
    }
}
