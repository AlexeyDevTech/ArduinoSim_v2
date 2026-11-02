namespace ArduSim.Integration.Tests;

public sealed class RunnerProcessTests
{
    [Fact]
    public async Task Runner_reports_its_version()
    {
        RunResult result = await RunnerProcess.RunAsync("--version");

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("ArduSim.Runner ", result.Stdout, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--bogus")]
    [InlineData("--run-for", "100")]
    [InlineData("--project", "x", "--dump-display", "out.png")]
    public async Task Runner_rejects_bad_command_lines_with_usage(params string[] args)
    {
        RunResult result = await RunnerProcess.RunAsync(args);

        Assert.Equal(64, result.ExitCode);
        Assert.Contains("Usage: ArduSim.Runner", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_project_file_is_reported()
    {
        string empty = Directory.CreateTempSubdirectory("ardusim-it-").FullName;

        RunResult result = await RunnerProcess.RunAsync("--project", empty);

        Assert.Equal(64, result.ExitCode);
        Assert.Contains("project.simproj.json not found", result.Stderr, StringComparison.Ordinal);
    }
}
