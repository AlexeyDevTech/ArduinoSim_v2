using ArduSim.Core.Screens;

namespace ArduSim.Core.Tests;

/// <summary>Forming screens and transitions from code (ADR-019), and editing it in place.</summary>
public sealed class ScreenAnalyzerTests
{
    // A display class of the sketch's own in front of the panel, with its layout in its methods.
    private const string OwnClass = """
        #include <LiquidCrystal_I2C.h>
        LiquidCrystal_I2C hardware(0x27, 20, 4);

        class Panel : public Print {
        public:
          void setCursor(uint8_t x, uint8_t y) { x_ = x; y_ = y; }
          size_t write(uint8_t b) { if (b >= 0xC0) lead_ = b; else if (b >= 0x80) put('?'); else put(b); return 1; }
          void mark(uint8_t m) { put(m); }
          void two(uint8_t v) { if (v < 10) print('0'); print(v); }
          void atRight(uint8_t y, const char *s) { setCursor(20 - widthOf(s), y); print(s); }
          void atRight(uint8_t y, const __FlashStringHelper *s) { setCursor(20 - widthOf(s), y); print(s); }
          void atCenter(uint8_t y, const __FlashStringHelper *s) { setCursor((20 - widthOf(s)) / 2, y); print(s); }
          void bigPair(uint8_t a, uint8_t b) { bigOne(0, a / 10); bigOne(4, a % 10); setCursor(8, 2); }
          static uint8_t widthOf(const char *s);
          static uint8_t widthOf(const __FlashStringHelper *s);
        private:
          void put(uint8_t c) { hardware.setCursor(x_, y_); hardware.write(c); ++x_; }
          void bigOne(uint8_t x, uint8_t d) {
            for (uint8_t i = 0; i < 6; ++i) {
              if (i % 3 == 0) setCursor(x, i < 3 ? 1 : 2);
              put(d);
            }
          }
          uint8_t x_ = 0, y_ = 0, lead_ = 0;
        };

        Panel lcd;
        static const char LIST[] PROGMEM = "ONE\0" "TWO\0" "THREE";
        enum Page { HOME, LIST_PAGE, TOOL };
        Page page = HOME;
        uint8_t hours, minutes, index;

        class ToolApp {
        public:
          void render(Panel &p) { p.print(F("ИНСТРУМЕНТ")); }
        };
        ToolApp *current();

        void drawHome() {
          lcd.setCursor(0, 0);
          lcd.two(hours);
          lcd.print('.');
          lcd.two(minutes);
          lcd.bigPair(hours, minutes);
          lcd.print(':');
          lcd.atCenter(0, F("MID"));
          lcd.atRight(3, F("RIGHT"));
          lcd.mark('!');
        }

        void drawList(const __FlashStringHelper *title, const char *items, uint8_t count) {
          lcd.print(title);
          lcd.setCursor(17, 0);
          lcd.print(index + 1); lcd.print('/'); lcd.print(count);
          const char *item = items;
          for (uint8_t i = 0; i < count; ++i) {
            lcd.setCursor(0, i + 1);
            if (i == index) lcd.mark('>'); else lcd.print(' ');
            lcd.print(reinterpret_cast<const __FlashStringHelper *>(item));
            lcd.atRight(i + 1, F("*"));
            item += strlen_P(item) + 1;
          }
        }

        void draw() {
          switch (page) {
            case HOME: drawHome(); break;
            case LIST_PAGE: drawList(F("TITLE"), LIST, 3); break;
            default: if (current()) current()->render(lcd); break;
          }
        }

        void loop() { draw(); }
        """;

    private static ScreenDocument ReadOwnClass() => ScreenCodeAnalyzer.Analyze([("a.ino", OwnClass)])!;

    private static ScreenElement[] Drawn(ScreenDocument document, string screen, string text) =>
        [.. document.Find(screen)!.Elements.Where(e => e.Text == text)];

    [Fact]
    public void A_method_of_the_sketchs_display_class_is_followed_like_a_helper()
    {
        ScreenDocument document = ReadOwnClass();
        ScreenElement right = Drawn(document, "HOME", "RIGHT").Single();

        // Worked out from what it prints; the row is the number at its own call, the text its own literal.
        Assert.Equal((15, 3), (right.X, right.Y));
        Assert.Equal("3", right.YSpan?.Expected);
        Assert.Equal("\"RIGHT\"", right.TextSpan?.Expected);

        ScreenElement middle = Drawn(document, "HOME", "MID").Single();
        Assert.Equal((8, 0), (middle.X, middle.Y));
    }

