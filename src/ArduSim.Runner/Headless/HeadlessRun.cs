using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ArduSim.Boards;
using ArduSim.Build;
using ArduSim.Build.Compilation;
using ArduSim.Build.Toolchains;
using ArduSim.Core.Boards;
using ArduSim.Core.Components;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Displays;
using ArduSim.Core.Projects;
using ArduSim.Core.Simulation;
using ArduSim.Core.Timing;
using ArduSim.Ipc;
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
        using CrashBlock crash = CrashBlock.CreatePrivate();
        var host = new SimulationHost(board, project.Scene, eeprom, options.Seed, crash);
        host.Pacer.Speed = options.Speed;
        Attach(host, options, output);
        if (options.RunForMilliseconds is double ms)
        {
            host.StopAt((ulong)(ms * VirtualTime.Millisecond));
        }

        foreach (ScheduledInput input in options.Inputs)
        {
            if (host.Simulation.FindComponent(input.Component) is not { } component)
            {
                Console.Error.WriteLine($"--input: the scene has no part '{input.Component}'.");
                return ProcessExit.Usage;
            }

            host.Engine.Clock.ScheduleAt((ulong)(input.Milliseconds * VirtualTime.Millisecond), () => component.ApplyInput(input.Name, input.Value));
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

        var builder = new FirmwareBuilder(toolchain, new ObjectCache(paths.ObjectCache, toolchain.Root), ShimLayout.Bundled);
        var request = new FirmwareBuildRequest(
            project.SketchDirectory(projectDirectory),
            buildDirectory ?? paths.BuildDirectoryFor(projectDirectory),
            board.Variant,
            board.Defines,
            ArduSimPaths.LibraryRoots(project.LibraryDirectories(projectDirectory)));
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

    /// <summary>
    /// What a display shows, as one line: LCD rows as <c>"row 0" "row 1"</c> (escaped as in
    /// <c>Hd44780.RowText</c>), an OLED as the number of lit pixels.
    /// </summary>
    private static string Rows(SimComponent component) => component.Display switch
    {
        { Kind: DisplayKind.Oled } frame => string.Create(CultureInfo.InvariantCulture, $"{frame.Dots.ToArray().Sum(b => System.Numerics.BitOperations.PopCount(b))} px on"),
        { } frame => string.Join(" ", frame.Text.Select(row => "\"" + row + "\"")),
        null => "",
    };

    /// <summary>--dump-display: one PNG per display at the end of the run.</summary>
    private static void DumpDisplays(SimulationHost host, string path, TextWriter output)
    {
        List<SimComponent> displays = [.. host.Simulation.Components.Where(c => c.Display is not null)];
        if (displays.Count == 0)
        {
            output.WriteLine("--dump-display: the scene has no display.");
            return;
        }

        foreach (SimComponent display in displays)
        {
            string file = displays.Count == 1 ? path : Path.Combine(Path.GetDirectoryName(path) ?? "", $"{Path.GetFileNameWithoutExtension(path)}-{display.Id}{Path.GetExtension(path)}");
            PngWriter.Write(file, DisplayImage.From(display.Display!));
            output.WriteLine($"display {display.Id} written to {file}");
        }
    }

    private static void Attach(SimulationHost host, HeadlessOptions options, TextWriter output)
    {
        BoardProfile board = host.Mcu.Board;
        string Stamp() => string.Create(CultureInfo.InvariantCulture, $"[{host.VirtualMilliseconds,12:F3} ms]");

        if (options.TracePins)
        {
            host.Mcu.PinChanged += change => output.WriteLine($"{Stamp()} {board.PinName(change.Pin)} {change.Drive}");
            foreach (SimComponent component in host.Simulation.Components)
            {
                var shown = new Dictionary<string, double>(component.State, StringComparer.Ordinal);
                component.StateChanged += c =>
                {
                    string changes = string.Join(" ", c.State
                        .Where(kv => !shown.TryGetValue(kv.Key, out double old) || !old.Equals(kv.Value))
                        .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                        .Select(kv => string.Create(CultureInfo.InvariantCulture, $"{kv.Key}={kv.Value:0.###}")));
                    foreach ((string key, double value) in c.State)
                    {
                        shown[key] = value;
                    }

                    if (changes.Length > 0)
                    {
                        output.WriteLine($"{Stamp()} {c.Id} {changes}");
                    }
                };
            }
        }

        // Displays print their visible text when it changed, 20 ms of virtual time after the first
        // change, so a line written character by character (about 1 ms each over I2C) shows up once.
        var displays = new List<Action>();
        foreach (SimComponent component in host.Simulation.Components.Where(c => c.Display is not null))
        {
            string shown = Rows(component);
            bool pending = false;
            void Flush()
            {
                pending = false;
                string text = Rows(component);
                if (text != shown)
                {
                    shown = text;
                    output.WriteLine($"{Stamp()} {component.Id}: {text}");
                }
            }

            component.StateChanged += _ =>
            {
                if (!pending)
                {
                    pending = true;
                    host.Engine.Clock.Schedule(20 * VirtualTime.Millisecond, Flush);
                }
            };
            displays.Add(Flush);
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

            foreach (Action flush in displays)
            {
                flush();
            }

            if (options.DumpDisplay is { } dumpPath)
            {
                DumpDisplays(host, dumpPath, output);
            }

            double wall = host.WallTime.TotalSeconds;
            string why = reason == StopReason.TimeLimit ? "time limit" : "stopped by user";
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"stopped ({why}) at {host.VirtualMilliseconds:F3} ms virtual after {wall:F2} s wall"));
        };
        host.Busy = () =>
        {
            Console.Error.WriteLine($"{Stamp()} error: the firmware has been busy for 2 s without calling the Arduino API (an endless loop?); the run was stopped.");
            return true;
        };
        host.Failed = (message, _) => Console.Error.WriteLine($"{Stamp()} error: {message}");
        ProcessExit.OnExit(() =>
        {
            dump?.Flush();
            dump?.Dispose();
        });
    }
}
