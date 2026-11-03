using ArduSim.Core.Buses;
using ArduSim.Core.Circuits;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Timing;

namespace ArduSim.Core.Components.Parts;

/// <summary>Supply and logic-level checks the sensor parts share.</summary>
internal static class PartSupply
{
    /// <summary>The voltage across a part's supply pins, or 0 while its ground pin is not on ground.</summary>
    public static double Across(NetState vcc, NetState gnd) =>
        vcc.HasVoltage && gnd.HasVoltage && gnd.Voltage <= 0.5 ? vcc.Voltage - gnd.Voltage : 0;

    /// <summary>A data line's logic level against half the supply (of 3.3 V while the part has none); null while it floats.</summary>
    public static bool? Level(NetState line, double supply) => line.HasVoltage ? line.Voltage >= (supply >= 2.5 ? supply : 3.3) / 2 : null;
}

/// <summary>
/// DHT11 or DHT22 (AM2302) temperature and humidity sensor on one open-drain data line (ADR-011).
/// The board starts a reading by holding DATA low (DHT22: at least 0.8 ms, DHT11: 18 ms) and
/// releasing it; the sensor answers with 80 µs low, 80 µs high and 40 bits, each 50 µs low and then
/// 26 µs (0) or 70 µs (1) high. DATA needs a pull-up: the MCU's (INPUT_PULLUP) or a resistor.
/// </summary>
[SimComponent]
public sealed class DhtSensor : SimComponent
{
    private const ulong Us = VirtualTime.Microsecond;

    private readonly List<ScheduledEvent> _reply = [];
    private Driver _data = null!;
    private double _temperature;
    private double _humidity;
    private bool? _level;
    private ulong _fallAt;
    private bool _answering;

    public DhtSensor(string id, ComponentProperties properties)
        : base(id, Spec, properties)
    {
    }

    public static ComponentDefinition Spec { get; } = new(
        "dht",
        "Part_Dht",
        "sensor",
        50,
        80,
        [
            new PinDefinition("VCC", PinKind.Power, 10, 80),
            new PinDefinition("DATA", PinKind.Digital, 20, 80),
            new PinDefinition("NC", PinKind.Passive, 30, 80),
            new PinDefinition("GND", PinKind.Ground, 40, 80),
        ],
        [
            new PropertyDefinition("model", PropertyKind.Choice, "dht22", ["dht22", "dht11"]),
            new PropertyDefinition("temperature", PropertyKind.Number, "24", Minimum: -40, Maximum: 80, Unit: "°C") { Live = true },
            new PropertyDefinition("humidity", PropertyKind.Number, "40", Minimum: 0, Maximum: 100, Unit: "%") { Live = true },
        ])
    {
        Presets =
        [
            new ComponentPreset("dht22", "Part_Dht22", new Dictionary<string, string> { ["model"] = "dht22" }),
            new ComponentPreset("dht11", "Part_Dht11", new Dictionary<string, string> { ["model"] = "dht11" }),
        ],
    };

    private bool IsDht11 => Properties.Choice("model") == "dht11";

    private string Model => IsDht11 ? "DHT11" : "DHT22";

    private double Supply => PartSupply.Across(Net("VCC"), Net("GND"));

    protected override void OnAttach()
    {
        _temperature = Properties.Number("temperature");
        _humidity = Properties.Number("humidity");
        _data = Circuit.AddDriver(Pin("DATA"), Id);
        Circuit.Watch(Pin("DATA"), OnData);
        WatchPins("VCC", "GND");
    }

    public override void OnCircuitReady()
    {
        SetState("temperature", _temperature);
        SetState("humidity", _humidity);
        _level = PartSupply.Level(Net("DATA"), Supply);
    }

    public override void ApplyInput(string name, double value)
    {
        switch (name)
        {
            case "temperature":
                _temperature = Math.Clamp(value, -40, 80);
                SetState("temperature", _temperature);
                break;
            case "humidity":
                _humidity = Math.Clamp(value, 0, 100);
                SetState("humidity", _humidity);
                break;
            default:
                base.ApplyInput(name, value);
                break;
        }
    }

