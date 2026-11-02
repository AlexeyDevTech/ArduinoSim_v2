namespace ArduSim.Ipc;

/// <param name="FirmwarePath">A built firmware.dll; the session does not build.</param>
/// <param name="Speed">Initial pacing factor; <see cref="double.PositiveInfinity"/> for max.</param>
public sealed record SessionStartInfo(string ProjectDirectory, string FirmwarePath, double Speed, int Seed);

public enum SessionEndKind
{
    /// <summary>Stopped on request.</summary>
    Stopped,

    /// <summary>The firmware crashed; <see cref="SessionEnd.Crash"/> says how.</summary>
    Crashed,

    /// <summary>The Runner ended without a crash record (bad project, internal error, killed from outside).</summary>
    RunnerExited,

    /// <summary>The Runner did not start or did not connect.</summary>
    FailedToStart,
}

/// <param name="Output">What the Runner wrote to stdout and stderr, for the diagnostic.</param>
public sealed record SessionEnd(SessionEndKind Kind, string Message, int? ExitCode, CrashDetails? Crash, string Output);

/// <summary>
/// The UI's only way to the simulation (ADR-003). Events arrive on background threads.
/// Implementations: <see cref="PipeSimulationSession"/> (a Runner process, the default) and the
/// in-process session in ArduSim.Runner (<c>--inproc</c>).
/// </summary>
public interface ISimulationSession : IAsyncDisposable
{
    /// <summary>Snapshots and other Runner → UI messages.</summary>
    event Action<IpcMessage>? MessageReceived;

    /// <summary>Raised once, when the run is over for any reason.</summary>
    event Action<SessionEnd>? Ended;

    Task StartAsync(SessionStartInfo start, CancellationToken cancellationToken);

    /// <summary>Queues a command (pause, step, input...); ignored once the run has ended.</summary>
    void Send(IpcMessage command);

    /// <summary>Ends the run: asks the Runner to stop, then kills it if it does not.</summary>
    Task StopAsync();
}
