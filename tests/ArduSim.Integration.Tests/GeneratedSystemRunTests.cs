using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ArduSim.Boards;
using ArduSim.Core.Projects;
using ArduSim.Core.Screens;

namespace ArduSim.Integration.Tests;

/// <summary>
/// A whole small device made only with what the constructor offers (ADR-019): data, actions, symbols, a clock, a
/// countdown and a notification. Built by the real toolchain and run on the simulated hardware, key by key.
/// </summary>
public sealed partial class GeneratedSystemRunTests(ToolchainFixture toolchain)
{
    private const string Empty = "void setup() {\n}\n\nvoid loop() {\n}\n";


    private static readonly string[] Wires =
    [
        "board.GND lcd1.GND", "board.5V lcd1.VCC", "board.A4 lcd1.SDA", "board.A5 lcd1.SCL",
        "kp1.R1 board.D2", "kp1.R2 board.D3", "kp1.R3 board.D4", "kp1.R4 board.D5", "kp1.C1 board.D6", "kp1.C2 board.D7", "kp1.C3 board.D8", "kp1.C4 board.D9",
        "bz1.1 board.D10", "bz1.2 board.GND",
    ];

    private const string Parts = """
        { "id": "lcd1", "type": "lcd", "x": 280, "y": 0, "rotation": 0, "flip": false, "properties": { "size": "20x4" } },
        { "id": "kp1", "type": "keypad", "x": 0, "y": 200, "rotation": 0, "flip": false },
        { "id": "bz1", "type": "buzzer", "x": 300, "y": 200, "rotation": 0, "flip": false }
        """;

    private readonly ToolchainFixture _toolchain = toolchain;

    private static ScreenTrigger Key(string key) => new(TriggerKind.Key, "kp1", key);

