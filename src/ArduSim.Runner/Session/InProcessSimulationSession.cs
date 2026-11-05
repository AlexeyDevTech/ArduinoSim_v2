using ArduSim.Boards;
using ArduSim.Core.Boards;
using ArduSim.Core.Projects;
using ArduSim.Core.Simulation;
using ArduSim.Ipc;

namespace ArduSim.Runner.Session;

/// <summary>
/// The <c>--inproc</c> dev session (ADR-003): the simulation runs on a thread of the app's own
/// process, so the whole stack can be stepped in one debugger. A crash in the sketch takes the app
/// down, and a stopped run parks its thread for the rest of the process's life (native code cannot
/// be unwound); each run loads its own copy of firmware.dll so globals start fresh.
/// </summary>
public sealed class InProcessSimulationSession : ISimulationSession
{
    private readonly TaskCompletionSource<SessionEnd> _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private SessionServer? _server;
    private SimulationHost? _host;
    private bool _stopRequested;

    public event Action<IpcMessage>? MessageReceived;

    public event Action<SessionEnd>? Ended;

    public Task StartAsync(SessionStartInfo start, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(start);
        ProjectFile project = ProjectFile.Load(start.ProjectDirectory);
        BoardProfile board = BoardCatalog.Find(project.Board) ?? throw new ProjectFormatException($"Unknown board '{project.Board}'.");
        string copy = Path.Combine(Path.GetTempPath(), "ardusim-inproc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(copy);
        string firmware = Path.Combine(copy, Path.GetFileName(start.FirmwarePath));
        File.Copy(start.FirmwarePath, firmware);
        string symbols = Path.ChangeExtension(start.FirmwarePath, ".pdb");
        if (File.Exists(symbols))
        {
            File.Copy(symbols, Path.ChangeExtension(firmware, ".pdb"));
        }

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(
            () =>
        {
            var eeprom = EepromMemory.FileBacked(Path.Combine(start.ProjectDirectory, ProjectFile.EepromFileName), board.EepromSize);
            _host = new SimulationHost(board, project.Scene, eeprom, start.Seed, CrashBlock.CreatePrivate(), KeyValueStore.FileBacked(Path.Combine(start.ProjectDirectory, ProjectFile.PreferencesFileName)), project.Settings, files: new ProjectFolderFiles(start.ProjectDirectory));
            _host.Pacer.Speed = start.Speed;
        _host.Mcu.SetRecording(start.Record);
            _host.Exit = code =>
            {
                eeprom.Dispose();
                Finish(_stopRequested || code == ProcessExit.Success
                    ? new SessionEnd(SessionEndKind.Stopped, "Stopped.", code, null, "")
                    : new SessionEnd(SessionEndKind.RunnerExited, $"The run ended with code {code}.", code, null, ""));
                Thread.Sleep(Timeout.Infinite);
            };
            _server = new SessionServer(_host, m => MessageReceived?.Invoke(m));
            started.SetResult();
            try
            {
                _host.Run(firmware);
            }
            catch (Interop.FirmwareException e)
            {
                Finish(new SessionEnd(SessionEndKind.FailedToStart, e.Message, null, null, ""));
            }
        },
            16 << 20)
        {
            IsBackground = true,
            Name = "ArduSim in-process simulation",
        };
        thread.Start();
        return started.Task;
    }

    public void Send(IpcMessage command) => _server?.Handle(command);

    public async Task StopAsync()
    {
        _stopRequested = true;
        Send(new StopRun());
        await Task.WhenAny(_ended.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        if (!_ended.Task.IsCompleted)
        {
            Finish(new SessionEnd(SessionEndKind.Stopped, "Stopped (the firmware was busy; its thread is left parked).", null, null, ""));
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void Finish(SessionEnd end)
    {
        if (_ended.TrySetResult(end))
        {
            Ended?.Invoke(end);
        }
    }
}
