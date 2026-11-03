using System.Globalization;
using ArduSim.Core.Buses;
using ArduSim.Core.Circuits;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Timing;

namespace ArduSim.Core.Components.Parts;

/// <summary>
/// DS3231 real-time clock on its ZS-042 module (I2C 0x68, 4.7 kΩ pull-ups, SQW/INT open drain with
/// a pull-up). The time starts from the computer's clock (or a fixed time) when the run starts and
/// goes on with virtual time; writing the time registers sets it for the rest of the run. Registers
/// follow the datasheet: BCD time and date with 12/24-hour mode and century, both alarms with their
/// mask bits raising A1F/A2F and pulling INT low when enabled, control and status (OSF clear: the
/// battery kept the time), the temperature in 0.25 °C steps. The 1 Hz square wave on SQW is
/// simulated; faster rates and the 32 kHz output are not.
/// </summary>
[SimComponent]
public sealed class Ds3231 : SimComponent, II2cDevice
{
    private const int TimeRegisters = 7;

    private readonly byte[] _registers = new byte[0x13];
    private readonly bool[] _timeWritten = new bool[TimeRegisters];
    private Driver _sqw = null!;
    private DateTime _start;
    private ulong _startAt;
    private int _dayOffset;
    private int _pointer;
    private bool _first;
    private bool _wave;
    private double _temperature;
    private ScheduledEvent? _tick;

    public Ds3231(string id, ComponentProperties properties)
        : base(id, Spec, properties)
    {
    }

    public static ComponentDefinition Spec { get; } = new(
        "ds3231",
        "Part_Ds3231",
        "module",
        90,
        70,
        [
            new PinDefinition("32K", PinKind.Digital, 20, 70),
            new PinDefinition("SQW", PinKind.Digital, 30, 70),
            new PinDefinition("SCL", PinKind.Digital, 40, 70),
            new PinDefinition("SDA", PinKind.Digital, 50, 70),
            new PinDefinition("VCC", PinKind.Power, 60, 70),
            new PinDefinition("GND", PinKind.Ground, 70, 70),
        ],
        [
            new PropertyDefinition("start", PropertyKind.Choice, "computer", ["computer", "fixed"]),
            new PropertyDefinition("time", PropertyKind.Text, "2026-01-01 12:00:00") { ShownWhen = ("start", "fixed") },
            new PropertyDefinition("temperature", PropertyKind.Number, "25", Minimum: -40, Maximum: 85, Unit: "°C") { Live = true },
        ]);

    public int Address => 0x68;

    public PinRef Sda => Pin("SDA");

    public PinRef Scl => Pin("SCL");

    private bool Powered => PartSupply.Across(Net("VCC"), Net("GND")) >= 2.3;

    private ulong Now => Context.Engine.Clock.Now;

    /// <summary>The clock's date and time now (the run's virtual time since the start).</summary>
    public DateTime Time => _start.AddTicks((long)((Now - _startAt) / 100));

    protected override void OnAttach()
    {
        _temperature = Properties.Number("temperature");
        _sqw = Circuit.AddDriver(Pin("SQW"), Id);
        Circuit.AddResistor(Pin("SDA"), Pin("VCC"), 4700, Id);
        Circuit.AddResistor(Pin("SCL"), Pin("VCC"), 4700, Id);
        Circuit.AddResistor(Pin("SQW"), Pin("VCC"), 10_000, Id);
        Context.I2c?.Register(this);
    }

    public override void OnCircuitReady()
    {
        _start = StartTime();
        _startAt = Now;
        _dayOffset = 0;
        _registers[0x0E] = 0x1C; // INTCN, RS2, RS1: interrupt mode, as at power-up
        _registers[0x0F] = 0x08; // EN32kHz; OSF clear because the battery kept the clock running
        SetState("temperature", _temperature);
        ScheduleTick();
    }

    public override void ApplyInput(string name, double value)
    {
        if (name != "temperature")
        {
            base.ApplyInput(name, value);
            return;
        }

        _temperature = Math.Clamp(value, -40, 85);
        SetState("temperature", _temperature);
    }

