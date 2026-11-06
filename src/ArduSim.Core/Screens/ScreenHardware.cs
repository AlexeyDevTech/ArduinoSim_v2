using System.Globalization;
using System.Text.Json;
using ArduSim.Core.Boards;
using ArduSim.Core.Projects;

namespace ArduSim.Core.Screens;

public enum ScreenInputKind
{
    /// <summary>A button between a pin and ground (with the pin's pull-up) or a supply (with a pull-down of its own).</summary>
    Button,

    /// <summary>A 4x4 matrix keypad.</summary>
    Keypad,

    /// <summary>A rotary encoder: CLK, DT and its button.</summary>
    Encoder,
}

/// <summary>An input part on the schematic and where it is wired.</summary>
/// <param name="Pins">Button: its pin. Keypad: rows R1-R4, then columns C1-C4. Encoder: CLK, DT, SW (-1 if not wired).</param>
/// <param name="ActiveLow">A button that pulls its pin low.</param>
public sealed record ScreenInput(string Part, ScreenInputKind Kind, IReadOnlyList<int> Pins, bool ActiveLow = true);

/// <summary>How the display and the inputs are wired, as the generated code needs to know.</summary>
/// <param name="Address">An I2C display's address; 0 on another bus.</param>
/// <param name="LcdPins">A parallel LCD's RS, E, D4, D5, D6, D7, as board pin numbers.</param>
/// <param name="OledSpiPins">An SPI OLED's DC, RES, CS, as board pin numbers.</param>
/// <param name="Controller">An OLED's controller: <c>ssd1306</c>, <c>sh1106</c>, <c>ssd1309</c>.</param>
public sealed record ScreenWiring(int Address, IReadOnlyList<int> LcdPins, IReadOnlyList<int> OledSpiPins, string Controller, IReadOnlyList<ScreenInput> Inputs);

/// <summary>Reads the display and the inputs off the schematic: it knows the wiring (ADR-019).</summary>
public static class ScreenHardware
{
    private static readonly string[] KeypadPins = ["R1", "R2", "R3", "R4", "C1", "C2", "C3", "C4"];

    /// <summary>The first character LCD or OLED in the scene, with the library that suits it.</summary>
    public static ScreenDisplay? DisplayOf(SceneDocument scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        foreach (ComponentDocument part in scene.Components)
        {
            if (part.Type == "lcd")
            {
                (int width, int height) = Size(Choice(part, "size", "16x2"));
                DisplayLibrary library = Choice(part, "interface", "i2c") == "i2c" ? DisplayLibrary.LiquidCrystalI2c : DisplayLibrary.LiquidCrystal;
                return new ScreenDisplay(DisplayFamily.CharLcd, width, height, library, "uiDisplay", part.Id);
            }

            if (part.Type == "oled" && Choice(part, "controller", "ssd1306") != "ssd1327")
            {
                string controller = Choice(part, "controller", "ssd1306");
                (int width, int height) = controller == "ssd1306" ? Size(Choice(part, "size", "128x64")) : (128, 64);
                DisplayLibrary library = controller == "sh1106" ? DisplayLibrary.AdafruitSh110x : DisplayLibrary.AdafruitSsd1306;
                return new ScreenDisplay(DisplayFamily.Oled, width, height, library, "uiDisplay", part.Id);
            }
        }

        return null;
    }

    public static ScreenWiring WiringOf(SceneDocument scene, BoardProfile board, string displayPart)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(board);
        var nets = new Nets(scene);
        ComponentDocument? boardPart = scene.Components.FirstOrDefault(c => c.Type == board.Id);
        int PinOf(string part, string pin) => boardPart is null ? -1 : nets.BoardPin(part, pin, boardPart.Id, board);

