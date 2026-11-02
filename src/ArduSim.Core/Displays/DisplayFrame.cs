namespace ArduSim.Core.Displays;

public enum DisplayKind
{
    /// <summary>HD44780 glass: rows x columns cells of 5x8 dots.</summary>
    CharacterLcd = 1,

    /// <summary>Monochrome OLED: Rows lines of Columns pixels, packed MSB-left.</summary>
    Oled = 2,
}

[Flags]
public enum DisplayStatus
{
    None = 0,

    /// <summary>The module has power; without it the glass is blank and dark.</summary>
    Powered = 1,

    /// <summary>The controller's display is on.</summary>
    On = 2,

    Backlight = 4,
}

/// <summary>
/// An immutable picture of a display for the view (ADR-006). For <see cref="DisplayKind.CharacterLcd"/>,
/// <see cref="Dots"/> holds rows x 8 dot rows of columns cells, each a 5-bit mask (bit 4 = leftmost
/// dot) at <c>(row * 8 + line) * columns + column</c>, and <see cref="Text"/> the visible rows as text.
/// For <see cref="DisplayKind.Oled"/>, <see cref="Dots"/> holds Rows lines of <c>(Columns + 7) / 8</c>
/// bytes, MSB the leftmost pixel, and <see cref="Text"/> is empty.
/// </summary>
/// <param name="Contrast">0 (nothing visible) to 1 (darkest).</param>
/// <param name="Version">Grows with every change, so a view redraws only new frames.</param>
public sealed record DisplayFrame(
    DisplayKind Kind,
    int Columns,
    int Rows,
    DisplayStatus Status,
    double Contrast,
    ReadOnlyMemory<byte> Dots,
    IReadOnlyList<string> Text,
    long Version);
