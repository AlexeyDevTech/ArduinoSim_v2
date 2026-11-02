using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Reflection;
using ArduSim.Boards;
using ArduSim.Core.Boards;
using ArduSim.Core.Projects;
using ArduSim.Core.Simulation;
using ArduSim.Ipc;

namespace ArduSim.Runner.Session;

/// <summary><c>ArduSim.Runner --ipc</c>: the process the app starts for every run (ADR-003).</summary>
internal static class PipeServer
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    public sealed record Options(string Pipe, string CrashBlock, string ProjectDirectory, string Firmware, double Speed, int Seed);

    public static async Task<int> RunAsync(Options options)
    {
        ProjectFile project;
        BoardProfile board;
        try
        {
            project = ProjectFile.Load(options.ProjectDirectory);
            board = BoardCatalog.Find(project.Board) ?? throw new ProjectFormatException($"Unknown board '{project.Board}'.");
        }
        catch (ProjectFormatException e)
        {
            Console.Error.WriteLine(e.Message);
            return ProcessExit.Usage;
        }

        var pipe = new NamedPipeClientStream(".", options.Pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
        using (var timeout = new CancellationTokenSource(ConnectTimeout))
        {
            await pipe.ConnectAsync(timeout.Token);
        }

        var channel = new IpcChannel(pipe);
        if (await channel.ReadAsync(CancellationToken.None) is not Hello { ProtocolVersion: IpcProtocol.Version })
        {
            Console.Error.WriteLine($"The app does not speak IPC protocol {IpcProtocol.Version}.");
            return ProcessExit.Usage;
        }

        string version = typeof(PipeServer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        await channel.WriteAsync(new Hello(IpcProtocol.Version, version), CancellationToken.None);

        var outbox = new BlockingCollection<IpcMessage>();
        var writer = new Thread(() =>
        {
            foreach (IpcMessage message in outbox.GetConsumingEnumerable())
            {
                try
                {
                    channel.WriteAsync(message, CancellationToken.None).GetAwaiter().GetResult();
                }
                catch (IOException)
                {
                    return;
                }
            }
        })
        {
            IsBackground = true,
            Name = "ArduSim IPC writer",
        };
        writer.Start();

        using EepromMemory eeprom = EepromMemory.FileBacked(Path.Combine(options.ProjectDirectory, ProjectFile.EepromFileName), board.EepromSize);
        using CrashBlock crash = CrashBlock.OpenNamed(options.CrashBlock);
        var host = new SimulationHost(board, project.Scene, eeprom, options.Seed, crash);
        host.Pacer.Speed = options.Speed;
        host.Exit = code =>
        {
            outbox.CompleteAdding();
            writer.Join(TimeSpan.FromMilliseconds(500));
            ProcessExit.Terminate(code);
        };
        host.Failed = (message, _) => Console.Error.WriteLine(message);
        var server = new SessionServer(host, message =>
        {
            if (!outbox.IsAddingCompleted)
            {
                outbox.Add(message);
            }
        });

        _ = Task.Run(async () =>
        {
            try
            {
                while (await channel.ReadAsync(CancellationToken.None) is { } message)
                {
                    server.Handle(message);
                }
            }
            catch (IOException)
            {
            }

            // The app went away: stop, and if the firmware is too busy to reach a scheduling point, go anyway.
            host.RequestStop(StopReason.UserRequest);
            await Task.Delay(TimeSpan.FromSeconds(2));
            ProcessExit.Terminate(ProcessExit.Success);
        });

        try
        {
            host.Run(options.Firmware);
        }
        catch (Interop.FirmwareException e)
        {
            Console.Error.WriteLine(e.Message);
            return ProcessExit.FirmwareError;
        }

        return ProcessExit.Success; // not reached
    }
}
