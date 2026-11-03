using ArduSim.Core.Components;
using ArduSim.Core.Components.Parts;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Displays;
using ArduSim.Core.Projects;
using ArduSim.Core.Simulation;
using ArduSim.Core.Storage;
using ArduSim.Core.Timing;

namespace ArduSim.Core.Tests;

/// <summary>The M9 parts (outputs and modules) in a circuit, driven through the MCU as their libraries do.</summary>
public sealed class OutputTests : IDisposable
{
    private readonly SimulationEngine _engine = new();
    private readonly EepromMemory _eeprom = EepromMemory.InMemory(1024);

    public void Dispose() => _eeprom.Dispose();

    private IEnumerable<SimDiagnostic> Warnings(string code) => _engine.Log.Entries.Where(e => e.Code == code);

    private BoardSimulation Build(IEnumerable<ComponentDocument> parts, params string[] wires) => Scenes.Create(Scenes.Scene(parts, wires), _engine, _eeprom);

    // ---- servo and relay --------------------------------------------------------------------------

    [Fact]
    public void Servo_angle_follows_the_pulse_width()
    {
        BoardSimulation sim = Build([Scenes.Part("s1", "servo")], "board.D9 s1.PWM", "board.5V s1.V+", "board.GND s1.GND");
        Mcu mcu = sim.Mcu;
        mcu.PinMode(9, 1);
        foreach ((uint width, double angle) in new[] { (544u, 0.0), (1472u, 90.0), (2400u, 180.0) })
        {
            ulong start = _engine.Clock.Now;
            mcu.DigitalWrite(9, 1);
            mcu.DelayMicros(width - 4); // the write itself takes 4 µs
            mcu.DigitalWrite(9, 0);
            mcu.DelayMicros(20_000 - (uint)((_engine.Clock.Now - start) / 1000));
            Assert.Equal(angle, sim.FindComponent("s1")!.State["angle"], 0);
        }
    }

    [Fact]
    public void Relay_pulls_in_10_ms_after_a_low_input_and_drops_out_5_ms_after_it_goes_high()
    {
        BoardSimulation sim = Build([Scenes.Part("rl1", "relay")], "board.D7 rl1.IN", "board.5V rl1.VCC", "board.GND rl1.GND", "board.5V rl1.COM", "board.D8 rl1.NO");
        Mcu mcu = sim.Mcu;
        mcu.PinMode(8, 0);
        mcu.PinMode(7, 1);
        mcu.DigitalWrite(7, 1);
        mcu.DelayMicros(1000);
        Assert.Equal(0, sim.FindComponent("rl1")!.State["closed"]);
        mcu.DigitalWrite(7, 0);
        mcu.DelayMicros(9_000);
        Assert.Equal(0, sim.FindComponent("rl1")!.State["closed"]);
        mcu.DelayMicros(2_000);
        Assert.Equal(1, mcu.DigitalRead(8));
        mcu.DigitalWrite(7, 1);
        mcu.DelayMicros(6_000);
        Assert.Equal(0, sim.FindComponent("rl1")!.State["closed"]);
    }

    // ---- seven segments ---------------------------------------------------------------------------

    [Fact]
    public void Multiplexed_digits_read_as_text_and_glow_with_their_share_of_time()
    {
        string[] segments = ["A", "B", "C", "D", "E", "F", "G", "DP"];
        int[] segmentPins = [6, 7, 8, 9, 10, 11, 12, 13];
        int[] digitPins = [2, 3, 4, 5];
        var wires = new List<string>();
        wires.AddRange(digitPins.Select((p, i) => $"board.D{p} seg1.D{i + 1}"));
        wires.AddRange(segmentPins.Select((p, i) => $"board.D{p} seg1.{segments[i]}"));
        BoardSimulation sim = Build([Scenes.Part("seg1", "seven-segment", ("digits", "4"))], [.. wires]);
        Mcu mcu = sim.Mcu;
        foreach (int pin in digitPins.Concat(segmentPins))
        {
            mcu.PinMode(pin, 1);
            mcu.DigitalWrite(pin, digitPins.Contains(pin) ? 1 : 0);
        }

        byte[] font = [0x06, 0x5B, 0x4F, 0x66]; // 1 2 3 4
        for (int frame = 0; frame < 30; frame++)
        {
            for (int digit = 0; digit < 4; digit++)
            {
                for (int s = 0; s < 8; s++)
                {
                    mcu.DigitalWrite(segmentPins[s], ((font[digit] | (digit == 1 ? 0x80 : 0)) >> s) & 1);
                }

                mcu.DigitalWrite(digitPins[digit], 0);
                mcu.DelayMicros(2000);
                mcu.DigitalWrite(digitPins[digit], 1);
            }
        }

        DisplayFrame display = sim.FindComponent("seg1")!.Display!;
        Assert.Equal("12.34", display.Text[0]);
        Assert.InRange(display.Dots.Span[1], 40, 85); // b of digit 1: lit about a quarter of the time (2 or 3 of its 2 ms turns per 20 ms frame)
        Assert.Equal(0, display.Dots.Span[0]);
    }