    // Power: a sensor that loses its supply lets go of the line.
    protected override void Refresh()
    {
        if (Supply < 3 && _answering)
        {
            StopAnswer();
        }
    }

    private void OnData()
    {
        bool? level = PartSupply.Level(Net("DATA"), Supply);
        if (level == _level)
        {
            return;
        }

        bool? before = _level;
        _level = level;
        if (_answering)
        {
            return;
        }

        ulong now = Context.Engine.Clock.Now;
        if (level == false && before == true)
        {
            _fallAt = now;
        }
        else if (level == true && before == false)
        {
            StartSignalEnded(now - _fallAt);
        }
    }

    private void StartSignalEnded(ulong lowFor)
    {
        double supply = Supply;
        if (supply < 3)
        {
            if (lowFor >= 500 * Us)
            {
                Context.Log.ReportOnce($"dht-power-{Id}", SimSeverity.Warning, "sensor-power", $"{Id} ({Model}) has no power: connect VCC to 3.3–5 V and GND to GND.", Id);
            }

            return;
        }

        ulong minimum = IsDht11 ? 18 * VirtualTime.Millisecond : 800 * Us;
        if (lowFor < minimum)
        {
            if (lowFor >= 300 * Us)
            {
                Context.Log.ReportOnce(
                    $"dht-start-{Id}",
                    SimSeverity.Warning,
                    "dht-start",
                    $"{Id} ({Model}): the start signal was {lowFor / (double)VirtualTime.Millisecond:0.0#} ms low, but the {Model} needs at least {minimum / (double)VirtualTime.Millisecond:0.#} ms, so it does not answer. Check the sensor type in the sketch (DHT dht(pin, {Model})).",
                    Id);
            }

            return;
        }

        Answer(Frame());
    }

    private byte[] Frame()
    {
        byte[] data = new byte[5];
        if (IsDht11)
        {
            double t = InRange(_temperature, 0, 50, "°C");
            double h = InRange(_humidity, 20, 90, "%");
            int tenths = (int)Math.Round(t * 10);
            data[0] = (byte)Math.Round(h);
            data[2] = (byte)(tenths / 10);
            data[3] = (byte)(tenths % 10);
        }
        else
        {
            int h = (int)Math.Round(_humidity * 10);
            int t = (int)Math.Round(Math.Abs(_temperature) * 10) | (_temperature < 0 ? 0x8000 : 0);
            data[0] = (byte)(h >> 8);
            data[1] = (byte)h;
            data[2] = (byte)(t >> 8);
            data[3] = (byte)t;
        }

        data[4] = (byte)(data[0] + data[1] + data[2] + data[3]);
        return data;
    }

    private double InRange(double value, double low, double high, string unit)
    {
        if (value >= low && value <= high)
        {
            return value;
        }

        double clamped = Math.Clamp(value, low, high);
        Context.Log.ReportOnce(
            $"dht-range-{Id}-{unit}",
            SimSeverity.Warning,
            "dht-range",
            $"{Id}: {value:0.#} {unit} is outside the DHT11's range ({low:0}–{high:0} {unit}); it reports {clamped:0.#} {unit}.",
            Id);
        return clamped;
    }

    // 20-40 µs after the release: 80 µs low, 80 µs high, then each bit; the line is released at the end.
    private void Answer(byte[] data)
    {
        _answering = true;
        ulong at = 30 * Us;
        Drive(at, low: true);
        at += 80 * Us;
        Drive(at, low: false);
        at += 80 * Us;
        for (int i = 0; i < 40; i++)
        {
            bool one = ((data[i / 8] >> (7 - (i % 8))) & 1) != 0;
            Drive(at, low: true);
            at += 50 * Us;
            Drive(at, low: false);
            at += (one ? 70UL : 26UL) * Us;
        }

        Drive(at, low: true);
        at += 50 * Us;
        Drive(at, low: false);
        _reply.Add(Context.Engine.Clock.Schedule(at, StopAnswer));
    }

    private void Drive(ulong delay, bool low) =>
        _reply.Add(Context.Engine.Clock.Schedule(delay, () => _data.Set(low ? NetDrive.Strong(0) : NetDrive.None)));

