using System.Collections.Concurrent;
using System.Text;
using ArduSim.Build.Compilation;
using ArduSim.Core.Timing;
using ArduSim.Ipc;

namespace ArduSim.Integration.Tests;

/// <summary>The app's path to a simulation: a Runner process behind <see cref="PipeSimulationSession"/>.</summary>
public sealed class SessionTests(ToolchainFixture toolchain)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private sealed class Recorder
    {
        public ConcurrentQueue<Snapshot> Snapshots { get; } = new();

        public ConcurrentQueue<IpcMessage> Others { get; } = new();

        public void Receive(IpcMessage message)
        {
            if (message is Snapshot snapshot)
            {
                Snapshots.Enqueue(snapshot);
            }
            else
            {
                Others.Enqueue(message);
            }
        }

        public bool Saw(Func<Snapshot, bool> condition) => Snapshots.Any(condition);

        public static double? Component(Snapshot s, string id, string key) =>
            s.Components.FirstOrDefault(c => c.Id == id)?.State.TryGetValue(key, out double v) == true ? v : null;

        public string Serial => Encoding.ASCII.GetString(Snapshots.SelectMany(s => s.Serial).ToArray());
    }

    private async Task<(PipeSimulationSession Session, Recorder Recorder)> StartAsync(string projectPath, double speed)
    {
        string project = RunnerProcess.CopyProject(projectPath);
        string firmware = await RunnerProcess.BuildFirmwareAsync(toolchain.Toolchain, project);
        var session = new PipeSimulationSession(RunnerProcess.RunnerPath);
        var recorder = new Recorder();
        session.MessageReceived += recorder.Receive;
        await session.StartAsync(new SessionStartInfo(project, firmware, speed, 1), TestContext.Current.CancellationToken);
        return (session, recorder);
    }

    private static async Task Until(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out waiting for " + what);
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Blink_streams_snapshots_and_pauses_steps_and_stops()
    {
        (PipeSimulationSession session, Recorder r) = await StartAsync("examples/Blink", speed: 10);
        await using (session)
        {
            await Until(() => r.Saw(s => Recorder.Component(s, "led1", "brightness") == 1) && r.Saw(s => Recorder.Component(s, "board", "L") == 0 && s.TimeNs > VirtualTime.Second), "the LED to blink");
            Assert.Contains(r.Snapshots, s => s.Pins.Any(p => p.Pin == 13 && p.Kind == 2));

            session.Send(new PauseRun());
            await Until(() => r.Snapshots.LastOrDefault()?.State == RunState.Paused, "pause");
            ulong pausedAt = r.Snapshots.Last().TimeNs;
            await Task.Delay(300, TestContext.Current.CancellationToken);
            Assert.Equal(pausedAt, r.Snapshots.Last().TimeNs);

            session.Send(new StepRun(StepKind.Time, 100 * VirtualTime.Millisecond));
            await Until(() => r.Snapshots.Last() is { State: RunState.Paused } s && s.TimeNs > pausedAt, "the step to finish");
            Assert.InRange(r.Snapshots.Last().TimeNs - pausedAt, 100 * VirtualTime.Millisecond, 101 * VirtualTime.Millisecond);

            session.Send(new ResumeRun());
            await Until(() => r.Snapshots.Last().State == RunState.Running, "resume");
            await session.StopAsync();
            Assert.Equal(SessionEndKind.Stopped, (await session.Completion).Kind);
        }
    }

    [Fact]
    public async Task Button_presses_from_the_ui_toggle_the_led()
    {
        (PipeSimulationSession session, Recorder r) = await StartAsync("examples/Button", speed: 1);
        await using (session)
        {
            await Until(() => r.Serial.Contains("Press the button", StringComparison.Ordinal), "the greeting");
            session.Send(new ComponentInput("btn1", "pressed", 1));
            await Task.Delay(150, TestContext.Current.CancellationToken);
            session.Send(new ComponentInput("btn1", "pressed", 0));

            await Until(() => r.Saw(s => Recorder.Component(s, "led1", "brightness") == 1), "the LED to light");
            await Until(() => r.Serial.Contains("LED on", StringComparison.Ordinal), "the Serial message");
            await session.StopAsync();
        }
    }

    [Fact]
    public async Task A_crash_kills_only_the_runner_and_points_at_the_line()
    {
        (PipeSimulationSession session, _) = await StartAsync("tests/ArduSim.Integration.Tests/Projects/Crash", speed: double.PositiveInfinity);
        await using (session)
        {
            SessionEnd end = await session.Completion.WaitAsync(Patience, TestContext.Current.CancellationToken);

            Assert.Equal(SessionEndKind.Crashed, end.Kind);
            CrashDetails crash = end.Crash!;
            Assert.Equal("access violation", crash.ExceptionName);
            Assert.Equal((1u, 0ul), (crash.AccessKind, crash.AccessAddress)); // a write to address 0
            Assert.Contains(crash.LastCalls, c => c.Call == HostCall.DigitalWrite && c.A == 13 && c.B == 1);
            Assert.NotEqual(0ul, crash.FirmwareBase);
        }
    }

    [Fact]
    public async Task Crash_address_symbolizes_to_the_sketch_line()
    {
        string project = RunnerProcess.CopyProject("tests/ArduSim.Integration.Tests/Projects/Crash");
        string firmware = await RunnerProcess.BuildFirmwareAsync(toolchain.Toolchain, project);
        var session = new PipeSimulationSession(RunnerProcess.RunnerPath);
        await using (session)
        {
            await session.StartAsync(new SessionStartInfo(project, firmware, double.PositiveInfinity, 1), TestContext.Current.CancellationToken);
            CrashDetails crash = (await session.Completion.WaitAsync(Patience, TestContext.Current.CancellationToken)).Crash!;

            SourceLocation? location = FirmwareSymbols.Resolve(firmware, crash.FirmwareBase, crash.ExceptionAddress);

            Assert.NotNull(location);
            Assert.Equal("Crash.ino", Path.GetFileName(location.File));
            Assert.Equal(12, location.Line);
            Assert.Equal("loop", location.Function);
        }
    }

    [Fact]
    public async Task Busy_firmware_is_reported_and_stop_still_works()
    {
        (PipeSimulationSession session, Recorder r) = await StartAsync("tests/ArduSim.Integration.Tests/Projects/Runaway", speed: double.PositiveInfinity);
        await using (session)
        {
            await Until(() => r.Others.OfType<FirmwareBusy>().Any(), "the busy report");
            await session.StopAsync();
            Assert.Equal(SessionEndKind.Stopped, (await session.Completion).Kind);
        }
    }
}
