using System.Globalization;
using System.Text;

namespace ArduSim.Core.Screens;

/// <summary>A symbol of five by eight dots, from the top row down; the low five bits of each byte.</summary>
public sealed record ScreenGlyph
{
    public string Name { get; init; } = "";

    public IReadOnlyList<byte> Rows { get; init; } = new byte[8];
}

/// <summary>
/// What a generated project can show beyond ASCII (ADR-019). A character display's ROM differs between
/// panels, so that everything here is either an ASCII letter of the same shape, or a shape of its own that
/// goes into the panel's programmable characters. Lower case shows as capitals, there being only eight of
/// those characters at a time.
/// </summary>
public static class ScreenFont
{
    /// <summary>The ROM's full block: the same in every HD44780 font.</summary>
    public const byte RomBlock = 0xFF;

    public const string BigTop = "bigTop";
    public const string BigBottom = "bigBottom";
    public const string BigBoth = "bigBoth";

    // Letters of the same shape as an ASCII character, which take none of the programmable eight.
    private static readonly Dictionary<char, char> SameShape = new()
    {
        ['А'] = 'A', ['В'] = 'B', ['Е'] = 'E', ['Ё'] = 'E', ['З'] = '3', ['К'] = 'K', ['М'] = 'M', ['Н'] = 'H',
        ['О'] = 'O', ['Р'] = 'P', ['С'] = 'C', ['Т'] = 'T', ['Ь'] = 'b', ['Х'] = 'X',
    };

    private static readonly Dictionary<char, byte[]> Shapes = new()
    {
        ['Б'] = [0b11111, 0b10000, 0b10000, 0b11110, 0b10001, 0b10001, 0b11110, 0],
        ['Г'] = [0b11111, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0],
        ['Д'] = [0b00110, 0b01010, 0b01010, 0b01010, 0b01010, 0b11111, 0b10001, 0],
        ['Ж'] = [0b10101, 0b10101, 0b10101, 0b01110, 0b10101, 0b10101, 0b10101, 0],
        ['И'] = [0b10001, 0b10001, 0b10011, 0b10101, 0b11001, 0b10001, 0b10001, 0],
        ['Й'] = [0b01010, 0b00100, 0b10001, 0b10011, 0b10101, 0b11001, 0b10001, 0],
        ['Л'] = [0b00111, 0b01001, 0b01001, 0b01001, 0b01001, 0b01001, 0b10001, 0],
        ['П'] = [0b11111, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0],
        ['У'] = [0b10001, 0b10001, 0b10001, 0b01111, 0b00001, 0b10001, 0b01110, 0],
        ['Ф'] = [0b00100, 0b01110, 0b10101, 0b10101, 0b10101, 0b01110, 0b00100, 0],
        ['Ц'] = [0b10010, 0b10010, 0b10010, 0b10010, 0b10010, 0b10010, 0b11111, 0b00001],
        ['Ч'] = [0b10001, 0b10001, 0b10001, 0b01111, 0b00001, 0b00001, 0b00001, 0],
        ['Ш'] = [0b10101, 0b10101, 0b10101, 0b10101, 0b10101, 0b10101, 0b11111, 0],
        ['Щ'] = [0b10101, 0b10101, 0b10101, 0b10101, 0b10101, 0b10101, 0b11111, 0b00001],
        ['Ъ'] = [0b11000, 0b01000, 0b01000, 0b01110, 0b01001, 0b01001, 0b01110, 0],
        ['Ы'] = [0b10001, 0b10001, 0b10001, 0b11101, 0b10011, 0b10011, 0b11101, 0],
        ['Э'] = [0b01110, 0b10001, 0b00001, 0b00111, 0b00001, 0b10001, 0b01110, 0],
        ['Ю'] = [0b10010, 0b10101, 0b10101, 0b11101, 0b10101, 0b10101, 0b10010, 0],
        ['Я'] = [0b01111, 0b10001, 0b10001, 0b01111, 0b00101, 0b01001, 0b10001, 0],
        ['°'] = [0b01100, 0b10010, 0b10010, 0b01100, 0, 0, 0, 0],
        ['←'] = [0, 0b00100, 0b01000, 0b11111, 0b01000, 0b00100, 0, 0],
        ['→'] = [0, 0b00100, 0b00010, 0b11111, 0b00010, 0b00100, 0, 0],
        ['↑'] = [0b00100, 0b01110, 0b10101, 0b00100, 0b00100, 0b00100, 0, 0],
        ['↓'] = [0b00100, 0b00100, 0b00100, 0b10101, 0b01110, 0b00100, 0, 0],
        ['•'] = [0, 0, 0b01110, 0b01110, 0b01110, 0, 0, 0],
        ['№'] = [0b10010, 0b11010, 0b10110, 0b10010, 0b10011, 0b10010, 0b00011, 0],
        ['±'] = [0b00100, 0b00100, 0b11111, 0b00100, 0b00100, 0, 0b11111, 0],
        ['×'] = [0, 0b10001, 0b01010, 0b00100, 0b01010, 0b10001, 0, 0],
        ['÷'] = [0, 0b00100, 0, 0b11111, 0, 0b00100, 0, 0],
        ['…'] = [0, 0, 0, 0, 0, 0, 0b10101, 0],
    };

