using ArduSim.Core.Screens;
using Xunit;

namespace ArduSim.Core.Tests;

/// <summary>What a generated project's data, symbols and actions are made of (ADR-019).</summary>
public sealed class ScreenDataTests
{
    private static readonly ScreenDisplay Lcd = new(DisplayFamily.CharLcd, 20, 4, DisplayLibrary.LiquidCrystalI2c, "uiDisplay", "lcd1");
    private static readonly ScreenWiring NoHardware = new(0x27, [], [], "ssd1306", []);

    private static ScreenEditor Project()
    {
        var editor = new ScreenEditor(ScreenEditor.Starting(Lcd, "Hello"));
        editor.AddVariable(VariableKind.Number, "count", []);
        editor.AddVariable(VariableKind.Countdown, "pause", []);
        editor.AddVariable(VariableKind.Flag, "sound", ["OFF", "ON"]);
        return editor;
    }

    [Theory]
    [InlineData("count")]
    [InlineData("count + 1")]
    [InlineData("(count + 1) * 2 >= hour")]
    [InlineData("!sound && pause_on")]
    [InlineData("-count")]
    [InlineData("pause_ms / 1000 % 60 != 3")]
    [InlineData("true")]
    [InlineData("")]
    public void What_can_be_read_is_fine(string expression)
    {
        Assert.True(ScreenExpression.Check(Project().Document, expression).IsFine);
    }

    [Theory]
    [InlineData("count +")]
    [InlineData("(count")]
    [InlineData("count)")]
    [InlineData("count count")]
    [InlineData("count; hour")]
    [InlineData("\"text\"")]
    [InlineData("count = 1")]
    public void What_cannot_be_read_says_so(string expression)
    {
        Assert.Equal(ExpressionFault.Syntax, ScreenExpression.Check(Project().Document, expression).Fault);
    }

    [Fact]
    public void A_name_nothing_has_is_named()
    {
        ExpressionCheck check = ScreenExpression.Check(Project().Document, "count + missing");

        Assert.Equal(ExpressionFault.UnknownName, check.Fault);
        Assert.Equal("missing", check.Name);
    }

    [Fact]
    public void A_name_nothing_has_is_a_hook_on_its_own_and_only_then()
    {
        ScreenDocument document = Project().Document;

        Assert.True(ScreenExpression.IsHookName(document, "temperature"));
        Assert.False(ScreenExpression.IsHookName(document, "count"));
        Assert.False(ScreenExpression.IsHookName(document, "hour"));
        Assert.False(ScreenExpression.IsHookName(document, "a + b"));
    }

    [Fact]
    public void A_big_number_keeps_its_size_on_an_int_of_sixteen_bits()
    {
        Assert.Equal("uiVar_x > 86400L", ScreenExpression.ToCode("x > 86400", n => "uiVar_" + n));
        Assert.Equal("!(uiVar_x + 1)", ScreenExpression.ToCode("!(x + 1)", n => "uiVar_" + n));
    }

    [Fact]
    public void Renaming_a_variable_goes_everywhere_it_is_named_and_undoes_as_one()
    {
        ScreenEditor editor = Project();
        ScreenElement value = editor.AddElement("home", ElementKind.Value, 0, 1, "count");
        editor.UpdateElement("home", value.Id, e => e with { When = "count > 1 && pause_on" });
        editor.AddTransition(null, new ScreenTrigger(TriggerKind.Condition, Value: "count == 3"), "home");
        editor.AddTransition("home", new ScreenTrigger(TriggerKind.Finished, Value: "pause"), "home");
        editor.UpdateTransition("home", 0, editor.Document.Find("home")!.Transitions[0] with { Actions = [new ScreenAction(ActionKind.Add, "count", "count + 1")] });

        Assert.True(editor.RenameVariable("count", "total"));
        Assert.True(editor.RenameVariable("pause", "rest"));

        ScreenNode home = editor.Document.Find("home")!;
        ScreenElement renamed = home.Elements.First(e => e.Id == value.Id);
        Assert.Equal("total", renamed.Text);
        Assert.Equal("total > 1 && rest_on", renamed.When);
        Assert.Equal("total == 3", editor.Document.FromAnywhere[0].Trigger.Value);
        Assert.Equal("rest", home.Transitions[0].Trigger.Value);
        Assert.Equal(new ScreenAction(ActionKind.Add, "total", "total + 1"), home.Transitions[0].Actions[0]);

        editor.Undo();
        Assert.Contains(editor.Document.Variables, v => v.Name == "pause");
        Assert.Contains(editor.Document.Variables, v => v.Name == "total");
    }

