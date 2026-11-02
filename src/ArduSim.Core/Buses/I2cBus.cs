using System.Globalization;
using ArduSim.Core.Circuits;
using ArduSim.Core.Diagnostics;

namespace ArduSim.Core.Buses;

/// <summary>An I2C slave (ADR-006). The bus calls it only while it is the addressed device.</summary>
public interface II2cDevice
{
    /// <summary>Component id, for warnings.</summary>
    string Id { get; }

    /// <summary>7-bit address.</summary>
    int Address { get; }

    PinRef Sda { get; }

    PinRef Scl { get; }

    /// <summary>A START (or repeated START) with this device's address; false = NACK (e.g. unpowered).</summary>
    bool OnStart(bool read);

    /// <summary>A byte from the master; false = NACK.</summary>
    bool OnWrite(byte value);

    /// <summary>A byte for the master; <paramref name="ack"/> is the master's acknowledge after it.</summary>
    byte OnRead(bool ack);

    void OnStop();
}

/// <summary>
/// The board's I2C master bus: a pair of nets (SDA, SCL). A transfer goes to the registered
/// device with the addressed 7-bit address whose SDA and SCL pins sit on those nets; when there
/// is none the address is NACKed, as on real hardware, and a warning names the address once.
/// </summary>
public sealed class I2cBus
{
    // Above this the only pull-ups are the MCU's internal ones (about 35 kΩ on an ATmega328P).
    private const double ExternalPullUpLimit = 20_000;

    private readonly Circuit _circuit;
    private readonly SimulationLog _log;
    private readonly List<II2cDevice> _devices = [];
    private II2cDevice? _active;
    private bool _reading;
    private bool _pullUpsChecked;

    public I2cBus(Circuit circuit, SimulationLog log, PinRef sda, PinRef scl)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(log);
        _circuit = circuit;
        _log = log;
        Sda = sda;
        Scl = scl;
    }

    public PinRef Sda { get; }

    public PinRef Scl { get; }

    public IReadOnlyList<II2cDevice> Devices => _devices;

    public void Register(II2cDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _devices.Add(device);
    }

    /// <summary>START (or repeated START) and the address byte; true when a device ACKs.</summary>
    public bool Start(int address, bool read)
    {
        CheckPullUps();
        address &= 0x7F;
        List<II2cDevice> matches = [.. _devices.Where(d => d.Address == address && OnBus(d))];
        if (matches.Count > 1)
        {
            _log.ReportOnce(
                $"i2c-conflict-{address}",
                SimSeverity.Warning,
                "i2c-conflict",
                $"Several devices answer at I2C address {Hex(address)} ({string.Join(", ", matches.Select(d => d.Id))}); only {matches[0].Id} is used.",
                matches[1].Id);
        }

        II2cDevice? device = matches.FirstOrDefault();
        if (_active is not null && _active != device)
        {
            // A repeated START to another address ends the previous device's transfer.
            _active.OnStop();
        }

        _active = null;
        if (device is null || !device.OnStart(read))
        {
            ReportNack(address, device);
            return false;
        }

        _active = device;
        _reading = read;
        return true;
    }

    public bool Write(byte value) => _active is { } device && !_reading && device.OnWrite(value);

    /// <summary>A byte from the addressed device; 0xFF (the idle bus) when none is reading.</summary>
    public byte Read(bool ack) => _active is { } device && _reading ? device.OnRead(ack) : (byte)0xFF;

    public void Stop()
    {
        _active?.OnStop();
        _active = null;
    }

    private static string Hex(int address) => "0x" + address.ToString("X2", CultureInfo.InvariantCulture);

    private bool OnBus(II2cDevice device) =>
        _circuit.NetOf(device.Sda) == _circuit.NetOf(Sda) && _circuit.NetOf(device.Scl) == _circuit.NetOf(Scl);

    private void ReportNack(int address, II2cDevice? responding)
    {
        if (responding is not null)
        {
            return; // the device itself said no (e.g. unpowered) and reports why
        }

        II2cDevice? swapped = _devices.FirstOrDefault(d => d.Address == address &&
            _circuit.NetOf(d.Sda) == _circuit.NetOf(Scl) && _circuit.NetOf(d.Scl) == _circuit.NetOf(Sda));
        II2cDevice? elsewhere = _devices.FirstOrDefault(d => d.Address == address);
        if (swapped is not null)
        {
            _log.ReportOnce($"i2c-nack-{address}", SimSeverity.Warning, "i2c-nack", $"No device answers at I2C address {Hex(address)}: {swapped.Id} has that address, but its SDA and SCL are swapped.", swapped.Id, "swapped");
        }
        else if (elsewhere is not null)
        {
            _log.ReportOnce($"i2c-nack-{address}", SimSeverity.Warning, "i2c-nack", $"No device answers at I2C address {Hex(address)}: {elsewhere.Id} has that address, but its SDA and SCL are not on the board's SDA ({Sda.Pin}) and SCL ({Scl.Pin}).", elsewhere.Id, "unwired");
        }
        else if (_devices.Where(OnBus).Select(d => $"{d.Id} ({Hex(d.Address)})").ToList() is { Count: > 0 } present)
        {
            _log.ReportOnce($"i2c-nack-{address}", SimSeverity.Warning, "i2c-nack", $"No device answers at I2C address {Hex(address)}; on the bus: {string.Join(", ", present)}.");
        }
        else
        {
            _log.ReportOnce($"i2c-nack-{address}", SimSeverity.Warning, "i2c-nack", $"No device answers at I2C address {Hex(address)}; nothing is connected to the bus.", variant: "empty");
        }
    }

    private void CheckPullUps()
    {
        if (_pullUpsChecked)
        {
            return;
        }

        _pullUpsChecked = true;
        NetState sda = _circuit.StateOf(Sda);
        NetState scl = _circuit.StateOf(Scl);
        if (sda.SourceResistance > ExternalPullUpLimit || scl.SourceResistance > ExternalPullUpLimit ||
            sda.Condition == NetCondition.Floating || scl.Condition == NetCondition.Floating)
        {
            _log.ReportOnce("i2c-pullups", SimSeverity.Info, "i2c-pullups", $"The I2C bus has no external pull-up resistors on SDA ({Sda.Pin}) and SCL ({Scl.Pin}); the simulation works, but real hardware usually needs about 4.7 kΩ to 5V.");
        }
    }
}
