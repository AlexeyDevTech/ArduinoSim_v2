using ArduSim.Core.Components.Parts;
using ArduSim.Core.Displays;
using ArduSim.Core.Projects;
using ArduSim.Core.Simulation;

namespace ArduSim.Core.Tests;

/// <summary>The M10 displays: TFT controllers and modules, the PCD8544 and the HD44780 A02 ROM (ADR-013).</summary>
public sealed class TftTests : IDisposable
{
    private readonly SimulationEngine _engine = new();
    private readonly EepromMemory _eeprom = EepromMemory.InMemory(1024);

    public void Dispose() => _eeprom.Dispose();

    // ---- the controller ---------------------------------------------------------------------------

    [Theory]
    [InlineData(0x48, 0, 0)]     // Adafruit_ILI9341 rotation 0: the picture's top-left at the glass's top-left
    [InlineData(0x28, 239, 0)]   // rotation 1: turned a quarter clockwise
    [InlineData(0x88, 239, 319)] // rotation 2: upside down
    [InlineData(0xE8, 0, 319)]   // rotation 3
    public void Ili9341_rotations_put_the_first_pixel_in_the_expected_corner(int madctl, int x, int y)
    {
        TftPanel panel = TftPanel.For("ili9341-240x320");
        TftController tft = Awake(panel, madctl);
        Window(tft, 0, 0, 0, 0);
        tft.Command(0x2C);
        Rgb565(tft, 0xF800); // red

        byte[] glass = Glass(tft, panel);
        Assert.Equal((255, 0, 0), Pixel(glass, panel, x, y));
        Assert.Equal(1, glass.Chunk(3).Count(p => p[0] != 0 || p[1] != 0 || p[2] != 0));
    }

    [Theory]
    [InlineData("st7735-128x160", 0xC0, 0, 0)]       // black tab: RGB
    [InlineData("st7735-128x160-green", 0xC8, 2, 1)] // green tab: BGR, GRAM 132x162
    [InlineData("st7735-128x128", 0xC8, 2, 3)]
    [InlineData("st7735-80x160", 0xC0, 24, 0)]
    [InlineData("st7789-240x240", 0xC0, 0, 80)]
    [InlineData("st7789-135x240", 0xC0, 53, 40)]
    public void A_module_fills_its_glass_through_the_window_its_library_uses(string module, int madctl, int columnStart, int rowStart)
    {
        TftPanel panel = TftPanel.For(module);
        TftController tft = Awake(panel, madctl);
        if (panel.NormallyBlack)
        {
            tft.Command(0x21); // INVON, as Adafruit_ST7789 sends for IPS panels
        }

        Window(tft, columnStart, rowStart, columnStart + panel.Width - 1, rowStart + panel.Height - 1);
        tft.Command(0x2C);
        for (int i = 0; i < panel.Width * panel.Height; i++)
        {
            Rgb565(tft, (i % panel.Width) == 0 ? (ushort)0x001F : (ushort)0xF800); // blue in the first column, red elsewhere
        }

        byte[] glass = Glass(tft, panel);
        Assert.Equal((255, 0, 0), Pixel(glass, panel, panel.Width - 1, panel.Height - 1));
        Assert.Equal((0, 0, 255), Pixel(glass, panel, 0, panel.Height / 2));
        Assert.Equal(panel.Width * panel.Height, glass.Chunk(3).Count(p => p[0] == 255 || p[2] == 255));
    }

    [Fact]
    public void An_ips_panel_without_inversion_on_shows_inverted_colors()
    {
        TftPanel panel = TftPanel.For("st7789-240x320");
        TftController tft = Awake(panel, 0xC0);
        Window(tft, 0, 0, 0, 0);
        tft.Command(0x2C);
        Rgb565(tft, 0x0000);
        Assert.Equal((255, 255, 255), Pixel(Glass(tft, panel), panel, 239, 319)); // black written, white shown

        tft.Command(0x21);
        Assert.Equal((0, 0, 0), Pixel(Glass(tft, panel), panel, 239, 319));
    }

