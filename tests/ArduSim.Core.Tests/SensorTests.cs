using ArduSim.Core.Components;
using ArduSim.Core.Components.Parts;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Projects;
using ArduSim.Core.Simulation;
using ArduSim.Core.Timing;

namespace ArduSim.Core.Tests;

/// <summary>The M8 parts in a circuit, driven through the MCU as their libraries and sketches do.</summary>
public sealed class SensorTests : IDisposable
{
    private readonly SimulationEngine _engine = new();
    private readonly EepromMemory _eeprom = EepromMemory.InMemory(1024);

    public void Dispose() => _eeprom.Dispose();

    private IEnumerable<SimDiagnostic> Warnings(string code) => _engine.Log.Entries.Where(e => e.Code == code);

    private BoardSimulation Build(ComponentDocument part, params string[] wires) => Scenes.Create(Scenes.Scene([part], wires), _engine, _eeprom);

    private double Microseconds(ulong since) => (_engine.Clock.Now - since) / (double)VirtualTime.Microsecond;

    // ---- DHT: the Adafruit library's read() with its digitalRead fallback ---------------------------

    private static byte[]? ReadDht(Mcu mcu, int pin, uint startMicros)
    {
        mcu.PinMode(pin, 2);
        mcu.DelayMicros(1000);
        mcu.PinMode(pin, 1);
        mcu.DigitalWrite(pin, 0);
        mcu.DelayMicros(startMicros);
        mcu.PinMode(pin, 2);
        mcu.DelayMicros(55);
        int Pulse(int level)
        {
            int count = 0;
            while (mcu.DigitalRead(pin) == level)
            {
                if (count++ >= 16_000)
                {
                    return -1;
                }
            }

            return count;
        }

        if (Pulse(0) < 0 || Pulse(1) < 0)
        {
            return null;
        }

        var cycles = new int[80];
        for (int i = 0; i < 80; i += 2)
        {
            cycles[i] = Pulse(0);
            cycles[i + 1] = Pulse(1);
        }

        var data = new byte[5];
        for (int i = 0; i < 40; i++)
        {
            if (cycles[2 * i] < 0 || cycles[(2 * i) + 1] < 0)
            {
                return null;
            }

            data[i / 8] = (byte)((data[i / 8] << 1) | (cycles[(2 * i) + 1] > cycles[2 * i] ? 1 : 0));
        }

        return (byte)(data[0] + data[1] + data[2] + data[3]) == data[4] ? data : null;
    }

    private const string DhtWiring = "board.5V dht1.VCC|board.GND dht1.GND|board.D7 dht1.DATA";

    [Fact]
    public void Dht22_answers_a_1_ms_start_with_tenths_and_a_sign_bit()
    {
        BoardSimulation sim = Build(Scenes.Part("dht1", "dht", ("temperature", -7.3), ("humidity", 55.5)), DhtWiring.Split('|'));

        byte[] data = ReadDht(sim.Mcu, 7, 1100)!;

        Assert.Equal(555, (data[0] << 8) | data[1]);
        Assert.Equal(0x8000 | 73, (data[2] << 8) | data[3]);
    }

    [Fact]
    public void Dht11_needs_an_18_ms_start_and_says_so_when_it_gets_less()
    {
        BoardSimulation sim = Build(Scenes.Part("dht1", "dht", ("model", "dht11"), ("temperature", 23.4), ("humidity", 61)), DhtWiring.Split('|'));

        Assert.Null(ReadDht(sim.Mcu, 7, 1100));
        Assert.Equal("dht1", Assert.Single(Warnings("dht-start")).ComponentId);

        sim.Mcu.DelayMicros(2_000_000);
        byte[] data = ReadDht(sim.Mcu, 7, 20_000)!;
        Assert.Equal([61, 0, 23, 4], data[..4]);
    }