    public bool OnStart(bool read)
    {
        if (!Powered)
        {
            return false;
        }

        _first = !read;
        if (read)
        {
            Snapshot();
        }

        return true;
    }

    public bool OnWrite(byte value)
    {
        if (_first)
        {
            _first = false;
            _pointer = value % _registers.Length;
            return true;
        }

        if (_pointer < TimeRegisters)
        {
            if (!_timeWritten.Any(w => w))
            {
                Snapshot();
            }

            _timeWritten[_pointer] = true;
        }

        _registers[_pointer] = _pointer == 0x0F ? (byte)((_registers[0x0F] & value & 0x83) | (value & 0x08)) : value;
        if (_pointer is 0x0E or 0x0F)
        {
            UpdateOutputs();
        }

        _pointer = (_pointer + 1) % _registers.Length;
        return true;
    }

    public byte OnRead(bool ack)
    {
        byte value = _registers[_pointer];
        _pointer = (_pointer + 1) % _registers.Length;
        return value;
    }

    public void OnStop()
    {
        if (_timeWritten.Any(w => w))
        {
            SetTime();
            Array.Clear(_timeWritten);
        }
    }

    // The datasheet's "user buffer": time and temperature copied into the registers when a read starts.
    private void Snapshot()
    {
        DateTime time = Time;
        bool twelve = (_registers[2] & 0x40) != 0;
        _registers[0] = Bcd(time.Second);
        _registers[1] = Bcd(time.Minute);
        _registers[2] = twelve ? (byte)(0x40 | (time.Hour >= 12 ? 0x20 : 0) | Bcd(((time.Hour + 11) % 12) + 1)) : Bcd(time.Hour);
        _registers[3] = (byte)(((((int)time.DayOfWeek + 6) % 7) + _dayOffset + 7) % 7 + 1);
        _registers[4] = Bcd(time.Day);
        _registers[5] = (byte)((time.Year >= 2100 ? 0x80 : 0) | Bcd(time.Month));
        _registers[6] = Bcd(time.Year % 100);
        int quarters = (int)Math.Round(_temperature * 4);
        _registers[0x11] = (byte)(sbyte)(quarters >> 2);
        _registers[0x12] = (byte)((quarters & 3) << 6);
    }

    private void SetTime()
    {
        try
        {
            int hour = (_registers[2] & 0x40) != 0
                ? (Unbcd(_registers[2] & 0x1F) % 12) + ((_registers[2] & 0x20) != 0 ? 12 : 0)
                : Unbcd(_registers[2] & 0x3F);
            int year = 2000 + Unbcd(_registers[6]) + ((_registers[5] & 0x80) != 0 ? 100 : 0);
            var time = new DateTime(year, Unbcd(_registers[5] & 0x1F), Unbcd(_registers[4] & 0x3F), hour, Unbcd(_registers[1] & 0x7F), Unbcd(_registers[0] & 0x7F), DateTimeKind.Unspecified);
            _start = time;
            _startAt = Now;
            _dayOffset = (_registers[3] - 1) - (((int)time.DayOfWeek + 6) % 7);
            ScheduleTick();
        }
        catch (ArgumentOutOfRangeException)
        {
            Context.Log.ReportOnce($"rtc-time-{Id}", SimSeverity.Warning, "rtc-time", $"{Id}: the time written to the DS3231 is not a valid date; the clock keeps its time.", Id);
        }
    }

    // Once a second, on the clock's own second: the alarms and the 1 Hz square wave.
    private void ScheduleTick()
    {
        _tick?.Cancel();
        DateTime time = Time;
        long toNext = TimeSpan.TicksPerSecond - (time.Ticks % TimeSpan.TicksPerSecond);
        _tick = Context.Engine.Clock.Schedule((ulong)toNext * 100, Tick);
    }

