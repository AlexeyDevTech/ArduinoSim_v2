using System.Globalization;
using ArduSim.Core.Boards;
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
/// table. Until nets arrive (M2), nothing outside the MCU drives a pin: an input without its
/// pull-up floats and reads noise, with a one-time warning.
/// </summary>
public sealed class Mcu
{
    private const int AnalogReferenceDefault = 1;

    private readonly SimulationEngine _engine;
    private readonly TimingTable _timing;
    private readonly PinState?[] _pins;
    private readonly Random _noise;
    private ulong _eepromReadyAt;

    public Mcu(BoardProfile board, SimulationEngine engine, EepromMemory eeprom, int seed)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(engine);
        Board = board;
        _engine = engine;
        _timing = board.Timing;
        Eeprom = eeprom;
#pragma warning disable CA5394 // a simulation needs a reproducible generator, not a secure one
        _noise = new Random(seed);
#pragma warning restore CA5394
        _pins = new PinState?[board.Pins.Max(p => p.Number) + 1];
        foreach (BoardPin pin in board.Pins)
        {
            _pins[pin.Number] = new PinState(pin);
        }

        Uarts = Enumerable.Range(0, board.SerialPorts).Select(i => new UartPort(i, engine)).ToList();
        Interrupts = new InterruptController(Level);
    }

    public event Action<PinChange>? PinChanged;

    public BoardProfile Board { get; }

    public EepromMemory Eeprom { get; }

    public IReadOnlyList<UartPort> Uarts { get; }

    public InterruptController Interrupts { get; }

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
            Log.ReportOnce($"analog-pin-{pin}", SimSeverity.Warning, "invalid-pin", $"analogRead({Board.PinName(pin)}): not an analog input on {Board.DisplayName}; it reads 0.");
        }
        else
        {
            Log.ReportOnce($"floating-analog-{pin}", SimSeverity.Warning, "floating-input", $"{state.Info.Name} is not connected; analogRead returns noise.");
            value = _noise.Next(Board.AdcMax + 1);
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
        bool? level = Level(state);
        if (level is { } known)
        {
            return known ? 1 : 0;
        }

        Log.ReportOnce($"floating-{state.Info.Number}", SimSeverity.Warning, "floating-input", $"{state.Info.Name} is floating: it is an input with no pull-up and nothing connected, so it reads random values.");
        return _noise.Next(2);
    }

    private bool? Level(int pin) => Find(pin) is { } state ? Level(state) : null;

    private static bool? Level(PinState state) => state.Mode switch
    {
        Simulation.PinMode.Output => state.ToneHz > 0 ? null : state.PwmDuty > 0 ? state.PwmDuty >= 128 : state.PortBit,
        Simulation.PinMode.InputPullup => true,
        _ => null,
    };

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
        PinDrive drive = state.Mode != Simulation.PinMode.Output ? new PinDrive(DriveKind.None, 0)
            : state.ToneHz > 0 ? new PinDrive(DriveKind.Tone, state.ToneHz)
            : state.PwmDuty > 0 ? new PinDrive(DriveKind.Pwm, state.PwmDuty)
            : new PinDrive(state.PortBit ? DriveKind.High : DriveKind.Low, 0);
        bool? level = Level(state);
        if (level != state.LastLevel)
        {
            Interrupts.OnLevelChanged(state.Info.Number, state.LastLevel, level);
            state.LastLevel = level;
        }

        if (drive != state.Drive)
        {
            state.Drive = drive;
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

    private sealed class PinState(BoardPin info)
    {
        public BoardPin Info { get; } = info;

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
