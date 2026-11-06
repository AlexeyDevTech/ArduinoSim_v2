using System.Text.RegularExpressions;
using ArduSim.Core.Projects;
using ArduSim.Core.Screens;

namespace ArduSim.Integration.Tests;

/// <summary>What the constructor writes into a hand-written sketch (ADR-019), built by the real toolchain and run: new code must compile and do what the map says.</summary>
public sealed partial class ScreenConstructorRunTests(ToolchainFixture toolchain)
{
    private const string Sketch = """
        #include <LiquidCrystal_I2C.h>
        #include <Keypad.h>

        LiquidCrystal_I2C lcd(0x27, 16, 2);
        const char KEYS[4][4] = {{'1','2','3','A'},{'4','5','6','B'},{'7','8','9','C'},{'*','0','#','D'}};
        byte ROWS[4] = {9, 8, 7, 6};
        byte COLS[4] = {5, 4, 3, 2};
        Keypad keypad(makeKeymap(KEYS), ROWS, COLS, 4, 4);

        enum Screen { HOME, INFO };
        Screen screen = HOME;

        void enterScreen(Screen next) {
          screen = next;
          lcd.clear();
        }

        void handleKey(char key) {
          switch (screen) {
            case HOME:
              if (key == '#') enterScreen(INFO);
              break;
            case INFO:
              if (key == '*') enterScreen(HOME);
              break;
          }
        }

        void drawHome() {
          lcd.setCursor(0, 0);
          lcd.print("Home");
        }

        void draw() {
          switch (screen) {
            case HOME:
              drawHome();
              break;
            case INFO:
              lcd.setCursor(0, 0);
              lcd.print("Info");
              break;
          }
        }

        void setup() {
          lcd.init();
          lcd.backlight();
        }

        void loop() {
          char key = keypad.getKey();
          if (key) handleKey(key);
          draw();
          delay(10);
        }
        """;

    private readonly ToolchainFixture _toolchain = toolchain;

    private static string Project()
    {
        string project = RunnerProcess.CopyProject("examples/Keypad4x4");
        string scene = File.ReadAllText(Path.Combine(project, ProjectFile.FileName));
        scene = scene.Replace(
            """{ "id": "kp1", "type": "keypad", "x": 130, "y": 60, "rotation": 0, "flip": false }""",
            """{ "id": "kp1", "type": "keypad", "x": 130, "y": 60, "rotation": 0, "flip": false }, { "id": "lcd1", "type": "lcd", "x": 480, "y": 300, "rotation": 0, "flip": false }""",
            StringComparison.Ordinal);
        scene = scene.Replace(
            """{ "id": "w1", "from": { "component": "kp1", "pin": "R1" }""",
            """{ "id": "l1", "from": { "component": "board", "pin": "GND" }, "to": { "component": "lcd1", "pin": "GND" }, "waypoints": [], "color": "auto" }, { "id": "l2", "from": { "component": "board", "pin": "5V" }, "to": { "component": "lcd1", "pin": "VCC" }, "waypoints": [], "color": "auto" }, { "id": "l3", "from": { "component": "board", "pin": "A4" }, "to": { "component": "lcd1", "pin": "SDA" }, "waypoints": [], "color": "auto" }, { "id": "l4", "from": { "component": "board", "pin": "A5" }, "to": { "component": "lcd1", "pin": "SCL" }, "waypoints": [], "color": "auto" }, { "id": "w1", "from": { "component": "kp1", "pin": "R1" }""",
            StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(project, ProjectFile.FileName), scene);
        foreach (string file in Directory.EnumerateFiles(Path.Combine(project, "sketch")))
        {
            File.Delete(file);
        }

        File.WriteAllText(Path.Combine(project, "sketch", "sketch.ino"), Sketch);
        return project;
    }

    private static string Edited(string text, IReadOnlyList<SourceEdit>? edits)
    {
        Assert.NotNull(edits);
        Assert.All(edits, e => Assert.True(e.Fits(text), e.ToString()));
        return ScreenCodeEdits.Apply(text, edits);
    }

    private static ScreenDocument Read(string text) => ScreenCodeAnalyzer.Analyze([("sketch.ino", text)])!;