    private static ScreenEditor Device(ScreenDisplay display)
    {
        var editor = new ScreenEditor(ScreenEditor.Starting(display, "{bell} ГЛАВНАЯ"));

        // Data: a saved flag and choice, a count, a countdown, a stopwatch.
        editor.AddVariable(VariableKind.Flag, "sound", ["ВЫКЛ", "ВКЛ"]);
        editor.UpdateVariable("sound", v => v with { Initial = 1, Saved = true });
        editor.AddVariable(VariableKind.Choice, "mode", ["ЧАЙ", "КОФЕ", "СУП"]);
        editor.UpdateVariable("mode", v => v with { Saved = true });
        editor.AddVariable(VariableKind.Number, "count", []);
        editor.UpdateVariable("count", v => v with { Min = 0, Max = 3 });
        editor.AddVariable(VariableKind.Countdown, "pause", []);
        editor.UpdateVariable("pause", v => v with { Initial = 90 });
        editor.AddVariable(VariableKind.Stopwatch, "lap", []);

        // Home: the clock in big digits, a date, and a line that is there only while the countdown runs.
        editor.UpdateElement("home", "e1", e => e with { Align = ElementAlign.Center });
        ScreenElement clock = editor.AddElement("home", ElementKind.BigNumber, 0, 1, "clock");
        editor.UpdateElement("home", clock.Id, e => e with { Format = ValueFormat.HourMinute });
        ScreenElement date = editor.AddElement("home", ElementKind.Value, 0, 3, "date");
        editor.UpdateElement("home", date.Id, e => e with { Width = 10, Format = ValueFormat.DateDayFirst });
        ScreenElement running = editor.AddElement("home", ElementKind.Text, 19, 3, "{play}");
        editor.UpdateElement("home", running.Id, e => e with { When = "pause_on" });

        // A menu: a counter, an arrow, values at the right edge, one item that changes a saved flag, one that goes back.
        ScreenNode menu = editor.AddScreen("Menu", 0, 0, asMenu: true);
        editor.SetMenu(menu.Id, editor.Document.Find(menu.Id)!.Menu! with
        {
            Up = Key("2"),
            Down = Key("8"),
            Select = Key("#"),
            Counter = true,
            Marker = "{arrowRight}",
            Rows = 3,
            Top = 1,
            Items =
            [
                new ScreenMenuItem { Text = "ЗВУК", Value = "sound", Actions = [new ScreenAction(ActionKind.Toggle, "sound")] },
                new ScreenMenuItem { Text = "РЕЖИМ", Value = "mode", Actions = [new ScreenAction(ActionKind.Toggle, "mode")] },
                new ScreenMenuItem { Text = "ПАУЗА" },
                new ScreenMenuItem { Text = "НАЗАД", Target = ScreenDocument.Back },
            ],
        });
        editor.AddElement(menu.Id, ElementKind.Text, 0, 0, "МЕНЮ");
        editor.AddTransition("home", Key("A"), menu.Id);
        editor.AddTransition(menu.Id, Key("*"), ScreenDocument.Back);

        // A countdown set by typing, with digits coming in from the right.
        ScreenNode timer = editor.AddScreen("Timer", 0, 0);
        ScreenElement left = editor.AddElement(timer.Id, ElementKind.BigNumber, 0, 1, "pause");
        editor.UpdateElement(timer.Id, left.Id, e => e with { Format = ValueFormat.MinuteSecond, When = "pause_on" });
        ScreenElement typed = editor.AddElement(timer.Id, ElementKind.Input, 0, 1, "pause");
        editor.UpdateElement(timer.Id, typed.Id, e => e with { Format = ValueFormat.MinuteSecond, Align = ElementAlign.Right, When = "!pause_on" });
        ScreenElement bar = editor.AddElement(timer.Id, ElementKind.Bar, 0, 3, "pause");
        editor.UpdateElement(timer.Id, bar.Id, e => e with { Width = 20, Maximum = "90" });
        editor.AddTransition(timer.Id, Key("*"), ScreenDocument.Back);
        editor.AddTransition(timer.Id, new ScreenTrigger(TriggerKind.Key, "kp1", "#", When: "!pause_on"), "");
        editor.UpdateTransition(timer.Id, 1, editor.Document.Find(timer.Id)!.Transitions[1] with { Actions = [new ScreenAction(ActionKind.InputCommit), new ScreenAction(ActionKind.Start, "pause")] });
        editor.AddTransition(timer.Id, Key("#"), "");
        editor.UpdateTransition(timer.Id, 2, editor.Document.Find(timer.Id)!.Transitions[2] with { Actions = [new ScreenAction(ActionKind.Stop, "pause")] });
        editor.AddTransition(timer.Id, Key("D"), "");
        editor.UpdateTransition(timer.Id, 3, editor.Document.Find(timer.Id)!.Transitions[3] with { Actions = [new ScreenAction(ActionKind.InputErase)] });
        editor.AddTransition("home", Key("C"), timer.Id);

        // A notification from anywhere when it runs out, with a beep; any key returns to where it was.
        ScreenNode done = editor.AddScreen("Done", 0, 0);
        ScreenElement doneText = editor.AddElement(done.Id, ElementKind.Text, 0, 1, "{bell} ВРЕМЯ ВЫШЛО {bell}");
        editor.UpdateElement(done.Id, doneText.Id, e => e with { Align = ElementAlign.Center });
        editor.UpdateScreen(done.Id, s => s with { OnEnter = [new ScreenAction(ActionKind.Beep, "bz1", "2000 300")] });
        editor.AddTransition(done.Id, new ScreenTrigger(TriggerKind.AnyKey), ScreenDocument.Back);
        editor.AddTransition(null, new ScreenTrigger(TriggerKind.Finished, Value: "pause"), done.Id);

        // A condition, once, when it becomes true.
        ScreenNode third = editor.AddScreen("Third", 0, 0);
        editor.AddElement(third.Id, ElementKind.Text, 0, 0, "ТРЕТИЙ");
        editor.AddTransition(third.Id, new ScreenTrigger(TriggerKind.AnyKey), ScreenDocument.Back);
        editor.AddTransition(null, new ScreenTrigger(TriggerKind.Condition, Value: "count == 3"), third.Id);
        editor.AddTransition("home", Key("5"), "");
        editor.UpdateTransition("home", 2, editor.Document.Find("home")!.Transitions[2] with { Actions = [new ScreenAction(ActionKind.Add, "count", "1")] });
        return editor;
    }