    [Theory]
    [InlineData("count")]
    [InlineData("hour")]
    [InlineData("2bad")]
    [InlineData("имя")]
    [InlineData("")]
    public void A_name_that_is_taken_or_unusable_is_refused(string name)
    {
        ScreenEditor editor = Project();

        Assert.False(editor.RenameVariable("sound", name));
        Assert.Contains(editor.Document.Variables, v => v.Name == "sound");
    }

    [Fact]
    public void A_new_variable_never_takes_a_name_something_has()
    {
        ScreenEditor editor = Project();

        Assert.Equal("count2", editor.AddVariable(VariableKind.Number, "count", []).Name);
        Assert.Equal("hour2", editor.AddVariable(VariableKind.Number, "hour", []).Name);
    }

    [Fact]
    public void Editing_a_built_in_symbol_makes_a_copy_that_takes_its_place()
    {
        ScreenEditor editor = Project();
        byte[] rows = [1, 2, 3, 4, 5, 6, 7, 8];

        editor.SetGlyphRows("bell", rows);

        Assert.Equal(rows, ScreenFont.Find(editor.Document, "bell")!.Rows);
        Assert.Single(editor.Document.Glyphs);
        editor.RemoveGlyph("bell");
        Assert.NotEqual(rows, ScreenFont.Find(editor.Document, "bell")!.Rows);
    }

    [Fact]
    public void Renaming_a_symbol_goes_with_it_into_texts()
    {
        ScreenEditor editor = Project();
        ScreenGlyph glyph = editor.AddGlyph("mine");
        editor.AddElement("home", ElementKind.Text, 0, 1, "a{mine}b");
        ScreenElement symbol = editor.AddElement("home", ElementKind.Symbol, 0, 2, "mine");

        Assert.True(editor.RenameGlyph(glyph.Name, "ours"));

        ScreenNode home = editor.Document.Find("home")!;
        Assert.Contains(home.Elements, e => e.Text == "a{ours}b");
        Assert.Equal("ours", home.Elements.First(e => e.Id == symbol.Id).Text);
    }

    [Theory]
    [InlineData('А', 'A')]
    [InlineData('в', 'B')]
    [InlineData('Ё', 'E')]
    [InlineData('ь', 'b')]
    public void A_letter_shaped_like_an_ascii_one_needs_no_symbol(char letter, char ascii)
    {
        Assert.Equal(ascii, ScreenFont.LookAlike(letter));
    }

    [Fact]
    public void Every_russian_letter_can_be_shown()
    {
        foreach (char letter in "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя")
        {
            Assert.True(ScreenFont.LookAlike(letter) is not null || ScreenFont.Shape(letter) is { Count: 8 }, letter.ToString());
        }
    }

    [Fact]
    public void A_text_splits_at_the_symbols_it_names_and_a_cell_is_a_cell()
    {
        ScreenDocument document = Project().Document;

        Assert.Equal(7, ScreenFont.CellsOf(document, "{bell} ДА {bell}"[..16] + "!"));
        Assert.Equal(6, ScreenFont.CellsOf(document, "{nope}"));
        Assert.Equal(3, ScreenFont.Split(document, "a{bell}b").Count);
    }

    [Fact]
    public void A_screen_counts_what_it_can_want_at_once_on_a_character_display()
    {
        var editor = new ScreenEditor(ScreenEditor.Starting(Lcd, "ЮЯ"));
        editor.AddVariable(VariableKind.Choice, "mode", ["Б", "ГД"]);
        ScreenElement value = editor.AddElement("home", ElementKind.Value, 0, 1, "mode");

        // Two from the text, and the longer of the two words: never both at once.
        Assert.Equal(4, ScreenCheck.SymbolsAtOnce(editor.Document, editor.Document.Find("home")!));

        editor.AddElement("home", ElementKind.BigNumber, 0, 2, "clock");
        Assert.Equal(7, ScreenCheck.SymbolsAtOnce(editor.Document, editor.Document.Find("home")!));
        Assert.NotNull(value);
    }

