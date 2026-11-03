using ArduSim.Core.Components.Parts;
using ArduSim.Core.Displays;
using ArduSim.Core.Projects;
using ArduSim.Core.Simulation;

namespace ArduSim.Core.Tests;

/// <summary>The SSD1309 and the grayscale SSD1327 (ADR-013), fed the command streams their libraries send.</summary>
public sealed class GrayOledTests : IDisposable
{
    // Adafruit_SSD1327::begin() for 128x128.
    private static readonly byte[] AdafruitSsd1327Init =
        [0xAE, 0x81, 0x80, 0xA0, 0x51, 0xA1, 0x00, 0xA2, 0x00, 0xA6, 0xA8, 0x7F, 0xB1, 0x11, 0xB3, 0x00, 0xAB, 0x01, 0xB6, 0x04, 0xBE, 0x0F, 0xBC, 0x08, 0xD5, 0x62, 0xFD, 0x12, 0xA4, 0xAF];

    // Adafruit_SSD1306::begin() for 128x64, as people drive 2.42" SSD1309 modules.
    private static readonly byte[] AdafruitSsd1306Init =
        [0xAE, 0xD5, 0x80, 0xA8, 0x3F, 0xD3, 0x00, 0x40, 0x8D, 0x14, 0x20, 0x00, 0xA1, 0xC8, 0xDA, 0x12, 0x81, 0xCF, 0xD9, 0xF1, 0xDB, 0x40, 0xA4, 0xA6, 0x2E, 0xAF];

    private readonly SimulationEngine _engine = new();
    private readonly EepromMemory _eeprom = EepromMemory.InMemory(1024);

    public void Dispose() => _eeprom.Dispose();

    private static void Commands(IOledController oled, params byte[] bytes)
    {
        foreach (byte b in bytes)
        {
            oled.Command(b);
        }
    }

    // ---- SSD1327 ----------------------------------------------------------------------------------

    /// <summary>After the Adafruit init, a full-screen write of a buffer laid out as Adafruit_GrayOLED does (even x in the high nibble).</summary>
    private static Ssd1327Controller WithPixels(params (int X, int Y, int Level)[] pixels)
    {
        var oled = new Ssd1327Controller();
        Commands(oled, AdafruitSsd1327Init);
        Commands(oled, 0x15, 0x00, 0x3F, 0x75, 0x00, 0x7F);
        var buffer = new byte[64 * 128];
        foreach ((int x, int y, int level) in pixels)
        {
            int at = (y * 64) + (x / 2);
            buffer[at] = (byte)(x % 2 == 0 ? (buffer[at] & 0x0F) | (level << 4) : (buffer[at] & 0xF0) | level);
        }

        foreach (byte b in buffer)
        {
            oled.Data(b);
        }

        return oled;
    }

    [Fact]
    public void Ssd1327_shows_an_adafruit_buffer_upright_with_its_gray_levels()
    {
        OledPanel panel = OledPanel.For(OledChip.Ssd1327, "");
        byte[] glass = WithPixels((0, 0, 15), (1, 0, 8), (127, 127, 15), (40, 70, 1)).Render(panel);

        Assert.Equal(255, glass[0]);
        Assert.Equal(255, glass[(127 * 128) + 127]);
        Assert.InRange(glass[1], 120, 140);                    // GS8: 14 of 28 DCLKs
        Assert.InRange(glass[(70 * 128) + 40], 1, 15);         // GS1: no current pulse, only the pre-charge
        Assert.Equal(4, glass.Count(level => level > 0));
    }

    [Fact]
    public void Ssd1327_display_modes_and_the_gray_table_change_the_light()
    {
        OledPanel panel = OledPanel.For(OledChip.Ssd1327, "");
        Ssd1327Controller oled = WithPixels((5, 5, 8));
        Commands(oled, 0xA7); // inverse
        Assert.Equal(255, oled.Render(panel)[0]);
        Assert.InRange(oled.Render(panel)[(5 * 128) + 5], 100, 140);

        Commands(oled, 0xA4, 0xB8, 1, 2, 3, 4, 5, 6, 7, 63, 63, 63, 63, 63, 63, 63, 63);
        Assert.Equal(255, oled.Render(panel)[(5 * 128) + 5]); // GS8 now as long as GS15

        Commands(oled, 0xA6); // all off
        Assert.All(oled.Render(panel), level => Assert.Equal(0, level));
        Commands(oled, 0xA5); // all on
        Assert.All(oled.Render(panel), level => Assert.Equal(255, level));
    }