    [Fact]
    public async Task A_device_made_on_the_tab_runs_key_by_key_on_a_2004()
    {
        Assert.True(_toolchain.Toolchain.IsComplete);
        string project = Project(Parts, Wires);
        Generate(project, Device(DisplayOf(project)).Document, null);

        int at = 1500;
        var keys = new List<string>();
        void Press(int index, int gap = 200)
        {
            keys.AddRange(["--input", string.Create(CultureInfo.InvariantCulture, $"{at}:kp1:k{index}=1"), "--input", string.Create(CultureInfo.InvariantCulture, $"{at + 60}:kp1:k{index}=0")]);
            at += gap;
        }

        const int K1 = 0, K2 = 1, KA = 3, K5 = 5, KC = 11, KStar = 12, K0 = 13, KHash = 14, KD = 15, K8 = 9;
        Press(KA);                    // the menu
        Press(KHash);                 // sound off
        Press(K8);                    // to the choice
        Press(KHash);                 // the next choice
        Press(KStar);                 // back home
        Press(KC);                    // the timer screen
        Press(K1);                    // 1
        Press(K2);                    // 12
        Press(K0);                    // 120
        Press(KD);                    // 12 again
        Press(KHash, 3200);           // 12 seconds: it starts, shown in big digits; home on a key later
        Press(KStar, 10000);          // back home while it runs, and on until it runs out
        Press(KA);                    // any key closes the notification, and only that
        Press(K5);                    // the count, three times, opens another screen once
        Press(K5);
        Press(K5, 500);

        RunResult result = await RunnerProcess.RunProjectAsync(project, ["--run-for", string.Create(CultureInfo.InvariantCulture, $"{at + 500}"), .. keys]);

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.DoesNotContain("warning", result.Stdout, StringComparison.Ordinal);
        string[][] frames = [.. Frames(result, "lcd1")];
        string text = string.Join('\n', frames.Select(f => string.Join('|', f)));
        Assert.Contains(frames, f => f[0].TrimEnd().EndsWith("1/4", StringComparison.Ordinal));
        int menuAtSecond = Array.FindIndex(frames, f => f[0].TrimEnd().EndsWith("2/4", StringComparison.Ordinal));
        Assert.True(menuAtSecond >= 0, text);

        // Typing comes in from the right, and D takes the last digit back.
        int twelve = Array.FindIndex(frames, f => f[1].StartsWith("00:12 ", StringComparison.Ordinal));
        int hundredTwenty = Array.FindIndex(frames, twelve, f => f[1].StartsWith("01:20 ", StringComparison.Ordinal));
        int twelveAgain = Array.FindIndex(frames, hundredTwenty, f => f[1].StartsWith("00:12 ", StringComparison.Ordinal));
        Assert.True(twelve >= 0 && hundredTwenty > twelve && twelveAgain > hundredTwenty, text);

        // Running out on another screen: the notification, with a beep, and only a key closes it.
        Assert.Contains(result.Timeline, e => e.Text == "bz1 frequency=2000");
        int done = Array.FindIndex(frames, f => f[1].Contains("BPEM", StringComparison.Ordinal));
        Assert.True(done > twelveAgain, text);
        int backHome = Array.FindIndex(frames, done, f => !f[1].Contains("BPEM", StringComparison.Ordinal) && f[3].StartsWith("01.01.2026", StringComparison.Ordinal));
        Assert.True(backHome > done, text);

        // The condition opened its screen once, and it is still open.
        Assert.StartsWith("TPET", frames[^1][0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task What_is_kept_in_eeprom_is_still_there_on_the_next_run()
    {
        string project = Project(Parts, Wires);
        var editor = new ScreenEditor(ScreenEditor.Starting(DisplayOf(project), "Hello"));
        editor.AddVariable(VariableKind.Number, "kept", []);
        editor.UpdateVariable("kept", v => v with { Saved = true, Initial = 5, Max = 1000 });
        ScreenElement shown = editor.AddElement("home", ElementKind.Value, 0, 1, "kept");
        editor.UpdateElement("home", shown.Id, e => e with { Width = 6 });
        editor.AddTransition("home", Key("1"), "");
        editor.UpdateTransition("home", 0, editor.Document.Find("home")!.Transitions[0] with { Actions = [new ScreenAction(ActionKind.Add, "kept", "2")] });
        Generate(project, editor.Document, null);

        RunResult first = await RunnerProcess.RunProjectAsync(project, "--run-for", "2000", "--input", "1400:kp1:k0=1", "--input", "1500:kp1:k0=0");
        RunResult second = await RunnerProcess.RunProjectAsync(project, "--run-for", "1500");

        Assert.True(first.ExitCode == 0, first.ToString());
        Assert.Equal("7", Frames(first, "lcd1")[^1][1].Trim());
        Assert.Equal("7", Frames(second, "lcd1")[^1][1].Trim());
    }

    [Theory]
    [InlineData("adafruit")]
    [InlineData("u8g2")]
    [InlineData("gyver")]
    public async Task An_oled_draws_letters_symbols_a_bar_and_big_digits_with_each_library(string library)
    {
        string project = Project(
            """{ "id": "oled1", "type": "oled", "x": 260, "y": 120, "rotation": 0, "flip": false }""",
            ["board.GND oled1.GND", "board.5V oled1.VCC", "board.A4 oled1.SDA", "board.A5 oled1.SCL"]);
        ScreenDisplay display = DisplayOf(project) with
        {
            Library = library switch
            {
                "u8g2" => DisplayLibrary.U8g2,
                "gyver" => DisplayLibrary.GyverOled,
                _ => DisplayLibrary.AdafruitSsd1306,
            },
        };
        var editor = new ScreenEditor(ScreenEditor.Starting(display, "ПРИВЕТ {heart}"));
        editor.AddVariable(VariableKind.Number, "level", []);
        editor.UpdateVariable("level", v => v with { Initial = 50 });
        editor.AddElement("home", ElementKind.BigNumber, 0, 12, "level");
        editor.AddElement("home", ElementKind.Bar, 0, 50, "level");
        Generate(project, editor.Document, null);

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "800", "--quiet");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.DoesNotContain("warning", result.Stdout, StringComparison.Ordinal);
        int lit = result.Timeline.Select(e => PixelLine().Match(e.Text)).Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Last();
        Assert.True(lit > 150, result.Stdout);
    }

    [Fact]
    public async Task A_parallel_1602_loads_its_programmable_characters_the_same()
    {
        string project = Project(
            """{ "id": "lcd1", "type": "lcd", "x": 280, "y": 0, "rotation": 0, "flip": false, "properties": { "interface": "parallel-4" } }""",
            ["lcd1.VSS board.GND", "lcd1.VDD board.5V", "lcd1.RW lcd1.VSS", "lcd1.RS board.D12", "lcd1.E board.D11", "lcd1.D4 board.D10", "lcd1.D5 board.D9", "lcd1.D6 board.D8", "lcd1.D7 board.D7", "lcd1.A board.5V", "lcd1.K board.GND"]);
        Generate(project, new ScreenEditor(ScreenEditor.Starting(DisplayOf(project), "ЖДЁМ {bell}")).Document, null);

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "800", "--quiet");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal("\\0\\1EM \\2", Frames(result, "lcd1")[^1][0].TrimEnd());
    }

    [Fact]
    public async Task An_esp32_keeps_its_data_and_reads_its_keys_the_same()
    {
        string project = RunnerProcess.CopyProject("examples/Esp32Oled");
        File.WriteAllText(Path.Combine(project, "sketch", "Esp32Oled.ino"), Empty);
        ProjectFile file = ProjectFile.Load(project);
        ScreenDisplay display = ScreenHardware.DisplayOf(file.Scene)!;
        var editor = new ScreenEditor(ScreenEditor.Starting(display, "ДАННЫЕ"));
        editor.AddVariable(VariableKind.Number, "kept", []);
        editor.UpdateVariable("kept", v => v with { Saved = true, Initial = 40, Max = 99 });
        editor.AddElement("home", ElementKind.BigNumber, 0, 16, "kept");
        editor.UpdateScreen("home", s => s with { OnEnter = [new ScreenAction(ActionKind.Add, "kept", "1")] });
        GeneratedScreens generated = ScreenCodeGenerator.Generate(editor.Document, ScreenHardware.WiringOf(file.Scene, Esp32DevKit.Profile, display.Part), null);
        string sketch = Path.Combine(project, "sketch");
        File.WriteAllText(Path.Combine(sketch, ScreenCodeGenerator.HeaderFile), generated.Header);
        File.WriteAllText(Path.Combine(sketch, ScreenCodeGenerator.SourceFile), generated.Source);
        File.WriteAllText(Path.Combine(sketch, ScreenCodeGenerator.HooksFile), generated.Hooks);
        string main = Path.Combine(sketch, "Esp32Oled.ino");
        string text = File.ReadAllText(main);
        foreach ((int offset, string insert) in ScreenCodeGenerator.WiringEdits(text).OrderByDescending(e => e.Offset))
        {
            text = text.Insert(offset, insert);
        }

        File.WriteAllText(main, text);

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "800", "--quiet");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.DoesNotContain("warning", result.Stdout, StringComparison.Ordinal);
        Assert.Contains(result.Timeline, e => PixelLine().IsMatch(e.Text));
    }

