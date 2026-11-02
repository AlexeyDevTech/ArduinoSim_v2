using ArduSim.Core.Displays;
using ArduSim.Core.Timing;

namespace ArduSim.Core.Tests;

/// <summary>The controller fed recorded command streams (as LiquidCrystal sends them), checked against text and dot art.</summary>
public sealed class Hd44780Tests
{
    private ulong _now;
    private readonly Hd44780 _lcd;

    public Hd44780Tests()
    {
        _lcd = new Hd44780(() => _now);
    }

    // 4-bit wiring: only DB7..DB4 are connected, DB3..DB0 read 1 through the controller's pull-ups.
    private void Nibble(bool rs, int nibble)
    {
        _lcd.Write(rs, ((nibble & 0x0F) << 4) | 0x0F);
        _now += 100 * VirtualTime.Microsecond;
    }

    private void Send(bool rs, int value)
    {
        Nibble(rs, value >> 4);
        Nibble(rs, value);
        _now += value is 0x01 or 0x02 or 0x03 ? 2 * VirtualTime.Millisecond : 0;
    }

    private void Command(int value) => Send(false, value);

    private void Print(string text)
    {
        foreach (char c in text)
        {
            Send(true, c);
        }
    }

    /// <summary>LiquidCrystal::begin(cols, rows) in 4-bit mode: the datasheet's initialization by instruction.</summary>
    private void Begin()
    {
        _now += 50 * VirtualTime.Millisecond;
        Nibble(false, 0x3);
        _now += 4500 * VirtualTime.Microsecond;
        Nibble(false, 0x3);
        _now += 4500 * VirtualTime.Microsecond;
        Nibble(false, 0x3);
        _now += 150 * VirtualTime.Microsecond;
        Nibble(false, 0x2);
        Command(0x28); // 4-bit, 2 lines, 5x8
        Command(0x0C); // display on, no cursor, no blink
        Command(0x01); // clear
        Command(0x06); // increment, no shift
    }

    private string[] Text(int columns, int rows) => [.. Enumerable.Range(0, rows).Select(r => _lcd.RowText(r, columns, rows))];

    [Fact]
    public void Init_sequence_ends_in_4_bit_two_line_mode_with_the_display_on()
    {
        Begin();

        Assert.False(_lcd.EightBitInterface);
        Assert.True(_lcd.TwoLines);
        Assert.True(_lcd.DisplayOn);
        Assert.False(_lcd.LowNibbleNext);
        Assert.Equal(0, _lcd.AddressCounter);
    }

    [Fact]
    public void Hello_world_on_a_1602()
    {
        Begin();
        Print("hello, world!");
        Command(0x80 | 0x40); // setCursor(0, 1)
        Print("42");

        Assert.Equal(["hello, world!   ", "42              "], Text(16, 2));
    }

    [Fact]
    public void A_2004_maps_rows_2_and_3_to_the_second_half_of_each_line()
    {
        Begin();
        int[] offsets = [0x00, 0x40, 0x14, 0x54];
        for (int row = 0; row < 4; row++)
        {
            Command(0x80 | offsets[row]);
            Print("row " + row);
        }

        Assert.Equal(["row 0               ", "row 1               ", "row 2               ", "row 3               "], Text(20, 4));
    }

    [Fact]
    public void Writing_past_the_end_of_line_1_continues_on_line_2()
    {
        Begin();
        Command(0x80 | 0x27);
        Print("ab");

        Assert.Equal(0x41, _lcd.AddressCounter);
        Assert.Equal((byte)'a', _lcd.Ddram[0x27]);
        Assert.Equal((byte)'b', _lcd.Ddram[0x40]);
    }

    [Fact]
    public void Create_char_writes_cgram_and_the_glyph_renders_from_it()
    {
        byte[] heart = [0b00000, 0b01010, 0b11111, 0b11111, 0b11111, 0b01110, 0b00100, 0b00000];
        Begin();
        Command(0x40 | (3 << 3)); // createChar(3, heart)
        foreach (byte row in heart)
        {
            Send(true, row);
        }

        Command(0x80); // setCursor(0, 0)
        Send(true, 3);

        byte[] dots = _lcd.Render(16, 2, blinkPhase: false);
        Assert.Equal(heart, Enumerable.Range(0, 8).Select(line => dots[line * 16]).ToArray());
        Assert.Equal("\\3", _lcd.RowText(0, 16, 2)[..2]);
    }

    [Fact]
    public void Scroll_display_left_moves_both_lines_and_home_undoes_it()
    {
        Begin();
        Print("abcdef");
        Command(0x80 | 0x40);
        Print("123456");

        Command(0x18); // scrollDisplayLeft
        Command(0x18);
        Assert.Equal(["cdef            ", "3456            "], Text(16, 2));

        Command(0x1C); // scrollDisplayRight
        Assert.Equal(["bcdef           ", "23456           "], Text(16, 2));

        Command(0x02); // home
        Assert.Equal(["abcdef          ", "123456          "], Text(16, 2));
    }

