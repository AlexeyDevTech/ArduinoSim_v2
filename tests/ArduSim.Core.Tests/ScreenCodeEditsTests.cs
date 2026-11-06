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

    // ---- positions the code does not write as plain numbers ----

    // NanoOS's home screen: centered text, with its column in a local variable.
    private const string Positions = """
        #include <LiquidCrystal_I2C.h>
        #define LCD_COLUMNS 20
        #define MARGIN 2
        const uint8_t VALUE_COLUMN = 12;

        LiquidCrystal_I2C lcd(0x27, LCD_COLUMNS, 4);
        int cursorColumn = 0;
        enum Screen { HOME, OTHER, THIRD };
        Screen screen = HOME;

        void clearLine(uint8_t row) {
          lcd.setCursor(0, row);
          for (uint8_t i = 0; i < LCD_COLUMNS; i++) lcd.print(' ');
          lcd.setCursor(0, row);
        }

        void drawHome() {
          char t[16];
          strcpy(t, "12:34");
          uint8_t len = strlen(t); uint8_t start = (LCD_COLUMNS - len) / 2;
          lcd.setCursor(start, 1); lcd.print(t);
          lcd.setCursor(MARGIN, 2); lcd.print("Name");
          lcd.setCursor(LCD_COLUMNS - 5, 2); lcd.print("End");
          lcd.setCursor(VALUE_COLUMN, 3); lcd.print("Val");
          lcd.setCursor(0, 0);
          lcd.print("Temp ");
          lcd.print(millis());
          lcd.print(" C");
        }

        void drawOther() {
          uint8_t shared = 3;
          lcd.setCursor(shared, 0); lcd.print("A");
          lcd.setCursor(shared, 1); lcd.print("B");
          lcd.setCursor(cursorColumn, 2); lcd.print("C");
          for (uint8_t i = 0; i < 3; i++) {
            lcd.setCursor(i, 3);
            lcd.print(i);
          }
        }

        void drawThird() {
          clearLine(0); lcd.print("Third");
          clearLine(1); lcd.print("More");
        }

        void draw() {
          switch (screen) {
            case HOME: drawHome(); break;
            case OTHER: drawOther(); break;
            case THIRD: drawThird(); break;
          }
        }

        void loop() { draw(); }
        """;

    private static ScreenDocument ReadPositions(string text = Positions) => ScreenCodeAnalyzer.Analyze([("a.ino", text)])!;

    private static ScreenElement Element(ScreenDocument document, string screen, string text) =>
        document.Find(screen)!.Elements.First(e => e.Text == text);

    [Fact]
    public void A_column_in_a_local_variable_is_worked_out_and_shown_as_written()
    {
        ScreenElement time = Element(ReadPositions(), "HOME", "t");

        Assert.Equal("start", time.XCode?.Text);
        Assert.False(time.XCode?.IsLocked);
        Assert.NotNull(time.XSpan);

        // Centered: (20 - the value's width of 2) / 2.
        Assert.Equal(9, time.X);
        Assert.True(time.IsPlaced);
    }

    [Fact]
    public void A_drag_writes_a_number_for_it_and_what_only_fed_it_goes()
    {
        ScreenDocument document = ReadPositions();
        ScreenElement time = Element(document, "HOME", "t");

        IReadOnlyList<SourceEdit>? edits = ScreenCodeEdits.Move(time, document.Display, 5, time.Y);
        string text = Do(edits, Positions);

        Assert.Contains("lcd.setCursor(5, 1); lcd.print(t);", text, StringComparison.Ordinal);
        Assert.DoesNotContain("start", text, StringComparison.Ordinal);
        Assert.DoesNotContain("len", text, StringComparison.Ordinal);
        Assert.Contains("char t[16];", text, StringComparison.Ordinal);
        Assert.Equal(5, Element(ReadPositions(text), "HOME", "t").X);
        Assert.Null(Element(ReadPositions(text), "HOME", "t").XCode);

        // And undo brings all of it back.
        Assert.Equal(Positions, ScreenCodeEdits.Apply(text, ScreenCodeEdits.Inverse(edits!)));
    }

    [Theory]
    [InlineData("Name", "MARGIN", 2)]
    [InlineData("End", "LCD_COLUMNS - 5", 15)]
    [InlineData("Val", "VALUE_COLUMN", 12)]
    public void A_name_or_a_constant_expression_shows_its_value_and_a_drag_replaces_it_here_only(string label, string written, int value)
    {
        ScreenDocument document = ReadPositions();
        ScreenElement element = Element(document, "HOME", label);
        Assert.Equal(value, element.X);
        Assert.Equal(written, element.XCode?.Text);

        string text = Do(ScreenCodeEdits.Move(element, document.Display, 7, element.Y), Positions);

        Assert.Equal(7, Element(ReadPositions(text), "HOME", label).X);

        // The definition is left alone: other things may use it.
        Assert.Contains("#define MARGIN 2", text, StringComparison.Ordinal);
        Assert.Contains("const uint8_t VALUE_COLUMN = 12;", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_variable_something_else_uses_stays()
    {
        ScreenDocument document = ReadPositions();

        string text = Do(ScreenCodeEdits.Move(Element(document, "OTHER", "A"), document.Display, 8, 0), Positions);

        Assert.Contains("lcd.setCursor(8, 0); lcd.print(\"A\");", text, StringComparison.Ordinal);
        Assert.Contains("uint8_t shared = 3;", text, StringComparison.Ordinal);
        Assert.Contains("lcd.setCursor(shared, 1);", text, StringComparison.Ordinal);
    }

    [Fact]
    public void What_changes_while_it_runs_is_not_frozen_by_a_drag_but_typing_replaces_it()
    {
        ScreenDocument document = ReadPositions();
        ScreenElement element = Element(document, "OTHER", "C");

        Assert.Equal("cursorColumn", element.XCode?.Text);
        Assert.True(element.XCode?.IsLocked);
        Assert.Null(element.XSpan);
        Assert.Null(element.CursorInsert);

        // Its row is a plain number and moves as ever; the column stays what the code says.
        string down = Do(ScreenCodeEdits.Move(element, document.Display, element.X, 3), Positions);
        Assert.Contains("lcd.setCursor(cursorColumn, 3);", down, StringComparison.Ordinal);

        string typed = Do(ScreenCodeEdits.SetExpression(element.XCode!, "cursorColumn + 1"), Positions);
        Assert.Contains("lcd.setCursor(cursorColumn + 1, 2);", typed, StringComparison.Ordinal);
    }

    [Fact]
    public void A_loops_variable_cannot_be_dragged()
    {
        ScreenElement element = Element(ReadPositions(), "OTHER", "i");

        Assert.Equal("i", element.XCode?.Text);
        Assert.True(element.XCode?.IsLocked);
        Assert.Null(element.XSpan);
        Assert.Null(element.CursorInsert);
    }

    [Fact]
    public void Text_printed_after_other_text_gets_a_cursor_of_its_own()
    {
        ScreenDocument document = ReadPositions();
        ScreenElement value = Element(document, "HOME", "millis()");
        Assert.Null(value.XSpan);
        Assert.NotNull(value.CursorInsert);

        string text = Do(ScreenCodeEdits.Move(value, document.Display, 10, 2), Positions);

        Assert.Contains("  lcd.print(\"Temp \");\n  lcd.setCursor(10, 2);\n  lcd.print(millis());", text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        ScreenDocument after = ReadPositions(text);
        Assert.Equal((10, 2), (Element(after, "HOME", "millis()").X, Element(after, "HOME", "millis()").Y));

        // What follows it goes along, as it does on the display.
        Assert.Equal(2, Element(after, "HOME", " C").Y);
        Assert.Equal(0, Element(after, "HOME", "Temp ").Y);
    }

    [Fact]
    public void Moving_text_after_a_shared_helper_leaves_the_helper_and_other_screens_alone()
    {
        ScreenDocument document = ReadPositions();
        ScreenElement third = Element(document, "THIRD", "Third");
        Assert.Null(third.XSpan);
        Assert.NotNull(third.CursorInsert);

        string text = Do(ScreenCodeEdits.Move(third, document.Display, 4, third.Y), Positions);

        Assert.Contains("void clearLine(uint8_t row) {\n  lcd.setCursor(0, row);", text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("clearLine(0); lcd.setCursor(4, 0); lcd.print(\"Third\");", text, StringComparison.Ordinal);
        Assert.Equal(0, Element(ReadPositions(text), "THIRD", "More").X);

        // Its row is the argument at its own call, which clears that row too.
        string down = Do(ScreenCodeEdits.Move(third, document.Display, third.X, 2), Positions);
        Assert.Contains("clearLine(2); lcd.print(\"Third\");", down, StringComparison.Ordinal);
    }

    [Fact]
    public void What_a_shared_helper_prints_first_takes_its_cursor_before_the_call_to_it()
    {
        const string Date = """
            #include <LiquidCrystal_I2C.h>
            LiquidCrystal_I2C lcd(0x27, 20, 4);
            int day = 1;
            int month = 2;
            void print2(int v) {
              if (v < 10) lcd.print('0');
              lcd.print(v);
            }
            void title(const char* text) {
              lcd.setCursor(0, 0);
              lcd.print(text);
            }
            void showDate() {
              title("Date");
              print2(day);
              lcd.print('.');
              print2(month);
            }
            void showOther() {
              title("Other");
            }
            void loop() { showDate(); showOther(); }
            """;
        ScreenDocument document = ScreenCodeAnalyzer.Analyze([("a.ino", Date)])!;
        ScreenNode screen = document.Find("showDate")!;
        ScreenElement value = screen.Elements.First(e => e.Text == "month");
        Assert.NotNull(value.CursorInsert);

        string text = Do(ScreenCodeEdits.Move(value, document.Display, 9, 2), Date);

        Assert.Contains("  lcd.print('.');\n  lcd.setCursor(9, 2);\n  print2(month);", text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("void print2(int v) {\n  if (v < 10) lcd.print('0');\n  lcd.print(v);\n}", text.ReplaceLineEndings("\n"), StringComparison.Ordinal);

        // A helper that sets the cursor itself would undo one set before the call: it stays as it is.
        ScreenElement titled = screen.Elements.First(e => e.Text == "Date");
        Assert.Null(titled.CursorInsert);
        Assert.Null(titled.XSpan);
    }

    [Fact]
    public void A_shape_with_a_variable_size_is_there_to_be_found()
    {
        const string Oled = """
            #include <Adafruit_SSD1306.h>
            Adafruit_SSD1306 oled(128, 64, &Wire, -1);
            int percent = 0;
            void showBar() {
              oled.drawRect(0, 10, 100, 8, SSD1306_WHITE);
              oled.fillRect(1, 11, percent, 6, SSD1306_WHITE);
              oled.display();
            }
            void loop() { showBar(); }
            """;

        ScreenElement[] elements = [.. ScreenCodeAnalyzer.Analyze([("a.ino", Oled)])!.Screens.Single().Elements];

        Assert.Equal(2, elements.Length);
        ScreenElement bar = elements[1];
        Assert.Equal(ElementKind.Box, bar.Kind);
        Assert.False(bar.IsPlaced);
        Assert.Equal("percent", bar.WidthCode?.Text);
        Assert.True(bar.WidthCode?.IsLocked);
        Assert.Null(bar.WidthSpan);
        Assert.Equal(1, bar.X);
        Assert.NotNull(bar.HeightSpan);
    }

    [Fact]
    public void A_literal_inside_a_helper_called_once_is_still_edited_there()
    {
        const string Once = """
            #include <LiquidCrystal_I2C.h>
            LiquidCrystal_I2C lcd(0x27, 16, 2);
            void title() { lcd.setCursor(3, 0); lcd.print("T"); }
            void showIt() { title(); }
            void loop() { showIt(); }
            """;
        ScreenDocument document = ScreenCodeAnalyzer.Analyze([("a.ino", Once)])!;
        ScreenElement element = document.Find("showIt")!.Elements.Single();

        string text = Do(ScreenCodeEdits.Move(element, document.Display, 6, 0), Once);

        Assert.Contains("lcd.setCursor(6, 0);", text, StringComparison.Ordinal);
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