    [Fact]
    public async Task With_a_ds3231_on_the_schematic_the_clock_is_that_chip()
    {
        string project = Project(
            """
            { "id": "lcd1", "type": "lcd", "x": 280, "y": 0, "rotation": 0, "flip": false, "properties": { "size": "20x4" } },
            { "id": "rtc1", "type": "ds3231", "x": 280, "y": 200, "rotation": 0, "flip": false, "properties": { "start": "fixed", "time": "2031-05-17 08:09:10" } }
            """,
            ["board.GND lcd1.GND", "board.5V lcd1.VCC", "board.A4 lcd1.SDA", "board.A5 lcd1.SCL", "board.GND rtc1.GND", "board.5V rtc1.VCC", "board.A4 rtc1.SDA", "board.A5 rtc1.SCL"]);
        var editor = new ScreenEditor(ScreenEditor.Starting(DisplayOf(project), "CLOCK"));
        ScreenElement date = editor.AddElement("home", ElementKind.Value, 0, 1, "date");
        editor.UpdateElement("home", date.Id, e => e with { Width = 10, Format = ValueFormat.DateYearFirst });
        ScreenElement time = editor.AddElement("home", ElementKind.Value, 0, 2, "clock");
        editor.UpdateElement("home", time.Id, e => e with { Width = 8, Format = ValueFormat.HourMinuteSecond });
        Generate(project, editor.Document, null);

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "2000", "--quiet");

