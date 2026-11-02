using ArduSim.Core.Components.Parts;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Displays;
using ArduSim.Core.Projects;
using ArduSim.Core.Simulation;
using ArduSim.Core.Timing;

namespace ArduSim.Core.Tests;

/// <summary>SSD1306/SH1106 fed recorded command streams (as Adafruit_SSD1306 and U8g2 send them), and the OLED part on its buses.</summary>
public sealed class OledTests : IDisposable
{
    // Adafruit_SSD1306::begin(SSD1306_SWITCHCAPVCC) for 128x64.
    private static readonly byte[] AdafruitInit64 =
        [0xAE, 0xD5, 0x80, 0xA8, 0x3F, 0xD3, 0x00, 0x40, 0x8D, 0x14, 0x20, 0x00, 0xA1, 0xC8, 0xDA, 0x12, 0x81, 0xCF, 0xD9, 0xF1, 0xDB, 0x40, 0xA4, 0xA6, 0x2E, 0xAF];

    // The same for 128x32: multiplex 32, sequential COM pins.
    private static readonly byte[] AdafruitInit32 =
        [0xAE, 0xD5, 0x80, 0xA8, 0x1F, 0xD3, 0x00, 0x40, 0x8D, 0x14, 0x20, 0x00, 0xA1, 0xC8, 0xDA, 0x02, 0x81, 0x8F, 0xD9, 0xF1, 0xDB, 0x40, 0xA4, 0xA6, 0x2E, 0xAF];

    private readonly SimulationEngine _engine = new();
    private readonly EepromMemory _eeprom = EepromMemory.InMemory(1024);

    public void Dispose() => _eeprom.Dispose();

    private static void Commands(OledController oled, params byte[] bytes)
    {
        foreach (byte b in bytes)
        {
            oled.Command(b);
        }
    }

    private static bool Lit(byte[] pixels, OledPanel panel, int x, int y) => (pixels[(y * ((panel.Width + 7) / 8)) + (x / 8)] & (0x80 >> (x % 8))) != 0;

    private static int Count(byte[] pixels) => pixels.Sum(b => System.Numerics.BitOperations.PopCount(b));

    /// <summary>A 128x64 controller after the Adafruit init with RAM pixel (column, row) set.</summary>
    private static OledController WithPixel(byte[] init, int column, int row)
    {
        var oled = new OledController(OledChip.Ssd1306);
        Commands(oled, init);
        Commands(oled, 0x21, 0, 127, 0x22, 0, 7);
        for (int i = 0; i < 1024; i++)
        {
            oled.Data(i == ((row / 8) * 128) + column ? (byte)(1 << (row % 8)) : (byte)0);
        }

        return oled;
    }

    [Fact]
    public void Adafruit_init_shows_ram_upright_on_a_128x64_module()
    {
        OledPanel panel = OledPanel.For(OledChip.Ssd1306, "128x64");
        byte[] corner = WithPixel(AdafruitInit64, 0, 0).Render(panel);
        byte[] far = WithPixel(AdafruitInit64, 127, 63).Render(panel);
        byte[] middle = WithPixel(AdafruitInit64, 70, 20).Render(panel);

        Assert.True(Lit(corner, panel, 0, 0));
        Assert.True(Lit(far, panel, 127, 63));
        Assert.True(Lit(middle, panel, 70, 20));
        Assert.Equal(1, Count(middle));
    }

    [Fact]
    public void Without_remaps_the_picture_is_rotated_as_on_the_real_module()
    {
        OledController oled = WithPixel(AdafruitInit64, 10, 5);
        Commands(oled, 0xA0, 0xC0);

        Assert.True(Lit(oled.Render(OledPanel.For(OledChip.Ssd1306, "128x64")), OledPanel.For(OledChip.Ssd1306, "128x64"), 127 - 10, 63 - 5));
    }

    [Fact]
    public void A_128x32_module_needs_sequential_com_pins()
    {
        OledPanel panel = OledPanel.For(OledChip.Ssd1306, "128x32");
        OledController right = WithPixel(AdafruitInit32, 5, 9);
        Assert.True(Lit(right.Render(panel), panel, 5, 9));

        Commands(right, 0xDA, 0x12); // the 128x64 setting on a 128x32 glass interleaves the lines
        byte[] wrong = right.Render(panel);
        Assert.False(Lit(wrong, panel, 5, 9));
    }

