using ArduSim.Core.Buses;
using ArduSim.Core.Circuits;
using ArduSim.Core.Components.Parts;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Displays;
using ArduSim.Core.Projects;
using ArduSim.Core.Simulation;
using ArduSim.Core.Timing;

namespace ArduSim.Core.Tests;

/// <summary>The LCD part in a circuit, driven through the MCU the way LiquidCrystal and LiquidCrystal_I2C do.</summary>
public sealed class LcdTests : IDisposable
{
    private readonly SimulationEngine _engine = new();
    private readonly EepromMemory _eeprom = EepromMemory.InMemory(1024);

    public void Dispose() => _eeprom.Dispose();

    private IEnumerable<SimDiagnostic> Warnings(string code) => _engine.Log.Entries.Where(e => e.Code == code);

    private BoardSimulation Build(ComponentDocument lcd, params string[] wires) => Scenes.Create(Scenes.Scene([lcd], wires), _engine, _eeprom);

    // ---- parallel, 4-bit (LiquidCrystal lcd(12, 11, 5, 4, 3, 2)) -----------------------------------

    private static readonly string[] Parallel4Wiring =
    [
        "board.5V lcd1.VDD", "board.GND lcd1.VSS", "board.GND.2 lcd1.RW",
        "board.D12 lcd1.RS", "board.D11 lcd1.E",
        "board.D5 lcd1.D4", "board.D4 lcd1.D5", "board.D3 lcd1.D6", "board.D2 lcd1.D7",
        "board.5V lcd1.A", "board.GND lcd1.K",
    ];

    private static readonly int[] DataPins = [5, 4, 3, 2];

    private static void Pulse(Mcu mcu)
    {
        mcu.DigitalWrite(11, 0);
        mcu.DelayMicros(1);
        mcu.DigitalWrite(11, 1);
        mcu.DelayMicros(1);
        mcu.DigitalWrite(11, 0);
        mcu.DelayMicros(100);
    }

    private static void Write4(Mcu mcu, int value)
    {
        for (int i = 0; i < 4; i++)
        {
            mcu.DigitalWrite(DataPins[i], (value >> i) & 1);
        }

        Pulse(mcu);
    }

    private static void Send(Mcu mcu, int value, bool data)
    {
        mcu.DigitalWrite(12, data ? 1 : 0);
        Write4(mcu, value >> 4);
        Write4(mcu, value);
    }

    private static void BeginParallel(Mcu mcu, int rows)
    {
        foreach (int pin in new[] { 12, 11, 5, 4, 3, 2 })
        {
            mcu.PinMode(pin, 1);
        }

        mcu.DelayMicros(50_000);
        mcu.DigitalWrite(12, 0);
        mcu.DigitalWrite(11, 0);
        Write4(mcu, 0x03);
        mcu.DelayMicros(4500);
        Write4(mcu, 0x03);
        mcu.DelayMicros(4500);
        Write4(mcu, 0x03);
        mcu.DelayMicros(150);
        Write4(mcu, 0x02);
        Send(mcu, rows > 1 ? 0x28 : 0x20, data: false);
        Send(mcu, 0x0C, data: false);
        Send(mcu, 0x01, data: false);
        mcu.DelayMicros(2000);
        Send(mcu, 0x06, data: false);
    }

    private static void Print(Mcu mcu, string text)
    {
        foreach (char c in text)
        {
            Send(mcu, c, data: true);
        }
    }

    [Fact]
    public void Parallel_2004_in_4_bit_mode_shows_four_rows()
    {
        BoardSimulation sim = Build(Scenes.Part("lcd1", Lcd.TypeName, ("size", "20x4"), ("interface", "parallel-4")), Parallel4Wiring);
        var lcd = (Lcd)sim.FindComponent("lcd1")!;

        BeginParallel(sim.Mcu, 4);
        Print(sim.Mcu, "hello, world!");
        int[] offsets = [0x00, 0x40, 0x14, 0x54];
        for (int row = 1; row < 4; row++)
        {
            Send(sim.Mcu, 0x80 | offsets[row], data: false);
            Print(sim.Mcu, "line " + row);
        }

        Assert.True(lcd.Powered);
        Assert.Equal(["hello, world!       ", "line 1              ", "line 2              ", "line 3              "], lcd.Text);
        DisplayFrame frame = lcd.Display;
        Assert.Equal(DisplayStatus.Powered | DisplayStatus.On | DisplayStatus.Backlight, frame.Status);
        Assert.Empty(Warnings("lcd-busy"));
        Assert.Empty(Warnings("lcd-power"));
        Assert.Empty(Warnings("short-circuit"));
    }