    private void Tick()
    {
        DateTime time = Time;
        if (Alarm1Matches(time))
        {
            _registers[0x0F] |= 0x01;
        }

        if (time.Second == 0 && Alarm2Matches(time))
        {
            _registers[0x0F] |= 0x02;
        }

        _wave = !_wave;
        UpdateOutputs();
        _tick = Context.Engine.Clock.Schedule(VirtualTime.Second / 2, HalfTick);
    }

    private void HalfTick()
    {
        _wave = !_wave;
        UpdateOutputs();
        ScheduleTick();
    }

    private void UpdateOutputs()
    {
        byte control = _registers[0x0E];
        byte status = _registers[0x0F];
        bool interrupt = (control & 0x04) != 0;
        if (interrupt)
        {
            bool fired = ((status & 0x01) != 0 && (control & 0x01) != 0) || ((status & 0x02) != 0 && (control & 0x02) != 0);
            _sqw.Set(fired && Powered ? NetDrive.Strong(0) : NetDrive.None);
            return;
        }

        if ((control & 0x18) != 0)
        {
            Context.Log.ReportOnce($"rtc-sqw-{Id}", SimSeverity.Warning, "rtc-sqw", $"{Id}: only the 1 Hz square wave on SQW is simulated; the faster rates leave SQW high.", Id);
            _sqw.Set(NetDrive.None);
            return;
        }

        _sqw.Set(_wave && Powered ? NetDrive.Strong(0) : NetDrive.None);
    }

    private bool Alarm1Matches(DateTime time)
    {
        int mask = ((_registers[0x07] >> 7) & 1) | (((_registers[0x08] >> 7) & 1) << 1) | (((_registers[0x09] >> 7) & 1) << 2) | (((_registers[0x0A] >> 7) & 1) << 3);
        bool seconds = Unbcd(_registers[0x07] & 0x7F) == time.Second;
        bool minutes = Unbcd(_registers[0x08] & 0x7F) == time.Minute;
        bool hours = AlarmHour(_registers[0x09]) == time.Hour;
        return mask switch
        {
            0b1111 => true,
            0b1110 => seconds,
            0b1100 => seconds && minutes,
            0b1000 => seconds && minutes && hours,
            0b0000 => seconds && minutes && hours && DayMatches(_registers[0x0A], time),
            _ => false,
        };
    }

    private bool Alarm2Matches(DateTime time)
    {
        int mask = ((_registers[0x0B] >> 7) & 1) | (((_registers[0x0C] >> 7) & 1) << 1) | (((_registers[0x0D] >> 7) & 1) << 2);
        bool minutes = Unbcd(_registers[0x0B] & 0x7F) == time.Minute;
        bool hours = AlarmHour(_registers[0x0C]) == time.Hour;
        return mask switch
        {
            0b111 => true,
            0b110 => minutes,
            0b100 => minutes && hours,
            0b000 => minutes && hours && DayMatches(_registers[0x0D], time),
            _ => false,
        };
    }

    private static int AlarmHour(byte value) => (value & 0x40) != 0
        ? (Unbcd(value & 0x1F) % 12) + ((value & 0x20) != 0 ? 12 : 0)
        : Unbcd(value & 0x3F);

    private bool DayMatches(byte value, DateTime time) => (value & 0x40) != 0
        ? (value & 0x0F) == (((((int)time.DayOfWeek + 6) % 7) + _dayOffset + 7) % 7) + 1
        : Unbcd(value & 0x3F) == time.Day;

    private DateTime StartTime()
    {
        if (Properties.Choice("start") == "fixed")
        {
            string text = Properties.Text("time");
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fixedTime))
            {
                return fixedTime;
            }

            Context.Log.ReportOnce($"rtc-start-{Id}", SimSeverity.Warning, "rtc-start", $"{Id}: the start time '{text}' is not a date and time (2026-01-01 12:00:00); the clock starts from the computer's clock.", Id);
        }

        return DateTime.Now;
    }

    private static byte Bcd(int value) => (byte)(((value / 10) << 4) | (value % 10));

    private static int Unbcd(int value) => ((value >> 4) * 10) + (value & 0x0F);
}