    [Fact]
    public void Dht_follows_live_input_and_clamps_dht11_readings_to_its_range()
    {
        BoardSimulation sim = Build(Scenes.Part("dht1", "dht", ("model", "dht11")), DhtWiring.Split('|'));
        sim.FindComponent("dht1")!.ApplyInput("temperature", 70);

        byte[] data = ReadDht(sim.Mcu, 7, 20_000)!;

        Assert.Equal(50, data[2]);
        Assert.Single(Warnings("dht-range"));
        Assert.Equal(70, sim.FindComponent("dht1")!.State["temperature"]);
    }

    [Fact]
    public void Unpowered_dht_does_not_answer_and_warns()
    {
        BoardSimulation sim = Build(Scenes.Part("dht1", "dht"), "board.D7 dht1.DATA");

        Assert.Null(ReadDht(sim.Mcu, 7, 1100));
        Assert.Single(Warnings("sensor-power"));
    }

    // ---- HC-SR04 ----------------------------------------------------------------------------------

    private static readonly string[] SonarWiring = ["board.5V s1.VCC", "board.GND s1.GND", "board.D9 s1.TRIG", "board.D10 s1.ECHO"];

    private double Echo(Mcu mcu, uint trigger = 10)
    {
        mcu.PinMode(9, 1);
        mcu.DigitalWrite(9, 0);
        mcu.DelayMicros(2);
        mcu.DigitalWrite(9, 1);
        mcu.DelayMicros(trigger);
        mcu.DigitalWrite(9, 0);
        ulong start = _engine.Clock.Now;
        while (mcu.DigitalRead(10) == 0)
        {
            if (Microseconds(start) > 2000)
            {
                return 0;
            }
        }

        ulong rise = _engine.Clock.Now;
        while (mcu.DigitalRead(10) == 1)
        {
        }

        return Microseconds(rise);
    }

    [Fact]
    public void Echo_lasts_58_microseconds_per_centimetre()
    {
        BoardSimulation sim = Build(Scenes.Part("s1", "hc-sr04", ("distance", 50)), SonarWiring);

        Assert.InRange(Echo(sim.Mcu), 2915 - 8, 2915 + 8);
        sim.FindComponent("s1")!.ApplyInput("distance", 10);
        sim.Mcu.DelayMicros(60_000);
        Assert.InRange(Echo(sim.Mcu), 583 - 8, 583 + 8);
    }

    [Fact]
    public void Beyond_four_metres_echo_times_out_after_38_ms()
    {
        BoardSimulation sim = Build(Scenes.Part("s1", "hc-sr04", ("distance", 450)), SonarWiring);

        Assert.InRange(Echo(sim.Mcu), 37_990, 38_010);
    }

    [Fact]
    public void A_short_trigger_pulse_is_ignored_with_a_warning()
    {
        BoardSimulation sim = Build(Scenes.Part("s1", "hc-sr04"), SonarWiring);

        Assert.Equal(0, Echo(sim.Mcu, trigger: 0));
        Assert.Single(Warnings("sonar-trigger"));
    }

    // ---- IR receiver and remote -------------------------------------------------------------------

    [Fact]
    public void Remote_key_reaches_the_receiver_as_a_nec_frame()
    {
        var parts = new[] { Scenes.Part("ir1", "ir-receiver"), Scenes.Part("remote1", "ir-remote") };
        BoardSimulation sim = Scenes.Create(Scenes.Scene(parts, "board.5V ir1.VCC", "board.GND ir1.GND", "board.D11 ir1.OUT"), _engine, _eeprom);
        var edges = new List<(ulong Time, bool High)>();
        sim.Circuit.Watch(sim.Board.Pin("D11"), () => edges.Add((_engine.Clock.Now, sim.Circuit.StateOf(sim.Board.Pin("D11")).Voltage > 2.5)));
        sim.Mcu.PinMode(11, 0);
        Assert.Equal(1, sim.Mcu.DigitalRead(11));

        sim.FindComponent("remote1")!.ApplyInput("b0", 1);
        sim.Mcu.DelayMicros(80_000);
        sim.FindComponent("remote1")!.ApplyInput("b0", 0);

        ulong[] lengths = [.. edges.Zip(edges.Skip(1), (a, b) => b.Time - a.Time)];
        Assert.Equal(68, edges.Count); // 34 marks, each a fall and a rise
        Assert.Equal(9_000_000UL, lengths[0]);
        Assert.Equal(4_500_000UL, lengths[1]);
        uint data = 0;
        for (int bit = 0; bit < 32; bit++)
        {
            data |= (lengths[3 + (2 * bit)] > 1_000_000 ? 1u : 0) << bit;
        }

        Assert.Equal(0xBA45FF00u, data);
        Assert.Equal(1, sim.Mcu.DigitalRead(11));
    }

