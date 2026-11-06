using ArduSim.Core.Screens;

namespace ArduSim.Core.Tests;

/// <summary>Generated code's shape, the sketch's connection to it, and the model's undoable edits (ADR-019). Integration tests build and run it.</summary>
public sealed class ScreenGeneratorTests
{
    private static readonly ScreenDisplay Lcd = new(DisplayFamily.CharLcd, 16, 2, DisplayLibrary.LiquidCrystalI2c, "uiDisplay", "lcd1");
    private static readonly ScreenWiring NoInputs = new(0x27, [], [], "ssd1306", []);
    private static readonly ScreenWiring OneButton = new(0x27, [], [], "ssd1306", [new ScreenInput("home", ScreenInputKind.Button, [2])]);

    [Fact]
    public void Each_screen_gets_an_enum_value_and_a_case()
    {
        ScreenDocument document = ScreenEditor.Starting(Lcd, "Hi");

        GeneratedScreens generated = ScreenCodeGenerator.Generate(document, NoInputs, null);

        Assert.Contains("UI_HOME,", generated.Header, StringComparison.Ordinal);
        Assert.Contains("case UI_HOME:", generated.Source, StringComparison.Ordinal);
        Assert.Contains("LiquidCrystal_I2C uiDisplay(0x27, 16, 2);", generated.Source, StringComparison.Ordinal);
        Assert.Contains("extern LiquidCrystal_I2C uiDisplay;", generated.Header, StringComparison.Ordinal);
    }

    [Fact]
    public void A_button_named_like_a_screen_does_not_clash_with_it()
    {
        var editor = new ScreenEditor(ScreenEditor.Starting(Lcd, "Hi"));
        ScreenNode other = editor.AddScreen("Other", 0, 0);
        editor.AddTransition("home", new ScreenTrigger(TriggerKind.Button, "home"), other.Id);

        GeneratedScreens generated = ScreenCodeGenerator.Generate(editor.Document, OneButton, null);

        Assert.Contains("UI_HOME,", generated.Header, StringComparison.Ordinal);
        Assert.Contains("UI_ON_HOME,", generated.Header, StringComparison.Ordinal);
    }