    // When more than eight are wanted at once, the nearest ASCII character says what was meant.
    private static readonly Dictionary<char, char> Nearest = new()
    {
        ['Б'] = '6', ['Г'] = 'r', ['Д'] = 'D', ['Ж'] = '*', ['И'] = 'u', ['Й'] = 'u', ['Л'] = 'J', ['П'] = 'n',
        ['У'] = 'y', ['Ф'] = 'o', ['Ц'] = 'u', ['Ч'] = '4', ['Ш'] = 'w', ['Щ'] = 'w', ['Ъ'] = 'b', ['Ы'] = 'b',
        ['Э'] = '3', ['Ю'] = 'o', ['Я'] = 'R', ['°'] = 'o', ['←'] = '<', ['→'] = '>', ['↑'] = '^', ['↓'] = 'v',
        ['•'] = '.', ['№'] = 'N', ['±'] = '+', ['×'] = 'x', ['÷'] = '/', ['…'] = '.',
    };

    /// <summary>Symbols every project can use by name; one of its own with the same name takes their place.</summary>
    public static IReadOnlyList<ScreenGlyph> BuiltIn { get; } =
    [
        Glyph("bell", 0b00100, 0b01110, 0b01110, 0b01110, 0b11111, 0, 0b00100, 0),
        Glyph("arrowRight", 0, 0b00100, 0b00010, 0b11111, 0b00010, 0b00100, 0, 0),
        Glyph("arrowLeft", 0, 0b00100, 0b01000, 0b11111, 0b01000, 0b00100, 0, 0),
        Glyph("arrowUp", 0b00100, 0b01110, 0b10101, 0b00100, 0b00100, 0b00100, 0, 0),
        Glyph("arrowDown", 0b00100, 0b00100, 0b00100, 0b10101, 0b01110, 0b00100, 0, 0),
        Glyph("check", 0, 0b00001, 0b00010, 0b10100, 0b01000, 0, 0, 0),
        Glyph("degree", 0b01100, 0b10010, 0b10010, 0b01100, 0, 0, 0, 0),
        Glyph("note", 0b00010, 0b00011, 0b00010, 0b00010, 0b01110, 0b11110, 0b01100, 0),
        Glyph("heart", 0, 0b01010, 0b11111, 0b11111, 0b01110, 0b00100, 0, 0),
        Glyph("lock", 0b01110, 0b10001, 0b10001, 0b11111, 0b11011, 0b11011, 0b11111, 0),
        Glyph("play", 0b10000, 0b11000, 0b11100, 0b11110, 0b11100, 0b11000, 0b10000, 0),
        Glyph("pause", 0b11011, 0b11011, 0b11011, 0b11011, 0b11011, 0b11011, 0b11011, 0),
        Glyph("battery", 0b01110, 0b11111, 0b10001, 0b10001, 0b10001, 0b11111, 0b11111, 0),
    ];

