using System.Globalization;
using ArduSim.Core.Boards;
using ArduSim.Core.Buses;
using ArduSim.Core.Circuits;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Timing;

namespace ArduSim.Core.Simulation;

public enum PinMode
{
    Input = 0,
    Output = 1,
    InputPullup = 2,
}

public enum DriveKind
{
    /// <summary>The pin is an input and drives nothing.</summary>
    None,
    Low,
    High,
    Pwm,
    Tone,
}

/// <param name="Value">PWM duty (0-255) or tone frequency in Hz.</param>
public readonly record struct PinDrive(DriveKind Kind, int Value)
{
    public override string ToString() => Kind switch
    {
        DriveKind.None => "input",
        DriveKind.Low => "LOW",
        DriveKind.High => "HIGH",
        DriveKind.Pwm => string.Create(CultureInfo.InvariantCulture, $"PWM {Value}/255"),
        _ => string.Create(CultureInfo.InvariantCulture, $"tone {Value} Hz"),
    };
}

/// <summary>What a pin drives changed at virtual time <paramref name="Time"/>.</summary>
public readonly record struct PinChange(ulong Time, int Pin, PinDrive Drive);

/// <summary>
/// The microcontroller as the firmware sees it through the host API: pins, PWM, tone, ADC,
/// UARTs, EEPROM and external interrupts, each call charging its cost from the board's timing
/// table. Pins drive their nets in the <see cref="Circuit"/> (outputs strongly, INPUT_PULLUP
/// through ~35 kΩ) and read back the resolved net, so buttons, pull-ups and shorts behave as wired.
/// </summary>
public sealed class Mcu
{
    private const int AnalogReferenceDefault = 1;
    private const int AnalogReferenceInternal = 3;
    private const int AnalogReferenceExternal = 0;
    private const double InternalPullUp = 35_000;
    private const double InternalReference = 1.1;
    private const uint DefaultI2cClock = 100_000;

    private readonly SimulationEngine _engine;
    private readonly TimingTable _timing;
    private readonly Circuit _circuit;
    private readonly string _boardId;
    private readonly PinState?[] _pins;
    private readonly Random _noise;
    private ulong _eepromReadyAt;
    private ulong _i2cBitTime = VirtualTime.Second / DefaultI2cClock;
    private bool _i2cEnabled;

    /// <summary>Registers the MCU's pin drivers; build <paramref name="circuit"/> afterwards.</summary>
    /// <param name="i2c">The board's I2C bus (null when the board has none); devices registered on it answer the Wire calls.</param>
    public Mcu(BoardProfile board, SimulationEngine engine, EepromMemory eeprom, int seed, Circuit circuit, string boardId, I2cBus? i2c = null)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(circuit);
        Board = board;
        _engine = engine;
        _timing = board.Timing;
        _circuit = circuit;
        _boardId = boardId;
        Eeprom = eeprom;
        I2c = i2c;
#pragma warning disable CA5394 // a simulation needs a reproducible generator, not a secure one
        _noise = new Random(seed);
#pragma warning restore CA5394
        _pins = new PinState?[board.Pins.Max(p => p.Number) + 1];
        foreach (BoardPin pin in board.Pins)
        {
            var reference = new PinRef(boardId, pin.Name);
            var state = new PinState(pin, reference, circuit.AddDriver(reference, boardId));
            _pins[pin.Number] = state;
            circuit.Watch(reference, () => OnNetChanged(state));
        }

