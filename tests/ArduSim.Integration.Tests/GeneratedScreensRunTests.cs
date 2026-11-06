using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ArduSim.Boards;
using ArduSim.Core.Projects;
using ArduSim.Core.Screens;

namespace ArduSim.Integration.Tests;

/// <summary>What the screen editor generates (ADR-019), built by the real toolchain and run on the simulated hardware.</summary>
public sealed partial class GeneratedScreensRunTests(ToolchainFixture toolchain)
{
    private const string Empty = "void setup() {\n}\n\nvoid loop() {\n}\n";

    private static readonly string[] LcdI2cWires = ["board.GND lcd1.GND", "board.5V lcd1.VCC", "board.A4 lcd1.SDA", "board.A5 lcd1.SCL"];
    private static readonly string[] OledI2cWires = ["board.GND oled1.GND", "board.5V oled1.VCC", "board.A4 oled1.SDA", "board.A5 oled1.SCL"];

    private readonly ToolchainFixture _toolchain = toolchain;

    [Fact]
    public async Task A_2004_on_i2c_goes_through_a_menu_with_two_buttons_and_comes_back_on_a_timeout()
    {
        Assert.True(_toolchain.Toolchain.IsComplete);
        string project = Project(
            """
            { "id": "lcd1", "type": "lcd", "x": 280, "y": 0, "rotation": 0, "flip": false, "properties": { "size": "20x4" } },
            { "id": "btn1", "type": "pushbutton", "x": 0, "y": 200, "rotation": 0, "flip": false },
            { "id": "btn2", "type": "pushbutton", "x": 100, "y": 200, "rotation": 0, "flip": false }
            """,
            [.. LcdI2cWires, "btn1.1 board.D2", "btn1.2 board.GND", "btn2.1 board.D3", "btn2.2 board.GND"]);

        var editor = new ScreenEditor(ScreenEditor.Starting(DisplayOf(project), "Hello"));
        editor.AddElement("home", ElementKind.Value, 0, 1, "seconds");
        ScreenNode about = editor.AddScreen("About", 0, 0);
        editor.AddElement(about.Id, ElementKind.Text, 2, 0, "About");
        editor.AddTransition(about.Id, new ScreenTrigger(TriggerKind.Timeout, Value: "300"), "home");
        ScreenNode menu = editor.AddScreen("Menu", 0, 0, asMenu: true);
        editor.SetMenu(menu.Id, editor.Document.Find(menu.Id)!.Menu! with
        {
            Items = [new ScreenMenuItem { Text = "First", Target = about.Id }, new ScreenMenuItem { Text = "Second", Target = "home" }],
            Down = new ScreenTrigger(TriggerKind.Button, "btn1"),
            Select = new ScreenTrigger(TriggerKind.Button, "btn2"),
        });
        editor.AddTransition("home", new ScreenTrigger(TriggerKind.Button, "btn1"), menu.Id);
        Generate(project, editor.Document, "#include \"ui_screens.h\"\nvoid uiValue_seconds(char* out, uint8_t size) {\n  snprintf(out, size, \"%lu\", millis() / 1000);\n}\n");

        RunResult result = await RunnerProcess.RunProjectAsync(project, ["--run-for", "3200", "--quiet", .. Press(1200, "btn1"), .. Press(1500, "btn1"), .. Press(1800, "btn2"), .. Press(2100, "btn1"), .. Press(2400, "btn2")]);

        Assert.True(result.ExitCode == 0, result.ToString());
        List<string[]> frames = Frames(result, "lcd1");
        string text = string.Join('\n', frames.Select(f => string.Join('|', f)));
        int menuOpen = frames.FindIndex(f => f[1].StartsWith(">First", StringComparison.Ordinal) && f[2].StartsWith(" Second", StringComparison.Ordinal));
        Assert.True(menuOpen >= 0, text);
        int moved = frames.FindIndex(menuOpen, f => f[1].StartsWith(" First", StringComparison.Ordinal) && f[2].StartsWith(">Second", StringComparison.Ordinal));
        Assert.True(moved > menuOpen, text);
        int backHome = frames.FindIndex(moved, f => f[0].StartsWith("Hello", StringComparison.Ordinal));
        Assert.True(backHome > moved, text);
        int aboutOpen = frames.FindIndex(backHome, f => f[0] == "  About             ");
        Assert.True(aboutOpen > backHome, text);

        // The timeout brings it home, and the value is what the hook says.
        Assert.Equal("Hello               ", frames[^1][0]);
        Assert.Equal("3", frames[^1][1].Trim());
        Assert.DoesNotContain("warning", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_1602_on_parallel_pins_follows_a_keypad_an_encoder_and_a_key_from_any_screen()
    {
        string project = Project(
            """
            { "id": "lcd1", "type": "lcd", "x": 280, "y": 0, "rotation": 0, "flip": false, "properties": { "interface": "parallel-4" } },
            { "id": "kp1", "type": "keypad", "x": 0, "y": 200, "rotation": 0, "flip": false },
            { "id": "enc1", "type": "rotary-encoder", "x": 200, "y": 200, "rotation": 0, "flip": false }
            """,
            [
                "lcd1.VSS board.GND", "lcd1.VDD board.5V", "lcd1.RW lcd1.VSS", "lcd1.RS board.D12", "lcd1.E board.D11",
                "lcd1.D4 board.D10", "lcd1.D5 board.A0", "lcd1.D6 board.A1", "lcd1.D7 board.A2", "lcd1.A board.5V", "lcd1.K board.GND",
                "kp1.R1 board.D9", "kp1.R2 board.D8", "kp1.R3 board.D7", "kp1.R4 board.D6", "kp1.C1 board.D5", "kp1.C2 board.D4", "kp1.C3 board.D3", "kp1.C4 board.D2",
                "enc1.CLK board.A3", "enc1.DT board.A4", "enc1.SW board.A5", "enc1.+ board.5V", "enc1.GND board.GND",
            ]);

        var editor = new ScreenEditor(ScreenEditor.Starting(DisplayOf(project), "home"));
        ScreenNode one = editor.AddScreen("One", 0, 0);
        editor.AddElement(one.Id, ElementKind.Text, 0, 0, "one");
        ScreenNode two = editor.AddScreen("Two", 0, 0);
        editor.AddElement(two.Id, ElementKind.Text, 0, 1, "two");
        editor.AddTransition("home", new ScreenTrigger(TriggerKind.Key, "kp1", "5"), one.Id);
        editor.AddTransition(one.Id, new ScreenTrigger(TriggerKind.EncoderRight, "enc1"), two.Id);
        editor.AddTransition(two.Id, new ScreenTrigger(TriggerKind.EncoderLeft, "enc1"), one.Id);
        editor.AddTransition(null, new ScreenTrigger(TriggerKind.EncoderPress, "enc1"), "home");
        Generate(project, editor.Document, null);

        RunResult result = await RunnerProcess.RunProjectAsync(
            project,
            [
                "--run-for", "1600", "--quiet",
                "--input", "200:kp1:k5=1", "--input", "300:kp1:k5=0",
                "--input", "500:enc1:turn=1",
                "--input", "800:enc1:turn=-1",
                .. Press(1100, "enc1"),
            ]);

        Assert.True(result.ExitCode == 0, result.ToString());
        string[] rows = [.. Frames(result, "lcd1").Select(f => (f[0] + f[1]).Trim())];
        string[] order = [.. rows.Where(r => r is "home" or "one" or "two")];
        Assert.Equal(["home", "one", "two", "one", "home"], order);
    }

    [Fact]
    public async Task An_ssd1306_draws_text_and_shapes_with_adafruit_and_changes_on_a_timeout()
    {
        string project = Project("""{ "id": "oled1", "type": "oled", "x": 260, "y": 120, "rotation": 0, "flip": false }""", OledI2cWires);
        var editor = new ScreenEditor(ScreenEditor.Starting(DisplayOf(project), "Hi"));
        ScreenNode next = editor.AddScreen("Next", 0, 0);
        editor.AddElement(next.Id, ElementKind.Box, 0, 0);
        editor.UpdateElement(next.Id, "e1", e => e with { Width = 10, Height = 10 });
        editor.AddElement(next.Id, ElementKind.Frame, 20, 20);
        editor.AddElement(next.Id, ElementKind.Line, 0, 40);
        editor.AddTransition("home", new ScreenTrigger(TriggerKind.Timeout, Value: "400"), next.Id);
        Generate(project, editor.Document, null);

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "1000", "--quiet");

        Assert.True(result.ExitCode == 0, result.ToString());
        int[] lit = PixelsOn(result, "oled1");
        Assert.Contains(lit, n => n is > 0 and < 100);    // "Hi"
        Assert.True(lit[^1] > 100, result.Stdout);        // the box alone is 100
    }

    [Fact]
    public async Task An_sh1106_on_spi_draws_with_adafruit()
    {
        string project = Project(
            """{ "id": "oled1", "type": "oled", "x": 240, "y": 140, "rotation": 0, "flip": false, "properties": { "controller": "sh1106", "interface": "spi" } }""",
            ["board.GND oled1.GND", "board.5V oled1.VCC", "board.D13 oled1.D0", "board.D11 oled1.D1", "board.D8 oled1.RES", "board.D9 oled1.DC", "board.D10 oled1.CS"]);
        Generate(project, ScreenEditor.Starting(DisplayOf(project), "Hello"), null);

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "500", "--quiet");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.True(PixelsOn(result, "oled1")[^1] > 0, result.Stdout);
    }

