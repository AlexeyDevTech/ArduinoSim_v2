using System.Globalization;

namespace ArduSim.Integration.Tests;

/// <summary>Builds real sketches with the real toolchain and runs them in the real Runner.</summary>
public sealed class HeadlessRunTests(ToolchainFixture toolchain)
{
    private readonly ToolchainFixture _toolchain = toolchain;

    [Fact]
    public async Task Blink_toggles_D13_every_1000_ms_of_virtual_time()
    {
        Assert.True(_toolchain.Toolchain.IsComplete);
        string project = RunnerProcess.CopyProject("examples/Blink");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "3000");

        Assert.True(result.ExitCode == 0, result.ToString());
        var toggles = result.Timeline.Where(e => e.Text is "D13 HIGH" or "D13 LOW").ToList();
        Assert.Equal(["D13 LOW", "D13 HIGH", "D13 LOW", "D13 HIGH"], toggles.Select(t => t.Text));
        var blinks = toggles.Skip(1).ToList();
        Assert.All(blinks.Zip(blinks.Skip(1)), pair => Assert.Equal(1000.0, pair.Second.Milliseconds - pair.First.Milliseconds, 0.1));
        Assert.Contains("stopped (time limit) at 3000.", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Blink_lights_the_external_led_and_the_onboard_L()
    {
        string project = RunnerProcess.CopyProject("examples/Blink");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "1500");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Contains(result.Timeline, e => e.Text == "led1 brightness=1");
        Assert.Contains(result.Timeline, e => e.Text == "board L=1");
        Assert.Contains(result.Timeline, e => e.Text == "led1 brightness=0" && e.Milliseconds > 999);
    }

    [Fact]
    public async Task Button_example_toggles_the_led_on_each_press()
    {
        string project = RunnerProcess.CopyProject("examples/Button");

        RunResult result = await RunnerProcess.RunProjectAsync(
            project, "--run-for", "2000",
            "--input", "500:btn1:pressed=1", "--input", "600:btn1:pressed=0",
            "--input", "1500:btn1:pressed=1", "--input", "1600:btn1:pressed=0");

        Assert.True(result.ExitCode == 0, result.ToString());
        TimelineEvent on = result.Timeline.Single(e => e.Text == "led1 brightness=1");
        TimelineEvent off = result.Timeline.Single(e => e.Text == "led1 brightness=0" && e.Milliseconds > 1000);
        Assert.InRange(on.Milliseconds, 520, 530);   // 20 ms debounce after the press at 500 ms
        Assert.InRange(off.Milliseconds, 1520, 1530);
        Assert.Equal(["Press the button to toggle the LED.", "LED on", "LED off"], result.SerialLines);
        Assert.DoesNotContain("floating", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Floating_input_example_warns_once_and_reads_high_while_pressed()
    {
        string project = RunnerProcess.CopyProject("examples/FloatingInput");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "1200", "--input", "700:btn1:pressed=1");

        Assert.True(result.ExitCode == 0, result.ToString());
        TimelineEvent warning = Assert.Single(result.Timeline, e => e.Text.StartsWith("warning: D2 is floating", StringComparison.Ordinal));
        Assert.Contains("board.D2, btn1.1", warning.Text, StringComparison.Ordinal);
        Assert.Equal("1", result.SerialLines.Last());
    }

    [Fact]
    public async Task Serial_output_is_paced_at_the_baud_rate()
    {
        string project = RunnerProcess.CopyProject("tests/ArduSim.Integration.Tests/Projects/SerialTiming");
        string dump = Path.Combine(project, "serial.txt");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "500", "--dump-serial", dump);

        Assert.True(result.ExitCode == 0, result.ToString());
        long queued = Value(result, "queued_us=");
        long sent = Value(result, "sent_us=");
        const double frame = 1e6 * 10 / 9600; // µs per 8N1 byte
        Assert.InRange(queued, (long)(35 * frame), (long)(36 * frame + 500));
        Assert.InRange(sent, (long)(100 * frame), (long)(100 * frame + 500));
        Assert.StartsWith(new string('x', 100) + "\r\nqueued_us=", File.ReadAllText(dump), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Static_constructors_prototypes_eeprom_and_warnings_work_end_to_end()
    {
        string project = RunnerProcess.CopyProject("tests/ArduSim.Integration.Tests/Projects/Features");

        RunResult first = await RunnerProcess.RunProjectAsync(project, "--run-for", "1000");
        RunResult second = await RunnerProcess.RunProjectAsync(project, "--run-for", "100");

        Assert.True(first.ExitCode == 0, first.ToString());
        TimelineEvent early = first.Timeline.First(e => e.Text == "D5 HIGH");
        Assert.True(early.Milliseconds < 0.01, "the global object's constructor ran before setup()");
        Assert.Contains("bootbootboot", first.SerialLines);
        Assert.Contains("pi=3.142", first.SerialLines);
        Assert.Contains("runs=1", first.SerialLines);
        Assert.Contains("runs=2", second.SerialLines);
        Assert.Single(first.Timeline, e => e.Text.Contains("D2 is floating", StringComparison.Ordinal));
        Assert.Equal(4, first.SerialLines.Count(l => l.StartsWith("t=", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Build_errors_point_at_the_ino_line()
    {
        string project = RunnerProcess.CopyProject("tests/ArduSim.Integration.Tests/Projects/BuildError");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "100");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("BuildError.ino(6,3): error: use of undeclared identifier 'digitalWrit'", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Firmware_busy_without_api_calls_is_stopped()
    {
        string project = RunnerProcess.CopyProject("tests/ArduSim.Integration.Tests/Projects/Runaway");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "1000");

        Assert.Equal(4, result.ExitCode);
        Assert.Contains("busy for 2 s without calling the Arduino API", result.Stderr, StringComparison.Ordinal);
    }

    private static long Value(RunResult result, string prefix) =>
        long.Parse(result.SerialLines.Single(l => l.StartsWith(prefix, StringComparison.Ordinal))[prefix.Length..], CultureInfo.InvariantCulture);
}