    private void StopAnswer()
    {
        foreach (ScheduledEvent step in _reply)
        {
            step.Cancel();
        }

        _reply.Clear();
        _data.Set(NetDrive.None);
        _answering = false;
        _level = PartSupply.Level(Net("DATA"), Supply);
    }
}

/// <summary>
/// HC-SR04 ultrasonic distance sensor: a HIGH pulse of at least 10 µs on TRIG starts a measurement;
/// 460 µs after it ends ECHO goes HIGH for 58.3 µs per centimetre (there and back at 343 m/s).
/// Beyond 400 cm there is no echo and ECHO stays HIGH for 38 ms, as on common modules.
/// </summary>
[SimComponent]
public sealed class UltrasonicSensor : SimComponent
{
    public const double MaximumRange = 400;

    private const ulong Us = VirtualTime.Microsecond;

    private Driver _echo = null!;
    private double _distance;
    private bool? _trig;
    private ulong _riseAt;
    private bool _measuring;
    private bool _echoHigh;
    private ScheduledEvent? _start;
    private ScheduledEvent? _end;

    public UltrasonicSensor(string id, ComponentProperties properties)
        : base(id, Spec, properties)
    {
    }

    public static ComponentDefinition Spec { get; } = new(
        "hc-sr04",
        "Part_HcSr04",
        "sensor",
        90,
        50,
        [
            new PinDefinition("VCC", PinKind.Power, 30, 50),
            new PinDefinition("TRIG", PinKind.Digital, 40, 50),
            new PinDefinition("ECHO", PinKind.Digital, 50, 50),
            new PinDefinition("GND", PinKind.Ground, 60, 50),
        ],
        [new PropertyDefinition("distance", PropertyKind.Number, "100", Minimum: 2, Maximum: 500, Unit: "cm") { Live = true }]);

    private double Supply => PartSupply.Across(Net("VCC"), Net("GND"));

    protected override void OnAttach()
    {
        _distance = Properties.Number("distance");
        _echo = Circuit.AddDriver(Pin("ECHO"), Id);
        Circuit.Watch(Pin("TRIG"), OnTrig);
        WatchPins("VCC", "GND");
    }

    public override void OnCircuitReady()
    {
        SetState("distance", _distance);
        Refresh();
    }

    public override void ApplyInput(string name, double value)
    {
        if (name != "distance")
        {
            base.ApplyInput(name, value);
            return;
        }

        _distance = Math.Clamp(value, 2, 500);
        SetState("distance", _distance);
    }

    // ECHO is a push-pull output at the supply voltage; without power it lets go.
    protected override void Refresh()
    {
        double supply = Supply;
        if (supply < 3)
        {
            _start?.Cancel();
            _end?.Cancel();
            _measuring = false;
            _echoHigh = false;
            _echo.Set(NetDrive.None);
            return;
        }

        _echo.Set(NetDrive.Strong(_echoHigh ? supply : 0));
    }

    private void OnTrig()
    {
        double supply = Supply;
        bool? level = PartSupply.Level(Net("TRIG"), supply);
        if (level == _trig)
        {
            return;
        }

        bool? before = _trig;
        _trig = level;
        ulong now = Context.Engine.Clock.Now;
        if (level == true && before == false)
        {
            _riseAt = now;
        }
        else if (level == false && before == true)
        {
            Triggered(now - _riseAt);
        }
    }

    private void Triggered(ulong highFor)
    {
        if (Supply < 3)
        {
            Context.Log.ReportOnce($"sonar-power-{Id}", SimSeverity.Warning, "sensor-power", $"{Id} (HC-SR04) has no power: connect VCC to 5 V and GND to GND.", Id, "hc-sr04");
            return;
        }

        if (highFor < 10 * Us)
        {
            Context.Log.ReportOnce($"sonar-trig-{Id}", SimSeverity.Warning, "sonar-trigger", $"{Id}: the TRIG pulse was {highFor / (double)Us:0.#} µs; the HC-SR04 needs at least 10 µs and ignores it.", Id);
            return;
        }

        if (Supply < 4.5)
        {
            Context.Log.ReportOnce($"sonar-supply-{Id}", SimSeverity.Warning, "sonar-supply", $"{Id}: the HC-SR04 runs on 5 V; at {Supply:0.0} V a real module measures unreliably. The simulation measures anyway.", Id);
        }

        if (_measuring)
        {
            return;
        }

        _measuring = true;
        double distance = _distance;
        ulong echo = distance > MaximumRange ? 38 * VirtualTime.Millisecond : (ulong)Math.Round(distance * 2 / 0.0343 * Us);
        _start = Context.Engine.Clock.Schedule(460 * Us, () =>
        {
            SetEcho(true);
            _end = Context.Engine.Clock.Schedule(echo, () =>
            {
                SetEcho(false);
                _measuring = false;
            });
        });
    }

