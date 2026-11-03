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
/// External interrupts. Edges mark an interrupt pending; the host dispatches pending ones at the
/// next scheduling point, never while interrupts are disabled or an ISR is running (not preemptive).
/// A LOW-level interrupt stays pending for as long as its pin reads LOW, as on the ATmega328P.
/// </summary>
public sealed class InterruptController(Func<int, bool?> levelOf)
{
    private readonly SortedDictionary<int, (int Pin, InterruptMode Mode)> _attached = [];
    private readonly SortedSet<int> _pending = [];

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

    /// <summary>A reboot: nothing attached, nothing pending, interrupts enabled.</summary>
    public void Reset()
    {
        _attached.Clear();
        _pending.Clear();
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

    /// <summary>Takes the highest-priority pending interrupt (lowest number) if one may run now.</summary>
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

        return false;
    }

    public void EndDispatch() => InHandler = false;
}