    // TM1637Display: open drain by pinMode, bits LSB first, the ACK read on the ninth clock.
    private static void Tm1637Write(Mcu mcu, params byte[][] transactions)
    {
        const int clk = 2;
        const int dio = 3;
        void Delay() => mcu.DelayMicros(100);
        foreach (byte[] bytes in transactions)
        {
            mcu.PinMode(dio, 1);
            Delay();
            foreach (byte value in bytes)
            {
                int data = value;
                for (int i = 0; i < 8; i++)
                {
                    mcu.PinMode(clk, 1);
                    Delay();
                    mcu.PinMode(dio, (data & 1) != 0 ? 0 : 1);
                    Delay();
                    mcu.PinMode(clk, 0);
                    Delay();
                    data >>= 1;
                }

                mcu.PinMode(clk, 1);
                mcu.PinMode(dio, 0);
                Delay();
                mcu.PinMode(clk, 0);
                Delay();
                Assert.Equal(0, mcu.DigitalRead(dio)); // ACK
                mcu.PinMode(dio, 1);
                Delay();
                mcu.PinMode(clk, 1);
                Delay();
            }

            mcu.PinMode(dio, 1);
            Delay();
            mcu.PinMode(clk, 0);
            Delay();
            mcu.PinMode(dio, 0);
            Delay();
        }
    }

    [Fact]
    public void Tm1637_shows_digits_and_the_colon_written_like_tm1637display()
    {
        BoardSimulation sim = Build([Scenes.Part("tm1", "tm1637")], "board.D2 tm1.CLK", "board.D3 tm1.DIO", "board.5V tm1.VCC", "board.GND tm1.GND");
        Mcu mcu = sim.Mcu;
        mcu.DigitalWrite(2, 0);
        mcu.DigitalWrite(3, 0);
        mcu.PinMode(2, 0);
        mcu.PinMode(3, 0);

        Tm1637Write(mcu, [0x40], [0xC0, 0x06, 0x5B | 0x80, 0x4F, 0x66], [0x88 | 7]);

        DisplayFrame display = sim.FindComponent("tm1")!.Display!;
        Assert.Equal("12:34", display.Text[0]);
        Assert.Equal(1.0, display.Contrast, 2);
    }

    // ---- MAX7219 ----------------------------------------------------------------------------------

    private static void Max7219Word(Mcu mcu, int devices, int register, int data)
    {
        mcu.DigitalWrite(10, 0);
        for (int device = 0; device < devices; device++)
        {
            int word = (register << 8) | data;
            for (int bit = 15; bit >= 0; bit--)
            {
                mcu.DigitalWrite(12, (word >> bit) & 1);
                mcu.DigitalWrite(11, 1);
                mcu.DigitalWrite(11, 0);
            }
        }

        mcu.DigitalWrite(10, 1);
    }

    [Fact]
    public void Max7219_latches_on_load_and_starts_in_shutdown_bit_banged()
    {
        BoardSimulation sim = Build([Scenes.Part("mx1", "max7219", ("modules", "1"), ("hardware", "fc16"))], "board.D12 mx1.DIN", "board.D11 mx1.CLK", "board.D10 mx1.CS", "board.5V mx1.VCC", "board.GND mx1.GND");
        Mcu mcu = sim.Mcu;
        foreach (int pin in new[] { 10, 11, 12 })
        {
            mcu.PinMode(pin, 1);
        }

        Max7219Word(mcu, 1, 0x0B, 7);
        Max7219Word(mcu, 1, 0x01, 0b1000_0001);
        DisplayFrame off = sim.FindComponent("mx1")!.Display!;
        Assert.All(off.Dots.ToArray(), b => Assert.Equal(0, b));

        Max7219Word(mcu, 1, 0x0C, 1);
        DisplayFrame on = sim.FindComponent("mx1")!.Display!;
        Assert.Equal(0b1000_0001, on.Dots.Span[0]); // FC-16: digit 1 is the top row, bit 7 the left column
        Max7219Word(mcu, 1, 0x0F, 1);
        Assert.All(sim.FindComponent("mx1")!.Display!.Dots.ToArray(), b => Assert.Equal(0xFF, b));
    }

