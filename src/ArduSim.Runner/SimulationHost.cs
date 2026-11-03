using System.Collections.Concurrent;
using System.Diagnostics;
using ArduSim.Boards;
using ArduSim.Core.Boards;
using ArduSim.Core.Components;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Projects;
using ArduSim.Core.Simulation;
using ArduSim.Core.Timing;
using ArduSim.Ipc;
using ArduSim.Runner.Interop;
using ArduSim.Runner.Network;

namespace ArduSim.Runner;

/// <summary>Why a run ended.</summary>
internal enum StopReason
{
    TimeLimit,
    UserRequest,
}

/// <summary>
/// Hosts one simulation run on the calling thread: the engine, the board simulation (circuit,
/// parts, MCU), pacing, interrupt dispatch, pause and step, and the firmware's setup/loop.
/// Commands from other threads are queued and applied at scheduling points. One per process
/// (ADR-003); a run ends through <see cref="Exit"/>, which by default terminates the process.
/// </summary>
internal sealed unsafe class SimulationHost : IDisposable
{
    private static readonly TimeSpan BusyLimit = TimeSpan.FromSeconds(2);

    private const int MaxRestarts = 100;
    private const int RestartStackSize = 8 * 1024 * 1024;

    private readonly Stopwatch _wall = new();
    private readonly CrashBlock _crash;
    private readonly BlockingCollection<Action> _commands = new(new ConcurrentQueue<Action>());
    private FirmwareLibrary? _firmware;
    private SimHostApi* _api;
    private string? _firmwarePath;
    private int _restarts;
    private ulong _networkCheckedAt;
    private bool _networkLimitReported;
    private long _progress;
    private volatile bool _stopRequested;
    private volatile bool _waiting;
    private StopReason _stopReason;
    private bool _paused;
    private bool _pauseRequested;
    private bool _pauseAfterLoop;

    /// <param name="settings">Project settings: the Wi-Fi networks and whether the real network is on (ADR-010).</param>
    /// <param name="network">False turns the real network off whatever the project says (headless <c>--no-network</c>).</param>
    public SimulationHost(BoardProfile board, SceneDocument scene, EepromMemory eeprom, int seed, CrashBlock crash, KeyValueStore? kv = null, ProjectSettings? settings = null, bool network = true)
    {
        _crash = crash;
        Engine = new SimulationEngine();
        Simulation = BoardSimulation.Create(scene, board, ComponentRegistry.CreateDefault(BoardCatalog.All), Engine, eeprom, seed, kv, settings?.Wifi);
        Network = new NetworkService(Engine.Log, network && (settings?.Network?.Real ?? true));
        Pacer = new Pacer(() => _wall.Elapsed, Thread.Sleep);
        Engine.SchedulingPoint = OnSchedulingPoint;
    }

    public SimulationEngine Engine { get; }

    public BoardSimulation Simulation { get; }

    public Mcu Mcu => Simulation.Mcu;

    /// <summary>The real network behind the simulated Wi-Fi of ESP boards.</summary>
    public NetworkService Network { get; }

    public Pacer Pacer { get; }

    public TimeSpan WallTime => _wall.Elapsed;

    public bool IsPaused => _paused;

    public double VirtualMilliseconds => VirtualTime.ToMilliseconds(Engine.Clock.Now);

    /// <summary>Called once, right before a normal end.</summary>
    public Action<StopReason>? Stopping { get; set; }

    /// <summary>Called when the firmware made no host call for 2 s of wall time; return true to end the run.</summary>
    public Func<bool>? Busy { get; set; }

    /// <summary>Called for unrecoverable errors: firmware <c>fatal()</c> or a bug in the host.</summary>
    public Action<string, int>? Failed { get; set; }

    /// <summary>Called at every scheduling point after commands ran (snapshot publishing).</summary>
    public Action? Tick { get; set; }

    /// <summary>Called when the run pauses or resumes.</summary>
    public Action? RunStateChanged { get; set; }

    /// <summary>Ends the run; never returns. Default: terminate the process.</summary>
    public Action<int> Exit { get; set; } = ProcessExit.Terminate;

    public Mcu Enter(HostCall call, int a = 0, int b = 0)
    {
        _progress++;
        _crash.Record(call, a, b, Engine.Clock.Now);
        return Simulation.Mcu;
    }

    /// <summary>A network call: counted like any host call, charged its lwIP cost, and a scheduling point.</summary>
    public NetworkService EnterNetwork(int a = 0, int b = 0)
    {
        Enter(HostCall.Network, a, b);
        Engine.Finish(Mcu.Board.Timing.NetworkCall);
        return Network;
    }

    public void Dispose()
    {
        _commands.Dispose();
        Network.Dispose();
    }

    /// <summary>Queues work for the simulation thread (any thread may call this).</summary>
    public void Post(Action command) => _commands.Add(command);

    public void StopAt(ulong virtualTime) => Engine.Clock.ScheduleAt(virtualTime, () => RequestStop(StopReason.TimeLimit));

    public void RequestStop(StopReason reason)
    {
        _stopReason = reason;
        _stopRequested = true;
        Post(() => { });
    }

    public void Pause() => _pauseRequested = true;

    public void Resume()
    {
        _pauseRequested = false;
        _pauseAfterLoop = false;
        _paused = false;
    }

    public void StepLoop()
    {
        _pauseAfterLoop = true;
        _paused = false;
    }

    public void StepTime(ulong nanoseconds)
    {
        Engine.Clock.Schedule(nanoseconds, Pause);
        _paused = false;
    }

    public void SetSpeed(double speed)
    {
        Pacer.Speed = speed;
        Pacer.Reset(Engine.Clock.Now);
    }

