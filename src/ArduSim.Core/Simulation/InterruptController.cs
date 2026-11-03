using ArduSim.Core.Timing;

namespace ArduSim.Core.Simulation;

/// <summary>Arduino interrupt modes (values from the AVR core).</summary>
public enum InterruptMode
{
    Low = 0,
    Change = 1,
    Falling = 2,
    Rising = 3,
}

/// <summary>
/// External and timer interrupts. Edges and timer ticks mark an interrupt pending; the host
/// dispatches pending ones at the next scheduling point, never while interrupts are disabled or an
/// ISR is running (not preemptive). A LOW-level interrupt stays pending for as long as its pin reads
/// LOW, as on the ATmega328P. A timer tick that finds its interrupt still pending is lost, as a
/// hardware interrupt flag would lose it.
/// </summary>
public sealed class InterruptController(Func<int, bool?> levelOf, SimulationClock clock)
{
    /// <summary>Interrupt id of timer 0 in <c>sim_fw_dispatch_interrupt</c> (SIM_TIMER_INTERRUPT_BASE).</summary>
    public const int TimerBase = 64;

    /// <summary>Periodic timers a firmware may run at once (SIM_TIMER_COUNT).</summary>
    public const int TimerCount = 4;

    private readonly SortedDictionary<int, (int Pin, InterruptMode Mode)> _attached = [];
    private readonly SortedSet<int> _pending = [];
    private readonly ScheduledEvent?[] _timers = new ScheduledEvent?[TimerCount];
    private readonly bool[] _timerPending = new bool[TimerCount];

    public bool Enabled { get; set; } = true;

    public bool InHandler { get; private set; }

    public void Attach(int number, int pin, InterruptMode mode)
    {
        _attached[number] = (pin, mode);
        _pending.Remove(number);
    }

    public void Detach(int number)
    {
        _attached.Remove(number);
        _pending.Remove(number);
    }

    /// <summary>Starts (or restarts) timer <paramref name="timer"/>: its interrupt is due every <paramref name="period"/> ns from now.</summary>
    public void StartTimer(int timer, ulong period)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)timer, (uint)TimerCount, nameof(timer));
        ArgumentOutOfRangeException.ThrowIfZero(period);
        StopTimer(timer);
        ulong start = clock.Now;
        long ticks = 0;
        void Tick()
        {
            _timerPending[timer] = true;
            _timers[timer] = clock.ScheduleAt(start + ((ulong)++ticks * period), Tick);
        }

        _timers[timer] = clock.ScheduleAt(start + period, Tick);
    }

    public void StopTimer(int timer)
    {
        if ((uint)timer < TimerCount)
        {
            _timers[timer]?.Cancel();
            _timers[timer] = null;
            _timerPending[timer] = false;
        }
    }

    /// <summary>A reboot: nothing attached, no timers, nothing pending, interrupts enabled.</summary>
    public void Reset()
    {
        _attached.Clear();
        _pending.Clear();
        for (int timer = 0; timer < TimerCount; timer++)
        {
            StopTimer(timer);
        }

        Enabled = true;
        InHandler = false;
    }

    internal void OnLevelChanged(int pin, bool? before, bool? after)
    {
        if (before is null || after is null || before == after)
        {
            return;
        }

        foreach ((int number, (int attachedPin, InterruptMode mode)) in _attached)
        {
            bool fires = attachedPin == pin && mode switch
            {
                InterruptMode.Change => true,
                InterruptMode.Rising => after.Value,
                InterruptMode.Falling => !after.Value,
                _ => false,
            };
            if (fires)
            {
                _pending.Add(number);
            }
        }
    }

    /// <summary>
    /// Takes the highest-priority pending interrupt if one may run now: external interrupts (lowest
    /// number first), then timers. Timer <c>n</c> is reported as <see cref="TimerBase"/> + n.
    /// </summary>
    public bool TryBeginDispatch(out int number)
    {
        number = -1;
        if (!Enabled || InHandler)
        {
            return false;
        }

        foreach ((int candidate, (int pin, InterruptMode mode)) in _attached)
        {
            if (_pending.Remove(candidate) || (mode == InterruptMode.Low && levelOf(pin) == false))
            {
                number = candidate;
                InHandler = true;
                return true;
            }
        }

        for (int timer = 0; timer < TimerCount; timer++)
        {
            if (_timerPending[timer])
            {
                _timerPending[timer] = false;
                number = TimerBase + timer;
                InHandler = true;
                return true;
            }
        }

        return false;
    }

    public void EndDispatch() => InHandler = false;
}
