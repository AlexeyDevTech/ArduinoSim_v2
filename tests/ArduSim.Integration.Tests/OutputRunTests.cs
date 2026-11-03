using System.Text.RegularExpressions;

namespace ArduSim.Integration.Tests;

/// <summary>The M9 examples (outputs and modules) and their libraries, run headlessly (ADR-012).</summary>
public sealed class OutputRunTests
{
    [Fact]
    public async Task ServoKnob_example_moves_the_servo_with_the_pot_through_the_servo_library()
    {
        string project = RunnerProcess.CopyProject("examples/ServoKnob");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "1500", "--input", "1000:pot1:value=1");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal(["Angle: 90", "Angle: 0"], result.SerialLines);
        Assert.Contains(result.Timeline, e => e.Text == "servo1 angle=0" && e.Milliseconds > 1000);
    }

    [Fact]
    public async Task Relay_example_switches_the_led_10_ms_after_the_input()
    {
        string project = RunnerProcess.CopyProject("examples/Relay");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "2100");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal(["Relay on", "Relay off", "Relay on"], result.SerialLines);
        Assert.Contains(result.Timeline, e => e.Text == "led1 brightness=1" && e.Milliseconds > 9.9 && e.Milliseconds < 10.1);
        Assert.Contains(result.Timeline, e => e.Text == "led1 brightness=0" && e.Milliseconds > 1004.9 && e.Milliseconds < 1005.1);
    }

    [Fact]
    public async Task SevenSegment_example_multiplexes_four_digits_with_sevseg()
    {
        string project = RunnerProcess.CopyProject("examples/SevenSegment");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "1300", "--quiet");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Contains(result.Timeline, e => e.Text == "seg1: \"  0.5\"");
        Assert.Contains(result.Timeline, e => e.Text == "seg1: \"  1.2\"");
    }

    [Fact]
    public async Task Tm1637_example_counts_seconds_with_a_blinking_colon()
    {
        string project = RunnerProcess.CopyProject("examples/Tm1637");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "2300", "--quiet");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal(["tm1: \"00:00\"", "tm1: \"0000\"", "tm1: \"00:01\"", "tm1: \"0001\"", "tm1: \"00:02\""], result.Timeline.Select(e => e.Text).Where(t => t.StartsWith("tm1:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task MatrixParola_example_scrolls_text_over_hardware_spi()
    {
        string project = RunnerProcess.CopyProject("examples/MatrixParola");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "3000", "--quiet");

        Assert.True(result.ExitCode == 0, result.ToString());
        int[] lit = [.. result.Timeline.Select(e => Regex.Match(e.Text, @"^mx1: (\d+) px on$")).Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))];
        Assert.True(lit.Length > 20, string.Join(",", lit));
        Assert.True(lit.Max() > 60, string.Join(",", lit));
    }

    [Fact]
    public async Task MatrixLedControl_example_draws_a_beating_heart_bit_banged()
    {
        string project = RunnerProcess.CopyProject("examples/MatrixLedControl");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "900", "--quiet");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal(["mx1: 40 px on", "mx1: 20 px on", "mx1: 40 px on"], result.Timeline.Select(e => e.Text).Where(t => t.StartsWith("mx1:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task NeoPixelRing_example_shows_a_rainbow_through_adafruit_neopixel()
    {
        string project = RunnerProcess.CopyProject("examples/NeoPixelRing");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "100", "--quiet");

        Assert.True(result.ExitCode == 0, result.ToString());
        string first = result.Timeline.First(e => e.Text.StartsWith("ring1: ", StringComparison.Ordinal)).Text;
        Assert.Equal(16, first.Split(' ').Length - 1);
        Assert.StartsWith("ring1: #500000 ", first, StringComparison.Ordinal); // red at hue 0, brightness 80
    }

    [Fact]
    public async Task FastLedMatrix_example_lights_the_whole_panel_through_fastled()
    {
        string project = RunnerProcess.CopyProject("examples/FastLedMatrix");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "100", "--quiet");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Contains(result.Timeline, e => e.Text == "panel1: 64 of 64 lit");
    }

    [Fact]
    public async Task Ds3231Clock_example_reads_the_time_and_takes_the_alarm_on_sqw()
    {
        string project = RunnerProcess.CopyProject("examples/Ds3231Clock");
        string file = Path.Combine(project, "project.simproj.json");
        await File.WriteAllTextAsync(file, (await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken)).Replace("\"start\": \"computer\"", "\"start\": \"fixed\", \"time\": \"2026-06-01 08:00:27\"", StringComparison.Ordinal), TestContext.Current.CancellationToken);

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "4500", "--quiet");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal(
            ["2026-06-01 08:00:27  23.50 C", "2026-06-01 08:00:28  23.50 C", "2026-06-01 08:00:29  23.50 C", "2026-06-01 08:00:30  23.50 C", "Alarm: half past the minute", "2026-06-01 08:00:31  23.50 C"],
            result.SerialLines);
    }

    [Fact]
    public async Task SdCardLog_example_reads_its_config_and_leaves_the_log_in_the_project_folder()
    {
        string project = RunnerProcess.CopyProject("examples/SdCardLog");
        string log = Path.Combine(project, "sd", "log.csv");
        Assert.False(File.Exists(log));

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "2500", "--quiet", "--input", "1500:pot1:value=1");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal(["Card ready", "Logging every 1000 ms", "Logged 767", "Logged 767", "Logged 0"], result.SerialLines);
        string[] lines = await File.ReadAllLinesAsync(log, TestContext.Current.CancellationToken);
        Assert.Equal(3, lines.Length);
        Assert.EndsWith(",0", lines[^1], StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(project, "sd", "config.txt")));
    }

    [Fact]
    public async Task Esp32_runs_esp32servo_neopixel_fastled_and_sd()
    {
        string project = RunnerProcess.CopyProject("tests/ArduSim.Integration.Tests/Projects/Esp32Outputs");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "500");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal(["sd ok"], result.SerialLines);
        Assert.Contains(result.Timeline, e => e.Text == "servo1 angle=41.8");
        Assert.Contains(result.Timeline, e => e.Text == "strip1: #000000 #0A141E #000000");
        Assert.Contains(result.Timeline, e => e.Text == "strip2: #008000 #000000");
        Assert.Equal("written by the ESP32", (await File.ReadAllTextAsync(Path.Combine(project, "sd", "esp32.txt"), TestContext.Current.CancellationToken)).TrimEnd());
        Assert.DoesNotContain("warning", result.Stdout, StringComparison.Ordinal);
    }
}
