using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ArduSim.Boards;
using ArduSim.Build;
using ArduSim.Build.Compilation;
using ArduSim.Build.Toolchains;
using ArduSim.Core.Boards;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Projects;
using ArduSim.Core.Simulation;
using ArduSim.Core.Timing;
using ArduSim.Runner.Interop;

namespace ArduSim.Runner.Headless;

/// <summary>
/// <c>ArduSim.Runner --project ...</c>: build the project, run it, print pin changes, Serial
/// lines and warnings with their virtual time, and optionally dump the Serial output.
/// </summary>
internal static class HeadlessRun
{
    public static async Task<int> RunAsync(HeadlessOptions options)
    {
        TextWriter output = Console.Out;
        ProjectFile project;
        try
        {
            project = ProjectFile.Load(options.ProjectDirectory);
        }
        catch (ProjectFormatException e)
        {
            Console.Error.WriteLine(e.Message);
            return ProcessExit.Usage;
        }

        BoardProfile? board = BoardCatalog.Find(project.Board);
        if (board is null)
        {
            Console.Error.WriteLine($"Unknown board '{project.Board}'. Known boards: {string.Join(", ", BoardCatalog.All.Select(b => b.Id))}.");
            return ProcessExit.Usage;
        }

        output.WriteLine($"ArduSim.Runner: {board.DisplayName}, {options.ProjectDirectory}");
        BuildResult build;
        try
        {
            build = await BuildAsync(project, board, options.ProjectDirectory, options.BuildDirectory);
        }
        catch (ToolchainException e)
        {
            Console.Error.WriteLine($"Toolchain unavailable ({e.Failure}): {e.Message}");
            return ProcessExit.ToolchainUnavailable;
        }

        foreach (BuildDiagnostic d in build.Diagnostics.Where(d => d.Severity != DiagnosticSeverity.Note))
        {
            string location = d.File is null ? "" : $"{d.File}({d.Line},{d.Column}): ";
            (build.Succeeded ? output : Console.Error).WriteLine($"{location}{d.Severity.ToString().ToLowerInvariant()}: {d.Message}");
        }

        if (!build.Succeeded)
        {
            Console.Error.WriteLine("Build failed.");
            Console.Error.Write(build.Output);
            return ProcessExit.BuildFailed;
        }

        BuildStatistics stats = build.Statistics;
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"build: {stats.Units} units, {stats.Units - stats.CacheHits} compiled, {stats.CacheHits} cached, {(stats.Linked ? "linked" : "up to date")} in {stats.Elapsed.TotalSeconds:F2} s"));

        using EepromMemory eeprom = EepromMemory.FileBacked(Path.Combine(options.ProjectDirectory, ProjectFile.EepromFileName), board.EepromSize);
        var host = new SimulationHost(board, eeprom, options.Seed);
        host.Pacer.Speed = options.Speed;
        Attach(host, options, output);
        if (options.RunForMilliseconds is double ms)
        {
            host.StopAt((ulong)(ms * VirtualTime.Millisecond));
        }

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            host.RequestStop(StopReason.UserRequest);
        };

        try
        {
            host.Run(build.FirmwarePath!);
        }
        catch (FirmwareException e)
        {
            Console.Error.WriteLine(e.Message);
            return ProcessExit.FirmwareError;
        }

        return ProcessExit.Success; // not reached: a run ends by terminating the process
    }

    private static async Task<BuildResult> BuildAsync(ProjectFile project, BoardProfile board, string projectDirectory, string? buildDirectory)
    {
        ArduSimPaths paths = ArduSimPaths.Default;
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        var toolchains = new ToolchainManager(paths.Toolchains, http);
        Toolchain toolchain = toolchains.FindInstalled() ?? await InstallAsync(toolchains);

        var libraryRoots = new List<string>(project.LibraryDirectories(projectDirectory));
        string documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Arduino", "libraries");
        if (Directory.Exists(documents))
        {
            libraryRoots.Add(documents);
        }

        libraryRoots.Add(Path.Combine(ArduSimPaths.BundledContentRoot, "libraries"));
        var builder = new FirmwareBuilder(toolchain, new ObjectCache(paths.ObjectCache, toolchain.Root), ShimLayout.Bundled);
        var request = new FirmwareBuildRequest(
            project.SketchDirectory(projectDirectory),
            buildDirectory ?? Path.Combine(paths.Builds, BuildFolderName(projectDirectory)),
            board.Variant,
            board.Defines,
            libraryRoots);
        return await builder.BuildAsync(request, CancellationToken.None);
    }

    private static async Task<Toolchain> InstallAsync(ToolchainManager toolchains)
    {
        Console.Error.WriteLine($"Installing toolchain {toolchains.Manifest.Id} ({toolchains.Manifest.SizeBytes / (1024 * 1024)} MiB download, once)...");
        return await toolchains.EnsureInstalledAsync(new ConsoleProgress(), CancellationToken.None);
    }

    /// <summary>Prints every stage change and every 10 %, synchronously (Progress&lt;T&gt; would post out of order).</summary>
    private sealed class ConsoleProgress : IProgress<ToolchainProgress>
    {
        private ToolchainStage? _stage;
        private int _decile = -1;

        public void Report(ToolchainProgress value)
        {
            int percent = value.Total > 0 ? (int)(value.Completed * 100 / value.Total) : 0;
            if (value.Stage == _stage && percent / 10 == _decile)
            {
                return;
            }

            _stage = value.Stage;
            _decile = percent / 10;
            Console.Error.WriteLine($"  {value.Stage.ToString().ToLowerInvariant()} {percent}%");
        }
    }

    private static string BuildFolderName(string projectDirectory)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(projectDirectory.ToUpperInvariant()));
        return Path.GetFileName(projectDirectory) + "-" + Convert.ToHexStringLower(hash)[..12];
    }

    private static void Attach(SimulationHost host, HeadlessOptions options, TextWriter output)
    {
        BoardProfile board = host.Mcu.Board;
        string Stamp() => string.Create(CultureInfo.InvariantCulture, $"[{host.VirtualMilliseconds,12:F3} ms]");

        if (options.TracePins)
        {
            host.Mcu.PinChanged += change => output.WriteLine($"{Stamp()} {board.PinName(change.Pin)} {change.Drive}");
        }

        FileStream? dump = options.DumpSerial is null ? null : new FileStream(options.DumpSerial, FileMode.Create, FileAccess.Write, FileShare.Read);
        var line = new StringBuilder();
        foreach (UartPort uart in host.Mcu.Uarts)
        {
            uart.ByteTransmitted += (port, value) =>
            {
                dump?.WriteByte(value);
                if (value == '\n')
                {
                    output.WriteLine($"{Stamp()} {port.Name}: {line.ToString().TrimEnd('\r')}");
                    line.Clear();
                }
                else
                {
                    line.Append((char)value);
                }
            };
        }

        host.Engine.Log.Reported += d =>
            output.WriteLine($"{Stamp()} {d.Severity.ToString().ToLowerInvariant()}: {d.Message}");

        host.Stopping = reason =>
        {
            if (line.Length > 0)
            {
                output.WriteLine($"{Stamp()} Serial: {line}");
            }

            double wall = host.WallTime.TotalSeconds;
            string why = reason == StopReason.TimeLimit ? "time limit" : "stopped by user";
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"stopped ({why}) at {host.VirtualMilliseconds:F3} ms virtual after {wall:F2} s wall"));
        };
        host.Busy = () => Console.Error.WriteLine($"{Stamp()} error: the firmware has been busy for 2 s without calling the Arduino API (an endless loop?); the run was stopped.");
        host.Failed = (message, _) => Console.Error.WriteLine($"{Stamp()} error: {message}");
        ProcessExit.OnExit(() =>
        {
            dump?.Flush();
            dump?.Dispose();
        });
    }
}