    [Fact]
    public void Ssd1327_lock_ignores_everything_but_the_unlock()
    {
        Ssd1327Controller oled = WithPixels();
        Commands(oled, 0xFD, 0x16, 0xAE, 0x81, 0x10);
        oled.Data(0xFF);
        Assert.True(oled.DisplayOn);
        Assert.Equal(0x80, oled.Contrast);

        Commands(oled, 0xFD, 0x12, 0xAE);
        Assert.False(oled.DisplayOn);
    }

    [Fact]
    public void Ssd1327_vertical_increment_fills_a_column_down_the_rows_first()
    {
        var oled = new Ssd1327Controller();
        Commands(oled, 0xA0, 0x04, 0x15, 2, 3, 0x75, 0, 1);
        foreach (byte b in new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55 })
        {
            oled.Data(b);
        }

        Assert.Equal(0x55, oled.Ram[2]);       // wrapped back to row 0, column 2
        Assert.Equal(0x22, oled.Ram[64 + 2]);  // row 1, column 2
        Assert.Equal(0x33, oled.Ram[3]);
        Assert.Equal(0x44, oled.Ram[64 + 3]);
    }

    // ---- SSD1309 ----------------------------------------------------------------------------------

    [Fact]
    public void Ssd1309_runs_the_adafruit_ssd1306_init_without_a_charge_pump()
    {
        var oled = new OledController(OledChip.Ssd1309);
        var unknown = new List<byte>();
        oled.UnknownCommand += unknown.Add;
        Commands(oled, AdafruitSsd1306Init);
        Commands(oled, 0x21, 0, 127, 0x22, 0, 7);
        oled.Data(0x01);

        Assert.Equal([0x8D], unknown); // not an SSD1309 command; its 0x14 then sets the column's high nibble
        Assert.True(oled.DisplayOn);
        Assert.True(oled.PanelSupply);
        byte[] glass = oled.Render(OledPanel.For(OledChip.Ssd1309, ""));
        Assert.Equal(0x80, glass[0]); // RAM column 0, row 0 at the top-left
    }

    [Fact]
    public void Ssd1309_scroll_setup_takes_seven_bytes_with_a_column_range()
    {
        var oled = new OledController(OledChip.Ssd1309);
        Commands(oled, AdafruitSsd1306Init);
        Commands(oled, 0x21, 0, 127, 0x22, 0, 0);
        for (int column = 0; column < 128; column++)
        {
            oled.Data(column is 10 or 100 ? (byte)0x01 : (byte)0x00);
        }

        Commands(oled, 0x26, 0x00, 0x00, 0x07, 0x00, 0x00, 0x00, 0x3F, 0x2F); // columns 0-63 only
        Assert.True(oled.ScrollActive);
        oled.ScrollStep();
        Assert.Equal(0x01, oled.Ram[11]);
        Assert.Equal(0x01, oled.Ram[100]); // outside the range: stays
        Assert.Equal(0x00, oled.Ram[10]);

        Commands(oled, 0x2E, 0x2D, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x7F); // content scroll left, one column
        Assert.Equal(0x01, oled.Ram[10]);
        Assert.Equal(0x01, oled.Ram[99]);
    }

    [Fact]
    public void Ssd1309_lock_holds_commands_and_ram_until_unlocked()
    {
        var oled = new OledController(OledChip.Ssd1309);
        Commands(oled, 0xAF, 0xFD, 0x16, 0xAE);
        oled.Data(0xFF);
        Assert.True(oled.Locked);
        Assert.True(oled.DisplayOn);
        Assert.Equal(0, oled.Ram[0]);

        Commands(oled, 0xFD, 0x12, 0xAE);
        Assert.False(oled.DisplayOn);
    }

    [Fact]
    public void A_ssd1327_module_on_i2c_sends_a_gray_frame()
    {
        BoardSimulation sim = Scenes.Create(
            Scenes.Scene([Scenes.Part("oled1", Oled.TypeName, ("controller", "ssd1327"), ("address", "0x3D"))], "board.5V oled1.VCC", "board.GND oled1.GND", "board.A4 oled1.SDA", "board.A5 oled1.SCL"),
            _engine,
            _eeprom);
        Mcu mcu = sim.Mcu;
        var oled = (Oled)sim.FindComponent("oled1")!;
        mcu.I2cBegin();
        Assert.True(mcu.I2cStart(0x3D, read: false));
        mcu.I2cWrite(0x00); // control byte: a command stream
        foreach (byte b in AdafruitSsd1327Init)
        {
            mcu.I2cWrite(b);
        }

        mcu.I2cStop();
        mcu.I2cStart(0x3D, read: false);
        mcu.I2cWrite(0x40); // data
        mcu.I2cWrite(0xF0);
        mcu.I2cStop();

        Assert.Equal(DisplayKind.GrayOled, oled.Display.Kind);
        Assert.Equal(128 * 128, oled.Display.Dots.Length);
        Assert.Equal(1, oled.LitPixels);
    }
}