    [Fact]
    public void Wrong_color_order_swaps_red_and_blue()
    {
        TftPanel panel = TftPanel.For("ili9341-240x320");
        TftController tft = Awake(panel, 0x40); // MX without BGR
        Window(tft, 0, 0, 0, 0);
        tft.Command(0x2C);
        Rgb565(tft, 0xF800);
        Assert.Equal((0, 0, 255), Pixel(Glass(tft, panel), panel, 0, 0));
    }

    [Fact]
    public void Pixel_formats_of_12_16_and_18_bits_decode_to_the_same_colors()
    {
        TftPanel panel = TftPanel.For("ili9341-240x320");
        TftController tft = Awake(panel, 0x48);
        Window(tft, 0, 0, 3, 0);
        tft.Command(0x3A);
        tft.Data(0x66); // 18-bit: R, G, B in the top six bits
        tft.Command(0x2C);
        foreach (byte b in new byte[] { 0xFC, 0x00, 0x00, 0x00, 0xFC, 0x00 })
        {
            tft.Data(b);
        }

        tft.Command(0x3A);
        tft.Data(0x03); // 12-bit: two pixels in three bytes
        tft.Command(0x3C); // RAMWRC: carry on at column 2
        foreach (byte b in new byte[] { 0x00, 0xFF, 0xFF })
        {
            tft.Data(b);
        }

        byte[] glass = Glass(tft, panel);
        Assert.Equal((255, 0, 0), Pixel(glass, panel, 0, 0));
        Assert.Equal((0, 255, 0), Pixel(glass, panel, 1, 0));
        Assert.Equal((0, 0, 255), Pixel(glass, panel, 2, 0));
        Assert.Equal((255, 255, 255), Pixel(glass, panel, 3, 0));
    }

    [Fact]
    public void Writes_wrap_inside_the_window()
    {
        TftPanel panel = TftPanel.For("ili9341-240x320");
        TftController tft = Awake(panel, 0x48);
        Window(tft, 10, 20, 11, 21);
        tft.Command(0x2C);
        foreach (ushort color in new ushort[] { 0xF800, 0x07E0, 0x001F, 0xFFFF, 0x07E0 })
        {
            Rgb565(tft, color);
        }

        byte[] glass = Glass(tft, panel);
        Assert.Equal((0, 255, 0), Pixel(glass, panel, 10, 20)); // the fifth pixel wrapped over the first
        Assert.Equal((0, 255, 0), Pixel(glass, panel, 11, 20));
        Assert.Equal((0, 0, 255), Pixel(glass, panel, 10, 21));
        Assert.Equal((255, 255, 255), Pixel(glass, panel, 11, 21));
    }

    [Fact]
    public void Vertical_scrolling_shows_memory_from_the_scroll_start()
    {
        TftPanel panel = TftPanel.For("ili9341-240x320");
        TftController tft = Awake(panel, 0x48);
        Window(tft, 0, 10, 0, 10);
        tft.Command(0x2C);
        Rgb565(tft, 0xFFFF);
        tft.Command(0x33); // VSCRDEF: no fixed areas
        foreach (byte b in new byte[] { 0, 0, 0x01, 0x40, 0, 0 })
        {
            tft.Data(b);
        }

        tft.Command(0x37); // VSCSAD 10
        tft.Data(0);
        tft.Data(10);
        Assert.Equal((255, 255, 255), Pixel(Glass(tft, panel), panel, 0, 0));

        tft.Command(0x13); // NORON ends scrolling
        Assert.Equal((255, 255, 255), Pixel(Glass(tft, panel), panel, 0, 10));
    }

    [Fact]
    public void Asleep_or_off_a_tn_panel_is_white_and_an_ips_panel_black()
    {
        TftPanel tn = TftPanel.For("ili9341-240x320");
        TftPanel ips = TftPanel.For("st7789-240x240");
        Assert.Equal((255, 255, 255), Pixel(Glass(new TftController(TftChip.Ili9341, 240, 320), tn), tn, 5, 5));
        Assert.Equal((0, 0, 0), Pixel(Glass(new TftController(TftChip.St7789, 240, 320), ips), ips, 5, 5));
    }

