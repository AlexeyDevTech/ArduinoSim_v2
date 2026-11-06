using ArduSim.Build.Import;

namespace ArduSim.Integration.Tests;

/// <summary>The M11 boards, the logic analyzer's recording and a PlatformIO project, run headlessly (ADR-014, ADR-015).</summary>
public sealed class ToolsRunTests
{
    [Fact]
    public async Task UnoBlink_example_blinks_the_board_led_at_the_pots_rate()
    {
        string project = RunnerProcess.CopyProject("examples/UnoBlink");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "1200");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal(["Uno ready"], result.SerialLines);
        Assert.Contains(result.Timeline, e => e.Text == "D13 LOW" && e.Milliseconds is > 500 and < 560);
        Assert.Contains(result.Timeline, e => e.Text == "led1 brightness=1");
        Assert.DoesNotContain("warning", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LedCurrents_example_shows_what_each_resistor_lets_through_and_warns_without_one()
    {
        string project = RunnerProcess.CopyProject("examples/LedCurrents");
        string table = Path.Combine(project, "circuit.txt");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "1200", "--input", "600:btn1:pressed=1", "--dump-circuit", table);

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Contains("Divider: 1.60 V", result.SerialLines);
        string[] lines = await File.ReadAllLinesAsync(table, TestContext.Current.CancellationToken);
        Assert.Contains("led1.A 1.733 V 13.333 mA peak 13.333 mA", lines);
        Assert.Contains("led2.A 1.607 V 0.718 mA peak 0.718 mA", lines);
        Assert.Contains("led3.A 2.571 V 97.143 mA peak 97.143 mA", lines);
        Assert.Contains(result.Timeline, e => e.Text == "led1 brightness=1");
        Assert.Contains(result.Timeline, e => e.Text == "led2 brightness=0.379");
        Assert.Contains("warning: D6 carries 97 mA", result.Stdout, StringComparison.Ordinal);
        Assert.Contains("warning: led3 carries 97 mA", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_library_in_the_projects_libs_folder_needs_no_setting()
    {
        string project = RunnerProcess.CopyProject("examples/Blink");
        string library = Path.Combine(project, "libs", "Greeter", "src");
        Directory.CreateDirectory(library);
        await File.WriteAllTextAsync(Path.Combine(project, "libs", "Greeter", "library.properties"), "name=Greeter\nversion=1.0\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(library, "Greeter.h"), "#pragma once\nconst char* greeting();\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(library, "Greeter.cpp"), "#include \"Greeter.h\"\nconst char* greeting() { return \"hello from libs\"; }\n", TestContext.Current.CancellationToken);

        // A half-installed library is not a library.
        Directory.CreateDirectory(Path.Combine(project, "libs", ".staging-1"));
        await File.WriteAllTextAsync(Path.Combine(project, "libs", ".staging-1", "Greeter.h"), "#error this one must not be found\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(project, "sketch", "Blink.ino"),
            "#include <Greeter.h>\nvoid setup() { Serial.begin(9600); Serial.println(greeting()); }\nvoid loop() {}\n",
            TestContext.Current.CancellationToken);

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "50");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal(["hello from libs"], result.SerialLines);
    }

    // Reported while the scene is built, before the Runner listens: it used to be lost.
    [Fact]
    public async Task An_led_across_the_rails_warns_from_the_very_start()
    {
        string project = RunnerProcess.CopyProject("examples/LedCurrents");
        string file = Path.Combine(project, "project.simproj.json");
        string text = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(file, text.Replace("\"pin\": \"D6\"", "\"pin\": \"5V\"", StringComparison.Ordinal), TestContext.Current.CancellationToken);

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "10");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Contains("warning: led3 carries 340 mA", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_uno_sketch_cannot_use_a6()
    {
        string project = RunnerProcess.CopyProject("examples/UnoBlink");
        string sketch = Path.Combine(project, "sketch", "UnoBlink.ino");
        await File.WriteAllTextAsync(sketch, "void setup() { analogRead(A6); }\nvoid loop() {}\n", TestContext.Current.CancellationToken);

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "10");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("A6", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mega2560Ports_example_drives_port_a_and_writes_to_two_serial_ports()
    {
        string project = RunnerProcess.CopyProject("examples/Mega2560Ports");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "800");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Contains(result.Timeline, e => e.Text == "D22 HIGH");
        Assert.Contains(result.Timeline, e => e.Text == "D23 HIGH");
        Assert.Contains(result.Timeline, e => e.Text == "led1 brightness=1");
        Assert.Contains(result.Timeline, e => e.Text == "Serial: Mega ready");
        Assert.Contains(result.Timeline, e => e.Text == "Serial: A15 = 767");
        Assert.Contains(result.Timeline, e => e.Text == "Serial1: step 1");
    }

    [Fact]
    public async Task ProMini3v3_example_runs_at_8_mhz_on_3_3_volts()
    {
        string project = RunnerProcess.CopyProject("examples/ProMini3v3");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "600");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Contains("F_CPU = 8000000", result.SerialLines);
        Assert.Contains("1.65 V", result.SerialLines); // half of 3.3 V, read with a 10-bit ADC over the supply
    }

    [Fact]
    public async Task BuzzerMelody_example_shows_its_frequencies()
    {
        string project = RunnerProcess.CopyProject("examples/BuzzerMelody");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "500");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Contains(result.Timeline, e => e.Text == "bz1 frequency=262");
        Assert.Contains(result.Timeline, e => e.Text == "bz1 frequency=294");
    }

    [Fact]
    public async Task LogicAnalyzerDemo_example_records_edges_and_every_hardware_bus()
    {
        string project = RunnerProcess.CopyProject("examples/LogicAnalyzerDemo");
        string trace = Path.Combine(project, "trace.txt");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "700", "--quiet", "--dump-trace", trace);

        Assert.True(result.ExitCode == 0, result.ToString());
        string[] lines = await File.ReadAllLinesAsync(trace, TestContext.Current.CancellationToken);
        Assert.Contains(lines, l => l.StartsWith("B ", StringComparison.Ordinal) && l.Contains(" I2cStart D0 01 18 19 ", StringComparison.Ordinal)); // DS3231 at 0x68, write, ACK
        Assert.Contains(lines, l => l.StartsWith("B ", StringComparison.Ordinal) && l.Contains(" SpiByte 0F ", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("B ", StringComparison.Ordinal) && l.Contains(" UartTx ", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("P ", StringComparison.Ordinal) && l.EndsWith(" 3 High 0", StringComparison.Ordinal)); // shiftOut's clock on D3
        Assert.DoesNotContain("overflow", lines);
    }

    [Fact]
    public async Task A_platformio_project_builds_from_plain_cpp_with_its_flags_and_include_folder()
    {
        string project = RunnerProcess.CopyProject("examples/Blink");
        foreach (string entry in Directory.EnumerateFileSystemEntries(project))
        {
            if (Directory.Exists(entry))
            {
                Directory.Delete(entry, recursive: true);
            }
            else
            {
                File.Delete(entry);
            }
        }

        Directory.CreateDirectory(Path.Combine(project, "src", "app"));
        Directory.CreateDirectory(Path.Combine(project, "include"));
        Directory.CreateDirectory(Path.Combine(project, "lib", "Greeter", "src"));
        CancellationToken cancel = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Path.Combine(project, "platformio.ini"), """
            [env:nano]
            platform = atmelavr
            board = nanoatmega328
            framework = arduino
            monitor_speed = 9600
            build_flags = -DGREETING=\"hello\" -DTIMES=2
            """, cancel);
        await File.WriteAllTextAsync(Path.Combine(project, "include", "config.h"), "#define BAUD 9600\n", cancel);
        await File.WriteAllTextAsync(Path.Combine(project, "lib", "Greeter", "src", "Greeter.h"), "void greet();\n", cancel);
        await File.WriteAllTextAsync(Path.Combine(project, "lib", "Greeter", "src", "Greeter.cpp"), """
            #include <Arduino.h>
            #include "Greeter.h"
            void greet() { for (int i = 0; i < TIMES; i++) Serial.println(GREETING); }
            """, cancel);
        await File.WriteAllTextAsync(Path.Combine(project, "src", "app", "start.cpp"), """
            #include <Arduino.h>
            #include "config.h"
            void start() { Serial.begin(BAUD); }
            """, cancel);
        await File.WriteAllTextAsync(Path.Combine(project, "src", "main.cpp"), """
            #include <Arduino.h>
            #include <Greeter.h>
            void start();
            void setup() { start(); greet(); }
            void loop() {}
            """, cancel);

        PlatformIoProject imported = PlatformIoProject.Read(project);
        ArduSim.Core.Projects.ProjectTemplate.CreateAround(project, imported.BoardId, imported.SourceDirectory, new ArduSim.Core.Projects.ProjectSettings
        {
            LibraryPaths = imported.LibraryDirectories,
            IncludePaths = imported.IncludeDirectories,
            Defines = imported.Defines,
        });

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "100");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal(["hello", "hello"], result.SerialLines);
    }
}