    [Fact]
    public void Scrolling_wraps_around_the_40_character_line()
    {
        Begin();
        Command(0x80 | 39);
        Print("Z");
        Command(0x80);

        Command(0x1C); // the window moves left: position 39 becomes column 0

        Assert.Equal("Z               ", _lcd.RowText(0, 16, 2));
    }

    [Fact]
    public void Autoscroll_shifts_the_display_with_each_character()
    {
        Begin();
        Command(0x80 | 16);
        Command(0x07); // entry mode: increment + shift (LiquidCrystal::autoscroll)
        Print("XY");

        Assert.Equal(2, _lcd.DisplayShift);
        Assert.Equal("              XY", _lcd.RowText(0, 16, 2));
    }

    [Fact]
    public void Right_to_left_entry_decrements_the_address()
    {
        Begin();
        Command(0x80 | 5);
        Command(0x04); // entry mode: decrement (LiquidCrystal::rightToLeft)
        Print("abc");

        Assert.Equal("   cba          ", _lcd.RowText(0, 16, 2));
    }

    [Fact]
    public void Clear_resets_shift_and_address_and_keeps_the_display_mode()
    {
        Begin();
        Print("text");
        Command(0x18);
        Command(0x01);

        Assert.Equal(["                ", "                "], Text(16, 2));
        Assert.Equal(0, _lcd.DisplayShift);
        Assert.Equal(0, _lcd.AddressCounter);
        Assert.True(_lcd.DisplayOn);
    }

    [Fact]
    public void Busy_flag_follows_the_execution_times_and_status_carries_the_address()
    {
        Begin();
        Print("ab");

        _lcd.Write(false, 0x0F); // clear, high nibble
        _lcd.Write(false, 0x1F); // clear, low nibble: executes now
        Assert.True(_lcd.Busy);
        Assert.Equal(0x80, _lcd.Status & 0x80);

        _now += Hd44780.LongExecution;
        Assert.False(_lcd.Busy);
        Assert.Equal(0x00, _lcd.Status);
    }

    [Fact]
    public void Reading_status_and_data_in_4_bit_mode_takes_two_nibbles()
    {
        Begin();
        Print("Q");
        _now += Hd44780.LongExecution;
        Command(0x80); // address 0

        // Status: busy is clear, address counter 0.
        Assert.Equal(0x00, _lcd.ReadLines(false));
        _lcd.EndRead(false);
        Assert.Equal(0x00, _lcd.ReadLines(false));
        _lcd.EndRead(false);

        // Data at address 0 is 'Q' (0x51): high nibble then low nibble on DB7..DB4.
        _now += Hd44780.LongExecution;
        Assert.Equal(0x50, _lcd.ReadLines(true));
        _lcd.EndRead(true);
        Assert.Equal(0x10, _lcd.ReadLines(true));
        _lcd.EndRead(true);
        Assert.Equal(1, _lcd.AddressCounter);
    }

    [Fact]
    public void Display_off_renders_nothing_and_cursor_and_blink_mark_the_address()
    {
        Begin();
        Print("A");
        Command(0x08); // display off
        Assert.All(_lcd.Render(16, 2, blinkPhase: true), b => Assert.Equal(0, b));

        Command(0x0F); // display, cursor and blink on; cursor at address 1
        byte[] steady = _lcd.Render(16, 2, blinkPhase: false);
        byte[] blinking = _lcd.Render(16, 2, blinkPhase: true);
        Assert.Equal(0x1F, steady[(7 * 16) + 1]);
        Assert.Equal(0x00, steady[(6 * 16) + 1]);
        Assert.All(Enumerable.Range(0, 8), line => Assert.Equal(0x1F, blinking[(line * 16) + 1]));
    }

    [Fact]
    public void Rom_glyphs_match_the_a00_table()
    {
        Span<byte> glyph = stackalloc byte[8];
        Hd44780Font.A00((byte)'A', glyph);
        Assert.Equal(
            """
            .###.
            #...#
            #...#
            #...#
            #####
            #...#
            #...#
            .....
            """.ReplaceLineEndings("\n"),
            Art(glyph));

        Hd44780Font.A00(0xDF, glyph); // the degree-like mark sketches use for °C
        Assert.Equal("###..\n#.#..\n###..\n.....\n.....\n.....\n.....\n.....", Art(glyph));

        Hd44780Font.A00(0x5C, glyph); // ¥, not a backslash, in the A00 ROM
        Assert.Equal("#...#\n.#.#.\n#####\n..#..\n#####\n..#..\n..#..\n.....", Art(glyph));
    }

    private static string Art(ReadOnlySpan<byte> glyph)
    {
        var lines = new List<string>();
        foreach (byte row in glyph)
        {
            lines.Add(new string([.. Enumerable.Range(0, 5).Select(bit => (row & (0x10 >> bit)) != 0 ? '#' : '.')]));
        }

        return string.Join('\n', lines);
    }
}
