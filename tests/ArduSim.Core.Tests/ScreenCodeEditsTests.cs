using ArduSim.Core.Screens;

namespace ArduSim.Core.Tests;

/// <summary>What the constructor writes into code that was read (ADR-019), in the sketch's own style.</summary>
public sealed class ScreenCodeEditsTests
{
    private const string Code = """
        #include <LiquidCrystal_I2C.h>
        #define KEY_OK '#'

        LiquidCrystal_I2C lcd(0x27, 20, 4);
        enum Screen { HOME, INFO, LAST_ONE };
        Screen screen = HOME;

        void go(Screen next) {
          screen = next;
        }

        void onKey(char key) {
          switch (screen) {
            case HOME:
              if (key == KEY_OK) go(INFO);
              break;
            case INFO:
              if (key == '1') {
                go(LAST_ONE);
              }
              break;
            default:
              if (key == '9') go(HOME); else lcd.clear();
              break;
          }
        }

        void drawHome() {
          lcd.setCursor(1, 0); lcd.print("Hi");
          lcd.setCursor(0, 1);
          lcd.print(millis());
          if (screen == HOME) lcd.print("!");
        }

        void draw() {
          switch (screen) {
            case HOME: drawHome(); break;
            case INFO:
              lcd.setCursor(0, 0);
              lcd.print(F("Info"));
              break;
            case LAST_ONE: lcd.print("x"); break;
          }
        }

        void loop() { onKey(0); draw(); }
        """;

    private static ScreenDocument Read(string text = Code) => ScreenCodeAnalyzer.Analyze([("a.ino", text)])!;

    private static string Do(IReadOnlyList<SourceEdit>? edits, string text = Code)
    {
        Assert.NotNull(edits);
        return ScreenCodeEdits.Apply(text, edits);
    }

    [Fact]
    public void A_set_of_edits_is_made_at_once_and_its_inverse_undoes_it()
    {
        var edits = new[]
        {
            new SourceEdit(new SourceSpan("a", 2, 1, "c"), "XYZ"),
            new SourceEdit(new SourceSpan("a", 0, 1, "a"), ""),
            new SourceEdit(new SourceSpan("a", 5, 0, ""), "--"),
        };

        string after = ScreenCodeEdits.Apply("abcdefg", edits);

        Assert.Equal("bXYZde--fg", after);
        Assert.Equal("abcdefg", ScreenCodeEdits.Apply(after, ScreenCodeEdits.Inverse(edits)));
    }