    [Fact]
    public void Holding_a_key_sends_repeat_frames_every_108_ms()
    {
        var parts = new[] { Scenes.Part("ir1", "ir-receiver"), Scenes.Part("remote1", "ir-remote") };
        BoardSimulation sim = Scenes.Create(Scenes.Scene(parts, "board.5V ir1.VCC", "board.GND ir1.GND", "board.D11 ir1.OUT"), _engine, _eeprom);
        var falls = new List<ulong>();
        sim.Circuit.Watch(sim.Board.Pin("D11"), () =>
        {
            if (sim.Circuit.StateOf(sim.Board.Pin("D11")).Voltage < 1)
            {
                falls.Add(_engine.Clock.Now);
            }
        });

        sim.FindComponent("remote1")!.ApplyInput("b5", 1);
        sim.Mcu.DelayMicros(330_000);
        sim.FindComponent("remote1")!.ApplyInput("b5", 0);
        sim.Mcu.DelayMicros(300_000);

        // 34 marks for the frame, then 2 per repeat frame at 108, 216 and 324 ms.
        Assert.Equal(34 + 6, falls.Count);
        Assert.Equal(108 * VirtualTime.Millisecond, falls[34] - falls[0]);
    }

    // ---- encoder, keypad, joystick ----------------------------------------------------------------

    [Fact]
    public void Clockwise_detent_pulls_clk_low_before_dt_and_returns_both_high()
    {
        BoardSimulation sim = Build(Scenes.Part("enc1", "rotary-encoder"), "board.5V enc1.+", "board.GND enc1.GND", "board.D2 enc1.CLK", "board.D3 enc1.DT");
        Mcu mcu = sim.Mcu;
        var seen = new List<(int Clk, int Dt)>();
        sim.FindComponent("enc1")!.ApplyInput("turn", 1);
        for (int i = 0; i < 10; i++)
        {
            (int, int) now = (mcu.DigitalRead(2), mcu.DigitalRead(3));
            if (seen.Count == 0 || seen[^1] != now)
            {
                seen.Add(now);
            }

            mcu.DelayMicros(1000);
        }

        Assert.Equal([(0, 1), (0, 0), (1, 0), (1, 1)], seen);
        Assert.Equal(1, sim.FindComponent("enc1")!.State["step"]);
    }

    [Fact]
    public void Counter_clockwise_detents_queue_and_lead_with_dt()
    {
        BoardSimulation sim = Build(Scenes.Part("enc1", "rotary-encoder"), "board.5V enc1.+", "board.GND enc1.GND", "board.D2 enc1.CLK", "board.D3 enc1.DT");
        Mcu mcu = sim.Mcu;
        sim.FindComponent("enc1")!.ApplyInput("turn", -2);
        mcu.DelayMicros(1000);

        Assert.Equal((1, 0), (mcu.DigitalRead(2), mcu.DigitalRead(3)));
        mcu.DelayMicros(20_000);
        Assert.Equal(-2, sim.FindComponent("enc1")!.State["step"]);
        Assert.Equal((1, 1), (mcu.DigitalRead(2), mcu.DigitalRead(3)));
    }

    private static readonly int[] KeypadRows = [9, 8, 7, 6];

