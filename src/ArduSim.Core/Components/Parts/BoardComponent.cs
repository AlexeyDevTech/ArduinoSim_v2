using ArduSim.Core.Boards;
using ArduSim.Core.Circuits;

namespace ArduSim.Core.Components.Parts;

/// <summary>
/// The board as a part: its supply and ground pins drive their rails, pins of the same rail are
/// connected on the board, RST is pulled up, and the on-board "L" LED follows its pin (it is
/// buffered on the Nano, so even a pull-up lights it). The MCU pins themselves are driven by <c>Mcu</c>.
/// </summary>
public sealed class BoardComponent : SimComponent
{
    private const double ResetPullUp = 10_000;

    private readonly BoardProfile _board;

    public BoardComponent(string id, BoardProfile board, ComponentProperties properties)
        : base(id, DefinitionFor(board), properties)
    {
        _board = board;
    }

    public BoardProfile Board => _board;

    public static ComponentDefinition DefinitionFor(BoardProfile board)
    {
        ArgumentNullException.ThrowIfNull(board);
        IEnumerable<PinDefinition> io = board.Pins.Select(p => new PinDefinition(p.Name, p.Has(PinCapabilities.Analog) ? PinKind.Analog : PinKind.Digital, p.X, p.Y));
        IEnumerable<PinDefinition> power = board.PowerPins.Select(p => new PinDefinition(
            p.Name,
            p.Kind switch { PowerPinKind.Supply => PinKind.Power, PowerPinKind.Ground => PinKind.Ground, _ => PinKind.Passive },
            p.X,
            p.Y));
        return new ComponentDefinition(board.Id, "Board_" + board.Id, "board", board.Width, board.Height, [.. io, .. power], []);
    }

    protected override void OnAttach()
    {
        foreach (IGrouping<string, PowerPin> rail in _board.PowerPins.GroupBy(p => p.Rail))
        {
            PowerPin first = rail.First();
            foreach (PowerPin other in rail.Skip(1))
            {
                Circuit.Connect(Pin(first.Name), Pin(other.Name));
            }

            NetDrive drive = first.Kind switch
            {
                PowerPinKind.Supply => NetDrive.Strong(first.Voltage),
                PowerPinKind.Ground => NetDrive.Strong(0),
                PowerPinKind.Reset => NetDrive.Weak(first.Voltage, ResetPullUp),
                _ => NetDrive.None,
            };
            if (drive != NetDrive.None)
            {
                Circuit.AddDriver(Pin(first.Name), Id).Set(drive);
            }
        }

        if (BuiltinLedPin is { } led)
        {
            WatchPins(led);
        }
    }

    protected override void Refresh()
    {
        SetState("power", 1);
        if (BuiltinLedPin is { } led)
        {
            NetState state = Net(led);
            SetState("L", state.HasVoltage && state.Voltage >= 2.0 ? Math.Min(state.Duty, 1) : 0);
        }
    }

    private string? BuiltinLedPin => _board.BuiltinLed is int number ? _board.PinName(number) : null;
}
