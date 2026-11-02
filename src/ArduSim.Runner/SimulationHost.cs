using System.Diagnostics;
using ArduSim.Core.Boards;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Simulation;
using ArduSim.Core.Timing;
using ArduSim.Runner.Interop;

namespace ArduSim.Runner;

/// <summary>Why a run ended.</summary>
internal enum StopReason
{
    TimeLimit,
    UserRequest,
}

/// <summary>
/// Hosts one simulation run on the calling thread: the engine, the MCU model, pacing, interrupt
/// dispatch, and the firmware's setup/loop. One per process (ADR-003); the run ends by
/// terminating the process.
/// </summary>
internal sealed unsafe class SimulationHost
{
    private static readonly TimeSpan BusyLimit = TimeSpan.FromSeconds(2);

    private readonly Stopwatch _wall = new();
    private FirmwareLibrary? _firmware;
    private long _progress;
    private volatile bool _stopRequested;
    private StopReason _stopReason;

    public SimulationHost(BoardProfile board, EepromMemory eeprom, int seed)
    {
        Engine = new SimulationEngine();
        Mcu = new Mcu(board, Engine, eeprom, seed);
        Pacer = new Pacer(() => _wall.Elapsed, Thread.Sleep);
        Engine.SchedulingPoint = OnSchedulingPoint;
    }

    public SimulationEngine Engine { get; }

    public Mcu Mcu { get; }

    public Pacer Pacer { get; }

    public TimeSpan WallTime => _wall.Elapsed;

    /// <summary>Called once, right before the process ends normally.</summary>
    public Action<StopReason>? Stopping { get; set; }

    /// <summary>Called when the firmware made no host call for 2 s of wall time.</summary>
    public Action? Busy { get; set; }

    /// <summary>Called for unrecoverable errors: firmware <c>fatal()</c> or a bug in the host.</summary>
    public Action<string, int>? Failed { get; set; }

    public Mcu Enter()
    {
        _progress++;
        return Mcu;
    }

    public void StopAt(ulong virtualTime) => Engine.Clock.ScheduleAt(virtualTime, () => RequestStop(StopReason.TimeLimit));

    public void RequestStop(StopReason reason)
    {
        _stopReason = reason;
        _stopRequested = true;
    }

    /// <summary>Loads the firmware and runs setup() and loop() until the run is stopped. Never returns.</summary>
    public void Run(string firmwarePath)
    {
        SimHostApi* api = HostCallbacks.Create(this);
        _firmware = FirmwareLibrary.Load(firmwarePath, api);
        _wall.Start();
        Pacer.Reset(Engine.Clock.Now);
        StartWatchdog();

        _firmware.Setup();
        while (true)
        {
            _firmware.Loop();
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
        Engine.Log.Report(severity, "firmware", message);
    }

    public void FirmwareFatal(string message) => Fail("firmware: " + message, ProcessExit.FirmwareError);

    public void InternalError(Exception error) => Fail("internal error in the simulator: " + error, ProcessExit.InternalError);

    private void Fail(string message, int exitCode)
    {
        Failed?.Invoke(message, exitCode);
        ProcessExit.Terminate(exitCode);
    }

    private void OnSchedulingPoint()
    {
        _progress++;
        if (_stopRequested)
        {
            Stopping?.Invoke(_stopReason);
            ProcessExit.Terminate(ProcessExit.Success);
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

        Pacer.Pace(Engine.Clock.Now);
    }

    private void StartWatchdog()
    {
        var thread = new Thread(() =>
        {
            long last = -1;
            var idle = Stopwatch.StartNew();
            while (true)
            {
                Thread.Sleep(250);
                long now = Interlocked.Read(ref _progress);
                if (now != last)
                {
                    last = now;
                    idle.Restart();
                }
                else if (idle.Elapsed >= BusyLimit)
                {
                    Busy?.Invoke();
                    ProcessExit.Terminate(ProcessExit.FirmwareBusy);
                }
            }
        })
        {
            IsBackground = true,
            Name = "ArduSim watchdog",
        };
        thread.Start();
    }

    public double VirtualMilliseconds => VirtualTime.ToMilliseconds(Engine.Clock.Now);
}
