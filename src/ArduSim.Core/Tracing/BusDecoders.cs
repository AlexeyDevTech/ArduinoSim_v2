using System.Globalization;

namespace ArduSim.Core.Tracing;

public enum FrameKind : byte
{
    Data,

    /// <summary>A byte nobody acknowledged, or a UART framing error.</summary>
    Error,
}

/// <summary>One labeled span of a bus row.</summary>
public readonly record struct DecodedFrame(ulong Start, ulong End, string Text, FrameKind Kind = FrameKind.Data);

/// <summary>
/// Turns one bus's traffic into labeled frames: hardware events as they come, and bit-banged traffic
/// from the edges on the bus's pins. Fed in time order; keeps its state between batches.
/// </summary>
public abstract class BusDecoder
{
    private readonly List<DecodedFrame> _frames = [];

    public IReadOnlyList<DecodedFrame> Frames => _frames;

    /// <summary>The pins whose samples this decoder needs.</summary>
    public abstract IReadOnlyList<int> Pins { get; }

    public abstract void OnSample(in PinSample sample);

    /// <summary>A hardware event; ignored unless it is this decoder's bus on its pins.</summary>
    public abstract void OnEvent(in BusEvent e);

    /// <summary>Virtual time has reached <paramref name="now"/> with no further samples before it.</summary>
    public virtual void Advance(ulong now)
    {
    }

    /// <summary>Forgets frames that end before <paramref name="time"/>.</summary>
    public void DropBefore(ulong time)
    {
        int count = 0;
        while (count < _frames.Count && _frames[count].End < time)
        {
            count++;
        }

        _frames.RemoveRange(0, count);
    }

    protected void Add(DecodedFrame frame) => _frames.Add(frame);

    protected static string Hex(int value) => "0x" + value.ToString("X2", CultureInfo.InvariantCulture);
}

/// <summary>I2C on SDA and SCL: <c>S 0x3C W A</c>, <c>0x40 A</c>, <c>P</c>.</summary>
public sealed class I2cDecoder(int sda, int scl) : BusDecoder
{
    private bool _sda = true;
    private bool _scl = true;
    private bool _inFrame;
    private bool _expectAddress;
    private int _bits;
    private int _value;
    private ulong _byteStart;
    private ulong _startTime;

    public override IReadOnlyList<int> Pins { get; } = [sda, scl];

    public override void OnEvent(in BusEvent e)
    {
        if (!e.IsI2c || e.PinA != sda || e.PinB != scl)
        {
            return;
        }

        switch (e.Kind)
        {
            case BusEventKind.I2cStart:
                Add(new DecodedFrame(e.Time, e.End, Address(e.Value, e.Extra != 0), e.Extra != 0 ? FrameKind.Data : FrameKind.Error));
                break;
            case BusEventKind.I2cWrite:
                Add(new DecodedFrame(e.Time, e.End, Hex(e.Value) + (e.Extra != 0 ? " A" : " N"), e.Extra != 0 ? FrameKind.Data : FrameKind.Error));
                break;
            case BusEventKind.I2cRead:
                Add(new DecodedFrame(e.Time, e.End, Hex(e.Value) + (e.Extra != 0 ? " A" : " N")));
                break;
            default:
                Add(new DecodedFrame(e.Time, e.End, "P"));
                break;
        }
    }

    public override void OnSample(in PinSample sample)
    {
        bool high = sample.Level is not SignalLevel.Low;
        if (sample.Pin == sda)
        {
            if (_scl && _sda && !high)
            {
                _inFrame = true;
                _expectAddress = true;
                _bits = 0;
                _startTime = sample.Time;
            }
            else if (_scl && !_sda && high && _inFrame)
            {
                _inFrame = false;
                Add(new DecodedFrame(sample.Time, sample.Time, "P"));
            }

            _sda = high;
        }
        else if (sample.Pin == scl)
        {
            if (!_scl && high && _inFrame)
            {
                OnBit(sample.Time);
            }

            _scl = high;
        }
    }

    private static string Address(int value, bool ack) =>
        "S " + Hex(value >> 1) + ((value & 1) != 0 ? " R" : " W") + (ack ? " A" : " N");