    [Fact]
    public void Page_addressing_as_u8g2_sends_it()
    {
        var oled = new OledController(OledChip.Ssd1306);
        Commands(oled, AdafruitInit64);
        Commands(oled, 0x20, 0x02, 0xB3, 0x10, 0x04); // page mode, page 3, column 4
        oled.Data(0xFF);

        OledPanel panel = OledPanel.For(OledChip.Ssd1306, "128x64");
        byte[] pixels = oled.Render(panel);
        Assert.Equal(8, Count(pixels));
        Assert.All(Enumerable.Range(24, 8), y => Assert.True(Lit(pixels, panel, 4, y)));
        Assert.Equal((3, 5), (oled.Page, oled.Column));
    }

    [Fact]
    public void Inverse_entire_on_off_and_charge_pump()
    {
        OledPanel panel = OledPanel.For(OledChip.Ssd1306, "128x64");
        OledController oled = WithPixel(AdafruitInit64, 0, 0);

        Commands(oled, 0xA7);
        Assert.Equal((128 * 64) - 1, Count(oled.Render(panel)));
        Commands(oled, 0xA6, 0xA5);
        Assert.Equal(128 * 64, Count(oled.Render(panel)));
        Commands(oled, 0xA4, 0xAE);
        Assert.Equal(0, Count(oled.Render(panel)));
        Commands(oled, 0xAF, 0x8D, 0x10); // charge pump off: no panel voltage
        Assert.Equal(0, Count(oled.Render(panel)));
    }

    [Fact]
    public void Multiplex_below_the_glass_height_leaves_half_the_glass_dark()
    {
        OledPanel panel = OledPanel.For(OledChip.Ssd1306, "128x64");
        OledController oled = WithPixel(AdafruitInit64, 3, 0);
        Commands(oled, 0xA7, 0xA8, 0x1F); // inverse, multiplex 32

        // With the reversed COM scan only COM0-31 run, and the module has them on its lower half.
        byte[] pixels = oled.Render(panel);
        Assert.False(Lit(pixels, panel, 0, 1));
        Assert.True(Lit(pixels, panel, 0, 40));
        Assert.False(Lit(pixels, panel, 3, 32)); // RAM row 0 is the first lit line
        Assert.Equal((128 * 32) - 1, Count(pixels));
    }

    [Fact]
    public void Horizontal_scroll_moves_the_scrolled_pages_one_column_per_step()
    {
        OledPanel panel = OledPanel.For(OledChip.Ssd1306, "128x64");
        OledController oled = WithPixel(AdafruitInit64, 127, 2);
        Commands(oled, 0x26, 0x00, 0x00, 0x00, 0x07, 0x00, 0xFF, 0x2F); // right, pages 0-7, every 5 frames

        Assert.True(oled.ScrollActive);
        oled.ScrollStep();
        Assert.True(Lit(oled.Render(panel), panel, 0, 2)); // column 127 wrapped around to 0
        Commands(oled, 0x2E);
        Assert.False(oled.ScrollActive);
    }

    [Fact]
    public void Diagonal_scroll_also_moves_the_rows_inside_the_vertical_area()
    {
        OledPanel panel = OledPanel.For(OledChip.Ssd1306, "128x64");
        OledController oled = WithPixel(AdafruitInit64, 10, 5);
        Commands(oled, 0xA3, 0x00, 0x40, 0x29, 0x00, 0x00, 0x00, 0x07, 0x01, 0x2F); // Adafruit startscrolldiagright

        oled.ScrollStep();

        byte[] pixels = oled.Render(panel);
        Assert.Equal(1, Count(pixels));
        Assert.True(Lit(pixels, panel, 11, 4)); // one column right, one row up
    }

    [Fact]
    public void Frame_rate_and_scroll_interval_follow_the_timing_registers()
    {
        var oled = new OledController(OledChip.Ssd1306);
        Assert.InRange(oled.FrameRate, 105, 109); // reset values: about 107 Hz at multiplex 64

        Commands(oled, AdafruitInit64); // D9 F1: K = 1 + 15 + 50
        Assert.InRange(oled.FrameRate, 86, 89);

        Commands(oled, 0x26, 0x00, 0x00, 0x07, 0x07, 0x00, 0xFF); // interval code 7 = 2 frames
        Assert.InRange(oled.ScrollStepInterval, 2 * VirtualTime.Second / 89, 2 * VirtualTime.Second / 86);
    }