    private void SetEcho(bool high)
    {
        _echoHigh = high;
        Refresh();
    }
}

/// <summary>
/// DS18B20 1-Wire thermometer (ADR-011) with its scratchpad, EEPROM copy, 9-12 bit resolution and
/// conversion times, alarm flags and parasite power (VDD on GND). The ROM's serial number comes from
/// the part id, so it is the same in every run. DQ needs a pull-up resistor (4.7 kΩ to VDD).
/// </summary>
[SimComponent]
public sealed class Ds18b20 : SimComponent
{
    private Device _device = null!;
    private Driver _dq = null!;
    private double _temperature;

    public Ds18b20(string id, ComponentProperties properties)
        : base(id, Spec, properties)
    {
    }

    public static ComponentDefinition Spec { get; } = new(
        "ds18b20",
        "Part_Ds18b20",
        "sensor",
        40,
        50,
        [
            new PinDefinition("GND", PinKind.Ground, 10, 50),
            new PinDefinition("DQ", PinKind.Digital, 20, 50),
            new PinDefinition("VDD", PinKind.Power, 30, 50),
        ],
        [new PropertyDefinition("temperature", PropertyKind.Number, "22", Minimum: -55, Maximum: 125, Unit: "°C") { Live = true }]);

    /// <summary>The 64-bit ROM: family 0x28, a serial number from the part id, CRC.</summary>
    public ulong Rom => _device.Rom;

    private bool Grounded => Net("GND") is { HasVoltage: true, Voltage: <= 0.5 };

    /// <summary>VDD on a supply: 3-5.5 V; on ground: parasite power from DQ; otherwise off.</summary>
    private PowerSource Power => !Grounded ? PowerSource.None : Net("VDD") switch
    {
        { HasVoltage: true, Voltage: >= 2.8 } => PowerSource.External,
        { HasVoltage: true, Voltage: <= 0.5 } => PowerSource.Parasite,
        _ => PowerSource.None,
    };

    protected override void OnAttach()
    {
        _temperature = Properties.Number("temperature");
        _dq = Circuit.AddDriver(Pin("DQ"), Id);
        _device = new Device(this, Context.Engine.Clock, _dq, SerialFrom(Id));
        Circuit.Watch(Pin("DQ"), OnLine);
        WatchPins("VDD", "GND");
    }

    public override void OnCircuitReady()
    {
        SetState("temperature", _temperature);
        Refresh();
    }

    public override void ApplyInput(string name, double value)
    {
        if (name != "temperature")
        {
            base.ApplyInput(name, value);
            return;
        }

        _temperature = Math.Clamp(value, -55, 125);
        SetState("temperature", _temperature);
    }

    protected override void Refresh()
    {
        if (Power == PowerSource.None)
        {
            _device.Abort();
        }

        OnLine();
    }

    private void OnLine()
    {
        NetState dq = Net("DQ");
        PowerSource power = Power;
        if (dq.Condition == NetCondition.Floating && Grounded)
        {
            Context.Log.ReportOnce($"onewire-pullup-{Id}", SimSeverity.Warning, "onewire-pullup", $"{Id}: the DQ line floats when the board lets go of it; a 1-Wire bus needs a pull-up resistor (4.7 kΩ from DQ to VDD).", Id);
        }

        bool? level = PartSupply.Level(dq, 4.4); // VIH 2.2 V
        if (power == PowerSource.None && level == false)
        {
            Context.Log.ReportOnce($"ds-power-{Id}", SimSeverity.Warning, "ds18b20-power", $"{Id} (DS18B20) has no power: connect GND to GND and VDD to 3.3–5 V (or VDD to GND for parasite power).", Id);
        }

        _device.OnLine(power == PowerSource.None ? null : level);
    }