        Assert.True(result.ExitCode == 0, result.ToString());
        string[] last = Frames(result, "lcd1")[^1];
        Assert.StartsWith("2031-05-17", last[1], StringComparison.Ordinal);
        Assert.StartsWith("08:09:1", last[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_hook_prints_cyrillic_and_draws_its_own_part_of_a_screen()
    {
        string project = Project(Parts, Wires);
        var editor = new ScreenEditor(ScreenEditor.Starting(DisplayOf(project), "Hook"));
        ScreenElement drawn = editor.AddElement("home", ElementKind.Drawn, 0, 1, "table");
        editor.UpdateScreen("home", s => s with { KeyHook = true, TickHook = true });
        Assert.NotNull(drawn);
        Generate(
            project,
            editor.Document,
            "#include \"ui_screens.h\"\nstatic char last = '-';\nstatic int ticks;\nvoid uiCode_table() {\n  uiOut.print(F(\"ДА \"));\n  uiOut.print(last);\n  uiAt(0, 2);\n  uiOut.print(ticks > 0 ? 1 : 0);\n}\nbool uiKey_home(char key) {\n  last = key;\n  return true;\n}\nvoid uiTick_home() {\n  ticks++;\n}\n");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "2000", "--input", "1400:kp1:k5=1", "--input", "1500:kp1:k5=0");

        Assert.True(result.ExitCode == 0, result.ToString());
        string[] last = Frames(result, "lcd1")[^1];
        Assert.EndsWith(" 5", last[1].TrimEnd(), StringComparison.Ordinal);
        Assert.StartsWith("1", last[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_screen_wanting_more_than_eight_symbols_still_shows_something_of_each()
    {
        string project = Project(Parts, Wires);
        var editor = new ScreenEditor(ScreenEditor.Starting(DisplayOf(project), "БГДЖИЙЛПУФЦ"));
        Generate(project, editor.Document, null);

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "2000", "--quiet");

        Assert.True(result.ExitCode == 0, result.ToString());
        string row = Frames(result, "lcd1")[^1][0];
        Assert.Equal(11, Regex.Count(row.TrimEnd(), @"\\[0-7]|[^\\]"));
    }

    private static ScreenDisplay DisplayOf(string project) => ScreenHardware.DisplayOf(ProjectFile.Load(project).Scene)!;

    private static void Generate(string project, ScreenDocument document, string? hooks)
    {
        ScreenWiring wiring = ScreenHardware.WiringOf(ProjectFile.Load(project).Scene, Nano.Profile, document.Display.Part);
        GeneratedScreens generated = ScreenCodeGenerator.Generate(document, wiring, hooks);
        string sketch = Path.Combine(project, "sketch");
        File.WriteAllText(Path.Combine(sketch, ScreenCodeGenerator.HeaderFile), generated.Header);
        File.WriteAllText(Path.Combine(sketch, ScreenCodeGenerator.SourceFile), generated.Source);
        File.WriteAllText(Path.Combine(sketch, ScreenCodeGenerator.HooksFile), generated.Hooks);
        string path = Path.Combine(sketch, "sketch.ino");
        string text = File.ReadAllText(path);
        foreach ((int offset, string insert) in ScreenCodeGenerator.WiringEdits(text, document.Display.Existing).OrderByDescending(e => e.Offset))
        {
            text = text.Insert(offset, insert);
        }

        File.WriteAllText(path, text);
    }

    private static string Project(string parts, IEnumerable<string> wires)
    {
        string project = Directory.CreateTempSubdirectory("ardusim-system-").FullName;
        var json = new StringBuilder();
        json.Append("""{ "schemaVersion": 1, "board": "arduino-nano-v3", "sketchPath": "sketch", "scene": { "components": [ { "id": "board", "type": "arduino-nano-v3", "x": 0, "y": 0, "rotation": 0, "flip": false }, """);
        json.Append(parts).Append(""" ], "wires": [ """);
        int number = 0;
        json.AppendJoin(", ", wires.Select(wire =>
        {
            string[] ends = wire.Split(' ');
            string[] from = ends[0].Split('.', 2);
            string[] to = ends[1].Split('.', 2);
            number++;
            return string.Create(CultureInfo.InvariantCulture, $$"""{ "id": "w{{number}}", "from": { "component": "{{from[0]}}", "pin": "{{from[1]}}" }, "to": { "component": "{{to[0]}}", "pin": "{{to[1]}}" }, "waypoints": [], "color": "auto" }""");
        }));
        json.Append(""" ] }, "settings": { "speed": 1.0, "serial": { "baud": 9600, "lineEnding": "LF" }, "libraryPaths": [] } }""");
        File.WriteAllText(Path.Combine(project, "project.simproj.json"), json.ToString());
        Directory.CreateDirectory(Path.Combine(project, "sketch"));
        File.WriteAllText(Path.Combine(project, "sketch", "sketch.ino"), Empty);
        return project;
    }

    private static List<string[]> Frames(RunResult result, string id) =>
        [.. result.Timeline
            .Where(e => e.Text.StartsWith(id + ": \"", StringComparison.Ordinal))
            .Select(e => QuotedRow().Matches(e.Text).Select(m => m.Groups[1].Value).ToArray())];

    [GeneratedRegex("\"([^\"]*)\"")]
    private static partial Regex QuotedRow();

    [GeneratedRegex(@"^\w+: (\d+) px on$")]
    private static partial Regex PixelLine();
}
