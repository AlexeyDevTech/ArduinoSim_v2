using ArduSim.Core.Circuits;
using ArduSim.Core.Timing;

namespace ArduSim.Core.Buses;

/// <summary>
/// The slave side of the 1-Wire protocol, timed from the edges of the DQ net (ADR-011). A low pulse
/// of at least 480 µs is a reset, answered 30 µs after the line rises by a 120 µs presence pulse.
/// Every other falling edge starts a time slot: a listening device reads the line 30 µs after the
/// edge (still low = 0); a talking device sends a 0 by holding the line low for 45 µs. Devices on
/// one net share it by wired-AND, as on the real bus, which is what the ROM search relies on; for
/// 300 µs after a reset every edge belongs to the presence pulses.
/// The ROM layer (read, match, skip, search, alarm search) is here; function commands are the
/// device's.
/// </summary>
public abstract class OneWireSlave
{
    public const ulong ResetMinimum = 480 * VirtualTime.Microsecond;
    public const ulong PresenceDelay = 30 * VirtualTime.Microsecond;
    public const ulong PresenceLength = 120 * VirtualTime.Microsecond;
    public const ulong SampleTime = 30 * VirtualTime.Microsecond;
    public const ulong HoldTime = 45 * VirtualTime.Microsecond;

    /// <summary>After a reset, edges are presence pulses (this device's or others') until this much later.</summary>
    public const ulong PresenceWindow = 300 * VirtualTime.Microsecond;

    private readonly SimulationClock _clock;
    private readonly Driver _driver;
    private readonly Queue<bool> _out = new();
    private bool? _level;
    private ulong _fallAt;
    private ulong _quietUntil;
    private bool _fallIsOurs;
    private bool _pulling;
    private bool _slotTalks;
    private ScheduledEvent? _release;
    private ScheduledEvent? _presence;
    private Phase _phase = Phase.Idle;
    private int _bits;
    private int _shift;
    private int _romBit;
    private int _searchStep;

    protected OneWireSlave(SimulationClock clock, Driver driver)
    {
        _clock = clock;
        _driver = driver;
    }

    private enum Phase
    {
        /// <summary>Not selected: only a reset wakes the device.</summary>
        Idle,
        RomCommand,
        MatchRom,
        ReadRom,
        Search,
        Function,
    }

    /// <summary>Family code, serial number and CRC, least significant byte first on the wire.</summary>
    public abstract ulong Rom { get; }

    /// <summary>Whether the device takes part in an alarm search.</summary>
    protected virtual bool AlarmActive => false;

    /// <summary>
    /// Read slots after the queued data ran out (a busy flag, the power supply bit); null while the
    /// device listens.
    /// </summary>
    protected Func<bool>? Poll { get; set; }

    protected ulong Now => _clock.Now;

    /// <summary>The DQ line changed: <paramref name="level"/> is null while it floats or the device is off.</summary>
    public void OnLine(bool? level)
    {
        if (level == _level)
        {
            return;
        }

        bool? before = _level;
        _level = level;
        if (level == false && before == true)
        {
            _fallAt = _clock.Now;
            _fallIsOurs = _pulling || _clock.Now < _quietUntil;
            if (!_fallIsOurs)
            {
                SlotStarted();
            }
        }
        else if (level == true && before == false && !_fallIsOurs)
        {
            SlotEnded(_clock.Now - _fallAt);
        }
    }

    /// <summary>Power lost or the run reset: the device lets go of the line and forgets the transaction.</summary>
    public void Abort()
    {
        _presence?.Cancel();
        _release?.Cancel();
        Pull(false);
        _out.Clear();
        Poll = null;
        _phase = Phase.Idle;
    }

    /// <summary>A function command byte, once the ROM layer selected this device.</summary>
    protected abstract void OnFunctionByte(byte value);

