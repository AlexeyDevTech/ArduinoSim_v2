using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using ArduSim.Build;
using ArduSim.Build.Toolchains;

[assembly: AssemblyFixture(typeof(ArduSim.Integration.Tests.ToolchainFixture))]

namespace ArduSim.Integration.Tests;

/// <summary>Installs the pinned toolchain once (in the normal per-user folder) before any test builds firmware.</summary>
public sealed class ToolchainFixture : IAsyncLifetime
{
    public Toolchain Toolchain { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        Toolchain = await new ToolchainManager(ArduSimPaths.Default.Toolchains, http).EnsureInstalledAsync(null, CancellationToken.None);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>One line of the headless runner's timeline: <c>[  1000.008 ms] D13 LOW</c>.</summary>
public sealed record TimelineEvent(double Milliseconds, string Text);

public sealed partial record RunResult(int ExitCode, string Stdout, string Stderr)
{
    public IReadOnlyList<TimelineEvent> Timeline => Stdout.Split('\n')
        .Select(l => Stamped().Match(l.TrimEnd('\r')))
        .Where(m => m.Success)
        .Select(m => new TimelineEvent(double.Parse(m.Groups["ms"].Value, CultureInfo.InvariantCulture), m.Groups["text"].Value))
        .ToList();

    public IEnumerable<string> SerialLines => Timeline.Where(e => e.Text.StartsWith("Serial: ", StringComparison.Ordinal)).Select(e => e.Text["Serial: ".Length..]);

    public override string ToString() => $"exit {ExitCode}\n--- stdout\n{Stdout}\n--- stderr\n{Stderr}";

    [GeneratedRegex(@"^\[\s*(?<ms>[0-9.]+) ms\] (?<text>.*)$")]
    private static partial Regex Stamped();
}

/// <summary>Runs ArduSim.Runner as a separate process, exactly as the app does.</summary>
public static class RunnerProcess
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>Copies a project into a fresh temp folder so runs never write into the repository.</summary>
    public static string CopyProject(string relativePath)
    {
        string source = Path.Combine(RepositoryRoot, relativePath);
        string target = Directory.CreateTempSubdirectory("ardusim-it-").FullName;
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFileName(file), "eeprom.bin", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }

        return target;
    }

    /// <summary>Builds and runs a project headlessly with its build output inside the project copy.</summary>
    public static Task<RunResult> RunProjectAsync(string projectDirectory, params string[] extra) =>
        RunAsync(["--project", projectDirectory, "--build-dir", Path.Combine(projectDirectory, ".build"), .. extra]);

    public static async Task<RunResult> RunAsync(params string[] args)
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

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return new RunResult(process.ExitCode, await stdout, await stderr);
    }

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ArduSim.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("ArduSim.sln not found above " + AppContext.BaseDirectory);
    }
}