        ComponentDocument? display = scene.Components.FirstOrDefault(c => c.Id == displayPart);
        int address = 0;
        IReadOnlyList<int> lcdPins = [];
        IReadOnlyList<int> spiPins = [];
        string controller = "ssd1306";
        if (display is not null)
        {
            string bus = Choice(display, "interface", "i2c");
            if (bus == "i2c")
            {
                address = ParseAddress(Choice(display, "address", display.Type == "lcd" ? "0x27" : "0x3C"));
            }
            else if (display.Type == "lcd")
            {
                lcdPins = [PinOf(display.Id, "RS"), PinOf(display.Id, "E"), PinOf(display.Id, "D4"), PinOf(display.Id, "D5"), PinOf(display.Id, "D6"), PinOf(display.Id, "D7")];
            }
            else
            {
                spiPins = [PinOf(display.Id, "DC"), PinOf(display.Id, "RES"), PinOf(display.Id, "CS")];
            }

            if (display.Type == "oled")
            {
                controller = Choice(display, "controller", "ssd1306");
            }
        }

        var inputs = new List<ScreenInput>();
        foreach (ComponentDocument part in scene.Components)
        {
            switch (part.Type)
            {
                case "pushbutton":
                    if (boardPart is not null && nets.Button(part.Id, boardPart.Id, board) is { } button)
                    {
                        inputs.Add(new ScreenInput(part.Id, ScreenInputKind.Button, [button.Pin], button.ActiveLow));
                    }

                    break;
                case "keypad":
                    int[] matrix = [.. KeypadPins.Select(p => PinOf(part.Id, p))];
                    if (matrix.All(p => p >= 0))
                    {
                        inputs.Add(new ScreenInput(part.Id, ScreenInputKind.Keypad, matrix));
                    }

                    break;
                case "rotary-encoder":
                    int clock = PinOf(part.Id, "CLK");
                    int data = PinOf(part.Id, "DT");
                    if (clock >= 0 && data >= 0)
                    {
                        inputs.Add(new ScreenInput(part.Id, ScreenInputKind.Encoder, [clock, data, PinOf(part.Id, "SW")]));
                    }

                    break;
                default:
                    break;
            }
        }

        return new ScreenWiring(address, lcdPins, spiPins, controller, inputs);
    }

    private static string Choice(ComponentDocument part, string name, string fallback) =>
        part.Properties is { } properties && properties.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;

    private static (int, int) Size(string text)
    {
        string[] parts = text.Split('x');
        return (int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    private static int ParseAddress(string text) =>
        int.TryParse(text.AsSpan(text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? 2 : 0), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value) ? value : 0;

    // The pins each wire joins: enough to ask which board pin a part's pin is on.
    private sealed class Nets
    {
        private readonly Dictionary<(string Part, string Pin), (string, string)> _parent = [];

        public Nets(SceneDocument scene)
        {
            foreach (WireDocument wire in scene.Wires)
            {
                _parent[Find((wire.From.Component, wire.From.Pin))] = Find((wire.To.Component, wire.To.Pin));
            }
        }

        private (string, string) Find((string, string) pin)
        {
            while (_parent.TryGetValue(pin, out (string, string) next) && next != pin)
            {
                pin = next;
            }

            return pin;
        }

        private IEnumerable<string> BoardPinsOn(string part, string pin, string boardId)
        {
            (string, string) root = Find((part, pin));
            if (part == boardId)
            {
                yield return pin;
            }

            foreach ((string Part, string Pin) candidate in _parent.Keys)
            {
                if (candidate.Part == boardId && Find(candidate) == root)
                {
                    yield return candidate.Pin;
                }
            }

            if (root.Item1 == boardId)
            {
                yield return root.Item2;
            }
        }

        public int BoardPin(string part, string pin, string boardId, BoardProfile board)
        {
            foreach (string name in BoardPinsOn(part, pin, boardId))
            {
                if (board.FindPin(name) is { } found)
                {
                    return found.Number;
                }
            }

            return -1;
        }

        public (int Pin, bool ActiveLow)? Button(string part, string boardId, BoardProfile board)
        {
            int one = BoardPin(part, "1", boardId, board);
            int two = BoardPin(part, "2", boardId, board);
            if (one >= 0 == two >= 0)
            {
                return null;
            }

            string other = one >= 0 ? "2" : "1";
            bool toSupply = BoardPinsOn(part, other, boardId).Any(name => board.PowerPins.Any(p => p.Name == name && p.Kind == PowerPinKind.Supply));
            return (Math.Max(one, two), !toSupply);
        }
    }
}