    [Fact]
    public void Status_reads_report_the_power_mode_and_the_ili9341_read_index_picks_a_byte()
    {
        var tft = new TftController(TftChip.Ili9341, 240, 320);
        tft.Command(0x0A);
        Assert.Equal(0x08, tft.Read()); // after reset: sleeping, normal mode, display off
        tft.Command(0x11);
        tft.Command(0x29);
        tft.Command(0x0A);
        Assert.Equal(0x9C, tft.Read());

        tft.Command(0xD9);
        tft.Data(0x12); // index 2
        tft.Command(0xD3);
        Assert.Equal(0x93, tft.Read());
    }

    // ---- the modules ------------------------------------------------------------------------------

    [Fact]
    public void An_ili9341_module_draws_over_hardware_spi_and_answers_on_sdo()
    {
        BoardSimulation sim = Scenes.Create(
            Scenes.Scene([Scenes.Part("tft1", Ili9341.TypeName)], "board.5V tft1.VCC", "board.GND tft1.GND", "board.D10 tft1.CS", "board.D8 tft1.RESET", "board.D9 tft1.DC", "board.D11 tft1.SDI", "board.D13 tft1.SCK", "board.D12 tft1.SDO", "board.D6 tft1.LED"),
            _engine,
            _eeprom);
        Mcu mcu = sim.Mcu;
        var tft = (Ili9341)sim.FindComponent("tft1")!;
        foreach (int pin in new[] { 6, 8, 9, 10 })
        {
            mcu.PinMode(pin, 1);
            mcu.DigitalWrite(pin, 1);
        }

        mcu.SpiBegin();
        mcu.SpiSettings(8_000_000, 1, 0);
        mcu.DigitalWrite(10, 0);
        Send(mcu, false, 0x11);
        Send(mcu, false, 0x29);
        Send(mcu, false, 0x36, 0x48);
        Send(mcu, false, 0x3A, 0x55);
        Send(mcu, false, 0x2A, 0, 0, 0, 9);
        Send(mcu, false, 0x2B, 0, 0, 0, 9);
        Send(mcu, false, 0x2C);
        mcu.DigitalWrite(9, 1);
        for (int i = 0; i < 100; i++)
        {
            mcu.SpiTransfer(0x07);
            mcu.SpiTransfer(0xE0); // green
        }

        Send(mcu, false, 0x0A);
        mcu.DigitalWrite(9, 1);
        Assert.Equal(0x9C, mcu.SpiTransfer(0x00));
        mcu.DigitalWrite(10, 1);

        Assert.Equal(100, tft.LitPixels); // GRAM starts black: only the green square shows
        Assert.Equal(1, tft.Backlight);
        Assert.Equal((byte)255, tft.Display.Dots.Span[1]);
        Assert.Equal((byte)0, tft.Display.Dots.Span[0]);

        mcu.DigitalWrite(6, 0);
        Assert.Equal(0, tft.Backlight);
    }

    [Fact]
    public void A_st7789_module_without_cs_is_always_selected_and_its_backlight_is_pulled_up()
    {
        BoardSimulation sim = Scenes.Create(
            Scenes.Scene([Scenes.Part("tft1", St7789.TypeName)], "board.5V tft1.VCC", "board.GND tft1.GND", "board.D8 tft1.RES", "board.D9 tft1.DC", "board.D11 tft1.SDA", "board.D13 tft1.SCL"),
            _engine,
            _eeprom);
        Mcu mcu = sim.Mcu;
        var tft = (St7789)sim.FindComponent("tft1")!;
        Assert.DoesNotContain(tft.Definition.Pins, p => p.Name == "CS");
        mcu.PinMode(8, 1);
        mcu.DigitalWrite(8, 1);
        mcu.PinMode(9, 1);
        mcu.SpiBegin();
        Send(mcu, false, 0x11);
        Send(mcu, false, 0x29);
        Send(mcu, false, 0x21);

        Assert.True(tft.Controller.DisplayOn);
        Assert.True(tft.Controller.Inverted);
        Assert.Equal(1, tft.Backlight);
        Assert.Equal(0, tft.LitPixels); // GRAM starts black
    }