    [Fact]
    public void A_list_counts_the_worst_place_it_can_scroll_to()
    {
        var editor = new ScreenEditor(ScreenEditor.Starting(Lcd, "A"));
        ScreenNode menu = editor.AddScreen("m", 0, 0, asMenu: true);
        editor.SetMenu(menu.Id, editor.Document.Find(menu.Id)!.Menu! with
        {
            Rows = 2,
            Items = [new ScreenMenuItem { Text = "БГ" }, new ScreenMenuItem { Text = "ДЖ" }, new ScreenMenuItem { Text = "ИЙ" }, new ScreenMenuItem { Text = "A" }],
        });

        Assert.Equal(4, ScreenCheck.SymbolsAtOnce(editor.Document, editor.Document.Find(menu.Id)!));
    }

    [Fact]
    public void Too_many_symbols_on_one_screen_are_a_problem_and_a_letter_with_no_shape_too()
    {
        var editor = new ScreenEditor(ScreenEditor.Starting(Lcd, "БГДЖИЙЛПУФЦ"));
        editor.AddElement("home", ElementKind.Text, 0, 1, "€");

        IReadOnlyList<ScreenProblem> problems = ScreenCheck.Of(editor.Document, NoHardware);

        Assert.Contains(problems, p => p.Kind == ScreenProblemKind.TooManySymbols && p.Detail == "11");
        Assert.Contains(problems, p => p.Kind == ScreenProblemKind.NoShape && p.Detail == "€");
    }

    [Fact]
    public void What_will_not_work_is_found_before_it_is_built()
    {
        ScreenEditor editor = Project();
        ScreenElement bad = editor.AddElement("home", ElementKind.Value, 0, 1, "count");
        editor.UpdateElement("home", bad.Id, e => e with { When = "missing > 1" });
        editor.AddElement("home", ElementKind.Input, 0, 2, "count");
        editor.AddElement("home", ElementKind.Input, 0, 3, "nothing");
        editor.AddElement("home", ElementKind.Symbol, 5, 3, "nope");
        ScreenNode other = editor.AddScreen("Other", 0, 0);
        editor.AddTransition(other.Id, new ScreenTrigger(TriggerKind.AnyKey), "home");
        editor.UpdateTransition(other.Id, 0, editor.Document.Find(other.Id)!.Transitions[0] with
        {
            Actions =
            [
                new ScreenAction(ActionKind.InputCommit),
                new ScreenAction(ActionKind.Toggle, "count"),
                new ScreenAction(ActionKind.Start, "count"),
                new ScreenAction(ActionKind.Beep, "bz1", "100 100"),
            ],
        });

        IReadOnlyList<ScreenProblem> problems = ScreenCheck.Of(editor.Document, NoHardware);

        Assert.Contains(problems, p => p.Kind == ScreenProblemKind.UnknownName && p.Detail == "missing");
        Assert.Contains(problems, p => p.Kind == ScreenProblemKind.TwoInputs);
        Assert.Contains(problems, p => p.Kind == ScreenProblemKind.NoKeypad);
        Assert.Contains(problems, p => p.Kind == ScreenProblemKind.WrongVariable && p.Detail == "nothing");
        Assert.Contains(problems, p => p.Kind == ScreenProblemKind.UnknownSymbol && p.Detail == "nope");
        Assert.Contains(problems, p => p.Kind == ScreenProblemKind.NoInput && p.Screen == other.Id);
        Assert.Contains(problems, p => p.Kind == ScreenProblemKind.WrongVariable && p.Detail == "count" && p.Screen == other.Id);
        Assert.Contains(problems, p => p.Kind == ScreenProblemKind.NoPart && p.Detail == "bz1");
    }

    [Fact]
    public void A_project_with_nothing_wrong_has_no_problems()
    {
        ScreenEditor editor = Project();
        editor.AddElement("home", ElementKind.Value, 0, 1, "count");
        editor.AddElement("home", ElementKind.Value, 0, 2, "temperature");

        Assert.Empty(ScreenCheck.Of(editor.Document, NoHardware));
    }

