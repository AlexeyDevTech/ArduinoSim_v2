using System.Globalization;
using System.Text.RegularExpressions;

namespace ArduSim.Integration.Tests;

/// <summary>Runs alone, after the parallel tests, so the wall-clock numbers are not shared with other runs.</summary>
[CollectionDefinition(nameof(PerformanceTests), DisableParallelization = true)]
public sealed class RunsAlone;

/// <summary>Brief §6 performance targets that can be measured headlessly.</summary>
[Collection(nameof(PerformanceTests))]
public sealed partial class PerformanceTests
{
    [Fact]
    public async Task Firmware_makes_at_least_2_million_host_calls_per_second()
    {
        string project = RunnerProcess.CopyProject("tests/ArduSim.Integration.Tests/Projects/HostCallRate");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "10000", "--quiet");

        Assert.True(result.ExitCode == 0, result.ToString());
        double wall = double.Parse(WallTime().Match(result.Stdout).Groups[1].Value, CultureInfo.InvariantCulture);
        const double Calls = 10_000_000.0 / 5 * 2; // millis() (1 µs) + digitalRead() (4 µs) pairs in 10 s of virtual time
        double rate = Calls / wall;
        Assert.True(rate >= 2_000_000, $"{rate:N0} host calls/s ({wall} s wall)");
    }

    [GeneratedRegex(@"after ([0-9.]+) s wall")]
    private static partial Regex WallTime();
}