    [Fact]
    public void Four_chained_max7219_take_their_words_over_hardware_spi_device_zero_on_the_right()
    {
        BoardSimulation sim = Build([Scenes.Part("mx1", "max7219", ("modules", "4"))], "board.D11 mx1.DIN", "board.D13 mx1.CLK", "board.D10 mx1.CS", "board.5V mx1.VCC", "board.GND mx1.GND");
        Mcu mcu = sim.Mcu;
        mcu.PinMode(10, 1);
        mcu.SpiBegin();
        void Send(params (int Register, int Data)[] words)
        {
            mcu.DigitalWrite(10, 0);
            foreach ((int register, int data) in words)
            {
                mcu.SpiTransfer((byte)register);
                mcu.SpiTransfer((byte)data);
            }

            mcu.DigitalWrite(10, 1);
        }

        Send((0x0C, 1), (0x0C, 1), (0x0C, 1), (0x0C, 1));
        Send((0x0B, 7), (0x0B, 7), (0x0B, 7), (0x0B, 7));
        Send((0x01, 0x01), (0x00, 0), (0x00, 0), (0x01, 0x80)); // first word → device 3 (left), last → device 0 (right)

        ReadOnlySpan<byte> row0 = sim.FindComponent("mx1")!.Display!.Dots.Span[..4];
        Assert.Equal([0x01, 0x00, 0x00, 0x80], row0.ToArray());
    }

    // ---- WS2812 -----------------------------------------------------------------------------------

    [Fact]
    public void A_frame_fills_the_first_strip_and_goes_on_from_dout_to_the_next()
    {
        BoardSimulation sim = Build(
            [Scenes.Part("a", "ws2812", ("count", 2)), Scenes.Part("b", "ws2812", ("count", 2))],
            "board.D6 a.DIN", "a.DOUT b.DIN", "board.5V a.VCC", "board.GND a.GND", "board.5V b.VCC", "board.GND b.GND");
        Mcu mcu = sim.Mcu;
        mcu.PinMode(6, 1);
        ulong start = _engine.Clock.Now;

        mcu.LedStripWrite(6, [0xFF, 0, 0, 0, 0xFF, 0, 0, 0, 0xFF], 800);

        Assert.Equal(9 * 8 * 1250UL, _engine.Clock.Now - start);
        Assert.Equal([0, 0xFF, 0, 0xFF, 0, 0], sim.FindComponent("a")!.Display!.Dots.ToArray()); // GRB on the wire: green, then red
        Assert.Equal([0, 0, 0xFF, 0, 0, 0], sim.FindComponent("b")!.Display!.Dots.ToArray());
    }

    [Fact]
    public void A_strip_frame_from_an_input_pin_warns_and_goes_nowhere()
    {
        BoardSimulation sim = Build([Scenes.Part("a", "ws2812", ("count", 1))], "board.D6 a.DIN", "board.5V a.VCC", "board.GND a.GND");

        sim.Mcu.LedStripWrite(6, [1, 2, 3], 800);

        Assert.Single(Warnings("led-strip-pin"));
        Assert.Equal([0, 0, 0], sim.FindComponent("a")!.Display!.Dots.ToArray());
    }

    // ---- DS3231 -----------------------------------------------------------------------------------

    private static byte[] RtcRead(Mcu mcu, int register, int count)
    {
        Assert.True(mcu.I2cStart(0x68, false));
        mcu.I2cWrite((byte)register);
        Assert.True(mcu.I2cStart(0x68, true));
        byte[] bytes = [.. Enumerable.Range(0, count).Select(i => mcu.I2cRead(i < count - 1))];
        mcu.I2cStop();
        return bytes;
    }