    [Theory]
    [InlineData(ValueFormat.TwoDigits, "7", "07")]
    [InlineData(ValueFormat.HourMinute, "45296", "12:34")]
    [InlineData(ValueFormat.HourMinuteSecond, "45296", "12:34:56")]
    [InlineData(ValueFormat.MinuteSecond, "125", "02:05")]
    [InlineData(ValueFormat.MinuteSecondCentis, "125450", "02:05.45")]
    [InlineData(ValueFormat.DateDayFirst, "20261231", "31.12.2026")]
    [InlineData(ValueFormat.DateMonthFirst, "20261231", "12.31.2026")]
    [InlineData(ValueFormat.DateYearFirst, "20261231", "2026-12-31")]
    public void A_value_reads_as_its_format_says(ValueFormat format, string number, string expected)
    {
        Assert.Equal(expected, ScreenSample.Text(Project().Document, number, format, []));
    }

    [Fact]
    public void A_flag_or_a_choice_reads_as_its_word()
    {
        ScreenEditor editor = Project();
        editor.UpdateVariable("sound", v => v with { Initial = 1 });

        Assert.Equal("ON", ScreenSample.Text(editor.Document, "sound", ValueFormat.Auto, []));
        Assert.Equal("1", ScreenSample.Text(editor.Document, "1", ValueFormat.Auto, []));
        Assert.Null(ScreenSample.Text(editor.Document, "temperature", ValueFormat.Auto, []));
    }

