using ArduSim.Core.Screens;

namespace ArduSim.Core.Tests;

// What the Screens tab works out by running a sketch's own small functions on what is known (ADR-019).
public sealed class SketchRunTests
{
    // One function draws every list, and asks others for its heading, its length and its items.
    private const string Kiosk = """
        #include <LiquidCrystal_I2C.h>
        #define HAS_TEA 1
        #define HAS_COCOA 0
        #define BUILD_NAME "Kiosk 2.0"

        LiquidCrystal_I2C lcd(0x27, 20, 4);

        enum Page : uint8_t { IDLE, ROOT, SIZES, HOT_SHELF, COLD_SHELF, INFO };
        enum Shelf : uint8_t { SHELF_HOT, SHELF_COLD };
        enum Cup : uint8_t { CUP_SMALL, CUP_LARGE, CUP_KINDS };
        enum Layout : uint8_t { LAYOUT_PLAIN, LAYOUT_WIDE };

        static const char ROOT_ITEMS[] PROGMEM = "DRINKS\0" "SIZES\0" "SERVICE";
        static const uint8_t ROOT_TOTAL = 3;
        static const char SIZE_ITEMS[] PROGMEM = "SMALL\0" "LARGE"
        #if HAS_COCOA
          "\0BUCKET"
        #endif
          ;
        static const char RETURN_ITEM[] PROGMEM = "RETURN";

        struct Drink {
          void *maker;
          uint8_t shelf;
          char label[16];
        };

        static const Drink DRINK_TABLE[] PROGMEM = {
          {nullptr, SHELF_HOT, "COFFEE"},
        #if HAS_TEA
          {nullptr, SHELF_HOT, "TEA"},
        #endif
        #if HAS_COCOA
          {nullptr, SHELF_HOT, "COCOA"},
        #endif
          {nullptr, SHELF_COLD, "WATER"},
        };
        static const uint8_t DRINK_TOTAL = sizeof(DRINK_TABLE) / sizeof(DRINK_TABLE[0]);

        Page page = IDLE;
        Layout layout = LAYOUT_PLAIN;
        uint8_t cursorAt = 0, firstShown = 0;

        uint8_t digitsOf(uint32_t value, uint8_t base = 10);

        uint8_t digitsOf(uint32_t value, uint8_t base) {
          uint8_t n = 1;
          while (value >= base) { value /= base; ++n; }
          return n;
        }

        static const char *nth(const char *list, uint8_t n) {
          while (n--) list += strlen_P(list) + 1;
          return list;
        }

        uint8_t drinksOn(uint8_t shelf) {
          uint8_t n = 0;
          for (uint8_t i = 0; i < DRINK_TOTAL; ++i) {
            if (pgm_read_byte(&DRINK_TABLE[i].shelf) == shelf) ++n;
          }
          return n;
        }

        const Drink *drinkAt(uint8_t shelf, uint8_t place) {
          for (uint8_t i = 0; i < DRINK_TOTAL; ++i) {
            if (pgm_read_byte(&DRINK_TABLE[i].shelf) == shelf && place-- == 0) return &DRINK_TABLE[i];
          }
          return 0;
        }

        uint8_t listLength() {
          switch (page) {
            case ROOT: return ROOT_TOTAL;
            case SIZES: return CUP_KINDS + 1;
            default: return drinksOn((uint8_t)(page - HOT_SHELF)) + 1;
          }
        }

        const char *itemAt(uint8_t n) {
          switch (page) {
            case ROOT: return nth(ROOT_ITEMS, n);
            case SIZES:
              if (n < CUP_KINDS) return nth(SIZE_ITEMS, n);
              break;
            default: {
              const uint8_t shelf = (uint8_t)(page - HOT_SHELF);
              if (n < drinksOn(shelf)) return drinkAt(shelf, n)->label;
              break;
            }
          }
          return RETURN_ITEM;
        }

        const __FlashStringHelper *heading() {
          switch (page) {
            case ROOT: return F("MAIN");
            case SIZES: return F("CUP");
            case HOT_SHELF: return F("HOT");
            default: return F("COLD");
          }
        }

        void drawList() {
          const uint8_t total = listLength();
          lcd.setCursor(0, 0);
          lcd.print(heading());
          lcd.setCursor(20 - digitsOf(cursorAt + 1U) - digitsOf(total) - 1, 0);
          lcd.print(cursorAt + 1);
          lcd.print('/');
          lcd.print(total);
          if (cursorAt >= firstShown + 3) firstShown = cursorAt - 2;
          for (uint8_t i = firstShown; i < total && i < firstShown + 3; ++i) {
            lcd.setCursor(0, i - firstShown + 1);
            if (i == cursorAt) lcd.print('>'); else lcd.print(' ');
            lcd.print(reinterpret_cast<const __FlashStringHelper *>(itemAt(i)));
          }
        }

        void drawIdle() {
          switch (layout) {
            case LAYOUT_WIDE:
              lcd.setCursor(0, 1);
              lcd.print(F("WIDE"));
              break;
            default:
              lcd.setCursor(0, 0);
              lcd.print(F("PLAIN"));
          }
          lcd.setCursor(0, 3);
          lcd.print(F("OK"));
        }

        void drawInfo() {
          lcd.setCursor(0, 0);
          lcd.print(F(BUILD_NAME));
        }

        void draw() {
          lcd.clear();
          switch (page) {
            case IDLE: drawIdle(); break;
            case INFO: drawInfo(); break;
            default: drawList();
          }
        }

        void handleKey(char key) {
          switch (page) {
            case IDLE: if (key == '#') page = ROOT; break;
            case ROOT: if (key == '*') page = IDLE; if (key == '1') page = SIZES; if (key == '2') page = HOT_SHELF; if (key == '3') page = COLD_SHELF; if (key == '4') page = INFO; break;
            default: if (key == '*') page = ROOT;
          }
        }

        void loop() { draw(); }
        """;