    [Fact]
    public void Ds3231_counts_from_its_start_time_and_takes_a_new_time()
    {
        BoardSimulation sim = Build(
            [Scenes.Part("rtc1", "ds3231", ("start", "fixed"), ("time", "2026-12-31 23:59:58"), ("temperature", -2.25))],
            "board.A4 rtc1.SDA", "board.A5 rtc1.SCL", "board.5V rtc1.VCC", "board.GND rtc1.GND");
        Mcu mcu = sim.Mcu;
        mcu.I2cBegin();

        Assert.Equal([0x58, 0x59, 0x23, 4, 0x31, 0x12, 0x26], RtcRead(mcu, 0, 7));
        mcu.DelayMicros(2_500_000);
        Assert.Equal([0x00, 0x00, 0x00, 5, 0x01, 0x01, 0x27], RtcRead(mcu, 0, 7));
        Assert.Equal([0xFD, 0xC0], RtcRead(mcu, 0x11, 2)); // -2.25 °C: -3 + 0.75
        Assert.Equal(0, RtcRead(mcu, 0x0F, 1)[0] & 0x80); // OSF clear

        Assert.True(mcu.I2cStart(0x68, false));
        foreach (byte b in new byte[] { 0x00, 0x30, 0x15, 0x08, 0x02, 0x14, 0x03, 0x22 })
        {
            mcu.I2cWrite(b);
        }

        mcu.I2cStop();
        Assert.Equal([0x30, 0x15, 0x08], RtcRead(mcu, 0, 3));
    }

    [Fact]
    public void Ds3231_alarm_one_pulls_int_low_when_the_seconds_match()
    {
        BoardSimulation sim = Build(
            [Scenes.Part("rtc1", "ds3231", ("start", "fixed"), ("time", "2026-01-01 12:00:00"))],
            "board.A4 rtc1.SDA", "board.A5 rtc1.SCL", "board.5V rtc1.VCC", "board.GND rtc1.GND", "board.D2 rtc1.SQW");
        Mcu mcu = sim.Mcu;
        mcu.I2cBegin();
        mcu.PinMode(2, 0);
        Assert.True(mcu.I2cStart(0x68, false));
        foreach (byte b in new byte[] { 0x07, 0x02, 0x80, 0x80, 0x80 })
        {
            mcu.I2cWrite(b);
        }

        mcu.I2cStop();
        Assert.True(mcu.I2cStart(0x68, false));
        mcu.I2cWrite(0x0E);
        mcu.I2cWrite(0x05); // INTCN, A1IE
        mcu.I2cStop();

        mcu.DelayMicros(1_500_000);
        Assert.Equal(1, mcu.DigitalRead(2));
        mcu.DelayMicros(1_000_000);
        Assert.Equal(0, mcu.DigitalRead(2));
        Assert.Equal(0x01, RtcRead(mcu, 0x0F, 1)[0] & 0x01);
    }

    // ---- SD card ----------------------------------------------------------------------------------

    private sealed class MemoryFiles(params CardFile[] files) : IPartFiles
    {
        public IReadOnlyList<CardFile> Saved { get; private set; } = [];

        public IReadOnlyList<CardFile> Load(string folder) => files;

        public void Save(string folder, IReadOnlyList<CardFile> saved) => Saved = saved;
    }

    private static byte Command(Mcu mcu, int command, uint argument)
    {
        mcu.SpiTransfer((byte)(0x40 | command));
        mcu.SpiTransfer((byte)(argument >> 24));
        mcu.SpiTransfer((byte)(argument >> 16));
        mcu.SpiTransfer((byte)(argument >> 8));
        mcu.SpiTransfer((byte)argument);
        mcu.SpiTransfer(0x95);
        for (int i = 0; i < 8; i++)
        {
            byte r = mcu.SpiTransfer(0xFF);
            if (r != 0xFF)
            {
                return r;
            }
        }

        return 0xFF;
    }

    private static byte[] ReadBlock(Mcu mcu, uint block)
    {
        Assert.Equal(0, Command(mcu, 17, block * 512));
        while (mcu.SpiTransfer(0xFF) != 0xFE)
        {
        }

        byte[] data = [.. Enumerable.Range(0, 512).Select(_ => mcu.SpiTransfer(0xFF))];
        mcu.SpiTransfer(0xFF);
        mcu.SpiTransfer(0xFF);
        return data;
    }