        Uarts = Enumerable.Range(0, board.SerialPorts).Select(i => new UartPort(i, engine)).ToList();
        Interrupts = new InterruptController(Level);
    }

    public event Action<PinChange>? PinChanged;

    public BoardProfile Board { get; }

    public EepromMemory Eeprom { get; }

    public IReadOnlyList<UartPort> Uarts { get; }

    public InterruptController Interrupts { get; }

    public I2cBus? I2c { get; }

    /// <summary>The Wire clock; a byte costs 9 bit times, START and STOP one each (ADR-005).</summary>
    public uint I2cClock => (uint)(VirtualTime.Second / _i2cBitTime);

    public int AnalogReference { get; private set; } = AnalogReferenceDefault;

    private SimulationLog Log => _engine.Log;

    private ulong Now => _engine.Clock.Now;

    public PinDrive DriveOf(int pin) => Find(pin)?.Drive ?? new PinDrive(DriveKind.None, 0);

    public void PinMode(int pin, int mode)
    {
        if (Digital(pin, "pinMode") is { } state)
        {
            state.Mode = mode switch
            {
                1 => Simulation.PinMode.Output,
                2 => Simulation.PinMode.InputPullup,
                _ => Simulation.PinMode.Input,
            };
            if (state.Mode != Simulation.PinMode.Output)
            {
                state.PortBit = state.Mode == Simulation.PinMode.InputPullup;
            }

            Update(state);
        }

        _engine.Finish(_timing.PinMode);
    }

    public void DigitalWrite(int pin, int level)
    {
        if (Digital(pin, "digitalWrite") is { } state)
        {
            state.PortBit = level != 0;
            state.PwmDuty = 0;
            if (state.Mode == Simulation.PinMode.InputPullup || state.Mode == Simulation.PinMode.Input)
            {
                // On AVR, writing an input switches its pull-up.
                state.Mode = state.PortBit ? Simulation.PinMode.InputPullup : Simulation.PinMode.Input;
            }

            Update(state);
        }

        _engine.Finish(_timing.DigitalWrite);
    }

    public int DigitalRead(int pin)
    {
        int value = Digital(pin, "digitalRead") is { } state ? ReadLevel(state) : 0;
        _engine.Finish(_timing.DigitalRead);
        return value;
    }

    public int AnalogRead(int pin)
    {
        int value = 0;
        PinState? state = Find(pin);
        if (state is null || !state.Info.Has(PinCapabilities.Analog))
        {
            Log.ReportOnce($"analog-pin-{pin}", SimSeverity.Warning, "invalid-pin", $"analogRead({Board.PinName(pin)}): not an analog input on {Board.DisplayName}; it reads 0.", variant: "analog");
        }
        else
        {
            NetState net = _circuit.StateOf(state.Reference);
            if (net.HasVoltage)
            {
                double reference = ReferenceVoltage();
                value = (int)Math.Clamp(Math.Round(net.AverageVoltage / reference * Board.AdcMax), 0, Board.AdcMax);
            }
            else
            {
                ReportFloating(state, analog: true);
                value = _noise.Next(Board.AdcMax + 1);
            }
        }

        _engine.Finish(_timing.AnalogRead);
        return value;
    }

    public void AnalogReferenceMode(int mode)
    {
        AnalogReference = mode;
        _engine.Finish(_timing.ShortCall);
    }

    public void AnalogWrite(int pin, int value)
    {
        if (Digital(pin, "analogWrite") is { } state)
        {
            state.Mode = Simulation.PinMode.Output;
            if (!state.Info.Has(PinCapabilities.Pwm))
            {
                // As on AVR: a pin without PWM goes LOW below 128 and HIGH from 128.
                state.PortBit = value >= 128;
                state.PwmDuty = 0;
            }
            else if (value <= 0 || value >= 255)
            {
                state.PortBit = value >= 255;
                state.PwmDuty = 0;
            }
            else
            {
                state.PwmDuty = value;
            }

            Update(state);
        }

        _engine.Finish(_timing.AnalogWrite);
    }

    public void Tone(int pin, uint frequency, uint durationMs)
    {
        if (Digital(pin, "tone") is { } state)
        {
            state.Mode = Simulation.PinMode.Output;
            state.ToneEnd?.Cancel();
            state.ToneEnd = null;
            state.ToneHz = (int)Math.Min(frequency, int.MaxValue);
            if (durationMs > 0)
            {
                state.ToneEnd = _engine.Clock.Schedule(durationMs * VirtualTime.Millisecond, () => StopTone(state));
            }

            Update(state);
        }

        _engine.Finish(_timing.ShortCall);
    }

    public void NoTone(int pin)
    {
        if (Digital(pin, "noTone") is { } state)
        {
            StopTone(state);
        }

        _engine.Finish(_timing.ShortCall);
    }

    public ulong NowMicros()
    {
        ulong value = Now / VirtualTime.Microsecond;
        _engine.Finish(_timing.ClockRead);
        return value;
    }

    public void DelayMicros(uint microseconds)
    {
        _engine.Delay(microseconds * VirtualTime.Microsecond);
        _engine.Finish(0);
    }

    public void Yield() => _engine.Finish(0);

    public void SerialBegin(int port, uint baud, uint config)
    {
        Uart(port)?.Begin(baud, config);
        _engine.Finish(_timing.ShortCall);
    }

    public void SerialEnd(int port)
    {
        if (Uart(port) is { } uart)
        {
            _engine.WaitUntil(() => uart.TxIdle, uart.Name + " to finish sending");
            uart.End();
        }

        _engine.Finish(_timing.ShortCall);
    }

    public int SerialWrite(int port, ReadOnlySpan<byte> data)
    {
        if (Uart(port) is not { } uart)
        {
            _engine.Finish(_timing.ShortCall);
            return 0;
        }

        foreach (byte value in data)
        {
            while (!uart.TryEnqueue(value))
            {
                _engine.WaitUntil(() => !uart.TxFull, uart.Name + " TX buffer space");
            }
        }

        _engine.Finish(_timing.ShortCall);
        return data.Length;
    }

    public int SerialRead(int port) => SerialQuery(port, u => u.Read(), -1);

    public int SerialPeek(int port) => SerialQuery(port, u => u.Peek(), -1);

    public int SerialAvailable(int port) => SerialQuery(port, u => u.Available, 0);

    public int SerialAvailableForWrite(int port) => SerialQuery(port, u => u.AvailableForWrite, 0);

    public void SerialFlush(int port)
    {
        if (Uart(port) is { } uart)
        {
            _engine.WaitUntil(() => uart.TxIdle, uart.Name + " to finish sending");
        }

        _engine.Finish(_timing.ShortCall);
    }

    /// <summary>Wire.begin(): the TWI takes SDA and SCL and, as the AVR core does, turns on their internal pull-ups.</summary>
    public void I2cBegin()
    {
        if (I2cPins() is var (sda, scl))
        {
            foreach (PinState state in new[] { sda, scl })
            {
                state.Mode = Simulation.PinMode.InputPullup;
                state.PortBit = true;
                state.PwmDuty = 0;
                Update(state);
            }

            _i2cEnabled = true;
        }

        _engine.Finish(_timing.ShortCall);
    }

    public void I2cEnd()
    {
        _i2cEnabled = false;
        I2c?.Stop();
        _engine.Finish(_timing.ShortCall);
    }

    public void I2cSetClock(uint hz)
    {
        if (hz > 0)
        {
            _i2cBitTime = Math.Max(1, VirtualTime.Second / hz);
        }

        _engine.Finish(_timing.ShortCall);
    }

    /// <summary>START (repeated when no STOP came since the last one) and the address byte; true on ACK.</summary>
    public bool I2cStart(int address, bool read)
    {
        bool ack = RequireI2c() is { } bus && bus.Start(address, read);
        _engine.Finish(10 * _i2cBitTime);
        return ack;
    }

    public bool I2cWrite(byte value)
    {
        bool ack = RequireI2c() is { } bus && bus.Write(value);
        _engine.Finish(9 * _i2cBitTime);
        return ack;
    }

    public byte I2cRead(bool ack)
    {
        byte value = RequireI2c() is { } bus ? bus.Read(ack) : (byte)0xFF;
        _engine.Finish(9 * _i2cBitTime);
        return value;
    }

    public void I2cStop()
    {
        I2c?.Stop();
        _engine.Finish(_i2cBitTime);
    }

    public void BusNotSimulated(string bus)
    {
        Log.ReportOnce("bus-" + bus, SimSeverity.Warning, "not-simulated", $"{bus} is not simulated yet; transfers see no device.");
        _engine.Finish(_timing.ShortCall);
    }

    public byte EepromRead(int address)
    {
        WaitForEeprom();
        byte value = Eeprom.Read(EepromAddress(address));
        _engine.Finish(_timing.ShortCall);
        return value;
    }

    public void EepromWrite(int address, byte value)
    {
        WaitForEeprom();
        Eeprom.Write(EepromAddress(address), value);
        _eepromReadyAt = Now + _timing.EepromProgramming;
        _engine.Finish(_timing.ShortCall);
    }

    public void AttachInterrupt(int number, int mode)
    {
        if (Board.ExternalInterrupts.TryGetValue(number, out int pin))
        {
            Interrupts.Attach(number, pin, (InterruptMode)Math.Clamp(mode, 0, 3));
        }
        else
        {
            Log.ReportOnce($"interrupt-{number}", SimSeverity.Warning, "invalid-interrupt", $"attachInterrupt({number}): {Board.DisplayName} has no external interrupt {number}.");
        }

        _engine.Finish(_timing.ShortCall);
    }

    public void DetachInterrupt(int number)
    {
        Interrupts.Detach(number);
        _engine.Finish(_timing.ShortCall);
    }

    public void SetInterruptsEnabled(bool enabled)
    {
        Interrupts.Enabled = enabled;
        _engine.Finish(0);
    }

    private PinState? Find(int pin) => pin >= 0 && pin < _pins.Length ? _pins[pin] : null;

    private (PinState Sda, PinState Scl)? I2cPins() =>
        Board.I2cSda is { } sda && Board.I2cScl is { } scl && Find(sda) is { } sdaState && Find(scl) is { } sclState ? (sdaState, sclState) : null;

    private I2cBus? RequireI2c()
    {
        if (I2c is null)
        {
            Log.ReportOnce("i2c-none", SimSeverity.Warning, "i2c-unavailable", $"{Board.DisplayName} has no hardware I2C; Wire transfers see no device.");
            return null;
        }

        if (!_i2cEnabled)
        {
            Log.ReportOnce("i2c-begin", SimSeverity.Warning, "i2c-not-begun", $"Wire is used before Wire.begin(); a real board would hang here, the simulation carries on.");
        }

        return I2c;
    }

    private PinState? Digital(int pin, string operation)
    {
        PinState? state = Find(pin);
        if (state is null)
        {
            Log.ReportOnce($"invalid-pin-{pin}", SimSeverity.Warning, "invalid-pin", $"{operation}({pin}): {Board.DisplayName} has no pin {pin}; the call is ignored.");
            return null;
        }

        if (!state.Info.Has(PinCapabilities.Digital))
        {
            Log.ReportOnce($"analog-only-{pin}", SimSeverity.Warning, "analog-only-pin", $"{operation}({state.Info.Name}): {state.Info.Name} is analog-only on {Board.DisplayName}; the call is ignored.");
            return null;
        }

        return state;
    }

    private int ReadLevel(PinState state)
    {
        NetState net = _circuit.StateOf(state.Reference);
        if (Level(net) is { } known)
        {
            return known ? 1 : 0;
        }

        if (net.Condition == NetCondition.Floating)
        {
            ReportFloating(state, analog: false);
        }
        else if (net.Condition != NetCondition.Conflict)
        {
            Log.ReportOnce(
                $"undefined-{state.Info.Number}",
                SimSeverity.Warning,
                "undefined-level",
                $"{state.Info.Name} is at {net.Voltage:0.0#} V, between the LOW ({Board.InputLowVoltage:0.#} V) and HIGH ({Board.InputHighVoltage:0.#} V) thresholds, so it reads random values.",
                PointAt(state));
        }

        return _noise.Next(2);
    }

    private void ReportFloating(PinState state, bool analog)
    {
        string name = state.Info.Name;
        string net = _circuit.NetOf(state.Reference).Name;
        if (analog)
        {
            Log.ReportOnce($"floating-{state.Info.Number}", SimSeverity.Warning, "floating-input", $"{name} is floating: nothing drives or pulls its net ({net}), so analogRead returns noise.", PointAt(state), "analog");
        }
        else
        {
            Log.ReportOnce($"floating-{state.Info.Number}", SimSeverity.Warning, "floating-input", $"{name} is floating: nothing drives or pulls its net ({net}), so it reads random values.", PointAt(state));
        }
    }

    // Warnings point at the part wired to the pin, or at the board when nothing is.
    private string PointAt(PinState state) =>
        _circuit.NetOf(state.Reference).Pins.Select(p => p.Component).FirstOrDefault(c => c != _boardId) ?? _boardId;

    private bool? Level(int pin) => Find(pin) is { } state ? Level(_circuit.StateOf(state.Reference)) : null;

    private bool? Level(NetState net)
    {
        if (!net.HasVoltage)
        {
            return null;
        }

        double voltage = net.Duty < 1 ? (net.Duty >= 0.5 ? net.Voltage : 0) : net.Voltage;
        return voltage >= Board.InputHighVoltage ? true : voltage <= Board.InputLowVoltage ? false : null;
    }

    private double ReferenceVoltage()
    {
        if (AnalogReference == AnalogReferenceInternal)
        {
            return InternalReference;
        }

        if (AnalogReference == AnalogReferenceExternal)
        {
            NetState aref = _circuit.Contains(new PinRef(_boardId, "AREF")) ? _circuit.StateOf(new PinRef(_boardId, "AREF")) : default;
            if (aref.HasVoltage && aref.Voltage > 0)
            {
                return aref.Voltage;
            }

            Log.ReportOnce("aref", SimSeverity.Warning, "floating-input", $"analogReference(EXTERNAL) is set but AREF is not connected; using Vcc.", _boardId, "aref");
        }

        return Board.LogicVoltage;
    }

    private void OnNetChanged(PinState state)
    {
        bool? level = Level(_circuit.StateOf(state.Reference));
        if (level != state.LastLevel)
        {
            Interrupts.OnLevelChanged(state.Info.Number, state.LastLevel, level);
            state.LastLevel = level;
        }
    }

    private void StopTone(PinState state)
    {
        state.ToneEnd?.Cancel();
        state.ToneEnd = null;
        state.ToneHz = 0;
        state.PortBit = false;
        Update(state);
    }

    private void Update(PinState state)
    {
        double vcc = Board.LogicVoltage;
        (PinDrive drive, NetDrive net) = state.Mode switch
        {
            Simulation.PinMode.InputPullup => (new PinDrive(DriveKind.None, 0), NetDrive.Weak(vcc, InternalPullUp)),
            Simulation.PinMode.Input => (new PinDrive(DriveKind.None, 0), NetDrive.None),
            _ when state.ToneHz > 0 => (new PinDrive(DriveKind.Tone, state.ToneHz), NetDrive.Strong(vcc, 0.5, state.ToneHz)),
            _ when state.PwmDuty > 0 => (new PinDrive(DriveKind.Pwm, state.PwmDuty), NetDrive.Strong(vcc, state.PwmDuty / 255.0)),
            _ => (new PinDrive(state.PortBit ? DriveKind.High : DriveKind.Low, 0), NetDrive.Strong(state.PortBit ? vcc : 0)),
        };

        bool changed = drive != state.Drive;
        state.Drive = drive;
        state.Driver.Set(net);
        if (changed)
        {
            PinChanged?.Invoke(new PinChange(Now, state.Info.Number, drive));
        }
    }

    private UartPort? Uart(int port)
    {
        if (port >= 0 && port < Uarts.Count)
        {
            return Uarts[port];
        }

        Log.ReportOnce($"uart-{port}", SimSeverity.Warning, "invalid-serial", $"{Board.DisplayName} has no serial port {port}.");
        return null;
    }

    private int SerialQuery(int port, Func<UartPort, int> query, int fallback)
    {
        int value = Uart(port) is { } uart ? query(uart) : fallback;
        _engine.Finish(_timing.ShortCall);
        return value;
    }

    private void WaitForEeprom()
    {
        if (Now < _eepromReadyAt)
        {
            _engine.Delay(_eepromReadyAt - Now);
        }
    }

    private int EepromAddress(int address)
    {
        if (address >= 0 && address < Eeprom.Size)
        {
            return address;
        }

        int wrapped = (int)((uint)address % (uint)Eeprom.Size);
        Log.ReportOnce("eeprom-range", SimSeverity.Warning, "eeprom-address", $"EEPROM address {address} is outside 0..{Eeprom.Size - 1}; it wraps to {wrapped} as on the ATmega328P.");
        return wrapped;
    }

    private sealed class PinState(BoardPin info, PinRef reference, Driver driver)
    {
        public BoardPin Info { get; } = info;

        public PinRef Reference { get; } = reference;

        public Driver Driver { get; } = driver;

        public PinMode Mode { get; set; } = Simulation.PinMode.Input;

        /// <summary>The PORTx bit: output level, or pull-up enable for an input.</summary>
        public bool PortBit { get; set; }

        public int PwmDuty { get; set; }

        public int ToneHz { get; set; }

        public ScheduledEvent? ToneEnd { get; set; }

        public PinDrive Drive { get; set; } = new(DriveKind.None, 0);

        public bool? LastLevel { get; set; }
    }
}