    // FNV-1a over the id: a stable 48-bit serial number per part.
    private static ulong SerialFrom(string id)
    {
        ulong hash = 14695981039346656037;
        foreach (char c in id)
        {
            hash = (hash ^ c) * 1099511628211;
        }

        return hash & 0xFFFF_FFFF_FFFF;
    }

    private enum PowerSource
    {
        None,
        External,
        Parasite,
    }

    private sealed class Device : OneWireSlave
    {
        private const byte Family = 0x28;

        private readonly Ds18b20 _part;
        private readonly byte[] _eeprom = [0x4B, 0x46, 0x7F];
        private short _raw = 0x0550; // +85 °C, the power-on value
        private byte _high = 0x4B;
        private byte _low = 0x46;
        private byte _config = 0x7F;
        private int _scratchWrite = -1;
        private ulong _busyUntil;

        public Device(Ds18b20 part, SimulationClock clock, Driver driver, ulong serial)
            : base(clock, driver)
        {
            _part = part;
            Span<byte> rom = stackalloc byte[8];
            rom[0] = Family;
            for (int i = 0; i < 6; i++)
            {
                rom[1 + i] = (byte)(serial >> (8 * i));
            }

            rom[7] = Crc8(rom[..7]);
            Rom = BitConverter.ToUInt64(rom);
        }

        public override ulong Rom { get; }

        private int Resolution => 9 + ((_config >> 5) & 3);

        protected override bool AlarmActive => (sbyte)(_raw >> 4) >= (sbyte)_high || (sbyte)(_raw >> 4) <= (sbyte)_low;

        protected override void OnFunctionByte(byte value)
        {
            if (_scratchWrite >= 0)
            {
                switch (_scratchWrite++)
                {
                    case 0:
                        _high = value;
                        break;
                    case 1:
                        _low = value;
                        break;
                    default:
                        _config = (byte)((value & 0x60) | 0x1F);
                        _scratchWrite = -1;
                        break;
                }

                return;
            }

            switch (value)
            {
                case 0x44:
                    Convert();
                    break;
                case 0xBE:
                    byte[] pad = [(byte)_raw, (byte)(_raw >> 8), _high, _low, _config, 0xFF, 0x0C, 0x10, 0];
                    pad[8] = Crc8(pad.AsSpan(0, 8));
                    Send(pad);
                    Poll = () => true;
                    break;
                case 0x4E:
                    _scratchWrite = 0;
                    break;
                case 0x48:
                    _eeprom[0] = _high;
                    _eeprom[1] = _low;
                    _eeprom[2] = _config;
                    Busy(10 * VirtualTime.Millisecond);
                    break;
                case 0xB8:
                    (_high, _low, _config) = (_eeprom[0], _eeprom[1], _eeprom[2]);
                    Poll = () => true;
                    break;
                case 0xB4:
                    Poll = () => _part.Power != PowerSource.Parasite;
                    break;
            }
        }

        private void Convert()
        {
            ulong time = (93_750 * VirtualTime.Microsecond) << (Resolution - 9);
            Busy(time);
            int resolution = Resolution;
            _part.Context.Engine.Clock.Schedule(time, () => Converted(resolution));
        }

        private void Converted(int resolution)
        {
            if (_part.Power == PowerSource.Parasite && _part.Net("DQ").Condition != NetCondition.Driven)
            {
                _part.Context.Log.ReportOnce(
                    $"ds-parasite-{_part.Id}",
                    SimSeverity.Warning,
                    "ds18b20-parasite",
                    $"{_part.Id} runs on parasite power, but the board did not hold DQ high during the conversion; the reading stays at its old value.",
                    _part.Id);
                return;
            }

            int raw = (int)Math.Round(Math.Clamp(_part._temperature, -55, 125) * 16);
            _raw = (short)(raw & ~((1 << (12 - resolution)) - 1));
        }

        private void Busy(ulong time)
        {
            _busyUntil = Now + time;
            Poll = () => Now >= _busyUntil;
        }
    }
}