    [Fact]
    public void A_display_the_sketch_has_is_declared_not_defined_or_begun()
    {
        ScreenDisplay own = new(DisplayFamily.Oled, 128, 64, DisplayLibrary.GyverOled, "oled", "oled1", Existing: true, TypeName: "GyverOLED<SSD1306_128x64,OLED_BUFFER>");

        GeneratedScreens generated = ScreenCodeGenerator.Generate(ScreenEditor.Starting(own, "Hi"), NoInputs, null);

        Assert.Contains("extern GyverOLED<SSD1306_128x64,OLED_BUFFER> oled;", generated.Header, StringComparison.Ordinal);
        Assert.DoesNotContain("\nGyverOLED<", generated.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("oled.init()", generated.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Hooks_written_by_hand_stay_and_only_what_is_missing_is_added()
    {
        var editor = new ScreenEditor(ScreenEditor.Starting(Lcd, "Hi"));
        editor.AddElement("home", ElementKind.Value, 0, 1, "temperature");
        editor.AddElement("home", ElementKind.Value, 8, 1, "humidity");
        const string Mine = "// mine\nvoid uiValue_temperature(char* out, uint8_t size) { out[0] = 0; }\n";

        string hooks = ScreenCodeGenerator.Generate(editor.Document, NoInputs, Mine).Hooks;

        Assert.StartsWith(Mine, hooks, StringComparison.Ordinal);
        Assert.Contains("void uiValue_humidity(char* out, uint8_t size)", hooks, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(hooks, "uiValue_temperature"));

        // The second time round there is nothing to add.
        Assert.Equal(hooks, ScreenCodeGenerator.Generate(editor.Document, NoInputs, hooks).Hooks);
    }

    [Fact]
    public void A_sketch_is_connected_once_and_at_the_right_end_of_setup()
    {
        const string Sketch = "void setup() {\n  lcd.init();\n}\n\nvoid loop() {\n}\n";

        string own = Apply(Sketch, ScreenCodeGenerator.WiringEdits(Sketch));
        string existing = Apply(Sketch, ScreenCodeGenerator.WiringEdits(Sketch, existingDisplay: true));

        Assert.StartsWith("#include \"ui_screens.h\"", own, StringComparison.Ordinal);
        Assert.Contains("void setup() {\n  uiBegin();\n  lcd.init();", own, StringComparison.Ordinal);
        Assert.Contains("void loop() {\n  uiUpdate();", own, StringComparison.Ordinal);
        Assert.Contains("  lcd.init();\n  uiBegin();\n}", existing, StringComparison.Ordinal);
        Assert.Empty(ScreenCodeGenerator.WiringEdits(own));
    }

    [Fact]
    public void A_setup_on_one_line_still_gets_its_call()
    {
        const string Sketch = "void setup() {}\nvoid loop() {}\n";

        string existing = Apply(Sketch, ScreenCodeGenerator.WiringEdits(Sketch, existingDisplay: true));

        Assert.Contains("void setup() {\n  uiBegin();\n}", existing, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sketch_without_setup_and_loop_gets_both()
    {
        string connected = Apply("", ScreenCodeGenerator.WiringEdits(""));

        Assert.Contains("void setup() {\n  uiBegin();", connected, StringComparison.Ordinal);
        Assert.Contains("void loop() {\n  uiUpdate();", connected, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("LiquidCrystal_I2C lcd(0x27, 16, 2);", "lcd", "LiquidCrystal_I2C")]
    [InlineData("GyverOLED<SSD1306_128x64, OLED_BUFFER> oled;", "oled", "GyverOLED<SSD1306_128x64,OLED_BUFFER>")]
    public void A_display_at_file_level_is_found_with_its_type(string declaration, string name, string type)
    {
        ScreenDisplay? display = ScreenCodeAnalyzer.DisplayIn([("a.ino", declaration + "\nvoid setup() {}\n")]);

        Assert.NotNull(display);
        Assert.True(display.Existing);
        Assert.Equal(name, display.Variable);
        Assert.Equal(type, display.TypeName);
    }

    [Theory]
    [InlineData("class Device { LiquidCrystal_I2C lcd; };")]
    [InlineData("static LiquidCrystal_I2C lcd(0x27, 16, 2);")]
    [InlineData("void f() { LiquidCrystal_I2C lcd(0x27, 16, 2); }")]
    public void A_display_other_files_cannot_reach_is_not_offered(string code)
    {
        Assert.Null(ScreenCodeAnalyzer.DisplayIn([("a.ino", code)]));
    }

    [Fact]
    public void Removing_a_screen_takes_what_leads_to_it_and_undo_brings_all_back()
    {
        var editor = new ScreenEditor(ScreenEditor.Starting(Lcd, "Hi"));
        ScreenNode other = editor.AddScreen("Other", 0, 0);
        editor.AddTransition("home", new ScreenTrigger(TriggerKind.Timeout, Value: "100"), other.Id);
        editor.AddTransition(null, new ScreenTrigger(TriggerKind.Event, Value: "x"), other.Id);

        editor.RemoveScreen(other.Id);

        Assert.Empty(editor.Document.Find("home")!.Transitions);
        Assert.Empty(editor.Document.FromAnywhere);

        editor.Undo();

        Assert.Single(editor.Document.Find("home")!.Transitions);
        Assert.Single(editor.Document.FromAnywhere);
        Assert.NotNull(editor.Document.Find(other.Id));

        editor.Redo();

        Assert.Null(editor.Document.Find(other.Id));
    }

    [Fact]
    public void Removing_the_start_moves_the_start()
    {
        var editor = new ScreenEditor(ScreenEditor.Starting(Lcd, "Hi"));
        ScreenNode other = editor.AddScreen("Other", 0, 0);

        editor.RemoveScreen("home");

        Assert.Equal(other.Id, editor.Document.Start);
    }

    [Fact]
    public void A_drag_on_the_map_undoes_as_one_step()
    {
        var editor = new ScreenEditor(ScreenEditor.Starting(Lcd, "Hi"));

        editor.MoveOnMap("home", 10, 10, checkpoint: true);
        editor.MoveOnMap("home", 20, 20, checkpoint: false);
        editor.MoveOnMap("home", 30, 30, checkpoint: false);
        editor.Undo();

        Assert.Equal(0, editor.Document.Find("home")!.MapX);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void The_model_survives_its_file_without_what_only_a_scan_knows()
    {
        string folder = Directory.CreateTempSubdirectory("ardusim-screens-").FullName;
        var editor = new ScreenEditor(ScreenEditor.Starting(Lcd, "Привет"));
        editor.AddElement("home", ElementKind.Value, 3, 1, "temperature");
        ScreenNode other = editor.AddScreen("Other", 100, 50, asMenu: true);
        editor.AddTransition("home", new ScreenTrigger(TriggerKind.Key, "kp1", "#"), other.Id);

        editor.Document.Save(folder);
        ScreenDocument read = ScreenDocument.Load(folder)!;

        Assert.Equal(ScreenSource.Generated, read.Source);
        Assert.Equal(Lcd, read.Display);
        Assert.Equal("Привет", read.Find("home")!.Elements[0].Text);
        Assert.Equal("temperature", read.Find("home")!.Elements[1].Text);
        Assert.Equal("#", read.Find("home")!.Transitions[0].Trigger.Value);
        Assert.NotNull(read.Find(other.Id)!.Menu);
    }

    private static string Apply(string text, IReadOnlyList<(int Offset, string Text)> edits)
    {
        foreach ((int offset, string insert) in edits.OrderByDescending(e => e.Offset))
        {
            text = text.Insert(offset, insert);
        }

        return text;
    }
}
