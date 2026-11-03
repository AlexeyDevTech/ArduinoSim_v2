namespace ArduSim.Integration.Tests;

/// <summary>The ESP32 core (ADR-009, ADR-010): tasks, LEDC, Preferences with ESP.restart(), Wi-Fi and the real network.</summary>
public sealed class Esp32RunTests
{
    [Fact]
    public async Task A_freertos_task_runs_beside_loop_with_ledc_pwm()
    {
        string project = RunnerProcess.CopyProject("tests/ArduSim.Integration.Tests/Projects/Esp32Tasks");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "2100");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal("ESP32 ESP32-D0WD-V3, 240 MHz", result.SerialLines.First());
        Assert.Equal(["task at 0 ms", "task at 700 ms", "task at 1400 ms"], result.SerialLines.Where(l => l.StartsWith("task", StringComparison.Ordinal)));
        Assert.Contains(result.Timeline, e => e.Text == "D4 PWM 64/255");
        Assert.Contains(result.Timeline, e => e.Text == "board L=0" && e.Milliseconds > 499 && e.Milliseconds < 501);
    }

    [Fact]
    public async Task Preferences_and_eeprom_survive_restarts_and_runs()
    {
        string project = RunnerProcess.CopyProject("tests/ArduSim.Integration.Tests/Projects/Esp32Restart");

        RunResult first = await RunnerProcess.RunProjectAsync(project, "--run-for", "4000");
        RunResult second = await RunnerProcess.RunProjectAsync(project, "--run-for", "500");

        Assert.True(first.ExitCode == 0, first.ToString());
        Assert.Equal(["boot 1 at 6 ms, eeprom 0", "boot 2 at 6 ms, eeprom 1", "boot 3 at 6 ms, eeprom 2"], first.SerialLines);
        Assert.Equal(2, first.Timeline.Count(e => e.Text.Contains("The board restarted", StringComparison.Ordinal)));
        Assert.Equal(["boot 4 at 6 ms, eeprom 3"], second.SerialLines);
        Assert.Contains("\"type\": \"float\"", File.ReadAllText(Path.Combine(project, "preferences.json")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wifi_scan_connect_server_and_http_client_work_over_loopback()
    {
        string project = RunnerProcess.CopyProject("tests/ArduSim.Integration.Tests/Projects/Esp32Network");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "4500");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal("1 networks, first ArduSim", result.SerialLines.First());
        Assert.Contains("IP 192.168.1.100", result.SerialLines);
        Assert.Contains("served GET /hi HTTP/1.1", result.SerialLines);
        Assert.Contains(result.SerialLines, l => l.StartsWith("code 200 body hello", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_the_real_network_connections_fail_with_a_warning()
    {
        string project = RunnerProcess.CopyProject("tests/ArduSim.Integration.Tests/Projects/Esp32Network");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "4500", "--no-network");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Contains("IP 192.168.1.100", result.SerialLines);
        Assert.Contains(result.SerialLines, l => l.StartsWith("code -1", StringComparison.Ordinal));
        Assert.Contains("the real network is off for this project, so port 47821 is not reached", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Esp32Blink_example_blinks_the_external_and_onboard_leds()
    {
        string project = RunnerProcess.CopyProject("examples/Esp32Blink");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "2100");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal("ESP32 Blink on a ESP32-D0WD-V3 at 240 MHz", result.SerialLines.Single());
        var external = result.Timeline.Where(e => e.Text.StartsWith("led1 brightness=", StringComparison.Ordinal)).ToList();
        Assert.Equal(["led1 brightness=1", "led1 brightness=0", "led1 brightness=1", "led1 brightness=0"], external.Select(e => e.Text));
        Assert.Contains(result.Timeline, e => e.Text == "board L=0" && e.Milliseconds > 499 && e.Milliseconds < 501);
    }

    [Fact]
    public async Task Esp32Fade_example_ramps_the_led_brightness_with_ledc()
    {
        string project = RunnerProcess.CopyProject("examples/Esp32Fade");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "700");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Contains(result.Timeline, e => e.Text == "led1 brightness=0.5" || e.Text == "led1 brightness=0.501");
        Assert.Contains(result.Timeline, e => e.Text == "led1 brightness=1");
        Assert.Contains(result.SerialLines, l => l.StartsWith("breath done at ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Esp32Tasks_example_runs_two_tasks_and_a_queue()
    {
        string project = RunnerProcess.CopyProject("examples/Esp32Tasks");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "3100");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal(["count 1 from task count", "count 2 from task count", "count 3 from task count", "count 4 from task count"], result.SerialLines);
        Assert.InRange(result.Timeline.Count(e => e.Text.StartsWith("led1 brightness=", StringComparison.Ordinal)), 12, 14);
    }

    [Fact]
    public async Task Esp32WiFi_example_scans_connects_and_reports_the_request_without_a_network()
    {
        string project = RunnerProcess.CopyProject("examples/Esp32WiFi");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "4000", "--no-network");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal("1 network(s) found", result.SerialLines.First());
        Assert.Contains("Connected, IP 192.168.1.100", result.SerialLines);
        Assert.Contains("HTTP failed: connection refused", result.SerialLines);
    }

    [Fact]
    public async Task Esp32Preferences_example_counts_boots_across_restarts()
    {
        string project = RunnerProcess.CopyProject("examples/Esp32Preferences");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "6500");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal(["Boot number 1", "Boot number 2", "Boot number 3"], result.SerialLines.Where(l => l.StartsWith("Boot", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Esp32Oled_example_draws_on_the_ssd1306_over_the_default_i2c_pins()
    {
        string project = RunnerProcess.CopyProject("examples/Esp32Oled");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "1500");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Contains(result.Timeline, e => e.Text.StartsWith("oled1: ", StringComparison.Ordinal) && !e.Text.Contains(" 0 px", StringComparison.Ordinal));
        Assert.DoesNotContain("warning", result.Stdout, StringComparison.Ordinal);
    }
}