    // Three libraries with a display object the sketch already has: generated code must draw on that
    // one, in the sketch's own type, after the sketch began it, without assuming anything was set up.
    [Theory]
    [InlineData("#include <U8g2lib.h>\nU8G2_SSD1306_128X64_NONAME_F_HW_I2C u8g2(U8G2_R0, U8X8_PIN_NONE);\n\nvoid setup() {\n  u8g2.begin();\n}\n\nvoid loop() {\n}\n", "u8g2")]
    [InlineData("#include <GyverOLED.h>\nGyverOLED<SSD1306_128x64, OLED_BUFFER> oled;\n\nvoid setup() {\n  oled.init();\n}\n\nvoid loop() {\n}\n", "oled")]
    [InlineData("#include <Adafruit_SSD1306.h>\nAdafruit_SSD1306 screen(128, 64, &Wire, -1);\n\nvoid setup() {\n  screen.begin(SSD1306_SWITCHCAPVCC, 0x3C);\n}\n\nvoid loop() {\n}\n", "screen")]
    public async Task A_display_the_sketch_already_has_is_the_one_drawn_on(string sketch, string variable)
    {
        string project = Project("""{ "id": "oled1", "type": "oled", "x": 260, "y": 120, "rotation": 0, "flip": false }""", OledI2cWires, sketch);
        ScreenDisplay display = ScreenCodeAnalyzer.DisplayIn([(SketchPath(project), sketch)], DisplayOf(project))!;
        Assert.True(display.Existing);
        Assert.Equal(variable, display.Variable);
        Generate(project, ScreenEditor.Starting(display, "Hello"), null);

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "500", "--quiet");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.True(PixelsOn(result, "oled1")[^1] > 0, result.Stdout);
    }

    [Fact]
    public async Task A_character_display_the_sketch_already_has_is_the_one_drawn_on()
    {
        const string Sketch = "#include <LiquidCrystal_I2C.h>\nLiquidCrystal_I2C lcd(0x27, 16, 2);\n\nvoid setup() {\n  lcd.init();\n  lcd.backlight();\n}\n\nvoid loop() {\n}\n";
        string project = Project("""{ "id": "lcd1", "type": "lcd", "x": 280, "y": 0, "rotation": 0, "flip": false }""", LcdI2cWires, Sketch);
        ScreenDisplay display = ScreenCodeAnalyzer.DisplayIn([(SketchPath(project), Sketch)], DisplayOf(project))!;
        Generate(project, ScreenEditor.Starting(display, "Hello"), null);

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "1500", "--quiet");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal("Hello           ", Frames(result, "lcd1")[^1][0]);
    }

    [Fact]
    public async Task The_example_goes_through_its_menu_with_the_encoder_and_home_from_any_screen()
    {
        string project = RunnerProcess.CopyProject("examples/ScreensMenu");

        RunResult result = await RunnerProcess.RunProjectAsync(
            project,
            [
                "--run-for", "4400", "--quiet",
                .. Press(1200, "enc1"),                 // the menu
                "--input", "1600:enc1:turn=1",          // down to About
                .. Press(1900, "enc1"),                 // About, for 800 ms, then home
                .. Press(3100, "enc1"),                 // the menu again
                .. Press(3400, "enc1"),                 // Sensor
                .. Press(3800, "btn1"),                 // home from there
            ]);

        Assert.True(result.ExitCode == 0, result.ToString());
        List<string[]> frames = Frames(result, "lcd1");

        // Characters reach the display one at a time over I2C, so that a frame can catch a row half written.
        string[] firstRows = [.. frames.Select(f => f[0].TrimEnd()).Where(r => r is "ArduSim screens" or "Menu" or "Made on the map" or "Sensor on A0")];
        string[] order = [.. firstRows.Where((r, i) => i == 0 || firstRows[i - 1] != r)];
        Assert.Equal(["ArduSim screens", "Menu", "Made on the map", "ArduSim screens", "Menu", "Sensor on A0", "ArduSim screens"], order);
        Assert.Contains(frames, f => f[2].StartsWith(">About", StringComparison.Ordinal));
        Assert.Contains(frames, f => f[2].StartsWith("Up: 3 s", StringComparison.Ordinal) || f[2].StartsWith("Up: 4 s", StringComparison.Ordinal));
        Assert.DoesNotContain("warning", result.Stdout, StringComparison.Ordinal);
    }

    private static string[] Press(int at, string part) =>
        ["--input", string.Create(CultureInfo.InvariantCulture, $"{at}:{part}:pressed=1"), "--input", string.Create(CultureInfo.InvariantCulture, $"{at + 100}:{part}:pressed=0")];

    private static string SketchPath(string project) => Path.Combine(project, "sketch", "sketch.ino");

    private static ScreenDisplay DisplayOf(string project) => ScreenHardware.DisplayOf(ProjectFile.Load(project).Scene)!;

    /// <summary>What the app does before a build, and when screens are created.</summary>
    private static void Generate(string project, ScreenDocument document, string? hooks)
    {
        ScreenWiring wiring = ScreenHardware.WiringOf(ProjectFile.Load(project).Scene, Nano.Profile, document.Display.Part);
        GeneratedScreens generated = ScreenCodeGenerator.Generate(document, wiring, hooks);
        string sketch = Path.Combine(project, "sketch");
        File.WriteAllText(Path.Combine(sketch, ScreenCodeGenerator.HeaderFile), generated.Header);
        File.WriteAllText(Path.Combine(sketch, ScreenCodeGenerator.SourceFile), generated.Source);
        File.WriteAllText(Path.Combine(sketch, ScreenCodeGenerator.HooksFile), generated.Hooks);
        string text = File.ReadAllText(SketchPath(project));
        foreach ((int offset, string insert) in ScreenCodeGenerator.WiringEdits(text, document.Display.Existing).OrderByDescending(e => e.Offset))
        {
            text = text.Insert(offset, insert);
        }

        File.WriteAllText(SketchPath(project), text);
    }

    private static string Project(string parts, IEnumerable<string> wires, string sketch = Empty)
    {
        string project = Directory.CreateTempSubdirectory("ardusim-screens-").FullName;
        var json = new StringBuilder();
        json.Append("""{ "schemaVersion": 1, "board": "arduino-nano-v3", "sketchPath": "sketch", "scene": { "components": [ { "id": "board", "type": "arduino-nano-v3", "x": 0, "y": 0, "rotation": 0, "flip": false }, """);
        json.Append(parts).Append(""" ], "wires": [ """);
        int number = 0;
        json.AppendJoin(", ", wires.Select(wire =>
        {
            string[] ends = wire.Split(' ');
            (string fromPart, string fromPin) = Split(ends[0]);
            (string toPart, string toPin) = Split(ends[1]);
            number++;
            return string.Create(CultureInfo.InvariantCulture, $$"""{ "id": "w{{number}}", "from": { "component": "{{fromPart}}", "pin": "{{fromPin}}" }, "to": { "component": "{{toPart}}", "pin": "{{toPin}}" }, "waypoints": [], "color": "auto" }""");
        }));
        json.Append(""" ] }, "settings": { "speed": 1.0, "serial": { "baud": 9600, "lineEnding": "LF" }, "libraryPaths": [] } }""");
        File.WriteAllText(Path.Combine(project, "project.simproj.json"), json.ToString());
        Directory.CreateDirectory(Path.Combine(project, "sketch"));
        File.WriteAllText(SketchPath(project), sketch);
        return project;

        static (string, string) Split(string end)
        {
            int dot = end.IndexOf('.', StringComparison.Ordinal);
            return (end[..dot], end[(dot + 1)..]);
        }
    }

    private static List<string[]> Frames(RunResult result, string id) =>
        [.. result.Timeline
            .Where(e => e.Text.StartsWith(id + ": \"", StringComparison.Ordinal))
            .Select(e => QuotedRow().Matches(e.Text).Select(m => m.Groups[1].Value).ToArray())];

    private static int[] PixelsOn(RunResult result, string id) =>
        [.. result.Timeline.Select(e => PixelLine().Match(e.Text)).Where(m => m.Success && m.Groups[1].Value == id).Select(m => int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))];

    [GeneratedRegex("\"([^\"]*)\"")]
    private static partial Regex QuotedRow();

    [GeneratedRegex(@"^(\w+): (\d+) px on$")]
    private static partial Regex PixelLine();
}