    [Fact]
    public void Keypad_key_joins_its_row_and_column()
    {
        BoardSimulation sim = Build(
            Scenes.Part("kp1", "keypad"),
            "board.D9 kp1.R1", "board.D8 kp1.R2", "board.D7 kp1.R3", "board.D6 kp1.R4", "board.D5 kp1.C1", "board.D4 kp1.C2", "board.D3 kp1.C3", "board.D2 kp1.C4");
        Mcu mcu = sim.Mcu;
        foreach (int row in KeypadRows)
        {
            mcu.PinMode(row, 2);
        }

        sim.FindComponent("kp1")!.ApplyInput("k5", 1); // row 2, column 2: '5'
        mcu.PinMode(4, 1);
        mcu.DigitalWrite(4, 0);
        Assert.Equal([1, 0, 1, 1], KeypadRows.Select(mcu.DigitalRead));
        mcu.PinMode(4, 0);
        mcu.PinMode(5, 1);
        mcu.DigitalWrite(5, 0);
        Assert.Equal([1, 1, 1, 1], KeypadRows.Select(mcu.DigitalRead));
        Assert.Equal(5, sim.FindComponent("kp1")!.State["key"]);
        Assert.Equal('5', MatrixKeypad.Labels[5]);
    }

    [Fact]
    public void Joystick_reads_mid_scale_centred_and_full_scale_at_the_stops()
    {
        BoardSimulation sim = Build(Scenes.Part("js1", "joystick"), "board.5V js1.VCC", "board.GND js1.GND", "board.A0 js1.VRX", "board.A1 js1.VRY", "board.D2 js1.SW");
        Mcu mcu = sim.Mcu;
        SimComponent joystick = sim.FindComponent("js1")!;

        Assert.InRange(mcu.AnalogRead(14), 510, 513);
        joystick.ApplyInput("x", 1);
        joystick.ApplyInput("y", -1);
        Assert.Equal(1023, mcu.AnalogRead(14));
        Assert.Equal(0, mcu.AnalogRead(15));
        mcu.PinMode(2, 2);
        joystick.ApplyInput("pressed", 1);
        Assert.Equal(0, mcu.DigitalRead(2));
    }

    // ---- timer interrupts (ABI 2.2) ---------------------------------------------------------------

    [Fact]
    public void Timer_interrupt_ticks_every_period_inside_delay()
    {
        BoardSimulation sim = Scenes.Create(Scenes.Scene([]), _engine, _eeprom);
        Mcu mcu = sim.Mcu;
        var ticks = new List<ulong>();
        _engine.SchedulingPoint = () =>
        {
            while (mcu.Interrupts.TryBeginDispatch(out int id))
            {
                Assert.Equal(InterruptController.TimerBase + 1, id);
                ticks.Add(_engine.Clock.Now);
                mcu.Interrupts.EndDispatch();
            }
        };

        ulong start = _engine.Clock.Now;
        mcu.StartTimer(1, 50 * VirtualTime.Microsecond);
        mcu.DelayMicros(1000);
        mcu.StopTimer(1);
        mcu.DelayMicros(1000);

        Assert.Equal(20, ticks.Count);
        Assert.Equal([.. Enumerable.Range(1, 20).Select(i => start + ((ulong)i * 50 * VirtualTime.Microsecond))], ticks);
    }

    [Fact]
    public void Timer_ticks_wait_while_interrupts_are_off_and_too_short_periods_are_raised()
    {
        BoardSimulation sim = Scenes.Create(Scenes.Scene([]), _engine, _eeprom);
        Mcu mcu = sim.Mcu;
        int ticks = 0;
        _engine.SchedulingPoint = () =>
        {
            while (mcu.Interrupts.TryBeginDispatch(out _))
            {
                ticks++;
                mcu.Interrupts.EndDispatch();
            }
        };

        mcu.StartTimer(0, 1000);
        Assert.Single(Warnings("timer-period"));
        mcu.SetInterruptsEnabled(false);
        mcu.DelayMicros(100);
        Assert.Equal(0, ticks);
        mcu.SetInterruptsEnabled(true);
        Assert.Equal(1, ticks);
    }
}
