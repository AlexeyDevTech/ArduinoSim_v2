namespace ArduSim.Core.Buses;

/// <summary>
/// The scene's infrared light (ADR-011): there are no wires, and every IR receiver sees every frame
/// a remote sends. A frame is the demodulated envelope: mark and space durations, starting now.
/// </summary>
public sealed class InfraredLink
{
    private readonly List<Action<IReadOnlyList<ulong>>> _receivers = [];

    public void Listen(Action<IReadOnlyList<ulong>> receiver) => _receivers.Add(receiver);

    /// <param name="pulses">Nanoseconds of mark, space, mark, …; the first is a mark.</param>
    public void Send(IReadOnlyList<ulong> pulses)
    {
        foreach (Action<IReadOnlyList<ulong>> receiver in _receivers)
        {
            receiver(pulses);
        }
    }
}