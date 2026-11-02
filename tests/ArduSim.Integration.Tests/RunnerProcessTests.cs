using System.Diagnostics;

namespace ArduSim.Integration.Tests;

public sealed class RunnerProcessTests
{
    [Fact]
    public async Task Runner_reports_its_version()
    {
        (int exitCode, string stdout, _) = await RunRunner("--version");

        Assert.Equal(0, exitCode);
        Assert.StartsWith("ArduSim.Runner ", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runner_fails_loudly_on_unsupported_arguments()
    {
        (int exitCode, _, string stderr) = await RunRunner("--project", "examples/Blink");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("not implemented", stderr, StringComparison.Ordinal);
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunRunner(params string[] args)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "ArduSim.Runner.exe"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return (process.ExitCode, await stdout, await stderr);
    }
}
