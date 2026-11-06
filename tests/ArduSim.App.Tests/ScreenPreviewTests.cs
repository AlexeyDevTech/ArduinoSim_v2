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

    [Fact]
    public void What_only_shares_a_place_is_not_drawn()
    {
        ScreenElement zero = Text("0") with { IsAlternative = true };
        ScreenElement value = new() { Id = "v", Kind = ElementKind.Value, Text = "day", Width = 2 };

        Assert.Equal([value], ScreenPreview.Shown(new ScreenNode { Elements = [zero, value] }, Plain));
    }
}
