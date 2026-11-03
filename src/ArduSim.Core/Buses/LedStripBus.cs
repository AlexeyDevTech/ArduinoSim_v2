using ArduSim.Core.Circuits;

namespace ArduSim.Core.Buses;

/// <summary>A WS2812 frame as it arrives at a strip's DIN: the bytes in wire order.</summary>
public delegate void LedFrameHandler(ReadOnlySpan<byte> data);

/// <summary>
/// WS2812-style frames (ABI 2.3, ADR-012): a frame sent on a net reaches every strip whose DIN pin
/// is on it; a strip keeps the bytes for its own LEDs and sends the rest on from its DOUT.
/// </summary>
public sealed class LedStripBus(Circuit circuit)
{
    private readonly List<(PinRef Din, LedFrameHandler Receive)> _strips = [];

    public void Listen(PinRef din, LedFrameHandler receive) => _strips.Add((din, receive));

    /// <summary>Sends a frame out of <paramref name="from"/>; returns whether a strip took it.</summary>
    public bool Send(PinRef from, ReadOnlySpan<byte> data)
    {
        Net net = circuit.NetOf(from);
        bool taken = false;
        foreach ((PinRef din, LedFrameHandler receive) in _strips)
        {
            if (circuit.NetOf(din) == net)
            {
                receive(data);
                taken = true;
            }
        }

        return taken;
    }
}