    [Fact]
    public void Software_spi_reaches_a_st7735_on_any_pins()
    {
        BoardSimulation sim = Scenes.Create(
            Scenes.Scene([Scenes.Part("tft1", St7735.TypeName)], "board.5V tft1.VCC", "board.GND tft1.GND", "board.D4 tft1.SCK", "board.D5 tft1.SDA", "board.D6 tft1.A0", "board.D7 tft1.CS", "board.3V3 tft1.LED"),
            _engine,
            _eeprom);
        Mcu mcu = sim.Mcu;
        var tft = (St7735)sim.FindComponent("tft1")!;
        foreach (int pin in new[] { 4, 5, 6, 7 })
        {
            mcu.PinMode(pin, 1);
        }

        mcu.DigitalWrite(7, 0);
        foreach ((bool data, byte value) in new[] { (false, (byte)0x11), (false, (byte)0x29) })
        {
            mcu.DigitalWrite(6, data ? 1 : 0);
            for (int bit = 7; bit >= 0; bit--)
            {
                mcu.DigitalWrite(5, (value >> bit) & 1);
                mcu.DigitalWrite(4, 1);
                mcu.DigitalWrite(4, 0);
            }
        }

        Assert.False(tft.Controller.Sleeping);
        Assert.True(tft.Controller.DisplayOn);
        Assert.Equal(DisplayKind.ColorTft, tft.Display.Kind);
        Assert.Equal(128, tft.Display.Columns);
        Assert.Equal(160, tft.Display.Rows);
    }

    // ---- PCD8544 ----------------------------------------------------------------------------------

    [Fact]
    public void Pcd8544_shows_its_ram_once_powered_up_in_normal_mode_with_enough_vop()
    {
        var lcd = new Pcd8544Controller();
        lcd.Command(0x21); // extended set
        lcd.Command(0x80 | 50);
        lcd.Command(0x14);
        lcd.Command(0x20); // basic set, horizontal addressing
        lcd.Command(0x0C); // normal
        lcd.Command(0x40 | 1);
        lcd.Command(0x80 | 10);
        lcd.Data(0b0000_0101);
        lcd.Data(0xFF);

        Assert.True(lcd.Dark(10, 8));
        Assert.False(lcd.Dark(10, 9));
        Assert.True(lcd.Dark(10, 10));
        Assert.True(lcd.Dark(11, 15));
        Assert.Equal(50, lcd.Vop);

        lcd.Command(0x0D); // inverse
        Assert.False(lcd.Dark(10, 8));
        Assert.True(lcd.Dark(0, 0));
        lcd.Command(0x08); // blank
        Assert.False(lcd.Dark(0, 0));
    }

    [Fact]
    public void Pcd8544_vertical_addressing_fills_a_column_of_banks_first()
    {
        var lcd = new Pcd8544Controller();
        lcd.Command(0x22); // power on, vertical addressing
        for (int i = 0; i < 7; i++)
        {
            lcd.Data(0x01);
        }

        Assert.Equal(1, lcd.X);
        Assert.Equal(1, lcd.Y);
        Assert.Equal(0x01, lcd.Ram[5 * Pcd8544Controller.Columns]);
        Assert.Equal(0x01, lcd.Ram[1]);
    }

    [Fact]
    public void Pcd8544_contrast_needs_vop_and_darkens_the_background_when_too_high()
    {
        Assert.Equal(0, Pcd8544.Contrast(0).On);
        Assert.True(Pcd8544.Contrast(50).On > 0.7);
        Assert.Equal(0, Pcd8544.Contrast(60).Off);
        Assert.True(Pcd8544.Contrast(110).Off > 0.5);
    }