    // The parts big digits are made of, which no one picks by name.
    internal static ScreenGlyph[] BigParts { get; } =
    [
        Glyph(BigTop, 0b11111, 0b11111, 0b11111, 0, 0, 0, 0, 0),
        Glyph(BigBottom, 0, 0, 0, 0, 0, 0b11111, 0b11111, 0b11111),
        Glyph(BigBoth, 0b11111, 0b11111, 0, 0, 0, 0, 0b11111, 0b11111),
    ];

    /// <summary>Each digit, as two rows of three cells: T the top part, B the bottom, X both, F a full block.</summary>
    public static IReadOnlyList<string> BigDigits { get; } =
    [
        "FTFFBF", // 0
        "TF BFB", // 1
        "XXFFBB", // 2
        "XXFBBF", // 3
        "FBF  F", // 4
        "FXXBBF", // 5
        "FXXFBF", // 6
        "TTF  F", // 7
        "FXFFBF", // 8
        "FXFBBF", // 9
    ];

    private static ScreenGlyph Glyph(string name, params byte[] rows) => new() { Name = name, Rows = rows };

    /// <summary>The dots of a part of a big digit; null for an empty cell or a full block.</summary>
    public static IReadOnlyList<byte>? BigPart(char part) => part switch
    {
        'T' => BigParts[0].Rows,
        'B' => BigParts[1].Rows,
        'X' => BigParts[2].Rows,
        _ => null,
    };

    /// <summary>A symbol by name: the project's own first, then the built-in ones and the parts of big digits.</summary>
    public static ScreenGlyph? Find(ScreenDocument document, string name)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Glyphs.FirstOrDefault(g => g.Name == name)
            ?? BuiltIn.FirstOrDefault(g => g.Name == name)
            ?? BigParts.FirstOrDefault(g => g.Name == name);
    }

    /// <summary>The capital a letter is drawn as.</summary>
    public static char Capital(char letter) => letter is >= 'а' and <= 'я' or 'ё' ? char.ToUpperInvariant(letter) : letter;

    /// <summary>The ASCII character of the same shape, if a letter has one.</summary>
    public static char? LookAlike(char letter) => SameShape.TryGetValue(Capital(letter), out char ascii) ? ascii : null;

    /// <summary>A shape for what has no ASCII look-alike; null if there is none.</summary>
    public static IReadOnlyList<byte>? Shape(char letter) => Shapes.TryGetValue(Capital(letter), out byte[]? rows) ? rows : null;

    /// <summary>What shows when the display has no room for the shape.</summary>
    public static char NearestAscii(char letter) => Nearest.TryGetValue(Capital(letter), out char ascii) ? ascii : '?';

    /// <summary>A piece of what a text says: letters, or a symbol named in it.</summary>
    public readonly record struct TextPart(string? Letters, ScreenGlyph? Symbol);

    /// <summary>Splits a text at its <c>{name}</c>s. A name that is no symbol is just text.</summary>
    public static IReadOnlyList<TextPart> Split(ScreenDocument document, string text)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(text);
        var parts = new List<TextPart>();
        var letters = new StringBuilder();
        int at = 0;
        while (at < text.Length)
        {
            int close = text[at] == '{' ? text.IndexOf('}', at + 1) : -1;
            if (close > at && Find(document, text[(at + 1)..close]) is { } symbol)
            {
                if (letters.Length > 0)
                {
                    parts.Add(new TextPart(letters.ToString(), null));
                    letters.Clear();
                }

                parts.Add(new TextPart(null, symbol));
                at = close + 1;
            }
            else
            {
                letters.Append(text[at++]);
            }
        }

        if (letters.Length > 0)
        {
            parts.Add(new TextPart(letters.ToString(), null));
        }

        return parts;
    }

    /// <summary>How many cells a text takes: a symbol is one, whatever its name.</summary>
    public static int CellsOf(ScreenDocument document, string text) =>
        Split(document, text).Sum(p => p.Letters is { } letters ? new StringInfo(letters).LengthInTextElements : 1);
}