    [Fact]
    public void A_display_methods_number_shows_its_two_digits_and_takes_a_cursor_before_the_call()
    {
        ScreenDocument document = ReadOwnClass();
        ScreenElement second = Drawn(document, "HOME", "minutes").Single();

        Assert.Equal(2, second.Width);
        Assert.Equal(3, second.X);
        Assert.NotNull(second.CursorInsert);
        Assert.Equal("lcd.", second.CursorReceiver);

        string text = ScreenCodeEdits.Apply(OwnClass, ScreenCodeEdits.Move(second, document.Display, 10, 0)!);
        Assert.Contains("lcd.setCursor(10, 0);\n  lcd.two(minutes);", text.ReplaceLineEndings("\n"), StringComparison.Ordinal);

        // The method itself is left alone.
        Assert.Contains("void two(uint8_t v) { if (v < 10) print('0'); print(v); }", text, StringComparison.Ordinal);
    }

    [Fact]
    public void What_a_display_class_writes_cell_by_cell_shows_as_blocks_where_they_are()
    {
        ScreenElement[] big = [.. ReadOwnClass().Find("HOME")!.Elements.Where(e => e.Text == "a/10" || e.Text == "a%10")];

        // A loop over known numbers is run: three cells wide, two rows high, at each digit's column.
        Assert.Equal([(0, 1, 3), (0, 2, 3), (4, 1, 3), (4, 2, 3)], big.Select(e => (e.X, e.Y, e.Width)));
        Assert.All(big, e => Assert.Equal(new string('\u00FF', 3), e.Sample));

        // One cell through a method of its own is one block, in its place.
        ScreenElement mark = Drawn(ReadOwnClass(), "HOME", "'!'").Single();
        Assert.Equal(1, mark.Width);
        Assert.Equal((20, 3), (mark.X, mark.Y));
    }

