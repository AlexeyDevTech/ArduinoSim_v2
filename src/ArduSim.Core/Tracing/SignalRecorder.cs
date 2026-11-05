using ArduSim.Core.Circuits;

namespace ArduSim.Core.Tracing;

public enum SignalLevel : byte
{
    Low,
    High,

    /// <summary>Nothing drives the net.</summary>
    Floating,

    /// <summary>A PWM signal, drawn as a duty cycle: <see cref="PinSample.Value"/> is the duty in thousandths.</summary>
    Pwm,

    /// <summary>A tone: <see cref="PinSample.Value"/> is the frequency in Hz.</summary>
    Tone,
}

/// <summary>What a board pin's net carries from <paramref name="Time"/> on.</summary>
public readonly record struct PinSample(ulong Time, byte Pin, SignalLevel Level, int Value)
{
    /// <summary>The level of a net as a pin of a board with this logic voltage reads it.</summary>
    public static (SignalLevel Level, int Value) Classify(NetState net, double logicVoltage)
    {
        if (!net.HasVoltage)
        {
            return (SignalLevel.Floating, 0);
        }

        if (net.ToneHz > 0)
        {
            return (SignalLevel.Tone, net.ToneHz);
        }

        if (net.Duty is > 0 and < 1)
        {
            return (SignalLevel.Pwm, (int)Math.Round(net.Duty * 1000));
        }

        bool high = net.Voltage >= logicVoltage / 2 && net.Duty > 0;
        return (high ? SignalLevel.High : SignalLevel.Low, 0);
    }
}

public enum BusEventKind : byte
{
    /// <summary>START and the address byte: Value = address &lt;&lt; 1 | read, Extra = 1 on ACK; 10 bit times.</summary>
    I2cStart,

    /// <summary>A byte from the master: Extra = 1 on ACK; 9 bit times.</summary>
    I2cWrite,

    /// <summary>A byte from the device: Extra = 1 when the master ACKs; 9 bit times.</summary>
    I2cRead,

    /// <summary>STOP; one bit time.</summary>
    I2cStop,

    /// <summary>One full-duplex byte, MSB first: Value = MOSI, Extra = MISO; 8 bit times.</summary>
    SpiByte,

    /// <summary>The same, LSB first.</summary>
    SpiByteLsbFirst,

    /// <summary>A UART frame the board sends: Extra = bits per frame.</summary>
    UartTx,

    /// <summary>A UART frame the board receives: Extra = bits per frame.</summary>
    UartRx,
}

/// <summary>
/// A byte on a hardware bus (ADR-014): the simulator hands hardware I2C, SPI and UART bytes over whole,
/// so their nets have no edges of their own; these events say what the wires carried.
/// </summary>
/// <param name="PinA">I2C SDA, SPI MOSI, the UART line; <see cref="NoPin"/> when the bus has no such pin.</param>
/// <param name="PinB">I2C SCL, SPI SCK.</param>
/// <param name="PinC">SPI MISO.</param>
public readonly record struct BusEvent(ulong Time, uint Duration, BusEventKind Kind, byte Value, byte Extra, byte PinA, byte PinB, byte PinC)
{
    public const byte NoPin = 255;

    public ulong End => Time + Duration;

    public bool IsI2c => Kind is BusEventKind.I2cStart or BusEventKind.I2cWrite or BusEventKind.I2cRead or BusEventKind.I2cStop;

    public bool IsSpi => Kind is BusEventKind.SpiByte or BusEventKind.SpiByteLsbFirst;

    public bool IsUart => Kind is BusEventKind.UartTx or BusEventKind.UartRx;
}

/// <summary>
/// Collects pin changes and hardware bus bytes while recording is on, for the logic analyzer. The host
/// drains it with every snapshot; between two drains it keeps a bounded number of events and
/// remembers when it had to drop some.
/// </summary>
public sealed class SignalRecorder
{
    private const int PinCapacity = 400_000;
    private const int BusCapacity = 200_000;

    private readonly List<PinSample> _pins = [];
    private readonly List<BusEvent> _bus = [];
    private readonly Dictionary<byte, (SignalLevel Level, int Value)> _last = [];
    private bool _overflowed;

    /// <summary>Costs one test per event while off.</summary>
    public bool Enabled { get; private set; }

    public bool HasData => _pins.Count > 0 || _bus.Count > 0 || _overflowed;

    /// <summary>Events waiting for the next drain.</summary>
    public int Count => _pins.Count + _bus.Count;

    public void Start()
    {
        Enabled = true;
        _last.Clear();
    }

    public void Stop()
    {
        Enabled = false;
        _last.Clear();
    }

    /// <summary>A pin's net now carries this; repeats of the same state are dropped.</summary>
    public void Pin(ulong time, int pin, SignalLevel level, int value)
    {
        if (!Enabled)
        {
            return;
        }

        byte key = (byte)pin;
        if (_last.TryGetValue(key, out (SignalLevel Level, int Value) last) && last == (level, value))
        {
            return;
        }

        _last[key] = (level, value);
        if (_pins.Count >= PinCapacity)
        {
            _overflowed = true;
            return;
        }

        _pins.Add(new PinSample(time, key, level, value));
    }

    public void Bus(BusEvent value)
    {
        if (!Enabled)
        {
            return;
        }

        if (_bus.Count >= BusCapacity)
        {
            _overflowed = true;
            return;
        }

        _bus.Add(value);
    }

    /// <summary>Everything since the last drain, each list in time order.</summary>
    public (PinSample[] Pins, BusEvent[] Bus, bool Overflowed) Drain()
    {
        (PinSample[], BusEvent[], bool) result = ([.. _pins], [.. _bus], _overflowed);
        _pins.Clear();
        _bus.Clear();
        _overflowed = false;
        return result;
    }
}
