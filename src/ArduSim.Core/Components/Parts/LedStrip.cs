using System.Globalization;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Displays;

namespace ArduSim.Core.Components.Parts;

/// <summary>
/// WS2812B addressable LEDs as a strip, a ring or an 8x8 matrix (ADR-012). A frame from the board
/// (ABI 2.3) arrives at DIN as G, R, B bytes per LED; each LED takes its three bytes, those beyond
/// the last LED go on from DOUT to the next strip, and LEDs the frame does not reach keep their
/// color, as on the real chips. Matrix panels run row by row, optionally serpentine (every other
/// row backwards). The LEDs need 3.5-5 V.
/// </summary>
[SimComponent]
public sealed class LedStrip : SimComponent
{
    private static readonly Dictionary<string, ComponentDefinition> Layouts = new(StringComparer.Ordinal);

    // Declared before Spec: static initializers run in textual order.
    private static readonly IReadOnlyList<PropertyDefinition> PropertyList =
    [
        new PropertyDefinition("layout", PropertyKind.Choice, "strip", ["strip", "ring", "matrix"]),
        new PropertyDefinition("count", PropertyKind.Number, "8", Minimum: 1, Maximum: 144),
        new PropertyDefinition("serpentine", PropertyKind.Choice, "no", ["no", "yes"]) { ShownWhen = ("layout", "matrix") },
    ];

    private static readonly ComponentDefinition Strip8 = Build("strip", 8);

    private byte[] _rgb = [];
    private long _version;
    private DisplayFrame? _frame;

    public LedStrip(string id, ComponentProperties properties)
        : base(id, Pick(properties), properties)
    {
    }

    public static ComponentDefinition Spec => Strip8;

    private string Shape => Properties.Choice("layout");

    /// <summary>LEDs on the part: the count for a strip or ring, 64 for the matrix.</summary>
    public int Count => Shape == "matrix" ? 64 : (int)Properties.Number("count");

    public override DisplayFrame Display => _frame is { } frame && frame.Version == _version ? frame : _frame = Render();

    private static ComponentDefinition Pick(ComponentProperties properties)
    {
        string shape = properties.Choice("layout");
        int count = shape == "matrix" ? 64 : (int)properties.Number("count");
        return Build(shape, count);
    }

    private static ComponentDefinition Build(string shape, int count)
    {
        string key = shape + "/" + count.ToString(CultureInfo.InvariantCulture);
        lock (Layouts)
        {
            if (Layouts.TryGetValue(key, out ComponentDefinition? known))
            {
                return known;
            }

            (double width, double height, PinDefinition[] pins) = shape switch
            {
                "ring" => Ring(count),
                "matrix" => (200.0, 180.0, Side(200, 80)),
                _ => ((count * 20.0) + 40, 40.0, Side((count * 20.0) + 40, 10)),
            };
            var definition = new ComponentDefinition("ws2812", "Part_Ws2812", "output", width, height, pins, PropertyList)
            {
                Layout = Pick,
                Presets =
                [
                    new ComponentPreset("strip", "Part_Ws2812Strip", new Dictionary<string, string> { ["layout"] = "strip", ["count"] = "8" }),
                    new ComponentPreset("ring", "Part_Ws2812Ring", new Dictionary<string, string> { ["layout"] = "ring", ["count"] = "16" }),
                    new ComponentPreset("matrix", "Part_Ws2812Matrix", new Dictionary<string, string> { ["layout"] = "matrix" }),
                ],
            };
            Layouts[key] = definition;
            return definition;
        }
    }

    // A ring's pads sit below it: GND, VCC, DIN, DOUT.
    private static (double, double, PinDefinition[]) Ring(int count)
    {
        double diameter = Math.Max(80, Math.Round(((count * 9) + 30) / 20.0) * 20);
        double center = diameter / 2;
        return (diameter, diameter + 10,
        [
            new PinDefinition("GND", PinKind.Ground, center - 20, diameter + 10),
            new PinDefinition("VCC", PinKind.Power, center - 10, diameter + 10),
            new PinDefinition("DIN", PinKind.Digital, center, diameter + 10),
            new PinDefinition("DOUT", PinKind.Digital, center + 10, diameter + 10),
        ]);
    }

    // A strip or matrix: VCC, DIN and GND at the left end, DOUT at the right.
    private static PinDefinition[] Side(double width, double top) =>
    [
        new PinDefinition("VCC", PinKind.Power, 0, top),
        new PinDefinition("DIN", PinKind.Digital, 0, top + 10),
        new PinDefinition("GND", PinKind.Ground, 0, top + 20),
        new PinDefinition("DOUT", PinKind.Digital, width, top + 10),
    ];

    protected override void OnAttach()
    {
        _rgb = new byte[Count * 3];
        Context.LedStrips.Listen(Pin("DIN"), Receive);
        WatchPins("VCC", "GND");
    }

    private bool Powered => PartSupply.Across(Net("VCC"), Net("GND")) >= 3.5;

    // Without power the LEDs go dark and forget their colors.
    protected override void Refresh()
    {
        if (!Powered && _rgb.Any(b => b != 0))
        {
            Array.Clear(_rgb);
            Changed();
        }
    }

    private void Receive(ReadOnlySpan<byte> data)
    {
        if (!Powered)
        {
            Context.Log.ReportOnce($"ws2812-power-{Id}", SimSeverity.Warning, "sensor-power", $"{Id} (WS2812) has no power: connect VCC to 5 V and GND to GND.", Id, "ws2812");
            return;
        }

        int leds = Math.Min(Count, data.Length / 3);
        bool changed = false;
        for (int i = 0; i < leds; i++)
        {
            (byte g, byte r, byte b) = (data[3 * i], data[(3 * i) + 1], data[(3 * i) + 2]);
            changed |= _rgb[3 * i] != r || _rgb[(3 * i) + 1] != g || _rgb[(3 * i) + 2] != b;
            (_rgb[3 * i], _rgb[(3 * i) + 1], _rgb[(3 * i) + 2]) = (r, g, b);
        }

        if (changed)
        {
            Changed();
        }

        if (data.Length > Count * 3)
        {
            Context.LedStrips.Send(Pin("DOUT"), data[(Count * 3)..]);
        }
    }

    private void Changed()
    {
        _version++;
        NotifyDisplayChanged();
    }

    // Strips and rings in data order; the matrix row by row as it is seen.
    private DisplayFrame Render()
    {
        bool matrix = Shape == "matrix";
        byte[] dots = matrix ? new byte[64 * 3] : _rgb.ToArray();
        if (matrix)
        {
            bool serpentine = Properties.Choice("serpentine") == "yes";
            for (int i = 0; i < 64; i++)
            {
                int row = i / 8;
                int column = serpentine && row % 2 == 1 ? 7 - (i % 8) : i % 8;
                Array.Copy(_rgb, 3 * i, dots, 3 * ((row * 8) + column), 3);
            }
        }

        DisplayStatus status = Powered ? DisplayStatus.Powered | DisplayStatus.On : DisplayStatus.None;
        return new DisplayFrame(DisplayKind.RgbLeds, matrix ? 8 : Count, matrix ? 8 : 1, status, 1, dots, [], _version);
    }
}