    private static ScreenDocument ReadKiosk() => ScreenCodeAnalyzer.Analyze([("a.ino", Kiosk)])!;

    private static string[] Items(ScreenNode screen) => [.. screen.Menu!.Items.Select(i => i.Text)];

    private static string Row(ScreenNode screen, int y) =>
        string.Concat(screen.Elements.Where(e => e.Y == y && !e.IsAlternative).OrderBy(e => e.X).Select(e => e.Kind == ElementKind.Value ? e.Sample : e.Text));

    [Fact]
    public void Every_list_has_its_own_heading_items_and_counter()
    {
        ScreenDocument document = ReadKiosk();

        ScreenNode root = document.Find("ROOT")!;
        Assert.Equal(["DRINKS", "SIZES", "SERVICE"], Items(root));
        Assert.Equal("MAIN1/3", Row(root, 0));

        ScreenNode sizes = document.Find("SIZES")!;
        Assert.Equal(["SMALL", "LARGE", "RETURN"], Items(sizes));
        Assert.Equal("CUP1/3", Row(sizes, 0));
    }

    [Fact]
    public void A_table_gives_each_list_its_own_rows_and_leaves_out_what_is_not_built()
    {
        ScreenDocument document = ReadKiosk();

        ScreenNode hot = document.Find("HOT_SHELF")!;
        Assert.Equal(["COFFEE", "TEA", "RETURN"], Items(hot));
        Assert.Equal("HOT1/3", Row(hot, 0));

        ScreenNode cold = document.Find("COLD_SHELF")!;
        Assert.Equal(["WATER", "RETURN"], Items(cold));
        Assert.Equal("COLD1/2", Row(cold, 0));
    }

    [Fact]
    public void What_a_function_works_out_places_the_counter_at_the_right_edge()
    {
        ScreenNode root = ReadKiosk().Find("ROOT")!;

        ScreenElement one = root.Elements.Single(e => e.Sample == "1");
        Assert.Equal(17, one.X);
        Assert.Equal(19, root.Elements.Single(e => e.Sample == "3").X);
        Assert.Equal(1, root.Menu!.Top);
    }