    private void OnBit(ulong time)
    {
        if (_bits == 0)
        {
            _byteStart = time;
            _value = 0;
        }

        if (_bits < 8)
        {
            _value = (_value << 1) | (_sda ? 1 : 0);
            _bits++;
            return;
        }

        bool ack = !_sda;
        if (_expectAddress)
        {
            Add(new DecodedFrame(_startTime, time, Address(_value, ack), ack ? FrameKind.Data : FrameKind.Error));
            _expectAddress = false;
        }
        else
        {
            Add(new DecodedFrame(_byteStart, time, Hex(_value) + (ack ? " A" : " N")));
        }

        _bits = 0;
    }
}

/// <summary>SPI on MOSI, MISO and SCK, with an optional CS (active low): one frame per byte, sampled on the rising edge, MSB first.</summary>
public sealed class SpiDecoder(int mosi, int miso, int sck, int cs = -1) : BusDecoder
{
    private bool _mosi;
    private bool _miso = miso < 0; // no MISO pin: reads as idle high
    private bool _sck;
    private bool _selected = cs < 0;
    private int _bits;
    private int _out;
    private int _in;
    private ulong _start;

    public override IReadOnlyList<int> Pins { get; } = [.. new[] { mosi, miso, sck, cs }.Where(p => p >= 0)];

    public override void OnEvent(in BusEvent e)
    {
        if (e.IsSpi && e.PinA == mosi && e.PinB == sck)
        {
            Add(new DecodedFrame(e.Time, e.End, Label(e.Value, e.Extra)));
        }
    }

    public override void OnSample(in PinSample sample)
    {
        bool high = sample.Level is SignalLevel.High;
        if (sample.Pin == cs)
        {
            _selected = !high;
            _bits = 0;
        }
        else if (sample.Pin == mosi)
        {
            _mosi = high;
        }
        else if (sample.Pin == miso)
        {
            _miso = high;
        }
        else if (sample.Pin == sck)
        {
            if (high && !_sck && _selected)
            {
                OnBit(sample.Time);
            }

            _sck = high;
        }
    }

    private static string Label(int mosiByte, int misoByte) =>
        misoByte == 0xFF ? Hex(mosiByte) : Hex(mosiByte) + " / " + Hex(misoByte);

    private void OnBit(ulong time)
    {
        if (_bits == 0)
        {
            _start = time;
            _out = 0;
            _in = 0;
        }

        _out = (_out << 1) | (_mosi ? 1 : 0);
        _in = (_in << 1) | (_miso ? 1 : 0);
        if (++_bits == 8)
        {
            Add(new DecodedFrame(_start, time, Label(_out, _in)));
            _bits = 0;
        }
    }
}

/// <summary>UART (8N1) on one line, idle high: printable bytes as <c>'H'</c>, others in hex.</summary>
public sealed class UartDecoder(int line, uint baud) : BusDecoder
{
    private readonly ulong _bit = baud == 0 ? 1 : 1_000_000_000UL / baud;
    private bool _level = true;
    private bool _receiving;
    private ulong _start;
    private int _index;
    private int _value;

    public override IReadOnlyList<int> Pins { get; } = [line];

    public override void OnEvent(in BusEvent e)
    {
        if (e.IsUart && e.PinA == line)
        {
            Add(new DecodedFrame(e.Time, e.End, Label(e.Value)));
        }
    }

    public override void OnSample(in PinSample sample)
    {
        if (sample.Pin != line)
        {
            return;
        }

        Advance(sample.Time);
        bool high = sample.Level is not SignalLevel.Low;
        if (!_receiving && _level && !high)
        {
            _receiving = true;
            _start = sample.Time;
            _index = 0;
            _value = 0;
        }

        _level = high;
    }

    // Each bit is read in its middle: bit i of the frame (0 = start) at start + (i + 0.5) bit times.
    public override void Advance(ulong now)
    {
        while (_receiving)
        {
            ulong at = _start + (((ulong)_index + 1) * _bit) + (_bit / 2);
            if (at > now)
            {
                return;
            }

            if (_index < 8)
            {
                _value |= (_level ? 1 : 0) << _index;
                _index++;
                continue;
            }

            Add(new DecodedFrame(_start, at + (_bit / 2), Label(_value), _level ? FrameKind.Data : FrameKind.Error));
            _receiving = false;
        }
    }

    private static string Label(int value) =>
        value is >= 0x20 and < 0x7F ? "'" + (char)value + "'" : Hex(value);
}
