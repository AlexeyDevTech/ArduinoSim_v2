namespace ArduSim.Core.Timing;

/// <summary>Virtual time units. The clock counts nanoseconds (ADR-005).</summary>
public static class VirtualTime
{
    public const ulong Microsecond = 1_000;
    public const ulong Millisecond = 1_000_000;
    public const ulong Second = 1_000_000_000;

    public static double ToMilliseconds(ulong nanoseconds) => nanoseconds / (double)Millisecond;
}

/// <summary>An event in the queue; cancel it to make it a no-op.</summary>
public sealed class ScheduledEvent
{
    private readonly Action _action;

    internal ScheduledEvent(ulong time, Action action)
    {
        Time = time;
        _action = action;
    }

    public ulong Time { get; }

    public bool IsCancelled { get; private set; }

    public void Cancel() => IsCancelled = true;

    internal void Run() => _action();
}

/// <summary>
/// The virtual clock and its event queue. Events due at the same instant run in the order they
/// were scheduled, so a run is reproducible (ADR-005).
/// </summary>
public sealed class SimulationClock
{
    private readonly PriorityQueue<ScheduledEvent, (ulong Time, long Sequence)> _queue = new();
    private long _sequence;

    public ulong Now { get; private set; }

    public ulong? NextEventTime
    {
        get
        {
            while (_queue.TryPeek(out ScheduledEvent? next, out (ulong Time, long) key))
            {
                if (!next.IsCancelled)
                {
                    return key.Time;
                }

                _queue.Dequeue();
            }

            return null;
        }
    }

    public ScheduledEvent Schedule(ulong delay, Action action) => ScheduleAt(Now + delay, action);

    public ScheduledEvent ScheduleAt(ulong time, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var scheduled = new ScheduledEvent(Math.Max(time, Now), action);
        _queue.Enqueue(scheduled, (scheduled.Time, _sequence++));
        return scheduled;
    }

    /// <summary>Moves to <paramref name="target"/>, running every event due at or before it, each at its own time.</summary>
    public void AdvanceTo(ulong target)
    {
        while (_queue.TryPeek(out ScheduledEvent? next, out (ulong Time, long) key) && key.Time <= target)
        {
            _queue.Dequeue();
            if (next.IsCancelled)
            {
                continue;
            }

            Now = Math.Max(Now, key.Time);
            next.Run();
        }

        Now = Math.Max(Now, target);
    }

    public void Advance(ulong delta) => AdvanceTo(Now + delta);
}