    [Fact]
    public void Nothing_of_a_list_is_left_as_noise()
    {
        foreach (string id in new[] { "ROOT", "SIZES", "HOT_SHELF", "COLD_SHELF" })
        {
            ScreenNode list = ReadKiosk().Find(id)!;

            Assert.DoesNotContain(list.Elements, e => e.Text.Contains("itemAt", StringComparison.Ordinal) || e.Text.Contains("heading", StringComparison.Ordinal));
            Assert.All(list.Elements, e => Assert.True(e.IsPlaced, e.Text));
        }
    }

    [Fact]
    public void A_heading_of_this_screen_alone_can_be_edited_and_one_for_whatever_is_left_cannot()
    {
        ScreenDocument document = ReadKiosk();

        ScreenElement main = document.Find("ROOT")!.Elements.Single(e => e.Text == "MAIN");
        Assert.NotNull(main.TextSpan);
        Assert.Equal("\"MAIN\"", main.TextSpan!.Expected);

        Assert.Null(document.Find("COLD_SHELF")!.Elements.Single(e => e.Text == "COLD").TextSpan);
    }

    [Fact]
    public void Editing_a_heading_changes_that_screen_and_no_other()
    {
        ScreenDocument document = ReadKiosk();
        ScreenElement main = document.Find("ROOT")!.Elements.Single(e => e.Text == "MAIN");

        string text = ScreenCodeEdits.Apply(Kiosk, [ScreenCodeEdits.SetText(main.TextSpan, "TOP")!]);
        ScreenDocument again = ScreenCodeAnalyzer.Analyze([("a.ino", text)])!;

        Assert.Contains("case ROOT: return F(\"TOP\");", text, StringComparison.Ordinal);
        Assert.Equal("TOP1/3", Row(again.Find("ROOT")!, 0));
        Assert.Equal("CUP1/3", Row(again.Find("SIZES")!, 0));
    }

    [Fact]
    public void Nothing_is_added_to_code_that_other_screens_draw_too()
    {
        ScreenDocument document = ReadKiosk();

        Assert.Null(document.Find("ROOT")!.DrawInsert);
        Assert.Null(document.Find("COLD_SHELF")!.DrawInsert);
        Assert.NotNull(document.Find("INFO")!.DrawInsert);
    }

    [Fact]
    public void Items_worked_out_by_a_function_are_not_edited_from_the_tab()
    {
        ScreenNode root = ReadKiosk().Find("ROOT")!;

        Assert.All(root.Menu!.Items, i => Assert.Null(i.TextSpan));
    }

    [Fact]
    public void A_text_from_a_define_is_shown_and_not_edited()
    {
        ScreenElement name = Assert.Single(ReadKiosk().Find("INFO")!.Elements);

        Assert.Equal("Kiosk 2.0", name.Text);
        Assert.Null(name.TextSpan);
    }

    [Fact]
    public void A_switch_on_something_else_shows_one_of_its_cases()
    {
        ScreenNode idle = ReadKiosk().Find("IDLE")!;

        Assert.False(idle.Elements.Single(e => e.Text == "PLAIN").IsAlternative);
        Assert.True(idle.Elements.Single(e => e.Text == "WIDE").IsAlternative);
        Assert.False(idle.Elements.Single(e => e.Text == "OK").IsAlternative);
    }

    private static ScreenElement Printed(string body, string more = "")
    {
        string code = $$"""
            #include <LiquidCrystal.h>
            LiquidCrystal lcd(7, 8, 9, 10, 11, 12);
            enum Mode { STILL, FAST = 10, FASTER };
            static const int LIMIT = 4;
            struct Row { int a; const char *name; };
            static const Row ROWS[] = { {1, "one"}, {2, "two"}, {3, "three"} };
            static const char *const NAMES[] = { "red", "green" };
            {{more}}
            int answer() { {{body}} }
            void showIt() { lcd.print(answer()); }
            void loop() { showIt(); }
            """;

        return ScreenCodeAnalyzer.Analyze([("a.ino", code)])!.Screens.Single().Elements.Single();
    }