    [Fact]
    public async Task A_screen_an_element_and_transitions_added_to_a_sketch_build_and_work()
    {
        Assert.True(_toolchain.Toolchain.IsComplete);
        string project = Project();
        string text = Sketch;

        // A new screen, with text of its own, reached by a key from a screen, and back from any screen.
        text = Edited(text, ScreenCodeEdits.AddScreen(Read(text), "ABOUT"));
        ScreenDocument document = Read(text);
        Assert.NotNull(document.Find("ABOUT"));
        text = Edited(text, ScreenCodeEdits.AddElement(document.Find("ABOUT")!, document.Display, ElementKind.Text, 2, 1, 0, 0, "Made here"));
        document = Read(text);
        text = Edited(text, ScreenCodeEdits.AddTransition(document, "HOME", "5", "ABOUT"));
        document = Read(text);
        text = Edited(text, ScreenCodeEdits.AddTransition(document, null, "0", "HOME"));

        // And a new line on a screen that has its own function, and another transition taken out.
        document = Read(text);
        text = Edited(text, ScreenCodeEdits.AddElement(document.Find("HOME")!, document.Display, ElementKind.Text, 0, 1, 0, 0, "More"));
        document = Read(text);
        ScreenTransition old = document.Find("INFO")!.Transitions.Single(t => t.Target == "HOME" && t.Trigger.Value == "*");
        text = Edited(text, ScreenCodeEdits.Remove(old.RemoveSpans));

        document = Read(text);
        Assert.Contains(document.Find("HOME")!.Transitions, t => t is { Target: "ABOUT", Trigger.Value: "5" });
        Assert.Contains(document.FromAnywhere, t => t is { Target: "HOME", Trigger.Value: "0" });
        Assert.DoesNotContain(document.Find("INFO")!.Transitions, t => t.Trigger.Value == "*");
        Assert.Contains(document.Find("HOME")!.Elements, e => e is { Text: "More", X: 0, Y: 1 });
        Assert.Contains(document.Find("ABOUT")!.Elements, e => e is { Text: "Made here", X: 2, Y: 1 });

        File.WriteAllText(Path.Combine(project, "sketch", "sketch.ino"), text);

        // k5 is the keypad's 5, k13 its 0, k14 its #.
        RunResult result = await RunnerProcess.RunProjectAsync(
            project,
            "--run-for", "2200", "--quiet",
            "--input", "1200:kp1:k5=1", "--input", "1300:kp1:k5=0",
            "--input", "1600:kp1:k13=1", "--input", "1700:kp1:k13=0",
            "--input", "1900:kp1:k14=1", "--input", "2000:kp1:k14=0");

        Assert.True(result.ExitCode == 0, result.ToString());
        string[] rows = [.. result.Timeline
            .Where(e => e.Text.StartsWith("lcd1: \"", StringComparison.Ordinal))
            .Select(e => string.Join('|', QuotedRow().Matches(e.Text).Select(m => m.Groups[1].Value.TrimEnd())))];
        string joined = string.Join('\n', rows);
        int home = Array.IndexOf(rows, "Home|More");
        Assert.True(home >= 0, joined);
        int about = Array.IndexOf(rows, "ABOUT|  Made here", home);
        Assert.True(about > home, joined);
        int backHome = Array.IndexOf(rows, "Home|More", about);
        Assert.True(backHome > about, joined);
        int info = Array.IndexOf(rows, "Info|", backHome);
        Assert.True(info > backHome, joined);
    }

    // The owner's NanoOS has this shape: libraries reached only through headers under src, and the free-SRAM idiom.
    [Fact]
    public async Task A_sketch_with_its_includes_in_headers_under_src_builds_and_is_told_about_free_sram()
    {
        string project = Project();
        string sketch = Path.Combine(project, "sketch");
        Directory.CreateDirectory(Path.Combine(sketch, "src"));
        File.WriteAllText(Path.Combine(sketch, "src", "App.h"), "#pragma once\n#include <Arduino.h>\n#include <LiquidCrystal_I2C.h>\nvoid appBegin();\nuint16_t freeSram();\n");
        File.WriteAllText(Path.Combine(sketch, "src", "App.cpp"), "#include \"App.h\"\nstatic LiquidCrystal_I2C lcd(0x27, 16, 2);\nvoid appBegin() {\n  lcd.init();\n  lcd.backlight();\n  lcd.print(\"src\");\n}\nuint16_t freeSram() {\n  extern int __heap_start, *__brkval;\n  int v;\n  return (uint16_t)((uintptr_t)&v - (__brkval ? (uintptr_t)__brkval : (uintptr_t)&__heap_start));\n}\n");
        File.WriteAllText(Path.Combine(sketch, "sketch.ino"), "#include \"src/App.h\"\nvoid setup() {\n  appBegin();\n  Serial.begin(9600);\n  Serial.println(freeSram() > 0 ? \"ok\" : \"ok\");\n}\nvoid loop() {\n}\n");

        string firmware = await RunnerProcess.BuildFirmwareAsync(_toolchain.Toolchain, project);
        Assert.True(File.Exists(firmware));
        string log = await File.ReadAllTextAsync(Path.Combine(project, ".build", "build.log"), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("not found", log, StringComparison.Ordinal);

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "1500", "--quiet");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Contains("ok", result.SerialLines);
        Assert.Contains("__brkval", result.Stdout + result.Stderr, StringComparison.Ordinal);
    }

    [GeneratedRegex("\"([^\"]*)\"")]
    private static partial Regex QuotedRow();
}
