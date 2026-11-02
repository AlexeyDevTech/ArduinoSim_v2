using System.Globalization;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Timing;

namespace ArduSim.Core.Simulation;

/// <summary>
/// A hardware UART as the AVR core uses it: a 64-byte TX ring drained one frame at a time at the
/// baud rate, and a 64-byte RX buffer that drops bytes when full.
/// </summary>
public sealed class UartPort
{
    public const int TxBufferSize = 64;
    public const int RxBufferSize = 64;

    private readonly SimulationEngine _engine;
    private readonly Queue<byte> _tx = new();
    private readonly Queue<byte> _rx = new();
    private bool _shifting;
    private byte _shiftRegister;

    public UartPort(int index, SimulationEngine engine)
    {
        Index = index;
        _engine = engine;
    }

    /// <summary>A byte has left the TX pin completely (end of its stop bit).</summary>
    public event Action<UartPort, byte>? ByteTransmitted;

    public int Index { get; }

    public string Name => Index == 0 ? "Serial" : "Serial" + Index.ToString(CultureInfo.InvariantCulture);

    public bool IsOpen { get; private set; }

    public uint Baud { get; private set; }

    /// <summary>Bits per frame: start + data + parity + stop (8N1 = 10).</summary>
    public int FrameBits { get; private set; } = 10;

    public ulong FrameTime => Baud == 0 ? 0 : (ulong)FrameBits * VirtualTime.Second / Baud;

    /// <summary>True while a write would have to wait for the ring to drain.</summary>
    public bool TxFull => IsOpen && _shifting && _tx.Count >= TxBufferSize;

    public bool TxIdle => !_shifting && _tx.Count == 0;

    public int AvailableForWrite => IsOpen ? TxBufferSize - 1 - _tx.Count : 0;

    public int Available => _rx.Count;

    public void Begin(uint baud, uint config)
    {
        if (baud == 0)
        {
            _engine.Log.ReportOnce($"uart-baud-{Index}", SimSeverity.Warning, "serial-config", $"{Name}.begin(0): a baud rate of 0 is not valid; the port stays closed.");
            return;
        }

        int dataBits = (int)((config >> 1) & 3) + 5;
        int parityBits = ((config >> 4) & 3) != 0 ? 1 : 0;
        int stopBits = (int)((config >> 3) & 1) + 1;
        Baud = baud;
        FrameBits = 1 + dataBits + parityBits + stopBits;
        IsOpen = true;
    }

    public void End()
    {
        IsOpen = false;
        _rx.Clear();
    }

    /// <summary>Queues a byte for transmission; returns false while the ring is full.</summary>
    public bool TryEnqueue(byte value)
    {
        if (!IsOpen)
        {
            _engine.Log.ReportOnce($"uart-closed-{Index}", SimSeverity.Warning, "serial-not-begun", $"{Name} is written before {Name}.begin(); the output is dropped.");
            return true;
        }

        if (!_shifting)
        {
            StartFrame(value);
            return true;
        }

        if (_tx.Count >= TxBufferSize)
        {
            return false;
        }

        _tx.Enqueue(value);
        return true;
    }

    public int Read() => _rx.Count > 0 ? _rx.Dequeue() : -1;

    public int Peek() => _rx.Count > 0 ? _rx.Peek() : -1;

    /// <summary>Delivers bytes to the RX pin back to back at the current baud rate, starting now.</summary>
    public void Receive(ReadOnlySpan<byte> data)
    {
        ulong frame = FrameTime == 0 ? 10 * VirtualTime.Second / 9600 : FrameTime;
        ulong at = _engine.Clock.Now;
        foreach (byte value in data)
        {
            at += frame;
            _engine.Clock.ScheduleAt(at, () => Arrive(value));
        }
    }

    private void Arrive(byte value)
    {
        if (!IsOpen)
        {
            return;
        }

        if (_rx.Count >= RxBufferSize)
        {
            _engine.Log.ReportOnce($"uart-overflow-{Index}", SimSeverity.Warning, "serial-rx-overflow", $"{Name} receive buffer overflowed ({RxBufferSize} bytes); incoming bytes are lost until the sketch reads.");
            return;
        }

        _rx.Enqueue(value);
    }

    private void StartFrame(byte value)
    {
        _shifting = true;
        _shiftRegister = value;
        _engine.Clock.Schedule(FrameTime, FrameDone);
    }

    private void FrameDone()
    {
        ByteTransmitted?.Invoke(this, _shiftRegister);
        if (_tx.TryDequeue(out byte next))
        {
            StartFrame(next);
        }
        else
        {
            _shifting = false;
        }
    }
}
