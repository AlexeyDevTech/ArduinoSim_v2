using ArduSim.Core.Circuits;
using ArduSim.Core.Diagnostics;

namespace ArduSim.Core.Buses;

/// <summary>
/// An SPI slave (ADR-006). The hardware path hands it whole bytes while its CS is LOW; bit-banged
/// SPI (shiftOut, software-SPI constructors) reaches it as edges on its own SCK pin, which the
/// device watches itself. Both paths end in the same per-byte handler inside the device.
/// </summary>
public interface ISpiDevice
{
    /// <summary>Component id, for warnings.</summary>
    string Id { get; }

    PinRef Sck { get; }

    PinRef Mosi { get; }

    PinRef ChipSelect { get; }

    /// <summary>One byte from the master, MSB first as the device sees it; returns what it drives on MISO.</summary>
    byte OnTransfer(byte mosi);
}

/// <summary>
/// The board's hardware SPI: a byte goes full-duplex to the registered device whose SCK and MOSI
/// pins sit on the board's SCK/MOSI nets and whose CS net is LOW. Nobody selected reads 0xFF; two
/// selected devices are a conflict, reported once.
/// </summary>
public sealed class SpiBus
{
    private const double LowThreshold = 1.5;

    private readonly Circuit _circuit;
    private readonly SimulationLog _log;
    private readonly List<ISpiDevice> _devices = [];

    public SpiBus(Circuit circuit, SimulationLog log, PinRef sck, PinRef mosi, PinRef miso)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(log);
        _circuit = circuit;
        _log = log;
        Sck = sck;
        Mosi = mosi;
        Miso = miso;
    }

    public PinRef Sck { get; }

    public PinRef Mosi { get; }

    public PinRef Miso { get; }

    public IReadOnlyList<ISpiDevice> Devices => _devices;

    public void Register(ISpiDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _devices.Add(device);
    }

    /// <param name="lsbFirst">The master shifts LSB first; devices see the byte with its bits in wire order.</param>
    /// <param name="mode">SPI mode 0-3.</param>
    public byte Transfer(byte value, bool lsbFirst, int mode)
    {
        List<ISpiDevice> selected = [.. _devices.Where(d => OnBus(d) && Selected(d))];
        if (selected.Count == 0)
        {
            return 0xFF;
        }

        if (selected.Count > 1)
        {
            _log.ReportOnce(
                "spi-conflict",
                SimSeverity.Warning,
                "spi-conflict",
                $"Several SPI devices have CS LOW at once ({string.Join(", ", selected.Select(d => d.Id))}); only {selected[0].Id} is used, real devices would fight over MISO.",
                selected[1].Id);
        }

        if (mode is 1 or 2)
        {
            _log.ReportOnce(
                $"spi-mode-{selected[0].Id}",
                SimSeverity.Warning,
                "spi-mode",
                $"SPI mode {mode} is used with {selected[0].Id}, which samples MOSI on the rising edge (modes 0 and 3); real hardware would read shifted bits.",
                selected[0].Id);
        }

        byte wire = lsbFirst ? Reverse(value) : value;
        byte answer = selected[0].OnTransfer(wire);
        return lsbFirst ? Reverse(answer) : answer;
    }

    public static byte Reverse(byte value)
    {
        int result = 0;
        for (int bit = 0; bit < 8; bit++)
        {
            result |= ((value >> bit) & 1) << (7 - bit);
        }

        return (byte)result;
    }

    private bool OnBus(ISpiDevice device) =>
        _circuit.NetOf(device.Sck) == _circuit.NetOf(Sck) && _circuit.NetOf(device.Mosi) == _circuit.NetOf(Mosi);

    private bool Selected(ISpiDevice device) =>
        _circuit.StateOf(device.ChipSelect) is { HasVoltage: true } cs && cs.Voltage < LowThreshold;
}