    [Fact]
    public void Sd_card_initialises_in_spi_mode_and_reads_and_writes_blocks()
    {
        var files = new MemoryFiles(new CardFile("hello.txt", "hi"u8.ToArray()));
        BoardSimulation sim = Scenes.Create(
            Scenes.Scene([Scenes.Part("sd1", "sd-card")], "board.D10 sd1.CS", "board.D11 sd1.MOSI", "board.D12 sd1.MISO", "board.D13 sd1.SCK", "board.5V sd1.VCC", "board.GND sd1.GND"),
            _engine, _eeprom, files);
        Mcu mcu = sim.Mcu;
        mcu.PinMode(10, 1);
        mcu.SpiBegin();
        mcu.DigitalWrite(10, 0);

        Assert.Equal(0x01, Command(mcu, 0, 0));
        Assert.Equal(0x01, Command(mcu, 8, 0x1AA));
        Assert.Equal([0x00, 0x00, 0x01, 0xAA], [.. Enumerable.Range(0, 4).Select(_ => mcu.SpiTransfer(0xFF))]);
        byte ready;
        do
        {
            Command(mcu, 55, 0);
            ready = Command(mcu, 41, 0x40000000);
        }
        while (ready != 0);

        Assert.Equal(0, Command(mcu, 58, 0));
        Assert.Equal(0x80, mcu.SpiTransfer(0xFF)); // powered up, standard capacity
        byte[] mbr = ReadBlock(mcu, 0);
        Assert.Equal([0x55, 0xAA], mbr[510..]);

        byte[] data = [.. Enumerable.Range(0, 512).Select(i => (byte)i)];
        Assert.Equal(0, Command(mcu, 24, 100 * 512));
        mcu.SpiTransfer(0xFF);
        mcu.SpiTransfer(0xFE);
        foreach (byte b in data)
        {
            mcu.SpiTransfer(b);
        }

        mcu.SpiTransfer(0);
        mcu.SpiTransfer(0);
        Assert.Equal(0x05, mcu.SpiTransfer(0xFF) & 0x1F);
        while (mcu.SpiTransfer(0xFF) != 0xFF)
        {
        }

        Assert.Equal(data, ReadBlock(mcu, 100));
        sim.FindComponent("sd1")!.OnRunEnding();
        Assert.Contains(files.Saved, f => f.Path == "hello.txt");
    }

    [Fact]
    public void Fat_image_keeps_long_names_folders_and_contents()
    {
        CardFile[] files =
        [
            new("log.csv", "t,v\n1,2\n"u8.ToArray()),
            new("README", []),
            new("Data Log 2026.txt", [.. Enumerable.Range(0, 5000).Select(i => (byte)i)]),
            new("photos", null),
            new("photos/Holiday Picture.jpg", [1, 2, 3]),
            new("deep/a/b/c.txt", "c"u8.ToArray()),
        ];

        (FatImage image, IReadOnlyList<string> skipped) = FatImage.Create(files);

        Assert.Empty(skipped);
        var read = image.Files().ToDictionary(f => f.Path);
        foreach (CardFile file in files)
        {
            Assert.True(read.ContainsKey(file.Path), file.Path);
            Assert.Equal(file.Content, read[file.Path].Content);
        }

        Assert.True(read["deep/a"].IsFolder);
        Assert.False(image.IsDirty);
    }

    [Fact]
    public void Fat_image_skips_what_does_not_fit()
    {
        (_, IReadOnlyList<string> skipped) = FatImage.Create([new CardFile("big.bin", new byte[FatImage.Capacity]), new CardFile("ok.txt", [1]), new CardFile("bad?.txt", [1])]);

        Assert.Equal(["bad?.txt", "big.bin"], skipped.Order());
    }

    // ---- interrupts inside long calls ------------------------------------------------------------

    [Fact]
    public void A_timer_tick_inside_a_long_call_is_dispatched_at_its_time()
    {
        BoardSimulation sim = Scenes.Create(Scenes.Scene([]), _engine, _eeprom);
        Mcu mcu = sim.Mcu;
        var ticks = new List<ulong>();
        _engine.SchedulingPoint = () =>
        {
            while (mcu.Interrupts.TryBeginDispatch(out _))
            {
                ticks.Add(_engine.Clock.Now);
                mcu.Interrupts.EndDispatch();
            }
        };
        ulong start = _engine.Clock.Now;
        mcu.StartTimer(0, 30 * VirtualTime.Microsecond);

        mcu.AnalogRead(14); // 112 µs on a Nano

        Assert.Equal([start + 30_000, start + 60_000, start + 90_000], ticks);
    }
}
