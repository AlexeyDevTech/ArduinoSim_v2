using ArduSim.Core.Diagnostics;
using ArduSim.Core.Timing;

namespace ArduSim.Core.Simulation;

/// <summary>
/// The cooperative time engine (ADR-005): virtual time advances only when the firmware calls the
/// host, and every host call ends in a scheduling point where the host dispatches interrupts,
/// paces against the wall clock and processes commands.
/// </summary>
public sealed class SimulationEngine
{
    /// <summary>
    /// <c>delay()</c> advances in slices of at most this length, with a scheduling point after each;
    /// a slice also ends at the next scheduled event, so the interrupts it raises run on time.
    /// </summary>
    public const ulong DelaySlice = 100 * VirtualTime.Microsecond;

    public SimulationEngine()
    {
        Log = new SimulationLog(() => Clock.Now);
    }

    public SimulationClock Clock { get; } = new();

    public SimulationLog Log { get; }

    /// <summary>Set by the host: interrupt dispatch, pacing, command processing, stop checks.</summary>
    public Action? SchedulingPoint { get; set; }

    /// <summary>Charges the virtual cost of a host call and runs a scheduling point.</summary>
    public void Finish(ulong cost)
    {
        Clock.Advance(cost);
        SchedulingPoint?.Invoke();
    }

    public void Delay(ulong nanoseconds)
    {
        ulong end = Clock.Now + nanoseconds;
        while (Clock.Now < end)
        {
            ulong next = Math.Min(end, Clock.Now + DelaySlice);
            if (Clock.NextEventTime is { } due && due < next)
            {
                next = due;
            }

            Clock.AdvanceTo(next);
            SchedulingPoint?.Invoke();
        }
    }

    /// <summary>Advances from event to event until <paramref name="condition"/> holds (e.g. a TX buffer slot frees up).</summary>
    public void WaitUntil(Func<bool> condition, string what)
    {
        ArgumentNullException.ThrowIfNull(condition);
        while (!condition())
        {
            ulong next = Clock.NextEventTime ?? throw new InvalidOperationException($"Waiting for {what}, but nothing is scheduled that could end the wait.");
            Clock.AdvanceTo(next);
            SchedulingPoint?.Invoke();
        }
    }
}
