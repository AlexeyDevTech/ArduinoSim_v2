using ArduSim.Core.Simulation;
using ArduSim.Core.Timing;

namespace ArduSim.Core.Tests;

public sealed class SimulationClockTests
{
    [Fact]
    public void Events_run_in_time_order_and_ties_in_scheduling_order()
    {
        var clock = new SimulationClock();
        var order = new List<string>();
        clock.ScheduleAt(300, () => order.Add("c@300"));
        clock.ScheduleAt(100, () => order.Add("a@100"));
        clock.ScheduleAt(100, () => order.Add("b@100"));

        clock.AdvanceTo(1000);

        Assert.Equal(["a@100", "b@100", "c@300"], order);
        Assert.Equal(1000UL, clock.Now);
    }

    [Fact]
    public void Each_event_sees_its_own_time_and_may_schedule_more_within_the_advance()
    {
        var clock = new SimulationClock();
        var seen = new List<ulong>();
        clock.ScheduleAt(10, () =>
        {
            seen.Add(clock.Now);
            clock.Schedule(5, () => seen.Add(clock.Now));
        });

        clock.AdvanceTo(100);

        Assert.Equal([10UL, 15UL], seen);
    }

    [Fact]
    public void Cancelled_events_do_not_run_and_are_not_the_next_event()
    {
        var clock = new SimulationClock();
        bool ran = false;
        ScheduledEvent e = clock.ScheduleAt(50, () => ran = true);
        clock.ScheduleAt(80, () => { });
        e.Cancel();

        Assert.Equal(80UL, clock.NextEventTime);
        clock.AdvanceTo(100);
        Assert.False(ran);
    }
}

public sealed class SimulationEngineTests
{
    [Fact]
    public void Delay_advances_in_slices_with_a_scheduling_point_each()
    {
        var engine = new SimulationEngine();
        var points = new List<ulong>();
        engine.SchedulingPoint = () => points.Add(engine.Clock.Now);

        engine.Delay(VirtualTime.Millisecond);

        Assert.Equal(10, points.Count);
        Assert.Equal(VirtualTime.Millisecond, points[^1]);
        Assert.All(points.Zip(points.Skip(1)), p => Assert.Equal(SimulationEngine.DelaySlice, p.Second - p.First));
    }

    [Fact]
    public void WaitUntil_fails_loudly_when_nothing_can_end_the_wait()
    {
        var engine = new SimulationEngine();

        var error = Assert.Throws<InvalidOperationException>(() => engine.WaitUntil(() => false, "a byte"));
        Assert.Contains("a byte", error.Message, StringComparison.Ordinal);
    }
}

public sealed class PacerTests
{
    [Fact]
    public void Sleeps_when_virtual_time_runs_ahead_at_real_time_speed()
    {
        TimeSpan wall = TimeSpan.Zero;
        var sleeps = new List<TimeSpan>();
        var pacer = new Pacer(() => wall, d => { sleeps.Add(d); wall += d; }) { Speed = 1 };
        pacer.Reset(0);

        pacer.Pace(50 * VirtualTime.Millisecond);

        Assert.Equal(TimeSpan.FromMilliseconds(50), Assert.Single(sleeps));
    }

    [Fact]
    public void Ten_times_speed_needs_a_tenth_of_the_wall_time()
    {
        TimeSpan wall = TimeSpan.Zero;
        var sleeps = new List<TimeSpan>();
        var pacer = new Pacer(() => wall, d => { sleeps.Add(d); wall += d; }) { Speed = 10 };
        pacer.Reset(0);

        pacer.Pace(100 * VirtualTime.Millisecond);

        Assert.Equal(TimeSpan.FromMilliseconds(10), Assert.Single(sleeps));
    }

    [Fact]
    public void Unlimited_speed_never_sleeps()
    {
        var pacer = new Pacer(() => TimeSpan.Zero, _ => Assert.Fail("slept"));
        pacer.Reset(0);

        pacer.Pace(VirtualTime.Second);
    }
}