    [Fact]
    public void Unconnected_rw_is_pulled_high_so_writes_are_ignored_with_a_warning()
    {
        BoardSimulation sim = Build(Scenes.Part("lcd1", Lcd.TypeName, ("interface", "parallel-4")), [.. Parallel4Wiring.Where(w => !w.EndsWith(".RW", StringComparison.Ordinal))]);
        var lcd = (Lcd)sim.FindComponent("lcd1")!;

        BeginParallel(sim.Mcu, 2);
        Print(sim.Mcu, "hi");

        Assert.Equal("                ", lcd.Text[0]);
        SimDiagnostic warning = Assert.Single(Warnings("lcd-rw"));
        Assert.Equal("lcd1", warning.ComponentId);
    }

    [Fact]
    public void Without_power_the_lcd_is_blank_and_says_why()
    {
        BoardSimulation sim = Build(Scenes.Part("lcd1", Lcd.TypeName, ("interface", "parallel-4")), [.. Parallel4Wiring.Where(w => !w.EndsWith(".VDD", StringComparison.Ordinal))]);
        var lcd = (Lcd)sim.FindComponent("lcd1")!;

        BeginParallel(sim.Mcu, 2);
        Print(sim.Mcu, "hi");

        Assert.False(lcd.Powered);
        Assert.Equal(DisplayStatus.None, lcd.Display.Status);
        Assert.Single(Warnings("lcd-power"));
    }

    [Fact]
    public void Contrast_follows_v0_when_it_is_wired()
    {
        BoardSimulation wired = Build(Scenes.Part("lcd1", Lcd.TypeName, ("interface", "parallel-4")), [.. Parallel4Wiring, "board.GND lcd1.V0"]);
        Assert.Equal(1.0, ((Lcd)wired.FindComponent("lcd1")!).Display.Contrast);

        using var eeprom = EepromMemory.InMemory(1024);
        BoardSimulation unwired = Scenes.Create(Scenes.Scene([Scenes.Part("lcd1", Lcd.TypeName, ("interface", "parallel-4"), ("contrast", 0.4))], Parallel4Wiring), new SimulationEngine(), eeprom);
        Assert.Equal(0.4, ((Lcd)unwired.FindComponent("lcd1")!).Display.Contrast);
    }

    // ---- I2C backpack (LiquidCrystal_I2C lcd(0x27, 16, 2)) --------------------------------------

    private static readonly string[] I2cWiring = ["board.5V lcd1.VCC", "board.GND lcd1.GND", "board.A4 lcd1.SDA", "board.A5 lcd1.SCL"];

    private const int Backlight = 0x08;
    private const int Enable = 0x04;
    private const int Rs = 0x01;

    private static void Expander(Mcu mcu, int value, int address = 0x27)
    {
        mcu.I2cStart(address, read: false);
        mcu.I2cWrite((byte)(value | Backlight));
        mcu.I2cStop();
    }

    private static void Write4I2c(Mcu mcu, int value)
    {
        Expander(mcu, value);
        Expander(mcu, value | Enable);
        mcu.DelayMicros(1);
        Expander(mcu, value & ~Enable);
        mcu.DelayMicros(50);
    }

    private static void SendI2c(Mcu mcu, int value, int mode)
    {
        Write4I2c(mcu, (value & 0xF0) | mode);
        Write4I2c(mcu, ((value << 4) & 0xF0) | mode);
    }