    /// <summary>Queues bytes to send, least significant bit first, in the read slots that follow.</summary>
    protected void Send(params ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            for (int i = 0; i < 8; i++)
            {
                _out.Enqueue(((b >> i) & 1) != 0);
            }
        }
    }

    /// <summary>The 1-Wire CRC-8 (x^8 + x^5 + x^4 + 1, reflected), as the ROM and the scratchpad carry it.</summary>
    public static byte Crc8(ReadOnlySpan<byte> data)
    {
        byte crc = 0;
        foreach (byte value in data)
        {
            byte b = value;
            for (int i = 0; i < 8; i++)
            {
                bool mix = ((crc ^ b) & 1) != 0;
                crc >>= 1;
                if (mix)
                {
                    crc ^= 0x8C;
                }

                b >>= 1;
            }
        }

        return crc;
    }

    private bool Talking => _phase switch
    {
        Phase.ReadRom => true,
        Phase.Search => _searchStep < 2,
        Phase.Function => _out.Count > 0 || Poll is not null,
        _ => false,
    };

    private bool RomBit(int index) => ((Rom >> index) & 1) != 0;

    private void SlotStarted()
    {
        _slotTalks = Talking;
        if (!_slotTalks)
        {
            return;
        }

        bool bit = _phase switch
        {
            Phase.ReadRom => RomBit(_romBit++),
            Phase.Search => _searchStep == 0 ? RomBit(_romBit) : !RomBit(_romBit),
            _ => _out.TryDequeue(out bool queued) ? queued : Poll?.Invoke() ?? true,
        };
        if (_phase == Phase.ReadRom && _romBit == 64)
        {
            _phase = Phase.Function;
        }
        else if (_phase == Phase.Search)
        {
            _searchStep++;
        }

        if (!bit)
        {
            Pull(true);
            _release = _clock.Schedule(HoldTime, () => Pull(false));
        }
    }

    private void SlotEnded(ulong lowFor)
    {
        if (lowFor >= ResetMinimum)
        {
            Reset();
            return;
        }

        if (!_slotTalks && _phase != Phase.Idle)
        {
            Received(lowFor < SampleTime);
        }
    }

    private void Received(bool bit)
    {
        switch (_phase)
        {
            case Phase.MatchRom:
                if (bit != RomBit(_romBit++))
                {
                    _phase = Phase.Idle;
                }
                else if (_romBit == 64)
                {
                    _phase = Phase.Function;
                }

                return;
            case Phase.Search:
                _searchStep = 0;
                if (bit != RomBit(_romBit++))
                {
                    _phase = Phase.Idle;
                }
                else if (_romBit == 64)
                {
                    _phase = Phase.Function;
                }

                return;
        }

        _shift |= (bit ? 1 : 0) << _bits;
        if (++_bits < 8)
        {
            return;
        }

        byte value = (byte)_shift;
        _shift = 0;
        _bits = 0;
        if (_phase == Phase.RomCommand)
        {
            RomCommand(value);
        }
        else
        {
            OnFunctionByte(value);
        }
    }

    private void RomCommand(byte command)
    {
        _romBit = 0;
        _searchStep = 0;
        _phase = command switch
        {
            0x33 => Phase.ReadRom,
            0x55 => Phase.MatchRom,
            0xCC => Phase.Function,
            0xF0 => Phase.Search,
            0xEC => AlarmActive ? Phase.Search : Phase.Idle,
            _ => Phase.Idle,
        };
    }

    private void Reset()
    {
        Abort();
        _bits = 0;
        _shift = 0;
        _phase = Phase.RomCommand;
        _quietUntil = _clock.Now + PresenceWindow;
        _presence = _clock.Schedule(PresenceDelay, () =>
        {
            Pull(true);
            _presence = _clock.Schedule(PresenceLength, () => Pull(false));
        });
    }

    private void Pull(bool low)
    {
        _pulling = low;
        _driver.Set(low ? NetDrive.Strong(0) : NetDrive.None);
    }
}
