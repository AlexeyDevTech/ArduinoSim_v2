using System.Globalization;
using ArduSim.Core.Boards;
using ArduSim.Core.Buses;
using ArduSim.Core.Circuits;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Projects;
using ArduSim.Core.Timing;

namespace ArduSim.Core.Simulation;

public enum PinMode
{
    Input = 0,
    Output = 1,
    InputPullup = 2,
    InputPulldown = 3,
    OutputOpenDrain = 4,
}

public enum DriveKind
{
    /// <summary>The pin is an input and drives nothing.</summary>
    None,
    Low,
    High,
    Pwm,
    Tone,

    /// <summary>A DAC output at <see cref="PinDrive.Value"/>/255 of the supply.</summary>
    Analog,
}

/// <param name="Value">PWM duty scaled to 0-255, tone frequency in Hz, or the DAC value (0-255).</param>
public readonly record struct PinDrive(DriveKind Kind, int Value)
{
    public override string ToString() => Kind switch
    {
        DriveKind.None => "input",
        DriveKind.Low => "LOW",
        DriveKind.High => "HIGH",
        DriveKind.Pwm => string.Create(CultureInfo.InvariantCulture, $"PWM {Value}/255"),
        DriveKind.Analog => string.Create(CultureInfo.InvariantCulture, $"DAC {Value}/255"),
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

    private const double InternalReference = 1.1;
    private const uint DefaultI2cClock = 100_000;
    private const uint DefaultSpiClock = 4_000_000;
    private const ulong DefaultBitTime = VirtualTime.Second / DefaultI2cClock;

    private readonly SimulationEngine _engine;
    private readonly TimingTable _timing;
    private readonly Circuit _circuit;
    private readonly string _boardId;
    private readonly PinState?[] _pins;
    private readonly Random _noise;
    private ulong _eepromReadyAt;
    private readonly I2cChannel[] _i2c;
    private readonly SpiChannel[] _spi;

    /// <summary>Registers the MCU's pin drivers; build <paramref name="circuit"/> afterwards.</summary>
    /// <param name="i2c">The board's I2C bus (null when the board has none); devices registered on it answer the Wire calls.</param>
    /// <param name="spi">The board's hardware SPI bus (null when the board has none).</param>
    /// <param name="kv">The ESP Preferences store; an empty in-memory one when null.</param>
    public Mcu(BoardProfile board, SimulationEngine engine, EepromMemory eeprom, int seed, Circuit circuit, string boardId, I2cBus? i2c = null, SpiBus? spi = null, KeyValueStore? kv = null)
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
        Kv = kv ?? KeyValueStore.InMemory();
        I2c = i2c;
        Spi = spi;
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

        _i2c = [.. board.I2c.Select((pins, i) => new I2cChannel(i, pins, i == 0 ? i2c : null))];
        _spi = [.. board.Spi.Select((pins, i) => new SpiChannel(i, pins, i == 0 ? spi : null))];
        Uarts = Enumerable.Range(0, board.SerialPorts).Select(i => new UartPort(i, engine)).ToList();
        Interrupts = new InterruptController(Level);
    }

    public event Action<PinChange>? PinChanged;

    public BoardProfile Board { get; }

    public EepromMemory Eeprom { get; }

    public KeyValueStore Kv { get; }

    /// <summary>The simulated Wi-Fi radio of ESP boards; null on boards without one.</summary>
    public WifiRadio? Wifi { get; set; }

    /// <summary>Virtual time of the last boot; millis() and micros() count from here.</summary>
    public ulong BootTime { get; private set; }

    public IReadOnlyList<UartPort> Uarts { get; }

    public InterruptController Interrupts { get; }

    public I2cBus? I2c { get; }

    public SpiBus? Spi { get; }

    /// <summary>The SPI clock of bus 0 as the hardware uses it: the fastest F_CPU / 2..128 not above the request.</summary>
    public uint SpiClock => _spi.Length > 0 ? _spi[0].Clock : DefaultSpiClock;

    /// <summary>The Wire clock of bus 0; a byte costs 9 bit times, START and STOP one each (ADR-005).</summary>
    public uint I2cClock => _i2c.Length > 0 ? (uint)(VirtualTime.Second / _i2c[0].BitTime) : DefaultI2cClock;

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
                3 => Simulation.PinMode.InputPulldown,
                4 => Simulation.PinMode.OutputOpenDrain,
                _ => Simulation.PinMode.Input,
            };
            state.DacValue = -1;
            if (!Supports(state, state.Mode))
            {
                state.Mode = Simulation.PinMode.Input;
            }

            if (state.Mode is not (Simulation.PinMode.Output or Simulation.PinMode.OutputOpenDrain))
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
            state.DacValue = -1;
            if (Board.InputWriteSetsPullUp && state.Mode is Simulation.PinMode.InputPullup or Simulation.PinMode.Input)
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
        else if (Wifi is { Active: true } && state.Info.Has(PinCapabilities.Adc2))
        {
            Log.ReportOnce($"adc2-{pin}", SimSeverity.Warning, "adc2-wifi", $"analogRead({state.Info.Name}): {state.Info.Name} is on ADC2, which Wi-Fi uses while it is on; it reads 0 as on the ESP32. Use an ADC1 pin (GPIO 32-39).");
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

    /// <summary>PWM at the pin's resolution (8 bits unless <see cref="PwmConfig"/> set another); the maximum is a steady HIGH.</summary>
    public void AnalogWrite(int pin, int value)
    {
        if (Output(pin, "analogWrite") is { } state)
        {
            int max = (1 << state.PwmBits) - 1;
            state.Mode = Simulation.PinMode.Output;
            state.DacValue = -1;
            if (!state.Info.Has(PinCapabilities.Pwm))
            {
                // As on AVR: a pin without PWM goes LOW below half and HIGH from half.
                state.PortBit = value >= (max + 1) / 2;
                state.PwmDuty = 0;
            }
            else if (value <= 0 || value >= max)
            {
                state.PortBit = value >= max;
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

    /// <summary>ledcAttach / analogWriteFrequency / analogWriteResolution: the pin's PWM frequency and duty resolution.</summary>
    public void PwmConfig(int pin, uint frequency, int bits)
    {
        if (!Board.PwmConfigurable)
        {
            Log.ReportOnce("pwm-config", SimSeverity.Warning, "pwm-fixed", $"{Board.DisplayName} has fixed 8-bit PWM; the requested frequency and resolution are ignored.");
        }
        else if (Output(pin, "ledcAttach") is { } state)
        {
            if (bits is < 1 or > 20)
            {
                Log.ReportOnce($"pwm-bits-{pin}", SimSeverity.Warning, "pwm-resolution", $"PWM resolution {bits} bits on {state.Info.Name} is out of range 1..20; 8 bits are used.");
                bits = 8;
            }

            state.PwmBits = bits;
            state.PwmFrequency = frequency;
        }

        _engine.Finish(_timing.ShortCall);
    }

    /// <summary>dacWrite(): an 8-bit DAC drives value/255 of the supply.</summary>
    public void DacWrite(int pin, int value)
    {
        PinState? state = Find(pin);
        if (state is null || !state.Info.Has(PinCapabilities.Dac))
        {
            Log.ReportOnce($"dac-{pin}", SimSeverity.Warning, "invalid-pin", $"dacWrite({Board.PinName(pin)}): {Board.DisplayName} has no DAC on this pin; the call is ignored.", variant: "dac");
        }
        else
        {
            state.Mode = Simulation.PinMode.Output;
            state.PwmDuty = 0;
            state.DacValue = Math.Clamp(value, 0, 255);
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
        ulong value = (Now - BootTime) / VirtualTime.Microsecond;
        _engine.Finish(_timing.ClockRead);
        return value;
    }

    public void DelayMicros(uint microseconds)
    {
        _engine.Delay(microseconds * VirtualTime.Microsecond);
        _engine.Finish(0);
    }

    public void Yield() => _engine.Finish(0);

    public void SerialBegin(int port, uint baud, uint config) => SerialBegin(port, baud, config, -1, -1);

    /// <param name="rx">Receive pin, or -1 for the board's default (pins other than the default need <see cref="BoardProfile.BusPinsAnywhere"/>).</param>
    public void SerialBegin(int port, uint baud, uint config, int rx, int tx)
    {
        if ((rx >= 0 || tx >= 0) && !Board.BusPinsAnywhere)
        {
            Log.ReportOnce($"uart-pins-{port}", SimSeverity.Warning, "fixed-pins", $"Serial port {port} on {Board.DisplayName} has fixed pins; the requested pins are ignored.", variant: "serial");
        }

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

    public void I2cBegin() => I2cBegin(0, -1, -1);

    /// <summary>
    /// Wire.begin(): the bus takes SDA and SCL (the defaults, or chosen pins where the board routes
    /// buses anywhere) and, as the AVR and ESP32 cores do, turns on their internal pull-ups.
    /// </summary>
    public void I2cBegin(int bus, int sda, int scl)
    {
        if (I2cChannelOf(bus) is { } channel)
        {
            (int sdaPin, int sclPin) = (Pick(sda, channel.Defaults.Sda), Pick(scl, channel.Defaults.Scl));
            if (!Board.BusPinsAnywhere && (sdaPin != channel.Defaults.Sda || sclPin != channel.Defaults.Scl))
            {
                Log.ReportOnce($"i2c-pins-{bus}", SimSeverity.Warning, "fixed-pins", $"I2C bus {bus} on {Board.DisplayName} has fixed pins; the requested pins are ignored.", variant: "i2c");
                (sdaPin, sclPin) = (channel.Defaults.Sda, channel.Defaults.Scl);
            }

            if (Find(sdaPin) is { } sdaState && Find(sclPin) is { } sclState && I2c is { } devices
                && !sdaState.Info.Has(PinCapabilities.InputOnly) && !sclState.Info.Has(PinCapabilities.InputOnly))
            {
                channel.Bus = (channel.Bus ?? devices).WithPins(sdaState.Reference, sclState.Reference);
                foreach (PinState state in new[] { sdaState, sclState })
                {
                    state.Mode = Simulation.PinMode.InputPullup;
                    state.PortBit = true;
                    state.PwmDuty = 0;
                    state.DacValue = -1;
                    Update(state);
                }

                channel.Enabled = true;
            }
            else
            {
                Log.ReportOnce($"i2c-pins-bad-{bus}", SimSeverity.Warning, "bus-pins", $"{I2cName(bus)}.begin(): SDA {Board.PinName(sdaPin)} and SCL {Board.PinName(sclPin)} are not usable I2C pins on {Board.DisplayName}; give two pins that can drive.", variant: "i2c");
            }
        }

        _engine.Finish(_timing.ShortCall);
    }

    public void I2cEnd(int bus = 0)
    {
        if (I2cChannelOf(bus) is { } channel)
        {
            channel.Enabled = false;
            channel.Bus?.Stop();
        }

        _engine.Finish(_timing.ShortCall);
    }

    public void I2cSetClock(uint hz) => I2cSetClock(0, hz);

    public void I2cSetClock(int bus, uint hz)
    {
        if (hz > 0 && I2cChannelOf(bus) is { } channel)
        {
            channel.BitTime = Math.Max(1, VirtualTime.Second / hz);
        }

        _engine.Finish(_timing.ShortCall);
    }

    public bool I2cStart(int address, bool read) => I2cStart(0, address, read);

    /// <summary>START (repeated when no STOP came since the last one) and the address byte; true on ACK.</summary>
    public bool I2cStart(int bus, int address, bool read)
    {
        I2cChannel? channel = RequireI2c(bus);
        bool ack = channel?.Bus is { } active && active.Start(address, read);
        _engine.Finish(10 * (channel?.BitTime ?? DefaultBitTime));
        return ack;
    }

    public bool I2cWrite(byte value) => I2cWrite(0, value);

    public bool I2cWrite(int bus, byte value)
    {
        I2cChannel? channel = RequireI2c(bus);
        bool ack = channel?.Bus is { } active && active.Write(value);
        _engine.Finish(9 * (channel?.BitTime ?? DefaultBitTime));
        return ack;
    }

    public byte I2cRead(bool ack) => I2cRead(0, ack);

    public byte I2cRead(int bus, bool ack)
    {
        I2cChannel? channel = RequireI2c(bus);
        byte value = channel?.Bus is { } active ? active.Read(ack) : (byte)0xFF;
        _engine.Finish(9 * (channel?.BitTime ?? DefaultBitTime));
        return value;
    }

    public void I2cStop() => I2cStop(0);

    public void I2cStop(int bus)
    {
        I2cChannel? channel = bus >= 0 && bus < _i2c.Length ? _i2c[bus] : null;
        channel?.Bus?.Stop();
        _engine.Finish(channel?.BitTime ?? DefaultBitTime);
    }

    public void SpiBegin() => SpiBegin(0, new SpiPins(-1, -1, -1, -1));

    /// <summary>
    /// SPI.begin(): SCK and MOSI become outputs. As the AVR core, a board with fixed SPI pins also makes
    /// SS an output (HIGH if it was an input); on an ESP32 SS is the sketch's business.
    /// </summary>
    public void SpiBegin(int bus, SpiPins requested)
    {
        ArgumentNullException.ThrowIfNull(requested);
        if (SpiChannelOf(bus) is { } channel)
        {
            SpiPins defaults = channel.Defaults;
            var pins = new SpiPins(Pick(requested.Mosi, defaults.Mosi), Pick(requested.Miso, defaults.Miso), Pick(requested.Sck, defaults.Sck), Pick(requested.Ss, defaults.Ss));
            if (!Board.BusPinsAnywhere && pins != defaults)
            {
                Log.ReportOnce($"spi-pins-{bus}", SimSeverity.Warning, "fixed-pins", $"SPI bus {bus} on {Board.DisplayName} has fixed pins; the requested pins are ignored.", variant: "spi");
                pins = defaults;
            }

            if (Find(pins.Sck) is { } sck && Find(pins.Mosi) is { } mosi && Find(pins.Miso) is { } miso && Spi is { } devices
                && !sck.Info.Has(PinCapabilities.InputOnly) && !mosi.Info.Has(PinCapabilities.InputOnly))
            {
                channel.Bus = (channel.Bus ?? devices).WithPins(sck.Reference, mosi.Reference, miso.Reference);
                if (!Board.BusPinsAnywhere && Find(pins.Ss) is { } ss && ss.Mode != Simulation.PinMode.Output)
                {
                    ss.PortBit = true;
                    ss.Mode = Simulation.PinMode.Output;
                    Update(ss);
                }

                foreach (PinState state in new[] { sck, mosi })
                {
                    state.Mode = Simulation.PinMode.Output;
                    state.PwmDuty = 0;
                    state.DacValue = -1;
                    Update(state);
                }

                channel.Enabled = true;
            }
            else
            {
                Log.ReportOnce($"spi-pins-bad-{bus}", SimSeverity.Warning, "bus-pins", $"SPI bus {bus}: SCK {Board.PinName(pins.Sck)}, MISO {Board.PinName(pins.Miso)} and MOSI {Board.PinName(pins.Mosi)} are not usable SPI pins on {Board.DisplayName}.", variant: "spi");
            }
        }

        _engine.Finish(_timing.ShortCall);
    }

    public void SpiEnd(int bus = 0)
    {
        if (SpiChannelOf(bus) is { } channel)
        {
            channel.Enabled = false;
        }

        _engine.Finish(_timing.ShortCall);
    }

    public void SpiSettings(uint clockHz, int bitOrder, int mode) => SpiSettings(0, clockHz, bitOrder, mode);

    /// <param name="bitOrder">0 = LSB first, 1 = MSB first.</param>
    /// <param name="mode">SPI mode 0-3.</param>
    public void SpiSettings(int bus, uint clockHz, int bitOrder, int mode)
    {
        if (SpiChannelOf(bus) is { } channel)
        {
            channel.Clock = HardwareSpiClock(clockHz);
            channel.LsbFirst = bitOrder == 0;
            channel.Mode = mode & 3;
        }

        _engine.Finish(_timing.ShortCall);
    }

    public byte SpiTransfer(byte value) => SpiTransfer(0, value);

    /// <summary>One full-duplex byte; 8 bit times at the SPI clock.</summary>
    public byte SpiTransfer(int bus, byte value)
    {
        byte answer = 0xFF;
        SpiChannel? channel = null;
        if (Spi is null || bus < 0 || bus >= _spi.Length)
        {
            Log.ReportOnce("spi-none", SimSeverity.Warning, "spi-unavailable", $"{Board.DisplayName} has no hardware SPI bus {bus}; transfers read 0xFF.");
        }
        else
        {
            channel = _spi[bus];
            if (!channel.Enabled)
            {
                Log.ReportOnce($"spi-begin-{bus}", SimSeverity.Warning, "spi-not-begun", $"SPI.transfer() is used before SPI.begin(); a real board would hang here, the simulation carries on.");
            }

            answer = (channel.Bus ?? Spi).Transfer(value, channel.LsbFirst, channel.Mode);
        }

        _engine.Finish(8 * VirtualTime.Second / (channel?.Clock ?? DefaultSpiClock));
        return answer;
    }

    private uint HardwareSpiClock(uint requested)
    {
        if (Board.SpiAnyDivider)
        {
            return Math.Clamp(requested, 1, (uint)Board.CpuHz);
        }

        foreach (int divider in new[] { 2, 4, 8, 16, 32, 64, 128 })
        {
            uint clock = (uint)(Board.CpuHz / divider);
            if (clock <= requested)
            {
                return clock;
            }
        }

        return (uint)(Board.CpuHz / 128);
    }
    public byte EepromRead(int address)
    {
        WaitForEeprom();
        byte value = Eeprom.Read(EepromAddress(address));
        _engine.Finish(_timing.ShortCall);
        return value;
    }

    /// <returns>The stored length (the buffer gets at most its size), or -1 when the key does not exist.</returns>
    public int KvGet(string nameSpace, string key, out int type, Span<byte> buffer)
    {
        int length = -1;
        type = 0;
        if (Kv.Get(nameSpace, key) is { } entry)
        {
            type = (int)entry.Type;
            length = entry.Value.Length;
            entry.Value.AsSpan(0, Math.Min(length, buffer.Length)).CopyTo(buffer);
        }

        _engine.Finish(_timing.KvRead);
        return length;
    }

    /// <returns>0 when stored; -1 for a name NVS would reject or an unknown type.</returns>
    public int KvPut(string nameSpace, string key, int type, ReadOnlySpan<byte> data)
    {
        int result = -1;
        if (!KeyValueStore.IsValidName(nameSpace) || !KeyValueStore.IsValidName(key))
        {
            Log.ReportOnce($"kv-name-{nameSpace}-{key}", SimSeverity.Warning, "kv-name", $"Preferences: namespace \"{nameSpace}\" or key \"{key}\" is empty or longer than {KeyValueStore.MaxNameLength} bytes; NVS refuses it and nothing is stored.");
        }
        else if (Enum.IsDefined((KvType)type))
        {
            Kv.Put(nameSpace, key, (KvType)type, data.ToArray());
            result = 0;
        }

        _engine.Finish(_timing.KvWrite);
        return result;
    }

    public bool KvRemove(string nameSpace, string key)
    {
        bool removed = Kv.Remove(nameSpace, key);
        _engine.Finish(_timing.KvWrite);
        return removed;
    }

    public void KvClear(string nameSpace)
    {
        Kv.Clear(nameSpace);
        _engine.Finish(_timing.KvWrite);
    }

    public void WifiScanStart()
    {
        RequireWifi()?.StartScan();
        _engine.Finish(_timing.ShortCall);
    }

    /// <returns>Networks found, -1 while scanning, -2 when no scan ran.</returns>
    public int WifiScanCount()
    {
        int count = Wifi is not { } radio ? -2 : radio.Scanning ? -1 : radio.ScanResults?.Count ?? -2;
        _engine.Finish(_timing.ShortCall);
        return count;
    }

    public WifiNetworkSettings? WifiScanEntry(int index)
    {
        WifiNetworkSettings? entry = Wifi?.ScanResults is { } results && index >= 0 && index < results.Count ? results[index] : null;
        _engine.Finish(_timing.ShortCall);
        return entry;
    }

    public void WifiBegin(string ssid, string password)
    {
        RequireWifi()?.Begin(ssid, password);
        _engine.Finish(_timing.ShortCall);
    }

    public int WifiStatusCode()
    {
        int status = (int)(Wifi?.Status ?? WifiStatus.Disconnected);
        _engine.Finish(_timing.ShortCall);
        return status;
    }

    public void WifiDisconnect()
    {
        Wifi?.Disconnect();
        _engine.Finish(_timing.ShortCall);
    }

    /// <summary>Starts the access point; an empty name stops it.</summary>
    public bool WifiSoftAp(string ssid, string password)
    {
        if (ssid.Length == 0)
        {
            Wifi?.StopAccessPoint();
        }

        bool up = ssid.Length > 0 && (RequireWifi()?.StartAccessPoint(ssid, password) ?? false);
        _engine.Finish(_timing.ShortCall);
        return up;
    }

    public uint WifiAddress(int which)
    {
        uint address = Wifi?.Address(which) ?? 0;
        _engine.Finish(_timing.ShortCall);
        return address;
    }

    public int WifiRssi()
    {
        int rssi = Wifi?.Rssi ?? 0;
        _engine.Finish(_timing.ShortCall);
        return rssi;
    }

    private WifiRadio? RequireWifi()
    {
        if (Wifi is null)
        {
            Log.ReportOnce("wifi-none", SimSeverity.Warning, "wifi-unavailable", $"{Board.DisplayName} has no Wi-Fi; the Wi-Fi calls do nothing.");
        }

        return Wifi;
    }

    /// <summary>
    /// A reboot (ESP.restart): every pin back to an input, PWM, tones, DAC, UARTs, buses and interrupts
    /// off, millis() from 0. External parts keep their state, as on a real board.
    /// </summary>
    public void Reset()
    {
        foreach (PinState state in _pins.OfType<PinState>())
        {
            state.ToneEnd?.Cancel();
            state.ToneEnd = null;
            state.ToneHz = 0;
            state.Mode = Simulation.PinMode.Input;
            state.PortBit = false;
            state.PwmDuty = 0;
            state.PwmBits = 8;
            state.DacValue = -1;
            Update(state);
        }

        foreach (UartPort uart in Uarts)
        {
            uart.End();
        }

        foreach (I2cChannel channel in _i2c)
        {
            channel.Bus?.Stop();
            channel.Enabled = false;
            channel.BitTime = DefaultBitTime;
        }

        foreach (SpiChannel channel in _spi)
        {
            channel.Enabled = false;
            channel.Clock = DefaultSpiClock;
        }

        Interrupts.Reset();
        Wifi?.Disconnect();
        Wifi?.StopAccessPoint();
        AnalogReference = AnalogReferenceDefault;
        BootTime = Now;
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

    private static int Pick(int requested, int fallback) => requested >= 0 ? requested : fallback;

    private static string I2cName(int bus) => bus == 0 ? "Wire" : "Wire" + bus.ToString(CultureInfo.InvariantCulture);

    private I2cChannel? I2cChannelOf(int bus)
    {
        if (bus >= 0 && bus < _i2c.Length && I2c is not null)
        {
            return _i2c[bus];
        }

        Log.ReportOnce($"i2c-none-{bus}", SimSeverity.Warning, "i2c-unavailable", $"{Board.DisplayName} has no hardware I2C bus {bus}; Wire transfers see no device.");
        return null;
    }

    private SpiChannel? SpiChannelOf(int bus)
    {
        if (bus >= 0 && bus < _spi.Length && Spi is not null)
        {
            return _spi[bus];
        }

        Log.ReportOnce("spi-none", SimSeverity.Warning, "spi-unavailable", $"{Board.DisplayName} has no hardware SPI bus {bus}; transfers read 0xFF.");
        return null;
    }

    private I2cChannel? RequireI2c(int bus)
    {
        if (I2cChannelOf(bus) is not { } channel)
        {
            return null;
        }

        if (!channel.Enabled)
        {
            Log.ReportOnce($"i2c-begin-{bus}", SimSeverity.Warning, "i2c-not-begun", $"Wire is used before Wire.begin(); a real board would hang here, the simulation carries on.");
            channel.Bus ??= bus == 0 ? I2c : null;
        }

        return channel;
    }

    /// <summary>Whether the pin can take the mode; warns once and falls back to INPUT otherwise.</summary>
    private bool Supports(PinState state, PinMode mode)
    {
        string name = state.Info.Name;
        string board = Board.DisplayName;
        (string Variant, FormattableString Message)? problem = mode switch
        {
            Simulation.PinMode.Output or Simulation.PinMode.OutputOpenDrain when state.Info.Has(PinCapabilities.InputOnly) =>
                ("output", $"pinMode({name}): {name} is input-only on {board}; it stays an input."),
            Simulation.PinMode.InputPullup when state.Info.Has(PinCapabilities.InputOnly) =>
                ("pullup", $"pinMode({name}): {name} has no internal pull-up on {board}; it stays an input."),
            Simulation.PinMode.InputPulldown when !state.Info.Has(PinCapabilities.PullDown) =>
                ("pulldown", $"pinMode({name}): {name} has no internal pull-down on {board}; it stays an input."),
            Simulation.PinMode.OutputOpenDrain when !Board.BusPinsAnywhere =>
                ("open-drain", $"pinMode({name}): {name} has no open-drain mode on {board}; it stays an input."),
            _ => null,
        };
        if (problem is { } p)
        {
            Log.ReportOnce($"pin-mode-{state.Info.Number}-{mode}", SimSeverity.Warning, "pin-mode", p.Message, variant: p.Variant);
        }

        return problem is null;
    }

    /// <summary>A digital pin that can drive; warns once for an input-only pin.</summary>
    private PinState? Output(int pin, string operation)
    {
        if (Digital(pin, operation) is not { } state)
        {
            return null;
        }

        if (state.Info.Has(PinCapabilities.InputOnly))
        {
            Log.ReportOnce($"input-only-{pin}", SimSeverity.Warning, "pin-mode", $"{operation}({state.Info.Name}): {state.Info.Name} is input-only on {Board.DisplayName}; the call is ignored.", variant: "input-only");
            return null;
        }

        return state;
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
            Simulation.PinMode.InputPullup => (new PinDrive(DriveKind.None, 0), NetDrive.Weak(vcc, Board.PullResistance)),
            Simulation.PinMode.InputPulldown => (new PinDrive(DriveKind.None, 0), NetDrive.Weak(0, Board.PullResistance)),
            Simulation.PinMode.Input => (new PinDrive(DriveKind.None, 0), NetDrive.None),
            Simulation.PinMode.OutputOpenDrain when state.PortBit => (new PinDrive(DriveKind.None, 0), NetDrive.None),
            _ when state.DacValue >= 0 => (new PinDrive(DriveKind.Analog, state.DacValue), NetDrive.Strong(vcc * state.DacValue / 255.0)),
            _ when state.ToneHz > 0 => (new PinDrive(DriveKind.Tone, state.ToneHz), NetDrive.Strong(vcc, 0.5, state.ToneHz)),
            _ when state.PwmDuty > 0 => (PwmDrive(state), NetDrive.Strong(vcc, state.PwmDuty / (double)((1 << state.PwmBits) - 1))),
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

    // The view and the log show PWM as a 0-255 duty whatever the resolution.
    private static PinDrive PwmDrive(PinState state) =>
        new(DriveKind.Pwm, (int)Math.Round(state.PwmDuty * 255.0 / ((1 << state.PwmBits) - 1)));

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

        public int PwmBits { get; set; } = 8;

        public uint PwmFrequency { get; set; }

        /// <summary>DAC output value (0-255), or -1 when the DAC is off.</summary>
        public int DacValue { get; set; } = -1;

        public int ToneHz { get; set; }

        public ScheduledEvent? ToneEnd { get; set; }

        public PinDrive Drive { get; set; } = new(DriveKind.None, 0);

        public bool? LastLevel { get; set; }
    }

    private sealed class I2cChannel(int number, I2cPins defaults, I2cBus? bus)
    {
        public int Number { get; } = number;

        public I2cPins Defaults { get; } = defaults;

        /// <summary>The bus on the pins of the last begin(); bus 0 starts on its defaults.</summary>
        public I2cBus? Bus { get; set; } = bus;

        public ulong BitTime { get; set; } = DefaultBitTime;

        public bool Enabled { get; set; }
    }

    private sealed class SpiChannel(int number, SpiPins defaults, SpiBus? bus)
    {
        public int Number { get; } = number;

        public SpiPins Defaults { get; } = defaults;

        public SpiBus? Bus { get; set; } = bus;

        public uint Clock { get; set; } = DefaultSpiClock;

        public bool LsbFirst { get; set; }

        public int Mode { get; set; }

        public bool Enabled { get; set; }
    }
}