    [Fact]
    public void Sh1106_shows_ram_columns_2_to_129()
    {
        // U8g2 u8x8_d_sh1106_128x64_noname init (the parts that matter), then page 0 at column 2.
        var oled = new OledController(OledChip.Sh1106);
        Commands(oled, 0xAE, 0xD5, 0x80, 0xA8, 0x3F, 0xD3, 0x00, 0x40, 0xAD, 0x8B, 0xA1, 0xC8, 0xDA, 0x12, 0x81, 0xCF, 0xAF);
        Commands(oled, 0xB0, 0x02, 0x10);
        oled.Data(0x01);

        OledPanel panel = OledPanel.For(OledChip.Sh1106, "128x64");
        byte[] pixels = oled.Render(panel);
        Assert.Equal(1, Count(pixels));
        Assert.True(Lit(pixels, panel, 0, 0));
    }

    [Fact]
    public void Sh1106_ignores_ssd1306_only_commands()
    {
        var oled = new OledController(OledChip.Sh1106);
        var unknown = new List<byte>();
        oled.UnknownCommand += unknown.Add;

        Commands(oled, 0x8D, 0x14, 0x20, 0xF5);

        Assert.Equal([0xF5], unknown);  // SSD1306 commands pass silently, as on the chip
        Assert.Equal(4, oled.Column >> 4); // 0x14 is "higher column 4", a command of its own
        Assert.True(oled.PanelSupply);  // the DC-DC converter is on after power-on
    }

    // ---- the part on its buses ----------------------------------------------------------------

    private static readonly string[] SpiWiring =
        ["board.5V oled1.VCC", "board.GND oled1.GND", "board.D13 oled1.D0", "board.D11 oled1.D1", "board.D9 oled1.DC", "board.D10 oled1.CS", "board.D8 oled1.RES"];

    private static readonly string[] I2cWiring = ["board.5V oled1.VCC", "board.GND oled1.GND", "board.A4 oled1.SDA", "board.A5 oled1.SCL"];

    private BoardSimulation Build(ComponentDocument oled, params string[] wires) => Scenes.Create(Scenes.Scene([oled], wires), _engine, _eeprom);

    private static void Fill(Action<bool, byte> send)
    {
        foreach (byte b in AdafruitInit64)
        {
            send(false, b);
        }

        foreach (byte b in new byte[] { 0x21, 0, 127, 0x22, 0, 7 })
        {
            send(false, b);
        }

        for (int i = 0; i < 1024; i++)
        {
            send(true, (byte)(i < 128 ? 0xFF : 0x00)); // page 0 lit
        }
    }

    [Fact]
    public void Hardware_spi_reaches_the_selected_oled()
    {
        BoardSimulation sim = Build(Scenes.Part("oled1", Oled.TypeName, ("interface", "spi")), SpiWiring);
        Mcu mcu = sim.Mcu;
        var oled = (Oled)sim.FindComponent("oled1")!;
        foreach (int pin in new[] { 8, 9, 10 })
        {
            mcu.PinMode(pin, 1);
        }

        mcu.DigitalWrite(10, 1);
        mcu.DigitalWrite(8, 1);
        mcu.DigitalWrite(8, 0); // RES pulse
        mcu.DigitalWrite(8, 1);
        mcu.SpiBegin();
        mcu.SpiSettings(8_000_000, 1, 0);
        Assert.Equal(0xFF, mcu.SpiTransfer(0xAE)); // CS high: nobody listens
        mcu.DigitalWrite(10, 0);
        Fill((data, b) =>
        {
            mcu.DigitalWrite(9, data ? 1 : 0);
            mcu.SpiTransfer(b);
        });
        mcu.DigitalWrite(10, 1);

        Assert.Equal(128 * 8, oled.LitPixels);
        Assert.Equal(DisplayStatus.Powered | DisplayStatus.On, oled.Display.Status);
    }

    [Fact]
    public void Software_spi_on_any_pins_is_sampled_on_the_rising_edge_of_d0()
    {
        string[] wiring = ["board.5V oled1.VCC", "board.GND oled1.GND", "board.D4 oled1.D0", "board.D5 oled1.D1", "board.D6 oled1.DC", "board.D7 oled1.CS"];
        BoardSimulation sim = Build(Scenes.Part("oled1", Oled.TypeName, ("interface", "spi")), wiring);
        Mcu mcu = sim.Mcu;
        var oled = (Oled)sim.FindComponent("oled1")!;
        foreach (int pin in new[] { 4, 5, 6, 7 })
        {
            mcu.PinMode(pin, 1);
        }

        mcu.DigitalWrite(7, 0);
        Fill((data, b) =>
        {
            mcu.DigitalWrite(6, data ? 1 : 0);
            for (int bit = 7; bit >= 0; bit--) // shiftOut(D5, D4, MSBFIRST, b)
            {
                mcu.DigitalWrite(5, (b >> bit) & 1);
                mcu.DigitalWrite(4, 1);
                mcu.DigitalWrite(4, 0);
            }
        });

        Assert.Equal(128 * 8, oled.LitPixels);
    }