    private static void BeginI2c(Mcu mcu)
    {
        mcu.I2cBegin();
        mcu.DelayMicros(50_000);
        Expander(mcu, 0);
        mcu.DelayMicros(1000);
        Write4I2c(mcu, 0x03 << 4);
        mcu.DelayMicros(4500);
        Write4I2c(mcu, 0x03 << 4);
        mcu.DelayMicros(4500);
        Write4I2c(mcu, 0x03 << 4);
        mcu.DelayMicros(150);
        Write4I2c(mcu, 0x02 << 4);
        SendI2c(mcu, 0x28, 0);
        SendI2c(mcu, 0x0C, 0);
        SendI2c(mcu, 0x01, 0);
        mcu.DelayMicros(2000);
        SendI2c(mcu, 0x06, 0);
    }

    [Fact]
    public void I2c_1602_shows_text_with_the_backlight_on()
    {
        BoardSimulation sim = Build(Scenes.Part("lcd1", Lcd.TypeName), I2cWiring);
        var lcd = (Lcd)sim.FindComponent("lcd1")!;

        BeginI2c(sim.Mcu);
        foreach (char c in "hello, world!")
        {
            SendI2c(sim.Mcu, c, Rs);
        }

        Assert.Equal("hello, world!   ", lcd.Text[0]);
        Assert.True(lcd.Display.Status.HasFlag(DisplayStatus.Backlight));
        Assert.Empty(Warnings("i2c-nack"));
        Assert.Empty(Warnings("i2c-pullups")); // the backpack has its own pull-ups
    }

