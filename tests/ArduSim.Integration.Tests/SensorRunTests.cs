namespace ArduSim.Integration.Tests;

/// <summary>The M8 examples (inputs and sensors) and their libraries, run headlessly with scripted inputs (ADR-011).</summary>
public sealed class SensorRunTests
{
    [Fact]
    public async Task RotaryEncoder_example_counts_detents_both_ways_and_resets_on_the_button()
    {
        string project = RunnerProcess.CopyProject("examples/RotaryEncoder");

        RunResult result = await RunnerProcess.RunProjectAsync(
            project, "--run-for", "400", "--input", "100:enc1:turn=1", "--input", "150:enc1:turn=2", "--input", "250:enc1:turn=-1", "--input", "300:enc1:pressed=1", "--input", "350:enc1:pressed=0");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal(["Turn the knob.", "Position: 1", "Position: 2", "Position: 3", "Position: 2", "Button pressed, position reset"], result.SerialLines);
    }

    [Fact]
    public async Task Keypad4x4_example_reads_keys_through_the_keypad_library()
    {
        string project = RunnerProcess.CopyProject("examples/Keypad4x4");
        string[] keys = ["k0", "k1", "k2", "k8", "k14"]; // 1 2 3 7 #
        string[] inputs = [.. keys.SelectMany((k, i) => new[] { "--input", $"{100 + (200 * i)}:kp1:{k}=1", "--input", $"{200 + (200 * i)}:kp1:{k}=0" })];

        RunResult result = await RunnerProcess.RunProjectAsync(project, ["--run-for", "1300", .. inputs]);

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal(["Type a code, then #.", "Key: 1", "Key: 2", "Key: 3", "Key: 7", "Key: #", "Code: 1237"], result.SerialLines);
    }

    [Fact]
    public async Task Joystick_example_reads_both_axes_and_the_button()
    {
        string project = RunnerProcess.CopyProject("examples/Joystick");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "450", "--input", "150:js1:x=1", "--input", "250:js1:y=-0.5", "--input", "350:js1:pressed=1");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal(["x:512 y:512", "x:512 y:512", "x:1023 y:512", "x:1023 y:256", "x:1023 y:256 pressed"], result.SerialLines);
    }

    [Fact]
    public async Task Dht22_example_reads_through_the_adafruit_library_and_follows_live_values()
    {
        string project = RunnerProcess.CopyProject("examples/Dht22");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "4500", "--input", "3000:dht1:temperature=31.5", "--input", "3000:dht1:humidity=72");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal(
            ["Humidity: 40.0 %  Temperature: 24.0 C  Heat index: 23.5 C", "Humidity: 72.0 %  Temperature: 31.5 C  Heat index: 39.6 C"],
            result.SerialLines);
    }

    [Fact]
    public async Task Ds18b20_example_finds_both_sensors_on_one_bus_and_reads_them()
    {
        string project = RunnerProcess.CopyProject("examples/Ds18b20");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "3000", "--input", "1500:ds2:temperature=-12.0625");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal("Found 2 sensor(s)", result.SerialLines.First());
        Assert.All(result.SerialLines.Skip(1).Take(2), l => Assert.Matches("^Sensor [01]: 28[0-9A-F]{14}$", l));
        Assert.Equal(["T0 = 4.25 C  T1 = 22.50 C", "T0 = -12.06 C  T1 = 22.50 C"], result.SerialLines.Skip(3).Select(l => l.TrimEnd()));
        Assert.DoesNotContain("warning", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ultrasonic_example_measures_with_pulse_in_and_reports_out_of_range()
    {
        string project = RunnerProcess.CopyProject("examples/Ultrasonic");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "900", "--input", "300:sonar1:distance=15", "--input", "600:sonar1:distance=450");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal(["Distance: 100.0 cm", "Distance: 100.0 cm", "Distance: 15.0 cm", "Out of range", "Out of range"], result.SerialLines);
        Assert.Contains(result.Timeline, e => e.Text == "board L=1" && e.Milliseconds > 400 && e.Milliseconds < 450);
    }

    [Fact]
    public async Task IrRemote_example_decodes_nec_frames_and_repeats_with_irremote()
    {
        string project = RunnerProcess.CopyProject("examples/IrRemote");

        RunResult result = await RunnerProcess.RunProjectAsync(
            project, "--run-for", "800", "--input", "100:remote1:b5=1", "--input", "400:remote1:b5=0", "--input", "500:remote1:b13=1", "--input", "520:remote1:b13=0");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal(["Press a key on the remote.", "NEC address 0x0 command 0x43: PLAY/PAUSE", "(repeat)", "(repeat)", "NEC address 0x0 command 0x18: 2"], result.SerialLines);
        Assert.Contains(result.Timeline, e => e.Text == "board L=1");
        Assert.DoesNotContain("warning", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Esp32_runs_irremote_dht_and_dallas_temperature_on_its_freertos_core()
    {
        string project = RunnerProcess.CopyProject("tests/ArduSim.Integration.Tests/Projects/Esp32Sensors");

        RunResult result = await RunnerProcess.RunProjectAsync(
            project, "--run-for", "3500", "--input", "1000:remote1:b16=1", "--input", "1050:remote1:b16=0", "--input", "2000:remote1:b2=1", "--input", "2400:remote1:b2=0");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal(["ds18b20: 1", "ir: 0x1C", "ir: 0x47", "dht: 18.5 63.0 ds: 36.62"], result.SerialLines);
        Assert.DoesNotContain("warning", result.Stdout, StringComparison.Ordinal);
    }
}