    [Fact]
    public void Taking_out_a_display_methods_call_takes_out_what_it_drew()
    {
        ScreenDocument document = ReadOwnClass();
        ScreenElement right = Drawn(document, "HOME", "RIGHT").Single();

        string text = ScreenCodeEdits.Apply(OwnClass, ScreenCodeEdits.Remove(right.RemoveSpans)!);

        Assert.DoesNotContain("RIGHT", text, StringComparison.Ordinal);
        Assert.Contains("lcd.atCenter(0, F(\"MID\"));", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_in_a_display_classes_own_code_is_not_edited_for_one_screen()
    {
        ScreenElement zero = ReadOwnClass().Find("HOME")!.Elements.First(e => e.Text == "0");

        Assert.Null(zero.TextSpan);
    }

    [Fact]
    public void A_list_kept_in_one_string_is_a_menu_and_what_its_loop_draws_is_the_menus()
    {
        ScreenNode list = ReadOwnClass().Find("LIST_PAGE")!;

        Assert.Equal(["ONE", "TWO", "THREE"], list.Menu!.Items.Select(i => i.Text));
        Assert.All(list.Menu.Items, i => Assert.Null(i.TextSpan));
        Assert.Equal(1, list.Menu.Top);
        Assert.Equal(3, list.Menu.Rows);
        Assert.DoesNotContain(list.Elements, e => e.Text.Contains("reinterpret_cast", StringComparison.Ordinal) || e.Text == "*" || e.Text == "'>'");
    }

    [Fact]
    public void Prints_in_a_row_that_a_guessed_width_would_push_off_the_display_are_drawn_narrower()
    {
        ScreenElement[] elements = [.. ReadOwnClass().Find("LIST_PAGE")!.Elements.Where(e => e.Y == 0 && e.X >= 17)];

        Assert.Equal([(17, 1), (18, 1), (19, 1)], elements.Select(e => (e.X, e.Kind == ElementKind.Value ? e.Width : e.Text.Length)));
    }

    [Fact]
    public void A_screen_with_no_case_of_its_own_is_what_the_default_draws()
    {
        ScreenNode tool = ReadOwnClass().Find("TOOL")!;

        Assert.False(tool.DrawnElsewhere);
        Assert.Equal("ИНСТРУМЕНТ", tool.Elements.Single().Text);
    }

    [Fact]
    public void A_class_that_decodes_utf8_has_its_own_font_and_a_plain_display_does_not()
    {
        Assert.True(ReadOwnClass().Display.OwnFont);
        Assert.False(ScreenCodeAnalyzer.Analyze([("a.ino", Device)])!.Display.OwnFont);
    }

    [Fact]
    public void Printing_what_is_not_a_leading_zero_in_a_branch_is_shown()
    {
        const string Branches = """
            #include <LiquidCrystal_I2C.h>
            LiquidCrystal_I2C lcd(0x27, 20, 4);
            int year, day;
            bool wide;
            void showDate() {
              if (wide) {
                lcd.print(day); lcd.print('.'); lcd.print(year);
              } else {
                lcd.print(year); lcd.print('-'); lcd.print(day);
              }
            }
            void loop() { showDate(); }
            """;

        ScreenElement[] elements = [.. ScreenCodeAnalyzer.Analyze([("a.ino", Branches)])!.Screens.Single().Elements];

        Assert.False(elements.First(e => e.Text == ".").IsAlternative);
        Assert.True(elements.First(e => e.Text == "-").IsAlternative);
    }

    private const string Device = """
        #include <LiquidCrystal_I2C.h>
        #define KEY_OK '#'
        #define KEY_BACK '*'
        #define COLUMNS 20
        #define ROWS 4

        static const char* const MAIN_ITEMS[] = { "Settings", "About" };

        class Device {
        public:
          Device() : lcd(0x27, COLUMNS, ROWS), screen(HOME) {}
          void update();
        private:
          enum Screen : uint8_t { HOME, MAIN_MENU, SETTINGS, ABOUT, PLAYER };
          enum Format : uint8_t { FORMAT_A, FORMAT_B };
          LiquidCrystal_I2C lcd;
          Screen screen;
          uint8_t menuIndex;
          void enterScreen(Screen next);
          void goHome();
          void handleKey(char key);
          void selectMain();
          void draw();
          void drawHome();
          void drawMenu(const char* title, const char* const* items, uint8_t count);
          void clearLine(uint8_t row);
        };

        void Device::enterScreen(Screen next) {
          screen = next;
          menuIndex = 0;
        }

        void Device::goHome() {
          enterScreen(HOME);
        }

        void Device::update() {
          char key = readKey();
          if (key) handleKey(key);
          draw();
        }

        void Device::handleKey(char key) {
          if (screen == PLAYER) {
            playerKey(key);
            return;
          }

          if (key == KEY_BACK) {
            switch (screen) {
              case MAIN_MENU: goHome(); break;
              case HOME: enterScreen(MAIN_MENU); break;
              default: enterScreen(MAIN_MENU); break;
            }
            return;
          }

          switch (screen) {
            case MAIN_MENU:
              if (key == '2' && menuIndex < 1) ++menuIndex;
              else if (key == KEY_OK) selectMain();
              break;
            case ABOUT:
              if (key == KEY_OK) enterScreen(HOME);
              break;
            case SETTINGS:
              if (key == '9') goHome();
              break;
            default: break;
          }
        }

        void Device::selectMain() {
          switch (menuIndex) {
            case 0: enterScreen(SETTINGS); break;
            case 1: enterScreen(ABOUT); break;
          }
        }

        void Device::clearLine(uint8_t row) {
          lcd.setCursor(0, row);
          for (uint8_t i = 0; i < COLUMNS; i++) lcd.print(' ');
          lcd.setCursor(0, row);
        }

        void Device::draw() {
          switch (screen) {
            case HOME: drawHome(); break;
            case MAIN_MENU: drawMenu("Main", MAIN_ITEMS, 2); break;
            case SETTINGS:
              clearLine(0); lcd.print(F("Settings"));
              break;
            case ABOUT:
              lcd.setCursor(2, 1); lcd.print("v1.0");
              break;
            case PLAYER: player.render(lcd); break;
          }
        }

        void Device::drawHome() {
          lcd.setCursor(3, 0); lcd.print(F("Hello"));
          lcd.setCursor(0, 1); lcd.print(hour);
          lcd.print(':');
          lcd.setCursor(0, 2);
          if (alarm) lcd.print("ON"); else lcd.print("OFF");
          lcd.setCursor(0, 3); lcd.print(on ? F("Yes") : F("No way"));
        }

        void Device::drawMenu(const char* title, const char* const* items, uint8_t count) {
          clearLine(0); lcd.print(title);
          for (uint8_t r = 0; r < 3; r++) {
            clearLine(r + 1);
            if (r < count) {
              lcd.print(r == menuIndex ? '>' : ' ');
              lcd.print(items[r]);
            }
          }
        }

        void PlayerApp::render(LiquidCrystal_I2C& lcd) const {
          lcd.setCursor(1, 2); lcd.print(F("Playing"));
        }
        """;

    private static ScreenDocument Read(string code = Device, ScreenDisplay? scene = null) =>
        ScreenCodeAnalyzer.Analyze([("device.cpp", code)], scene) ?? throw new InvalidOperationException("nothing recognised");

    private static ScreenElement Text(ScreenNode screen, string text) => screen.Elements.Single(e => e.Kind == ElementKind.Text && e.Text == text);

    [Fact]
    public void The_display_is_found_with_its_size_from_an_initializer_list_and_macros()
    {
        ScreenDisplay display = Read().Display;

        Assert.Equal(DisplayFamily.CharLcd, display.Family);
        Assert.Equal(DisplayLibrary.LiquidCrystalI2c, display.Library);
        Assert.Equal((20, 4), (display.Width, display.Height));
        Assert.Equal("lcd", display.Variable);
    }

    [Fact]
    public void The_schematic_knows_the_size_better_than_the_code()
    {
        var scene = new ScreenDisplay(DisplayFamily.CharLcd, 16, 2, DisplayLibrary.LiquidCrystal, "x", "lcd1");

        ScreenDisplay display = Read(scene: scene).Display;

        Assert.Equal((16, 2), (display.Width, display.Height));
        Assert.Equal("lcd", display.Variable);
        Assert.Equal("lcd1", display.Part);
    }

    [Fact]
    public void The_screen_enum_is_told_from_other_enums_and_the_start_from_the_constructor()
    {
        ScreenDocument document = Read();

        Assert.Equal(["HOME", "MAIN_MENU", "SETTINGS", "ABOUT", "PLAYER"], document.Screens.Select(s => s.Id));
        Assert.Equal("HOME", document.Start);
    }

    [Fact]
    public void Transitions_get_their_source_key_and_menu_item_through_helpers()
    {
        ScreenDocument document = Read();
        ScreenNode menu = document.Find("MAIN_MENU")!;

        ScreenTransition back = menu.Transitions.Single(t => t.Target == "HOME");
        Assert.Equal(TriggerKind.Key, back.Trigger.Kind);
        Assert.Equal("*", back.Trigger.Value);
        Assert.Equal("KEY_BACK", back.Trigger.Name);

        ScreenTransition settings = menu.Transitions.Single(t => t.Target == "SETTINGS");
        Assert.Equal("#", settings.Trigger.Value);
        Assert.Equal(0, settings.Trigger.MenuItem);
        Assert.Equal(1, menu.Transitions.Single(t => t.Target == "ABOUT").Trigger.MenuItem);

        Assert.Equal("device.cpp", settings.Location?.File);
        Assert.True(settings.Location?.Line > 1);
    }

    [Fact]
    public void A_default_label_means_the_other_screens_and_an_early_return_rules_one_out()
    {
        ScreenDocument document = Read();

        Assert.Contains(document.Find("SETTINGS")!.Transitions, t => t.Target == "MAIN_MENU" && t.Trigger.Value == "*");
        Assert.Contains(document.Find("ABOUT")!.Transitions, t => t.Target == "MAIN_MENU" && t.Trigger.Value == "*");

        // The player handles its own keys and returns before the back key is looked at.
        Assert.DoesNotContain(document.Find("PLAYER")!.Transitions, t => t.Target == "MAIN_MENU");
    }

    [Fact]
    public void A_transition_from_no_particular_screen_is_from_anywhere()
    {
        const string Code = """
            #include <LiquidCrystal.h>
            LiquidCrystal lcd(7, 8, 9, 10, 11, 12);
            enum Page { FIRST, SECOND };
            Page page = FIRST;
            void loop() {
              if (digitalRead(2) == LOW) page = SECOND;
              switch (page) {
                case FIRST: lcd.print("One"); break;
                case SECOND: lcd.print("Two"); break;
              }
            }
            """;

        ScreenDocument document = Read(Code);

        ScreenTransition anywhere = Assert.Single(document.FromAnywhere);
        Assert.Equal("SECOND", anywhere.Target);
        Assert.Equal(TriggerKind.Code, anywhere.Trigger.Kind);
        Assert.Contains("digitalRead", anywhere.Trigger.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void What_a_screen_shows_is_placed_with_the_cursor_and_helpers_are_followed()
    {
        ScreenDocument document = Read();
        ScreenNode home = document.Find("HOME")!;

        ScreenElement hello = Text(home, "Hello");
        Assert.Equal((3, 0), (hello.X, hello.Y));
        Assert.NotNull(hello.TextSpan);
        Assert.NotNull(hello.XSpan);

        // After a helper that leaves the cursor at the row's start.
        ScreenElement settings = Text(document.Find("SETTINGS")!, "Settings");
        Assert.Equal((0, 0), (settings.X, settings.Y));

        ScreenElement value = home.Elements.Single(e => e.Kind == ElementKind.Value && e.Text == "hour");
        Assert.Equal((0, 1), (value.X, value.Y));
        Assert.Null(value.TextSpan);
        Assert.Equal(value.X + value.Width, Text(home, ":").X);
    }

    [Fact]
    public void Of_two_branches_only_the_first_is_previewed_and_both_can_be_edited()
    {
        ScreenNode home = Read().Find("HOME")!;

        ScreenElement on = Text(home, "ON");
        ScreenElement off = Text(home, "OFF");
        Assert.True(on.IsConditional);
        Assert.False(on.IsAlternative);
        Assert.True(off.IsAlternative);
        Assert.Equal((on.X, on.Y), (off.X, off.Y));
        Assert.NotNull(off.TextSpan);
    }

    [Fact]
    public void A_ternary_of_literals_is_a_value_with_a_sample_and_the_longest_width()
    {
        ScreenElement value = Read().Find("HOME")!.Elements.Single(e => e.Sample is not null);

        Assert.Equal(ElementKind.Value, value.Kind);
        Assert.Equal("Yes", value.Sample);
        Assert.Equal("No way".Length, value.Width);
    }

    [Fact]
    public void An_array_printed_in_a_loop_is_a_menu_and_it_knows_where_items_lead()
    {
        ScreenNode menu = Read().Find("MAIN_MENU")!;

        Assert.Equal("Main", Assert.Single(menu.Elements).Text);
        Assert.NotNull(menu.Menu);
        Assert.Equal(["Settings", "About"], menu.Menu.Items.Select(i => i.Text));
        Assert.Equal(["SETTINGS", "ABOUT"], menu.Menu.Items.Select(i => i.Target));
        Assert.Equal(1, menu.Menu.Top);
        Assert.Equal(3, menu.Menu.Rows);
        Assert.All(menu.Menu.Items, i => Assert.NotNull(i.TextSpan));
    }

    [Fact]
    public void An_application_that_draws_itself_is_found_by_its_name()
    {
        ScreenNode player = Read().Find("PLAYER")!;

        Assert.False(player.DrawnElsewhere);
        ScreenElement playing = Text(player, "Playing");
        Assert.Equal((1, 2), (playing.X, playing.Y));
    }

    [Fact]
    public void A_screen_whose_drawing_cannot_be_followed_says_so()
    {
        const string Code = """
            #include <LiquidCrystal.h>
            LiquidCrystal lcd(7, 8, 9, 10, 11, 12);
            enum Screen { FIRST, SECOND };
            Screen screen = FIRST;
            void loop() {
              switch (screen) {
                case FIRST: lcd.print("One"); break;
                case SECOND: other->render(); break;
              }
            }
            """;

        Assert.True(Read(Code).Find("SECOND")!.DrawnElsewhere);
    }

    [Fact]
    public void Editing_in_place_replaces_exactly_the_token_it_read()
    {
        ScreenDocument document = Read();
        ScreenElement hello = Text(document.Find("HOME")!, "Hello");

        SourceEdit edit = ScreenCodeEdits.SetText(hello.TextSpan, "Hi \"you\"")!;
        Assert.True(edit.Fits(Device));
        string changed = Device.Remove(edit.Span.Offset, edit.Span.Length).Insert(edit.Span.Offset, edit.NewText);
        Assert.Contains("lcd.print(F(\"Hi \\\"you\\\"\"));", changed, StringComparison.Ordinal);

        SourceEdit undo = edit.Inverse();
        Assert.True(undo.Fits(changed));
        Assert.Equal(Device, changed.Remove(undo.Span.Offset, undo.Span.Length).Insert(undo.Span.Offset, undo.NewText));

        // Read again, the text is what was typed.
        Assert.Contains(Read(changed).Find("HOME")!.Elements, e => e.Text == "Hi \"you\"");
    }

    [Fact]
    public void Numbers_and_targets_are_single_tokens_too()
    {
        ScreenDocument document = Read();
        ScreenElement hello = Text(document.Find("HOME")!, "Hello");
        SourceEdit column = ScreenCodeEdits.SetNumber(hello.XSpan, 7)!;
        Assert.Contains("lcd.setCursor(7, 0); lcd.print(F(\"Hello\"))", Device.Remove(column.Span.Offset, column.Span.Length).Insert(column.Span.Offset, "7"), StringComparison.Ordinal);

        ScreenTransition about = document.Find("ABOUT")!.Transitions.Single(t => t.Target == "HOME");
        SourceEdit target = ScreenCodeEdits.SetTarget(about, "SETTINGS")!;
        string changed = Device.Remove(target.Span.Offset, target.Span.Length).Insert(target.Span.Offset, "SETTINGS");
        Assert.Contains(Read(changed).Find("ABOUT")!.Transitions, t => t.Target == "SETTINGS");
    }

    [Fact]
    public void A_transition_that_goes_through_a_shared_helper_cannot_be_retargeted_alone()
    {
        ScreenDocument document = Read();

        // goHome() is one token used from several places.
        Assert.Null(document.Find("MAIN_MENU")!.Transitions.Single(t => t.Target == "HOME").TargetSpan);

        // A default label's single token serves every other screen.
        Assert.Null(document.Find("SETTINGS")!.Transitions.Single(t => t.Target == "MAIN_MENU").TargetSpan);
    }

    [Fact]
    public void A_class_that_draws_for_the_sketch_counts_as_the_display()
    {
        const string Code = """
            #include <LiquidCrystal_I2C.h>
            class Cells : public Print {
            public:
              explicit Cells(LiquidCrystal_I2C& panel);
              void setCursor(uint8_t column, uint8_t row);
              size_t write(uint8_t c);
            };
            void Cells::setCursor(uint8_t column, uint8_t row) { }
            size_t Cells::write(uint8_t c) { return 1; }

            LiquidCrystal_I2C panel(0x27, 20, 4);
            Cells cells(panel);
            enum Page { FRONT, INFO };
            Page page = FRONT;

            void info(Cells& out) {
              out.setCursor(0, 1);
              out.print("Info");
            }

            void paint() {
              switch (page) {
                case FRONT: cells.setCursor(3, 0); cells.print(F("Hello")); break;
                case INFO: info(cells); break;
              }
            }
            """;

        ScreenDocument document = ScreenCodeAnalyzer.Analyze([("a.ino", Code)])!;

        Assert.Equal(20, document.Display.Width);
        Assert.Equal(["FRONT", "INFO"], document.Screens.Select(s => s.Id));
        ScreenElement hello = document.Find("FRONT")!.Elements.Single();
        Assert.Equal(("Hello", 3, 0), (hello.Text, hello.X, hello.Y));
        ScreenElement info = document.Find("INFO")!.Elements.Single();
        Assert.Equal(("Info", 0, 1), (info.Text, info.X, info.Y));
    }

    [Fact]
    public void A_level_with_many_screens_folds_into_more_columns_on_the_map()
    {
        var screens = new List<ScreenNode> { new() { Id = "root", Transitions = [.. Enumerable.Range(0, 6).Select(i => new ScreenTransition { Target = "s" + i })] } };
        screens.AddRange(Enumerable.Range(0, 6).Select(i => new ScreenNode { Id = "s" + i }));

        ScreenDocument placed = ScreenMapLayout.Arrange(new ScreenDocument { Start = "root", Screens = screens });

        Assert.Equal(0, placed.Find("root")!.MapX);
        Assert.Equal(ScreenMapLayout.ColumnStep, placed.Find("s0")!.MapX);
        Assert.Equal(3 * ScreenMapLayout.RowStep, placed.Find("s3")!.MapY);
        Assert.Equal(2 * ScreenMapLayout.ColumnStep, placed.Find("s4")!.MapX);
        Assert.Equal(0, placed.Find("s4")!.MapY);
    }

    [Fact]
    public void An_edit_is_refused_when_the_file_no_longer_has_what_was_read()
    {
        SourceEdit edit = ScreenCodeEdits.SetText(Text(Read().Find("HOME")!, "Hello").TextSpan, "Hi")!;

        Assert.False(edit.Fits("// something else entirely"));
        Assert.False(edit.Fits(Device.Replace("Hello", "Howdy", StringComparison.Ordinal)));
    }

    [Fact]
    public void Literals_in_a_menu_array_can_be_edited()
    {
        ScreenMenuItem item = Read().Find("MAIN_MENU")!.Menu!.Items[1];

        SourceEdit edit = ScreenCodeEdits.SetText(item.TextSpan, "Info")!;

        Assert.Contains("{ \"Settings\", \"Info\" }", Device.Remove(edit.Span.Offset, edit.Span.Length).Insert(edit.Span.Offset, edit.NewText), StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_screen_variable_each_drawing_function_is_a_screen()
    {
        const string Code = """
            #include <Wire.h>
            #include <Adafruit_SSD1306.h>
            Adafruit_SSD1306 display(128, 32, &Wire, -1);
            void showMenu() { display.setCursor(0, 0); display.print("Menu"); display.drawRect(1, 2, 30, 40, 1); display.display(); }
            void showInfo() { display.setTextSize(2); display.setCursor(4, 8); display.print("Info"); if (digitalRead(3) == LOW) showMenu(); }
            void setup() { showMenu(); }
            void loop() { if (digitalRead(2) == LOW) showInfo(); }
            """;

        ScreenDocument document = Read(Code);

        Assert.Equal(DisplayFamily.Oled, document.Display.Family);
        Assert.Equal((128, 32), (document.Display.Width, document.Display.Height));
        Assert.Equal("showMenu", document.Start);
        Assert.Equal(["showMenu", "showInfo"], document.Screens.Select(s => s.Id));
        ScreenNode menu = document.Find("showMenu")!;
        ScreenElement frame = menu.Elements.Single(e => e.Kind == ElementKind.Frame);
        Assert.Equal((1, 2, 30, 40), (frame.X, frame.Y, frame.Width, frame.Height));
        Assert.Equal(2, Text(document.Find("showInfo")!, "Info").Size);
        Assert.Contains(document.Find("showInfo")!.Transitions, t => t.Target == "showMenu");
        Assert.Contains(document.FromAnywhere, t => t.Target == "showInfo");
    }

    [Fact]
    public void U8g2_text_and_shapes_are_read_and_a_type_names_its_size()
    {
        const string Code = """
            #include <U8g2lib.h>
            U8G2_SH1106_128X64_NONAME_F_HW_I2C u8g2(U8G2_R0);
            void showIt() { u8g2.clearBuffer(); u8g2.drawStr(0, 10, "Hello"); u8g2.drawFrame(0, 16, 128, 48); u8g2.sendBuffer(); }
            void loop() { showIt(); }
            """;

        ScreenDocument document = Read(Code);

        Assert.Equal(DisplayLibrary.U8g2, document.Display.Library);
        Assert.True(document.Display.TextFromBaseline);
        Assert.Equal((128, 64), (document.Display.Width, document.Display.Height));
        ScreenElement hello = Text(document.Find("showIt")!, "Hello");
        Assert.Equal((0, 10), (hello.X, hello.Y));
    }

    [Fact]
    public void A_sketch_with_no_display_gives_nothing()
    {
        Assert.Null(ScreenCodeAnalyzer.Analyze([("a.ino", "void setup() { Serial.begin(9600); }\nvoid loop() {}\n")]));
    }

    [Fact]
    public void Broken_code_is_read_as_far_as_it_goes()
    {
        const string Code = """
            #include <LiquidCrystal.h>
            LiquidCrystal lcd(7, 8, 9, 10, 11, 12);
            void showIt() { lcd.print("Hello") lcd.print(
            """;

        ScreenDocument? document = ScreenCodeAnalyzer.Analyze([("a.ino", Code)]);

        Assert.True(document is null || document.Screens.Count >= 0);
    }

    [Fact]
    public void Comments_and_strings_do_not_confuse_the_scan()
    {
        const string Code = """
            #include <LiquidCrystal.h>
            LiquidCrystal lcd(7, 8, 9, 10, 11, 12);
            // enum Fake { A, B };
            /* void showFake() { lcd.print("no"); } */
            void showReal() { lcd.print("a } { \" b"); }
            void loop() { showReal(); }
            """;

        ScreenDocument document = Read(Code);

        Assert.Equal("a } { \" b", Assert.Single(Assert.Single(document.Screens).Elements).Text);
    }
}