    [Fact]
    public void A_file_says_what_was_decided_and_reads_back_whole()
    {
        string folder = Directory.CreateTempSubdirectory("ardusim-screens-").FullName;
        ScreenEditor editor = Project();
        editor.UpdateVariable("sound", v => v with { Saved = true, Initial = 1 });
        editor.AddGlyph("mine", [1, 2, 3, 4, 5, 6, 7, 8]);
        ScreenElement big = editor.AddElement("home", ElementKind.BigNumber, 0, 1, "clock");
        editor.UpdateElement("home", big.Id, e => e with { Format = ValueFormat.HourMinute, When = "sound" });
        editor.AddTransition("home", new ScreenTrigger(TriggerKind.AnyKey, When: "sound"), ScreenDocument.Back);
        editor.UpdateTransition("home", 0, editor.Document.Find("home")!.Transitions[0] with { Actions = [new ScreenAction(ActionKind.Toggle, "sound")] });
        editor.UpdateScreen("home", s => s with { OnEnter = [new ScreenAction(ActionKind.Reset, "pause")], KeyHook = true });

        editor.Document.Save(folder);
        string text = File.ReadAllText(Path.Combine(folder, ScreenDocument.FileName));
        ScreenDocument read = ScreenDocument.Load(folder)!;

        Assert.Equal(2, read.SchemaVersion);
        Assert.DoesNotContain("\"maximum\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"align\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"tickHook\"", text, StringComparison.Ordinal);
        Assert.True(read.Variables.First(v => v.Name == "sound").Saved);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, read.Glyphs[0].Rows);
        ScreenNode home = read.Find("home")!;
        Assert.Equal(ValueFormat.HourMinute, home.Elements.First(e => e.Id == big.Id).Format);
        Assert.Equal("sound", home.Transitions[0].Trigger.When);
        Assert.Equal(ScreenDocument.Back, home.Transitions[0].Target);
        Assert.Equal(new ScreenAction(ActionKind.Toggle, "sound"), home.Transitions[0].Actions[0]);
        Assert.True(home.KeyHook);
        Assert.Equal(new ScreenAction(ActionKind.Reset, "pause"), home.OnEnter[0]);
    }

    [Fact]
    public void A_file_of_the_first_version_opens_as_it_was()
    {
        string folder = Directory.CreateTempSubdirectory("ardusim-screens-").FullName;
        File.WriteAllText(Path.Combine(folder, ScreenDocument.FileName), """
            { "schemaVersion": 1, "source": "Generated",
              "display": { "family": "CharLcd", "width": 16, "height": 2, "library": "LiquidCrystalI2c", "variable": "lcd", "part": "lcd1" },
              "start": "a",
              "screens": [ { "id": "a", "title": "A", "elements": [ { "id": "e1", "kind": "Text", "x": 1, "y": 1, "text": "Hi", "width": 0, "height": 0, "size": 1, "isConditional": false, "isAlternative": false, "isPlaced": true } ], "transitions": [] } ],
              "fromAnywhere": [] }
            """);

        ScreenDocument read = ScreenDocument.Load(folder)!;

        Assert.Empty(read.Variables);
        Assert.True(read.Find("a")!.Elements[0].IsPlaced);
        Assert.Equal(ElementAlign.Left, read.Find("a")!.Elements[0].Align);
    }

    [Fact]
    public void What_the_header_offers_grows_with_what_the_project_uses()
    {
        GeneratedScreens plain = ScreenCodeGenerator.Generate(new ScreenEditor(ScreenEditor.Starting(Lcd, "Hi")).Document, NoHardware, null);

        Assert.DoesNotContain("uiBack", plain.Header, StringComparison.Ordinal);
        Assert.DoesNotContain("UiTimer", plain.Header, StringComparison.Ordinal);
        Assert.DoesNotContain("uiLoaded", plain.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("EEPROM", plain.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("uiClock", plain.Source, StringComparison.Ordinal);

        ScreenEditor editor = Project();
        editor.UpdateVariable("sound", v => v with { Saved = true });
        editor.AddTransition("home", new ScreenTrigger(TriggerKind.AnyKey), ScreenDocument.Back);
        editor.AddElement("home", ElementKind.Value, 0, 1, "hour");
        GeneratedScreens full = ScreenCodeGenerator.Generate(editor.Document, NoHardware, null);

        Assert.Contains("void uiBack();", full.Header, StringComparison.Ordinal);
        Assert.Contains("extern UiTimer uiTimer_pause;", full.Header, StringComparison.Ordinal);
        Assert.Contains("extern int32_t uiVar_count;", full.Header, StringComparison.Ordinal);
        Assert.Contains("void uiSave();", full.Header, StringComparison.Ordinal);
        Assert.Contains("#include <EEPROM.h>", full.Source, StringComparison.Ordinal);
        Assert.Contains("static UiClock uiClock", full.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void An_expression_that_cannot_be_read_is_said_at_build_time_and_counts_as_nothing()
    {
        ScreenEditor editor = Project();
        ScreenElement value = editor.AddElement("home", ElementKind.Value, 0, 1, "count");
        editor.UpdateElement("home", value.Id, e => e with { When = "count +" });

        GeneratedScreens generated = ScreenCodeGenerator.Generate(editor.Document, NoHardware, null);

        Assert.Contains("#warning", generated.Source, StringComparison.Ordinal);
        Assert.Contains("if (0) {", generated.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_comparison_can_choose_a_word_without_a_switch_on_a_bool()
    {
        var editor = new ScreenEditor(ScreenEditor.Starting(Lcd, "Hi"));
        ScreenElement value = editor.AddElement("home", ElementKind.Value, 0, 1, "hour >= 12");
        editor.UpdateElement("home", value.Id, e => e with { Format = ValueFormat.List, Options = ["AM", "PM"] });

        GeneratedScreens generated = ScreenCodeGenerator.Generate(editor.Document, NoHardware, null);

        Assert.Contains("switch ((int32_t)(", generated.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_the_hooks_file_writes_stays_a_hook_when_it_comes_to_be_built_in()
    {
        var editor = new ScreenEditor(ScreenEditor.Starting(Lcd, "Hi"));
        editor.AddElement("home", ElementKind.Value, 0, 1, "uptime");

        GeneratedScreens generated = ScreenCodeGenerator.Generate(editor.Document, NoHardware, "void uiValue_uptime(char* out, uint8_t size) {\n}\n");

        Assert.Contains("uiValue_uptime(text, sizeof text);", generated.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Letters_in_the_hooks_file_are_given_their_shapes_too()
    {
        var editor = new ScreenEditor(ScreenEditor.Starting(Lcd, "Hi"));

        GeneratedScreens generated = ScreenCodeGenerator.Generate(editor.Document, NoHardware, "// Ж\n");

        Assert.Contains("case 0x0416: return UI_S_U0416;", generated.Source, StringComparison.Ordinal);
    }
}
