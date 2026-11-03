using ArduSim.Core.Circuits;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Timing;

namespace ArduSim.Core.Components.Parts;

/// <summary>
/// 38 kHz IR receiver module (VS1838B, TSOP382xx): OUT is active low — pulled up internally while it
/// sees no carrier, driven LOW during each burst (mark) of a frame from any IR remote in the scene.
/// A frame that arrives while another is still coming in is lost, as the two would garble.
/// </summary>
[SimComponent]
public sealed class IrReceiver : SimComponent
{
    private const double PullUp = 30_000;

    private readonly List<ScheduledEvent> _frame = [];
    private Driver _out = null!;
    private bool _mark;
    private ulong _busyUntil;

    public IrReceiver(string id, ComponentProperties properties)
        : base(id, Spec, properties)
    {
    }

    public static ComponentDefinition Spec { get; } = new(
        "ir-receiver",
        "Part_IrReceiver",
        "sensor",
        40,
        50,
        [
            new PinDefinition("OUT", PinKind.Digital, 10, 50),
            new PinDefinition("GND", PinKind.Ground, 20, 50),
            new PinDefinition("VCC", PinKind.Power, 30, 50),
        ],
        []);

    private double Supply => PartSupply.Across(Net("VCC"), Net("GND"));

    protected override void OnAttach()
    {
        _out = Circuit.AddDriver(Pin("OUT"), Id);
        Context.Infrared.Listen(Receive);
        WatchPins("VCC", "GND");
    }

    public override void OnCircuitReady()
    {
        SetState("receiving", 0);
        Refresh();
    }

    protected override void Refresh()
    {
        double supply = Supply;
        _out.Set(supply < 2.5 ? NetDrive.None : _mark ? NetDrive.Strong(0) : NetDrive.Weak(supply, PullUp));
    }

    private void Receive(IReadOnlyList<ulong> pulses)
    {
        SimulationClock clock = Context.Engine.Clock;
        if (Supply < 2.5)
        {
            Context.Log.ReportOnce($"ir-power-{Id}", SimSeverity.Warning, "sensor-power", $"{Id} (IR receiver) has no power: connect VCC to 3.3–5 V and GND to GND.", Id, "ir-receiver");
            return;
        }

        if (clock.Now < _busyUntil)
        {
            return;
        }

        _frame.RemoveAll(e => e.Time <= clock.Now);
        SetState("receiving", 1);
        ulong at = 0;
        for (int i = 0; i < pulses.Count; i++)
        {
            bool mark = i % 2 == 0;
            _frame.Add(clock.Schedule(at, () => SetMark(mark)));
            at += pulses[i];
        }

        _frame.Add(clock.Schedule(at, () =>
        {
            SetMark(false);
            SetState("receiving", 0);
        }));
        _busyUntil = clock.Now + at;
    }

    private void SetMark(bool mark)
    {
        _mark = mark;
        Refresh();
    }
}

/// <summary>
/// The 21-key IR remote of the common Arduino kits ("Car MP3"), sending NEC frames with address
/// 0x00 to every IR receiver in the scene. Holding a key sends NEC repeat frames every 108 ms.
/// </summary>
[SimComponent]
public sealed class IrRemote : SimComponent
{
    /// <summary>NEC command of each key, row by row: CH- CH CH+ / |◀◀ ▶▶| ▶‖ / − + EQ / 0 100+ 200+ / 1 2 3 / 4 5 6 / 7 8 9.</summary>
    public static readonly IReadOnlyList<byte> Commands =
    [
        0x45, 0x46, 0x47,
        0x44, 0x40, 0x43,
        0x07, 0x15, 0x09,
        0x16, 0x19, 0x0D,
        0x0C, 0x18, 0x5E,
        0x08, 0x1C, 0x5A,
        0x42, 0x52, 0x4A,
    ];

    private const ulong Unit = 562_500; // 562.5 µs
    private const ulong RepeatPeriod = 108 * VirtualTime.Millisecond;

    private int _held = -1;
    private ScheduledEvent? _repeat;

    public IrRemote(string id, ComponentProperties properties)
        : base(id, Spec, properties)
    {
    }

    public static ComponentDefinition Spec { get; } = new(
        "ir-remote",
        "Part_IrRemote",
        "input",
        80,
        170,
        [],
        []);

    /// <summary>The NEC frame for <paramref name="command"/>: 9 ms mark, 4.5 ms space, 32 bits LSB first (address, its inverse, command, its inverse), a stop mark.</summary>
    public static IReadOnlyList<ulong> NecFrame(byte address, byte command)
    {
        var pulses = new List<ulong> { 16 * Unit, 8 * Unit };
        uint data = address | ((uint)(byte)~address << 8) | ((uint)command << 16) | ((uint)(byte)~command << 24);
        for (int bit = 0; bit < 32; bit++)
        {
            pulses.Add(Unit);
            pulses.Add(((data >> bit) & 1) != 0 ? 3 * Unit : Unit);
        }

        pulses.Add(Unit);
        return pulses;
    }

    /// <summary>The NEC repeat frame: 9 ms mark, 2.25 ms space, a stop mark.</summary>
    public static IReadOnlyList<ulong> NecRepeat { get; } = [16 * Unit, 4 * Unit, Unit];

    protected override void OnAttach()
    {
    }

    public override void OnCircuitReady() => SetState("key", -1);

    /// <summary><c>b0</c>…<c>b20</c> (row by row, see <see cref="Commands"/>): 1 pressed, 0 released.</summary>
    public override void ApplyInput(string name, double value)
    {
        if (name.Length < 2 || name[0] != 'b' || !int.TryParse(name.AsSpan(1), System.Globalization.CultureInfo.InvariantCulture, out int key) || key < 0 || key >= Commands.Count)
        {
            base.ApplyInput(name, value);
            return;
        }

        if (value != 0)
        {
            Press(key);
        }
        else if (key == _held)
        {
            _repeat?.Cancel();
            _repeat = null;
            _held = -1;
            SetState("key", -1);
        }
    }

    private void Press(int key)
    {
        _repeat?.Cancel();
        _held = key;
        SetState("key", key);
        Context.Infrared.Send(NecFrame(0x00, Commands[key]));
        _repeat = Context.Engine.Clock.Schedule(RepeatPeriod, Repeat);
    }

    private void Repeat()
    {
        Context.Infrared.Send(NecRepeat);
        _repeat = Context.Engine.Clock.Schedule(RepeatPeriod, Repeat);
    }
}