    [Fact]
    public void Text_goes_at_the_end_of_a_screens_own_function()
    {
        ScreenDocument document = Read();

        string text = Do(ScreenCodeEdits.AddElement(document.Find("HOME")!, document.Display, ElementKind.Text, 4, 2, 0, 0, "New \"one\""));

        Assert.Contains("  lcd.setCursor(4, 2); lcd.print(F(\"New \\\"one\\\"\"));\n}", text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains(Read(text).Find("HOME")!.Elements, e => e is { Text: "New \"one\"", X: 4, Y: 2 });
    }

    [Fact]
    public void Text_goes_before_the_break_of_a_screen_drawn_in_its_case()
    {
        ScreenDocument document = Read();

        string text = Do(ScreenCodeEdits.AddElement(document.Find("INFO")!, document.Display, ElementKind.Value, 3, 1, 0, 0, "millis()"));

        Assert.Contains(Read(text).Find("INFO")!.Elements, e => e is { Kind: ElementKind.Value, Text: "millis()", X: 3, Y: 1 });
        Assert.Contains("lcd.print(F(\"Info\"));\n      lcd.setCursor(3, 1); lcd.print(millis());\n      break;", text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_case_on_one_line_stays_on_one_line()
    {
        ScreenDocument document = Read();

        string text = Do(ScreenCodeEdits.AddElement(document.Find("LAST_ONE")!, document.Display, ElementKind.Text, 0, 0, 0, 0, "y"));

        Assert.Contains("case LAST_ONE: lcd.print(\"x\"); lcd.setCursor(0, 0); lcd.print(F(\"y\")); break;", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Removing_text_takes_its_cursor_too_and_empty_lines_go()
    {
        ScreenDocument document = Read();
        ScreenElement hi = document.Find("HOME")!.Elements.Single(e => e.Text == "Hi");

        string text = Do(ScreenCodeEdits.Remove(hi.RemoveSpans));

        Assert.DoesNotContain("Hi", text, StringComparison.Ordinal);
        Assert.Contains("void drawHome() {\n  lcd.setCursor(0, 1);", text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_keeps_its_cursor_on_its_own_line_and_both_go()
    {
        ScreenDocument document = Read();
        ScreenElement value = document.Find("HOME")!.Elements.Single(e => e.Kind == ElementKind.Value);

        string text = Do(ScreenCodeEdits.Remove(value.RemoveSpans));

        Assert.DoesNotContain("millis", text, StringComparison.Ordinal);
        Assert.DoesNotContain("setCursor(0, 1)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void What_hangs_on_a_condition_cannot_go_on_its_own()
    {
        ScreenElement mark = Read().Find("HOME")!.Elements.Single(e => e.Text == "!");

        Assert.Empty(mark.RemoveSpans);
    }

    [Fact]
    public void A_new_screen_is_a_constant_and_a_case_before_the_end_of_the_switch()
    {
        string text = Do(ScreenCodeEdits.AddScreen(Read(), "MORE"));

        Assert.Contains("enum Screen { HOME, INFO, LAST_ONE, MORE };", text, StringComparison.Ordinal);
        ScreenNode more = Read(text).Find("MORE")!;
        Assert.Contains(more.Elements, e => e.Text == "MORE");
    }

    [Fact]
    public void A_counting_constant_stays_last()
    {
        string counted = Code.Replace("enum Screen { HOME, INFO, LAST_ONE };", "enum Screen { HOME, INFO, LAST_ONE, SCREEN_COUNT };", StringComparison.Ordinal);

        string text = Do(ScreenCodeEdits.AddScreen(Read(counted), "MORE"), counted);

        Assert.Contains("LAST_ONE, MORE, SCREEN_COUNT };", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_transition_goes_first_in_the_key_handler_in_the_sketchs_own_words()
    {
        string text = Do(ScreenCodeEdits.AddTransition(Read(), "INFO", "#", "HOME"));

        Assert.Contains("void onKey(char key) {\n  if (screen == INFO && key == KEY_OK) { go(HOME); return; }\n  switch", text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        ScreenTransition read = Read(text).Find("INFO")!.Transitions.Single(t => t.Target == "HOME");
        Assert.Equal("#", read.Trigger.Value);
        Assert.NotNull(read.KeySpan);
        Assert.NotEmpty(read.RemoveSpans);
    }

    [Fact]
    public void A_transition_from_any_screen_has_no_screen_in_it()
    {
        string text = Do(ScreenCodeEdits.AddTransition(Read(), null, "5", "HOME"));

        Assert.Contains("if (key == '5') { go(HOME); return; }", text, StringComparison.Ordinal);
        Assert.Contains(Read(text).FromAnywhere, t => t is { Target: "HOME", Trigger.Value: "5" });
    }

    [Fact]
    public void A_key_can_change_where_the_comparison_is_the_transitions_own()
    {
        ScreenTransition transition = Read().Find("HOME")!.Transitions.Single(t => t.Target == "INFO");
        Assert.NotNull(transition.KeySpan);

        string text = Do([new SourceEdit(transition.KeySpan, "'7'")]);

        Assert.Contains("if (key == '7') go(INFO);", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Removing_a_transition_takes_its_if_and_leaves_the_case()
    {
        ScreenTransition simple = Read().Find("HOME")!.Transitions.Single(t => t.Target == "INFO");
        string text = Do(ScreenCodeEdits.Remove(simple.RemoveSpans));
        Assert.DoesNotContain("go(INFO)", text, StringComparison.Ordinal);
        Assert.Contains("case HOME:\n      break;", text.ReplaceLineEndings("\n"), StringComparison.Ordinal);

        ScreenTransition braced = Read().Find("INFO")!.Transitions.Single(t => t.Target == "LAST_ONE");
        text = Do(ScreenCodeEdits.Remove(braced.RemoveSpans));
        Assert.DoesNotContain("go(LAST_ONE)", text, StringComparison.Ordinal);
        Assert.Contains("case INFO:\n      break;", text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void What_has_an_else_cannot_go_on_its_own()
    {
        ScreenDocument document = Read();

        Assert.All(document.Screens.SelectMany(s => s.Transitions).Where(t => t.Trigger.Value == "9"), t => Assert.Empty(t.RemoveSpans));
    }

    [Fact]
    public void Renaming_a_screen_changes_every_place_it_is_written()
    {
        string text = Do(ScreenCodeEdits.Rename(Read().Find("INFO")!.UseSpans, "DETAILS"));

        Assert.DoesNotContain("INFO", text, StringComparison.Ordinal);
        Assert.NotNull(Read(text).Find("DETAILS"));
    }

    [Fact]
    public void A_screen_goes_only_if_nothing_else_names_it()
    {
        // HOME is compared with in drawHome and is the first screen: it stays.
        Assert.Empty(Read().Find("HOME")!.RemoveSpans);

        ScreenDocument document = Read();
        string text = Do(ScreenCodeEdits.Remove(document.Find("LAST_ONE")!.RemoveSpans));

        Assert.DoesNotContain("LAST_ONE", text, StringComparison.Ordinal);
        Assert.Null(Read(text).Find("LAST_ONE"));
        Assert.Contains("enum Screen { HOME, INFO };", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sketch_with_no_enum_cannot_get_a_screen_but_its_screens_can_get_text()
    {
        const string Plain = """
            #include <LiquidCrystal_I2C.h>
            LiquidCrystal_I2C lcd(0x27, 16, 2);
            void showWelcome() { lcd.setCursor(0, 0); lcd.print("Welcome"); }
            void showBye() { lcd.print("Bye"); }
            void setup() { showWelcome(); }
            void loop() { showBye(); }
            """;
        ScreenDocument document = Read(Plain);

        Assert.Null(ScreenCodeEdits.AddScreen(document, "MORE"));
        Assert.Null(ScreenCodeEdits.AddTransition(document, null, "1", "showBye"));
        string text = Do(ScreenCodeEdits.AddElement(document.Find("showBye")!, document.Display, ElementKind.Text, 1, 1, 0, 0, "x"), Plain);
        Assert.Contains(Read(text).Find("showBye")!.Elements, e => e is { Text: "x", X: 1, Y: 1 });
    }

    [Fact]
    public void An_oled_shape_is_drawn_with_its_librarys_own_call()
    {
        const string Oled = """
            #include <Adafruit_SSD1306.h>
            Adafruit_SSD1306 oled(128, 64, &Wire, -1);
            void showIt() {
              oled.clearDisplay();
              oled.drawRect(1, 2, 30, 40, SSD1306_WHITE);
              oled.display();
            }
            void loop() { showIt(); }
            """;
        ScreenDocument document = Read(Oled);
        ScreenElement frame = document.Find("showIt")!.Elements.Single();
        Assert.NotNull(frame.WidthSpan);
        Assert.NotNull(frame.HeightSpan);

        // New drawing goes before what sends the buffer to the display.
        string text = Do(ScreenCodeEdits.AddElement(document.Find("showIt")!, document.Display, ElementKind.Box, 5, 6, 7, 8, ""), Oled);

        Assert.Contains("oled.fillRect(5, 6, 7, 8, SSD1306_WHITE);\n  oled.display();", text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }
}
