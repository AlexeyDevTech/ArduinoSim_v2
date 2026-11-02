using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Reflection;
using System.Text;

namespace ArduSim.Ipc;

/// <summary>
/// Runs the simulation in a fresh ArduSim.Runner process (ADR-003): a named pipe for messages and
/// a named crash block that outlives the process. A crash, a hang or a kill ends only the Runner.
/// </summary>
public sealed class PipeSimulationSession(string runnerPath) : ISimulationSession
{
    private const int OutputLimit = 64 * 1024;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(1);

    private readonly StringBuilder _output = new();
    private readonly TaskCompletionSource<SessionEnd> _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Process? _process;
    private CrashBlock? _crash;
    private IpcChannel? _channel;
    private Task? _reader;
    private volatile bool _stopRequested;
    private int _finished;

    public event Action<IpcMessage>? MessageReceived;

    public event Action<SessionEnd>? Ended;

    public Task<SessionEnd> Completion => _ended.Task;

    public async Task StartAsync(SessionStartInfo start, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(start);
        string id = Guid.NewGuid().ToString("N");
        string pipeName = "ArduSim-" + id;
        string crashName = "ArduSim-crash-" + id;
        _crash = CrashBlock.CreateNamed(crashName);
        var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        var info = new ProcessStartInfo(runnerPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in new[]
        {
            "--ipc", "--pipe", pipeName, "--crash-block", crashName,
            "--project", start.ProjectDirectory, "--firmware", start.FirmwarePath,
            "--speed", double.IsPositiveInfinity(start.Speed) ? "max" : start.Speed.ToString(CultureInfo.InvariantCulture),
            "--seed", start.Seed.ToString(CultureInfo.InvariantCulture),
        })
        {
            info.ArgumentList.Add(argument);
        }

        _process = new Process { StartInfo = info, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => Capture(e.Data);
        _process.ErrorDataReceived += (_, e) => Capture(e.Data);
        _process.Exited += (_, _) => _ = FinishAsync();
        try
        {
            _process.Start();
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            await server.DisposeAsync();
            Finish(new SessionEnd(SessionEndKind.FailedToStart, $"Could not start {runnerPath}: {e.Message}", null, null, ""));
            return;
        }

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        using var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _process.HasExited ? new CancellationToken(true) : CancellationToken.None);
        connect.CancelAfter(ConnectTimeout);
        try
        {
            Task waitForExit = _process.WaitForExitAsync(connect.Token);
            Task connected = server.WaitForConnectionAsync(connect.Token);
            if (await Task.WhenAny(connected, waitForExit) != connected)
            {
                throw new OperationCanceledException();
            }

            await connected;
            _channel = new IpcChannel(server);
            await _channel.WriteAsync(new Hello(IpcProtocol.Version, ProductVersion), connect.Token);
            if (await _channel.ReadAsync(connect.Token) is not Hello hello || hello.ProtocolVersion != IpcProtocol.Version)
            {
                throw new InvalidDataException($"The Runner does not speak IPC protocol {IpcProtocol.Version}.");
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or InvalidDataException)
        {
            KillQuietly();
            await server.DisposeAsync();
            await _process.WaitForExitAsync(CancellationToken.None);
            Finish(new SessionEnd(SessionEndKind.FailedToStart, "The Runner did not connect: " + e.Message, _process.ExitCode, null, CapturedOutput()));
            return;
        }

        _reader = Task.Run(ReadLoopAsync, CancellationToken.None);
    }

    public void Send(IpcMessage command)
    {
        IpcChannel? channel = _channel;
        if (channel is null || _finished != 0)
        {
            return;
        }

        _ = channel.WriteAsync(command, CancellationToken.None).ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
    }

    public async Task StopAsync()
    {
        _stopRequested = true;
        if (_process is null || _process.HasExited)
        {
            await FinishAsync();
            return;
        }

        Send(new StopRun());
        using var grace = new CancellationTokenSource(StopGrace);
        try
        {
            await _process.WaitForExitAsync(grace.Token);
        }
        catch (OperationCanceledException)
        {
            KillQuietly();
        }

        await _ended.Task;
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is { HasExited: false })
        {
            await StopAsync();
        }

        if (_reader is not null)
        {
            await _reader;
        }

        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        _process?.Dispose();
        _crash?.Dispose();
    }

    private static string ProductVersion =>
        typeof(PipeSimulationSession).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    private async Task ReadLoopAsync()
    {
        try
        {
            while (await _channel!.ReadAsync(CancellationToken.None) is { } message)
            {
                MessageReceived?.Invoke(message);
            }
        }
        catch (IOException)
        {
            // The Runner went away; the exit handler explains why.
        }
        catch (MessagePack.MessagePackSerializationException)
        {
            KillQuietly();
        }

        await FinishAsync();
    }

    private async Task FinishAsync()
    {
        if (_process is null || _finished != 0)
        {
            return;
        }

        await _process.WaitForExitAsync(CancellationToken.None);
        int exitCode = _process.ExitCode;
        CrashDetails? crash = _crash?.ReadCrash();
        SessionEnd end = crash is not null
            ? new SessionEnd(SessionEndKind.Crashed, "The firmware crashed: " + crash.ExceptionName + ".", exitCode, crash, CapturedOutput())
            : _stopRequested
                ? new SessionEnd(SessionEndKind.Stopped, "Stopped.", exitCode, null, CapturedOutput())
                : new SessionEnd(SessionEndKind.RunnerExited, string.Create(CultureInfo.InvariantCulture, $"The Runner exited with code {exitCode} (0x{exitCode:X8})."), exitCode, null, CapturedOutput());
        Finish(end);
    }

    private void Finish(SessionEnd end)
    {
        if (Interlocked.Exchange(ref _finished, 1) != 0)
        {
            return;
        }

        _ended.TrySetResult(end);
        Ended?.Invoke(end);
    }

    private void Capture(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_output)
        {
            if (_output.Length < OutputLimit)
            {
                _output.Append(line).Append('\n');
            }
        }
    }

    private string CapturedOutput()
    {
        lock (_output)
        {
            return _output.ToString();
        }
    }

    private void KillQuietly()
    {
        try
        {
            _process?.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // already gone
        }
    }
}