    [Fact]
    public void A_wrong_address_is_nacked_and_named_once()
    {
        BoardSimulation sim = Build(Scenes.Part("lcd1", Lcd.TypeName), I2cWiring);
        sim.Mcu.I2cBegin();

        Assert.False(sim.Mcu.I2cStart(0x3F, read: false));
        sim.Mcu.I2cStop();
        Assert.False(sim.Mcu.I2cStart(0x3F, read: false));
        sim.Mcu.I2cStop();

        SimDiagnostic warning = Assert.Single(Warnings("i2c-nack"));
        Assert.Contains("0x3F", warning.Message, StringComparison.Ordinal);
        Assert.Contains("lcd1 (0x27)", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Swapped_sda_and_scl_are_pointed_out()
    {
        BoardSimulation sim = Build(Scenes.Part("lcd1", Lcd.TypeName), "board.5V lcd1.VCC", "board.GND lcd1.GND", "board.A5 lcd1.SDA", "board.A4 lcd1.SCL");
        sim.Mcu.I2cBegin();

        Assert.False(sim.Mcu.I2cStart(0x27, read: false));

        SimDiagnostic warning = Assert.Single(Warnings("i2c-nack"));
        Assert.Equal("i2c-nack.swapped", warning.Key);
        Assert.Equal("lcd1", warning.ComponentId);
    }

    [Fact]
    public void Mjkdz_backpack_uses_its_own_bit_map()
    {
        BoardSimulation sim = Build(Scenes.Part("lcd1", Lcd.TypeName, ("backpack", "mjkdz"), ("address", "0x20")), I2cWiring);
        var lcd = (Lcd)sim.FindComponent("lcd1")!;
        LcdBackpackMap map = LcdBackpackMap.Mjkdz;
        sim.Mcu.I2cBegin();
        void Nibble(int nibble, bool rs)
        {
            int pins = (nibble & 0x0F) | (rs ? 1 << map.Rs : 0); // D4..D7 on P0..P3; backlight bit 7 low = on
            foreach (int value in new[] { pins | (1 << map.E), pins })
            {
                sim.Mcu.I2cStart(0x20, read: false);
                sim.Mcu.I2cWrite((byte)value);
                sim.Mcu.I2cStop();
            }
        }

        foreach (int n in new[] { 0x3, 0x3, 0x3, 0x2 })
        {
            Nibble(n, false);
            sim.Mcu.DelayMicros(5000);
        }

        foreach (int command in new[] { 0x28, 0x0C, 0x01 })
        {
            Nibble(command >> 4, false);
            Nibble(command, false);
            sim.Mcu.DelayMicros(2000);
        }

        Nibble('O' >> 4, true);
        Nibble('O', true);
        Nibble('K' >> 4, true);
        Nibble('K', true);

        Assert.Equal("OK              ", lcd.Text[0]);
        Assert.True(lcd.Display.Status.HasFlag(DisplayStatus.Backlight));
    }

    [Fact]
    public void Reading_the_busy_flag_through_the_backpack()
    {
        BoardSimulation sim = Build(Scenes.Part("lcd1", Lcd.TypeName), I2cWiring);
        BeginI2c(sim.Mcu);
        SendI2c(sim.Mcu, 0x01, 0); // clear: busy for 1.52 ms

        // RW high, D4-D7 written 1 (quasi-bidirectional inputs), E high: the controller drives the high nibble.
        Expander(sim.Mcu, 0xF0 | 0x02 | Enable);
        sim.Mcu.I2cStart(0x27, read: true);
        byte status = sim.Mcu.I2cRead(ack: false);
        sim.Mcu.I2cStop();

        Assert.Equal(0x80, status & 0x80); // DB7 = busy flag on P7
    }

    // ---- the bus itself -----------------------------------------------------------------------

    [Fact]
    public void Bus_timing_is_nine_bits_per_byte_plus_start_and_stop()
    {
        BoardSimulation sim = Build(Scenes.Part("lcd1", Lcd.TypeName), I2cWiring);
        sim.Mcu.I2cBegin();
        ulong start = _engine.Clock.Now;

        sim.Mcu.I2cStart(0x27, read: false);
        sim.Mcu.I2cWrite(0x08);
        sim.Mcu.I2cWrite(0x0C);
        sim.Mcu.I2cStop();
        Assert.Equal((10 + 9 + 9 + 1) * 10 * VirtualTime.Microsecond, _engine.Clock.Now - start);

        sim.Mcu.I2cSetClock(400_000);
        start = _engine.Clock.Now;
        sim.Mcu.I2cStart(0x27, read: false);
        sim.Mcu.I2cStop();
        Assert.Equal(11 * 2_500UL, _engine.Clock.Now - start);
    }

    [Fact]
    public void Repeated_start_to_another_device_ends_the_first_transfer()
    {
        var circuit = new Circuit(_engine.Log);
        var sda = new PinRef("board", "SDA");
        var scl = new PinRef("board", "SCL");
        var a = new FakeDevice("a", 0x10, sda, scl);
        var b = new FakeDevice("b", 0x11, sda, scl);
        foreach (PinRef pin in new[] { sda, scl })
        {
            circuit.AddDriver(pin, "board").Set(NetDrive.Weak(5, 4700));
        }

        circuit.Connect(sda, a.Sda);
        circuit.Build();
        var bus = new I2cBus(circuit, _engine.Log, sda, scl);
        bus.Register(a);
        bus.Register(b);

        Assert.True(bus.Start(0x10, read: false));
        Assert.True(bus.Write(0x42));
        Assert.True(bus.Start(0x11, read: true));
        Assert.Equal(0x99, bus.Read(ack: false));
        bus.Stop();

        Assert.Equal(["start w", "write 42", "stop"], a.Events);
        Assert.Equal(["start r", "read", "stop"], b.Events);
        Assert.Empty(Warnings("i2c-nack"));
    }

    private sealed class FakeDevice(string id, int address, PinRef sda, PinRef scl) : II2cDevice
    {
        public List<string> Events { get; } = [];

        public string Id { get; } = id;

        public int Address { get; } = address;

        public PinRef Sda { get; } = sda;

        public PinRef Scl { get; } = scl;

        public bool OnStart(bool read)
        {
            Events.Add(read ? "start r" : "start w");
            return true;
        }

        public bool OnWrite(byte value)
        {
            Events.Add($"write {value:X2}");
            return true;
        }

        public byte OnRead(bool ack)
        {
            Events.Add("read");
            return 0x99;
        }

        public void OnStop() => Events.Add("stop");
    }
}