    [Theory]
    [InlineData("return 2 + 3 * 4;", "14")]
    [InlineData("return (2 + 3) * 4 % 7;", "6")]
    [InlineData("return 7 > 3 && 2 > 1 ? 1 : 0;", "1")]
    [InlineData("return 0 && noSuch() ? 1 : 2;", "2")]
    [InlineData("int s = 0; for (int i = 0; i < 4; ++i) s += i; return s;", "6")]
    [InlineData("int s = 0; for (int i = 0; i < 9; i++) { if (i == 2) continue; if (i == 4) break; s += i; } return s;", "4")]
    [InlineData("int i = 0; while (i < 5) { i++; } return i;", "5")]
    [InlineData("int a = 3, b = 4; a *= b; b -= 1; return a + b;", "15")]
    [InlineData("switch (2) { case 1: return 10; case 2: case 3: return 20; default: return 30; }", "20")]
    [InlineData("switch (9) { case 1: return 10; default: return 30; }", "30")]
    [InlineData("int n = 0; switch (1) { case 1: n += 1; case 2: n += 2; break; case 3: n += 4; } return n;", "3")]
    [InlineData("return FASTER + LIMIT;", "15")]
    [InlineData("return sizeof(ROWS) / sizeof(ROWS[0]);", "3")]
    [InlineData("return ROWS[1].a;", "2")]
    [InlineData("const Row *r = &ROWS[2]; return r->a;", "3")]
    [InlineData("return strlen(ROWS[2].name);", "5")]
    [InlineData("return strlen(NAMES[1]);", "5")]
    [InlineData("return min(3, max(1, 2));", "2")]
    [InlineData("return 0x10 + 0b11 + 'A';", "84")]
    public void A_function_runs_on_what_is_known(string body, string expected)
    {
        Assert.Equal(expected, Printed(body).Sample);
    }

    [Theory]
    [InlineData("return analogRead(A0) / 4;")]
    [InlineData("return noSuch;")]
    [InlineData("if (digitalRead(2)) return 1; return 2;")]
    [InlineData("while (true) { } return 1;")]
    [InlineData("do { } while (true); return 1;")]
    [InlineData("return answer();")]
    [InlineData("return 1 / 0;")]
    public void What_cannot_be_told_stays_as_written(string body)
    {
        ScreenElement value = Printed(body);

        Assert.Equal(ElementKind.Value, value.Kind);
        Assert.Equal("answer()", value.Text);
        Assert.Null(value.Sample);
    }

    [Fact]
    public void A_value_from_a_function_keeps_its_place_in_the_code_and_shows_what_it_comes_to()
    {
        ScreenElement value = Printed("return 40 + 2;");

        Assert.Equal(ElementKind.Value, value.Kind);
        Assert.Equal("answer()", value.Text);
        Assert.Equal(2, value.Width);
        Assert.Equal("42", value.Sample);
    }

    [Fact]
    public void A_build_flag_the_sketch_does_not_set_leaves_both_sides_of_its_if()
    {
        const string Code = """
            #include <LiquidCrystal.h>
            LiquidCrystal lcd(7, 8, 9, 10, 11, 12);
            struct Row { int a; };
            static const Row ROWS[] = {
              {1},
            #ifdef SOME_FLAG
              {2},
            #else
              {3},
            #endif
            #if defined(OTHER_FLAG)
              {4},
            #endif
            #if UNKNOWN_FLAG
              {5},
            #else
              {6},
            #endif
            };
            int answer() { return sizeof(ROWS) / sizeof(ROWS[0]); }
            void showIt() { lcd.print(answer()); }
            void loop() { showIt(); }
            """;

        ScreenElement value = ScreenCodeAnalyzer.Analyze([("a.ino", Code)])!.Screens.Single().Elements.Single();

        Assert.Equal("6", value.Sample);
    }
}
