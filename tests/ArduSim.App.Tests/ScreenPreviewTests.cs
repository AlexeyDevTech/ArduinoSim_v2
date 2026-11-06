using System.Windows.Media.Imaging;
using ArduSim.App.Views;
using ArduSim.Core.Screens;
using Xunit;

namespace ArduSim.App.Tests;

/// <summary>What a screen's picture shows of what was read from code (ADR-019).</summary>
public sealed class ScreenPreviewTests
{
    private static readonly ScreenDisplay Plain = new(DisplayFamily.CharLcd, 8, 2, DisplayLibrary.LiquidCrystalI2c, "lcd");
    private static readonly ScreenDisplay OwnFont = Plain with { OwnFont = true };

    private static uint[] Pixels(ScreenDisplay display, params ScreenElement[] elements)
    {
        BitmapSource bitmap = ScreenPreview.Render(new ScreenNode { Elements = elements }, display);
        uint[] pixels = new uint[bitmap.PixelWidth * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels;
    }

    private static ScreenElement Text(string text, int x = 0, int y = 0, bool placed = true) =>
        new() { Id = text, Kind = ElementKind.Text, Text = text, X = x, Y = y, IsPlaced = placed };

    [Fact]
    public void Every_Cyrillic_capital_has_a_shape_through_a_class_with_its_own_font()
    {
        uint[] unknown = Pixels(OwnFont, Text("?"));
        foreach (char letter in "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ")
        {
            Assert.False(Pixels(OwnFont, Text(letter.ToString())).SequenceEqual(unknown), letter + " draws as a question mark");
        }

        Assert.Equal(Pixels(OwnFont, Text("Я")), Pixels(OwnFont, Text("я")));
    }

    [Fact]
    public void A_plain_display_keeps_its_question_mark_since_its_rom_has_no_such_letter()
    {
        Assert.Equal(Pixels(Plain, Text("?")), Pixels(Plain, Text("Я")));
    }

    [Theory]
    [InlineData("dt.day", "day")]
    [InlineData("app->minute", "minute")]
    [InlineData("seconds", "seconds")]
    public void A_value_shows_its_last_name(string expression, string shown)
    {
        Assert.Equal(shown, ScreenPreview.ValueLook(expression));
    }

    [Theory]
    [InlineData("(uint8_t)pgm_read_byte(weekday+i)")]
    [InlineData("menuIndex+1")]
    [InlineData("reinterpret_cast<const __FlashStringHelper *>(item)")]
    [InlineData("alarm.hour()")]
    public void Any_other_expression_shows_as_dots_and_not_as_the_start_of_its_code(string expression)
    {
        Assert.All(ScreenPreview.ValueLook(expression), c => Assert.Equal('¥', c));
    }

    [Fact]
    public void What_could_not_be_placed_draws_only_where_it_hides_nothing()
    {
        ScreenElement title = Text("TITLE");
        ScreenElement overIt = Text("LOST", 0, 0, placed: false);
        ScreenElement free = Text("FREE", 0, 1, placed: false);
        var screen = new ScreenNode { Elements = [title, overIt, free] };

        IReadOnlyList<ScreenElement> shown = ScreenPreview.Shown(screen, Plain);

        Assert.Equal([title, free], shown);
    }

    private static readonly ScreenDisplay Wide = new(DisplayFamily.CharLcd, 20, 4, DisplayLibrary.LiquidCrystalI2c, "lcd");

    private static (ScreenDocument Document, ScreenNode Screen) Generated(params ScreenElement[] elements)
    {
        var screen = new ScreenNode { Id = "a", Elements = elements };
        return (new ScreenDocument { Source = ScreenSource.Generated, Display = Wide, Start = "a", Screens = [screen] }, screen);
    }

    private static uint[] Pixels(ScreenDocument document, ScreenNode screen, string? selected = null)
    {
        BitmapSource bitmap = ScreenPreview.Render(screen, document.Display, document, selected);
        uint[] pixels = new uint[bitmap.PixelWidth * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels;
    }

    [Fact]
    public void Of_what_hangs_on_different_conditions_in_one_place_one_is_drawn_and_the_selected_one_first()
    {
        ScreenElement first = Text("ONE") with { Id = "1", When = "a" };
        ScreenElement second = Text("TWO") with { Id = "2", When = "!a" };
        ScreenElement always = Text("ALL", 0, 1) with { Id = "3" };
        (ScreenDocument document, ScreenNode screen) = Generated(first, second, always);

        Assert.Equal([first, always], ScreenPreview.Shown(screen, Wide, document));
        Assert.Equal([second, always], ScreenPreview.Shown(screen, Wide, document, "2"));
    }

    [Fact]
    public void What_hangs_on_the_same_condition_is_all_drawn()
    {
        ScreenElement first = Text("ONE") with { Id = "1", When = "a" };
        ScreenElement second = Text("TWO") with { Id = "2", When = "a" };
        (ScreenDocument document, ScreenNode screen) = Generated(first, second);

        Assert.Equal(2, ScreenPreview.Shown(screen, Wide, document).Count);
    }

    [Fact]
    public void A_text_lies_where_its_alignment_puts_it_and_the_picture_says_the_same()
    {
        ScreenElement right = Text("AB") with { Align = ElementAlign.Right };
        ScreenElement center = Text("AB") with { Align = ElementAlign.Center };
        (ScreenDocument document, ScreenNode _) = Generated(right);

        Assert.Equal((18 * 6) + 1, ScreenPreview.BoundsOf(right, Wide, document).X);
        Assert.Equal((9 * 6) + 1, ScreenPreview.BoundsOf(center, Wide, document).X);
    }

    [Fact]
    public void A_symbol_in_a_text_takes_one_cell_and_shows_its_dots()
    {
        ScreenElement named = Text("{bell}") with { Align = ElementAlign.Right };
        (ScreenDocument document, ScreenNode screen) = Generated(named);

        Assert.Equal((19 * 6) + 1, ScreenPreview.BoundsOf(named, Wide, document).X);
        Assert.NotEqual(Pixels(document, Generated().Screen), Pixels(document, screen));
    }

    [Fact]
    public void A_big_number_takes_two_rows_and_the_cells_of_its_digits()
    {
        var big = new ScreenElement { Id = "b", Kind = ElementKind.BigNumber, Text = "clock", Format = ValueFormat.HourMinute };
        (ScreenDocument document, ScreenNode screen) = Generated(big);

        System.Windows.Rect bounds = ScreenPreview.BoundsOf(big, Wide, document);

        Assert.Equal(17, bounds.Height);
        Assert.Equal((17 * 6) - 1, bounds.Width);
        Assert.NotEqual(Pixels(document, Generated().Screen), Pixels(document, screen));
    }

    [Theory]
    [InlineData(ElementKind.Bar, "50")]
    [InlineData(ElementKind.Symbol, "bell")]
    [InlineData(ElementKind.Input, "clock")]
    [InlineData(ElementKind.Drawn, "table")]
    public void Each_new_element_draws_something(ElementKind kind, string text)
    {
        var element = new ScreenElement { Id = "x", Kind = kind, Text = text, Width = 10, Height = 1 };
        (ScreenDocument document, ScreenNode screen) = Generated(element);

        Assert.NotEqual(Pixels(document, Generated().Screen), Pixels(document, screen));
    }

    [Fact]
    public void A_project_s_own_symbol_takes_the_place_of_the_built_in_one()
    {
        ScreenElement bell = new() { Id = "x", Kind = ElementKind.Symbol, Text = "bell" };
        (ScreenDocument document, ScreenNode screen) = Generated(bell);
        ScreenDocument own = document with { Glyphs = [new ScreenGlyph { Name = "bell", Rows = new byte[] { 31, 31, 31, 31, 31, 31, 31, 31 } }] };

        Assert.NotEqual(Pixels(document, screen), Pixels(own, screen));
    }

    [Fact]
    public void What_only_shares_a_place_is_not_drawn()
    {
        ScreenElement zero = Text("0") with { IsAlternative = true };
        ScreenElement value = new() { Id = "v", Kind = ElementKind.Value, Text = "day", Width = 2 };

        Assert.Equal([value], ScreenPreview.Shown(new ScreenNode { Elements = [zero, value] }, Plain));
    }
}