    [Fact]
    public void A_nokia_5110_module_takes_hardware_spi_with_ce_and_dc()
    {
        BoardSimulation sim = Scenes.Create(
            Scenes.Scene([Scenes.Part("lcd1", Pcd8544.TypeName)], "board.3V3 lcd1.VCC", "board.GND lcd1.GND", "board.D3 lcd1.RST", "board.D4 lcd1.CE", "board.D5 lcd1.DC", "board.D11 lcd1.DIN", "board.D13 lcd1.CLK", "board.D6 lcd1.LIGHT"),
            _engine,
            _eeprom);
        Mcu mcu = sim.Mcu;
        var lcd = (Pcd8544)sim.FindComponent("lcd1")!;
        foreach (int pin in new[] { 3, 4, 5, 6 })
        {
            mcu.PinMode(pin, 1);
            mcu.DigitalWrite(pin, 1);
        }

        mcu.SpiBegin();
        mcu.DigitalWrite(4, 0);
        mcu.DigitalWrite(5, 0);
        foreach (byte command in new byte[] { 0x21, 0xBF, 0x14, 0x20, 0x0C })
        {
            mcu.SpiTransfer(command);
        }

        mcu.DigitalWrite(5, 1);
        for (int i = 0; i < 84; i++)
        {
            mcu.SpiTransfer(0xFF);
        }

        mcu.DigitalWrite(4, 1);
        Assert.Equal(84 * 8, lcd.DarkPixels);
        Assert.Equal(1, lcd.Backlight);
        Assert.Equal(DisplayKind.GraphicLcd, lcd.Display.Kind);
    }

    // ---- HD44780 A02 ------------------------------------------------------------------------------

    [Fact]
    public void The_a02_rom_has_cyrillic_and_latin_1_where_a00_has_katakana()
    {
        Span<byte> rows = stackalloc byte[8];
        var a02 = new Hd44780(() => 0, romA02: true);
        a02.Glyph(0x80, rows); // Б
        Assert.Equal([0x00, 0x1F, 0x11, 0x10, 0x1E, 0x11, 0x11, 0x1E], rows.ToArray());
        a02.Glyph(0x41, rows); // A, one row lower than in A00
        Assert.Equal([0x00, 0x04, 0x0A, 0x11, 0x11, 0x1F, 0x11, 0x11], rows.ToArray());

        var a00 = new Hd44780(() => 0);
        a00.Glyph(0x80, rows);
        Assert.All(rows.ToArray(), r => Assert.Equal(0, r));
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private static TftController Awake(TftPanel panel, int madctl)
    {
        var tft = new TftController(panel.Chip, panel.RamWidth, panel.RamHeight);
        tft.Command(0x11);
        tft.Command(0x29);
        tft.Command(0x36);
        tft.Data((byte)madctl);
        tft.Command(0x3A);
        tft.Data(0x55);
        return tft;
    }

    private static void Window(TftController tft, int x0, int y0, int x1, int y1)
    {
        tft.Command(0x2A);
        foreach (int v in new[] { x0 >> 8, x0 & 0xFF, x1 >> 8, x1 & 0xFF })
        {
            tft.Data((byte)v);
        }

        tft.Command(0x2B);
        foreach (int v in new[] { y0 >> 8, y0 & 0xFF, y1 >> 8, y1 & 0xFF })
        {
            tft.Data((byte)v);
        }
    }

    private static void Rgb565(TftController tft, ushort color)
    {
        tft.Data((byte)(color >> 8));
        tft.Data((byte)color);
    }

    private static byte[] Glass(TftController tft, TftPanel panel)
    {
        var rgb = new byte[panel.Width * panel.Height * 3];
        tft.Render(panel, rgb);
        return rgb;
    }

    private static (int R, int G, int B) Pixel(byte[] glass, TftPanel panel, int x, int y)
    {
        int at = ((y * panel.Width) + x) * 3;
        return (glass[at], glass[at + 1], glass[at + 2]);
    }

    private static void Send(Mcu mcu, bool data, byte command, params byte[] parameters)
    {
        mcu.DigitalWrite(9, data ? 1 : 0);
        mcu.SpiTransfer(command);
        mcu.DigitalWrite(9, 1);
        foreach (byte p in parameters)
        {
            mcu.SpiTransfer(p);
        }
    }
}