    [Fact]
    public void I2c_control_bytes_select_commands_and_data()
    {
        BoardSimulation sim = Build(Scenes.Part("oled1", Oled.TypeName), I2cWiring);
        Mcu mcu = sim.Mcu;
        var oled = (Oled)sim.FindComponent("oled1")!;
        mcu.I2cBegin();

        // Adafruit sends commands as one transaction with control byte 0x00, data with 0x40.
        Assert.True(mcu.I2cStart(0x3C, read: false));
        mcu.I2cWrite(0x00);
        foreach (byte b in AdafruitInit64.Concat(new byte[] { 0x21, 0, 127, 0x22, 0, 7 }))
        {
            mcu.I2cWrite(b);
        }

        mcu.I2cStop();
        mcu.I2cStart(0x3C, read: false);
        mcu.I2cWrite(0x40);
        for (int i = 0; i < 16; i++)
        {
            mcu.I2cWrite(0x81);
        }

        mcu.I2cStop();

        // Co = 1: a single command byte, then another control byte (0xC0 = one data byte).
        mcu.I2cStart(0x3C, read: false);
        mcu.I2cWrite(0x80);
        mcu.I2cWrite(0xA7);
        mcu.I2cWrite(0x00);
        mcu.I2cWrite(0xA6);
        mcu.I2cStop();

        Assert.Equal(32, oled.LitPixels);
        Assert.False(oled.Controller.Inverse);
        Assert.False(mcu.I2cStart(0x3D, read: false)); // SA0 low: 0x3D is nobody
        mcu.I2cStop();
    }

    [Fact]
    public void Switching_on_without_the_charge_pump_warns_and_stays_dark()
    {
        BoardSimulation sim = Build(Scenes.Part("oled1", Oled.TypeName), I2cWiring);
        Mcu mcu = sim.Mcu;
        mcu.I2cBegin();
        mcu.I2cStart(0x3C, read: false);
        mcu.I2cWrite(0x00);
        mcu.I2cWrite(0x8D);
        mcu.I2cWrite(0x10); // external VCC
        mcu.I2cWrite(0xA5);
        mcu.I2cWrite(0xAF);
        mcu.I2cStop();

        Assert.Equal(0, ((Oled)sim.FindComponent("oled1")!).LitPixels);
        Assert.Single(_engine.Log.Entries, e => e.Code == "oled-supply");
    }

    [Fact]
    public void Scrolling_runs_on_the_frame_clock_in_virtual_time()
    {
        BoardSimulation sim = Build(Scenes.Part("oled1", Oled.TypeName), I2cWiring);
        Mcu mcu = sim.Mcu;
        var oled = (Oled)sim.FindComponent("oled1")!;
        mcu.I2cBegin();
        mcu.I2cStart(0x3C, read: false);
        mcu.I2cWrite(0x00);
        foreach (byte b in AdafruitInit64.Concat(new byte[] { 0xB0, 0x00, 0x10 }))
        {
            mcu.I2cWrite(b);
        }

        mcu.I2cStop();
        mcu.I2cStart(0x3C, read: false);
        mcu.I2cWrite(0x40);
        mcu.I2cWrite(0x01); // pixel (0, 0)
        mcu.I2cStop();
        mcu.I2cStart(0x3C, read: false);
        mcu.I2cWrite(0x00);
        foreach (byte b in new byte[] { 0x26, 0x00, 0x00, 0x07, 0x00, 0x00, 0xFF, 0x2F }) // right, page 0, every 2 frames
        {
            mcu.I2cWrite(b);
        }

        mcu.I2cStop();
        ulong step = oled.Controller.ScrollStepInterval;
        mcu.DelayMicros((uint)(((10 * step) + (step / 2)) / VirtualTime.Microsecond));

        byte[] pixels = oled.Display.Dots.ToArray();
        Assert.Equal(1, Count(pixels));
        Assert.True(Lit(pixels, oled.Panel, 10, 0));
    }
}
