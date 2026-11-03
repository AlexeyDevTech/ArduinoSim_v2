using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ArduSim.Integration.Tests;

/// <summary>The ESP8266 core on the NodeMCU v3 and the Wemos D1 mini (ADR-009, ADR-010).</summary>
public sealed class Esp8266RunTests
{
    [Fact]
    public async Task Esp8266Blink_example_drives_the_active_low_led_and_d6()
    {
        string project = RunnerProcess.CopyProject("examples/Esp8266Blink");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "1100");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal("ESP8266 Blink, chip C0FFEE, reset reason: Power On", result.SerialLines.Single());
        Assert.Equal(["board L=1", "board L=0", "board L=1"], result.Timeline.Where(e => e.Text.StartsWith("board L=", StringComparison.Ordinal)).Select(e => e.Text));
        Assert.Contains(result.Timeline, e => e.Text == "led1 brightness=1" && e.Milliseconds > 499 && e.Milliseconds < 501);
    }

    [Fact]
    public async Task Esp8266Fade_example_uses_the_analog_write_range()
    {
        string project = RunnerProcess.CopyProject("examples/Esp8266Fade");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "700");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Contains(result.Timeline, e => e.Text == "led1 brightness=0.03");
        Assert.Contains(result.Timeline, e => e.Text == "led1 brightness=1");
        Assert.Contains(result.SerialLines, l => l.StartsWith("breath done at ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Esp8266Eeprom_example_counts_boots_across_restarts_and_reports_the_reason()
    {
        string project = RunnerProcess.CopyProject("examples/Esp8266Eeprom");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "6500");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal(["Boot 1 (Power On)", "Boot 2 (Software/System restart)", "Boot 3 (Software/System restart)"],
            result.SerialLines.Where(l => l.StartsWith("Boot", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Esp8266Oled_example_draws_through_u8g2_with_its_compat_fix()
    {
        string project = RunnerProcess.CopyProject("examples/Esp8266Oled");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "1500");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Contains(result.Timeline, e => e.Text.StartsWith("oled1: ", StringComparison.Ordinal) && !e.Text.Contains(" 0 px", StringComparison.Ordinal));
        Assert.DoesNotContain("warning", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Esp8266WiFi_example_scans_connects_and_reports_the_request_without_a_network()
    {
        string project = RunnerProcess.CopyProject("examples/Esp8266WiFi");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "4000", "--no-network");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal(["1 network(s) found", "  ArduSim (-55 dBm, open)", "Got IP 192.168.1.100", "HTTP failed: connection refused"], result.SerialLines);
    }

    [Fact]
    public async Task Http_post_reaches_a_real_server_on_this_computer()
    {
        int port = FreePort();
        using var server = new HttpListener();
        server.Prefixes.Add($"http://127.0.0.1:{port}/");
        server.Start();
        Task<string> request = Task.Run(async () =>
        {
            HttpListenerContext context = await server.GetContextAsync();
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            string seen = $"{context.Request.HttpMethod} {context.Request.Url!.AbsolutePath} {context.Request.Headers["X-Board"]} {await reader.ReadToEndAsync()}";
            byte[] body = Encoding.UTF8.GetBytes("hello esp8266");
            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body);
            context.Response.Close();
            return seen;
        });
        string project = RunnerProcess.CopyProject("tests/ArduSim.Integration.Tests/Projects/Esp8266Http");
        string sketch = Path.Combine(project, "sketch", "Esp8266Http.ino");
        File.WriteAllText(sketch, File.ReadAllText(sketch).Replace("TEST_PORT", port.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "3000");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal("POST /hello esp8266 ping", await request.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(["code 200 body hello esp8266"], result.SerialLines);
    }

    [Fact]
    public async Task A_loop_that_never_yields_gets_the_soft_watchdog_warning()
    {
        string project = RunnerProcess.CopyProject("examples/Esp8266Fade");
        File.WriteAllText(Path.Combine(project, "sketch", "Esp8266Fade.ino"), "void setup() { pinMode(D2, OUTPUT); }\nvoid loop() { for (;;) { digitalWrite(D2, !digitalRead(D2)); } }\n");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "4000", "--quiet");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Single(result.Timeline, e => e.Text.Contains("a real ESP8266 resets here (soft WDT)", StringComparison.Ordinal));
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }
}