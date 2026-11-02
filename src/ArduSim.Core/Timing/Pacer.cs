namespace ArduSim.Core.Timing;

/// <summary>
/// Keeps virtual time in step with the wall clock at a speed factor (1 = real time,
/// <see cref="double.PositiveInfinity"/> = as fast as possible). Checked at scheduling points
/// at most once per millisecond of virtual time; sleeps when the simulation is ahead.
/// </summary>
public sealed class Pacer(Func<TimeSpan> wallClock, Action<TimeSpan> sleep)
{
    private static readonly TimeSpan SleepThreshold = TimeSpan.FromMilliseconds(2);
    private static readonly TimeSpan MaxLag = TimeSpan.FromMilliseconds(250);

    private double _speed = double.PositiveInfinity;
    private ulong _anchorVirtual;
    private TimeSpan _anchorWall;
    private ulong _lastCheck;

    public double Speed
    {
        get => _speed;
        set
        {
            if (!(value > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Speed must be positive.");
            }

            _speed = value;
            Reset(_lastCheck);
        }
    }

    public bool IsUnlimited => double.IsPositiveInfinity(_speed);

    public void Reset(ulong virtualNow)
    {
        _anchorVirtual = virtualNow;
        _anchorWall = wallClock();
        _lastCheck = virtualNow;
    }

    public void Pace(ulong virtualNow)
    {
        if (IsUnlimited || virtualNow - _lastCheck < VirtualTime.Millisecond)
        {
            return;
        }

        _lastCheck = virtualNow;
        TimeSpan due = TimeSpan.FromSeconds((virtualNow - _anchorVirtual) / (double)VirtualTime.Second / _speed);
        TimeSpan ahead = due - (wallClock() - _anchorWall);
        if (ahead > SleepThreshold)
        {
            sleep(ahead);
        }
        else if (ahead < -MaxLag)
        {
            // The host fell behind (debugger, heavy load): restart the reference instead of racing to catch up.
            Reset(virtualNow);
        }
    }
}