    /// <summary>Loads the firmware and runs setup() and loop() until the run ends. Never returns.</summary>
    public void Run(string firmwarePath, bool startPaused = false)
    {
        _api = HostCallbacks.Create(this, (SimCrashInfo*)_crash.Address);
        _firmwarePath = firmwarePath;

        // Static constructors run while the DLL loads and may already wait (LiquidCrystal's
        // constructor calls begin(), which delays 50 ms): the clock and the pacer start first.
        _wall.Start();
        Pacer.Reset(Engine.Clock.Now);
        StartWatchdog();
        _firmware = FirmwareLibrary.Load(firmwarePath, _api);
        _paused = startPaused;
        OnSchedulingPoint();
        RunSketch();
    }

    /// <summary>
    /// ESP.restart(): the board reboots. Native frames cannot be unwound, so a fresh copy of the
    /// firmware (new globals, static constructors again) runs setup() on a new thread, and the
    /// calling thread, inside the old copy, stays parked. Never returns.
    /// </summary>
    public void Restart()
    {
        if (++_restarts > MaxRestarts)
        {
            Fail($"firmware: the board restarted more than {MaxRestarts} times in one run; the run is stopped.", ProcessExit.FirmwareError);
        }

        Engine.Log.Report(SimSeverity.Info, "restart", $"The board restarted (ESP.restart()); setup() runs again.");
        Mcu.Reset();
        string copy = FirmwareLibrary.CopyFor(_firmwarePath!, _restarts);
        var next = new Thread(() => RunGeneration(copy), RestartStackSize) { IsBackground = true, Name = "firmware after restart" };
        next.Start();
        Thread.Sleep(Timeout.Infinite);
    }

    private void RunGeneration(string firmwarePath)
    {
        try
        {
            _firmware = FirmwareLibrary.Load(firmwarePath, _api);
            RunSketch();
        }
        catch (Exception e) when (e is FirmwareException)
        {
            Fail("firmware: " + e.Message, ProcessExit.FirmwareError);
        }
    }

    private void RunSketch()
    {
        _firmware!.Setup();
        while (true)
        {
            _firmware.Loop();
            if (_pauseAfterLoop)
            {
                _pauseAfterLoop = false;
                _pauseRequested = true;
            }

            // Returning from loop() is a scheduling point; main()'s loop overhead costs a little time,
            // so a sketch with an empty loop() still lets virtual time pass.
            Engine.Finish(Mcu.Board.Timing.ShortCall);
        }
    }

    public void FirmwareLog(int level, string message)
    {
        SimSeverity severity = level switch
        {
            >= 3 => SimSeverity.Error,
            2 => SimSeverity.Warning,
            _ => SimSeverity.Info,
        };
        Engine.Log.Report(severity, "firmware", $"{message}");
    }

    public void FirmwareFatal(string message) => Fail("firmware: " + message, ProcessExit.FirmwareError);

    public void InternalError(Exception error) => Fail("internal error in the simulator: " + error, ProcessExit.InternalError);

    private void Fail(string message, int exitCode)
    {
        Failed?.Invoke(message, exitCode);
        Exit(exitCode);
    }

    private void OnSchedulingPoint()
    {
        _progress++;
        while (_commands.TryTake(out Action? command))
        {
            command();
        }

        if (_pauseRequested)
        {
            _pauseRequested = false;
            _paused = true;
        }

        if (_paused)
        {
            WaitWhilePaused();
        }

        if (_stopRequested)
        {
            Stopping?.Invoke(_stopReason);
            Exit(ProcessExit.Success);
        }

        if (_firmware is not null && Mcu.Interrupts.TryBeginDispatch(out int interrupt))
        {
            try
            {
                _firmware.DispatchInterrupt(interrupt);
            }
            finally
            {
                Mcu.Interrupts.EndDispatch();
            }
        }

        LimitSpeedForNetwork();
        Pacer.Pace(Engine.Clock.Now);
        Tick?.Invoke();
    }

    // While sockets are open the sketch's timeouts must mean wall-clock time (ADR-010). Checked
    // once per millisecond of virtual time; the network's state changes far more slowly.
    private void LimitSpeedForNetwork()
    {
        ulong now = Engine.Clock.Now;
        if (now - _networkCheckedAt < VirtualTime.Millisecond)
        {
            return;
        }

        _networkCheckedAt = now;
        bool active = Network.Active;
        Pacer.SetLimit(active ? 1 : double.PositiveInfinity, now);
        if (active && Pacer.Speed > 1 && !_networkLimitReported)
        {
            _networkLimitReported = true;
            Engine.Log.Report(SimSeverity.Info, "net-real-time", $"Network: the run is held at real time (1×) while the sketch has network connections open.");
        }
    }

    private void WaitWhilePaused()
    {
        RunStateChanged?.Invoke();
        _wall.Stop();
        _waiting = true;
        while (_paused && !_stopRequested)
        {
            Tick?.Invoke();
            _commands.Take()();
        }

        _waiting = false;
        _wall.Start();
        Pacer.Reset(Engine.Clock.Now);
        RunStateChanged?.Invoke();
    }

    private void StartWatchdog()
    {
        var thread = new Thread(() =>
        {
            long last = -1;
            bool reported = false;
            var idle = Stopwatch.StartNew();
            while (true)
            {
                Thread.Sleep(250);
                long now = Interlocked.Read(ref _progress);
                if (now != last || _waiting)
                {
                    last = now;
                    reported = false;
                    idle.Restart();
                }
                else if (!reported && idle.Elapsed >= BusyLimit)
                {
                    reported = true;
                    if (Busy?.Invoke() == true)
                    {
                        Exit(ProcessExit.FirmwareBusy);
                    }
                }
            }
        })
        {
            IsBackground = true,
            Name = "ArduSim watchdog",
        };
        thread.Start();
    }
}
