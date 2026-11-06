using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArduSim.Core.Displays;
using ArduSim.Core.Screens;

namespace ArduSim.App.Views;

/// <summary>
/// A screen as a bitmap, one pixel per dot, for the map's thumbnails and the screen view (ADR-019).
/// A character display uses its real font ROM; an OLED the same 5×7 glyphs in a 6×8 cell, which is what
/// Adafruit's library draws with. Other OLED fonts differ: positions are right, glyphs approximate.
/// </summary>
public static class ScreenPreview
{
    private const uint LcdBack = 0xFF2A55EA;
    private const uint LcdCell = 0xFF3263F5;
    private const uint LcdText = 0xFFEEF4FF;
    private const uint OledBack = 0xFF060608;
    private const uint OledText = 0xFFCDE8FF;
    private const uint ValueColor = 0xFFFFD666;
    private const uint DimLcd = 0xFF9DB6FF;
    private const uint DimOled = 0xFF7A8FA5;

    // The ROM's centered dot, for what a value cannot be named by.
    private const char ValueDot = '\u00A5';

    /// <summary>Dots across and down for a display.</summary>
    public static (int Width, int Height) DotSize(ScreenDisplay display)
    {
        ArgumentNullException.ThrowIfNull(display);
        return display.Family == DisplayFamily.CharLcd ? ((display.Width * 6) + 1, (display.Height * 9) + 1) : (display.Width, display.Height);
    }

    /// <summary>Where an element lies, in dots of the bitmap.</summary>
    public static Rect BoundsOf(ScreenElement element, ScreenDisplay display, ScreenDocument? document = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(display);
        bool lcd = display.Family == DisplayFamily.CharLcd;
        bool generated = document is { Source: ScreenSource.Generated };
        int characters = element.Kind switch
        {
            ElementKind.Value => FieldOf(element, display),
            ElementKind.Input => ScreenSample.Mask(element).Length,
            ElementKind.Symbol => 1,
            ElementKind.Text when generated => Math.Max(ScreenFont.CellsOf(document!, element.Text), 1),
            _ => Math.Max(element.Text.Length, 1),
        };
        int shift = element.Kind == ElementKind.Text && generated ? ShiftOf(element, characters, display) : 0;
        if (element.Kind == ElementKind.BigNumber && generated)
        {
            string big = BigText(element, document!);
            return lcd
                ? new Rect((element.X * 6) + 1, (element.Y * 9) + 1, Math.Max((BigCells(big) * 6) - 1, 5), 17)
                : new Rect(element.X, TextTop(element, display), big.Length * 6 * Math.Max(element.Size, 2), 8 * Math.Max(element.Size, 2));
        }

        if (element.Kind is ElementKind.Bar or ElementKind.Drawn)
        {
            return lcd
                ? new Rect((element.X * 6) + 1, (element.Y * 9) + 1, Math.Max((element.Width * 6) - 1, 5), Math.Max((Math.Max(element.Kind == ElementKind.Drawn ? element.Height : 1, 1) * 9) - 1, 8))
                : new Rect(element.X, element.Y, Math.Max(element.Width, 1), Math.Max(element.Height, 1));
        }

        if (lcd)
        {
            return new Rect(((element.X + shift) * 6) + 1, (element.Y * 9) + 1, Math.Max((characters * 6) - 1, 5), 8);
        }

        return element.Kind switch
        {
            ElementKind.Line => new Rect(new Point(element.X, element.Y), new Point(element.Width, element.Height)),
            ElementKind.Frame or ElementKind.Box => new Rect(element.X, element.Y, Math.Max(element.Width, 1), Math.Max(element.Height, 1)),
            _ => new Rect(element.X + (shift * 6 * element.Size), TextTop(element, display), characters * 6 * element.Size, 8 * element.Size),
        };
    }

    // A field's width in cells: what it says, or to the display's edge.
    private static int FieldOf(ScreenElement element, ScreenDisplay display) =>
        element.Width > 0 ? element.Width : Math.Max(1, (display.Width - element.X) / (display.Family == DisplayFamily.CharLcd ? 1 : 6 * Math.Max(element.Size, 1)));

    private static int ShiftOf(ScreenElement element, int cells, ScreenDisplay display)
    {
        int room = Math.Max(FieldOf(element, display) - cells, 0);
        return element.Align switch
        {
            ElementAlign.Center => room / 2,
            ElementAlign.Right => room,
            _ => 0,
        };
    }

    private static string BigText(ScreenElement element, ScreenDocument document) =>
        ScreenSample.Text(document, element.Text, element.Format, []) is { Length: > 0 } sample ? sample : "00";

    // A digit is three cells and a gap, anything else one and a gap, and the last has no gap after it.
    private static int BigCells(string text) => text.Sum(c => char.IsAsciiDigit(c) ? 4 : 2) - 1;
    private static int TextTop(ScreenElement element, ScreenDisplay display) => display.TextFromBaseline ? element.Y - 7 : element.Y;

    /// <summary>What a screen's picture shows: not what only shares a place with something else, and what the code could not place only where it hides nothing.</summary>
    /// <param name="document">In a generated project, of what hangs on different conditions in one place only one can be drawn: <paramref name="selected"/>, or the first.</param>
    public static IReadOnlyList<ScreenElement> Shown(ScreenNode screen, ScreenDisplay display, ScreenDocument? document = null, string? selected = null)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(display);
        ScreenElement[] drawn = [.. screen.Elements.Where(e => !e.IsAlternative)];
        if (document is { Source: ScreenSource.Generated })
        {
            ScreenElement[] all = drawn;
            bool Gives(int i, int j) =>
                all[j].When.Length > 0 && all[j].When != all[i].When && BoundsOf(all[i], display, document).IntersectsWith(BoundsOf(all[j], display, document))
                && (all[j].Id == selected || (j < i && all[i].Id != selected));
            drawn = [.. all.Where((e, i) => e.When.Length == 0 || !Enumerable.Range(0, all.Length).Any(j => j != i && Gives(i, j)))];
        }

        Rect[] sure = [.. drawn.Where(IsSure).Select(e => BoundsOf(e, display))];
        return [.. drawn.Where(e => IsSure(e) || !sure.Any(r => r.IntersectsWith(BoundsOf(e, display))))];
    }

    private static bool IsSure(ScreenElement element) => element.IsPlaced || element.Kind is not (ElementKind.Text or ElementKind.Value);

    /// <summary>A value with nothing known to show: a name says what it is, and any other expression would only read as noise.</summary>
    public static string ValueLook(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        string last = expression[(Math.Max(expression.LastIndexOf('.'), expression.LastIndexOf('>')) + 1)..];
        bool isName = last.Length > 0 && !char.IsAsciiDigit(last[0]) && last.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
        return isName ? last : new string(ValueDot, 8);
    }

    // A glyph for a character: from the ROM, or from the font of what a generated project or a class of the sketch's own draws.
    private static void Glyph(char c, bool font, Span<byte> rows)
    {
        if (font && c >= 0x80)
        {
            if (ScreenFont.LookAlike(c) is { } ascii)
            {
                Hd44780Font.A00((byte)ascii, rows);
                return;
            }

            if (ScreenFont.Shape(c) is { } shape)
            {
                for (int i = 0; i < 8; i++)
                {
                    rows[i] = shape[i];
                }

                return;
            }
        }

        Hd44780Font.A00(c < 256 ? (byte)c : (byte)'?', rows);
    }

    /// <summary>One screen, frozen so that it can be kept and drawn at any scale.</summary>
    /// <param name="document">Its project: variables and symbols. Without one it shows text and shapes only.</param>
    /// <param name="selected">The element being worked on, which is drawn even if it hides under another with a condition.</param>
    public static BitmapSource Render(ScreenNode screen, ScreenDisplay display, ScreenDocument? document = null, string? selected = null)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(display);
        (int width, int height) = DotSize(display);
        uint[] pixels = new uint[width * height];
        bool lcd = display.Family == DisplayFamily.CharLcd;
        Array.Fill(pixels, lcd ? LcdBack : OledBack);
        if (lcd)
        {
            for (int row = 0; row < display.Height; row++)
            {
                for (int column = 0; column < display.Width; column++)
                {
                    Fill(pixels, width, height, (column * 6) + 1, (row * 9) + 1, 5, 8, LcdCell);
                }
            }
        }

        var canvas = new Canvas(pixels, width, height, display, document);
        foreach (ScreenElement element in Shown(screen, display, document, selected))
        {
            Draw(canvas, element);
        }

        if (screen.Menu is { } menu)
        {
            DrawMenu(canvas, menu);
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private sealed record Canvas(uint[] Pixels, int Width, int Height, ScreenDisplay Display, ScreenDocument? Document)
    {
        public bool IsLcd => Display.Family == DisplayFamily.CharLcd;

        public bool IsGenerated => Document is { Source: ScreenSource.Generated };

        public bool HasFont => IsGenerated || Display.OwnFont;
    }

    private static void Draw(Canvas c, ScreenElement element)
    {
        bool lcd = c.IsLcd;
        bool dim = element.IsConditional || element.When.Length > 0;
        uint text = dim ? (lcd ? DimLcd : DimOled) : (lcd ? LcdText : OledText);
        uint value = dim ? (lcd ? DimLcd : DimOled) : ValueColor;
        switch (element.Kind)
        {
            case ElementKind.Text:
                int cells = c.IsGenerated ? ScreenFont.CellsOf(c.Document!, element.Text) : element.Text.Length;
                int shift = c.IsGenerated ? ShiftOf(element, cells, c.Display) * (lcd ? 1 : 6 * element.Size) : 0;
                DrawText(c, element.Text, element.X + shift, TextTop(element, c.Display), element.Size, text);
                break;
            case ElementKind.Value:
                string sample = element.Sample ?? (c.IsGenerated ? ScreenSample.Text(c.Document!, element.Text, element.Format, element.Options) : null) ?? ValueLook(element.Text);
                int field = FieldOf(element, c.Display);
                string shown = sample.Length >= field ? sample[..field] : sample;
                int room = c.IsGenerated ? ShiftOf(element with { Width = field }, shown.Length, c.Display) * (lcd ? 1 : 6 * element.Size) : 0;
                DrawText(c, shown, element.X + room, TextTop(element, c.Display), element.Size, value);
                break;
            case ElementKind.BigNumber when c.IsGenerated:
                DrawBig(c, element, value);
                break;
            case ElementKind.Bar:
                DrawBar(c, element, value);
                break;
            case ElementKind.Symbol when c.IsGenerated && ScreenFont.Find(c.Document!, element.Text) is { } glyph:
                DrawRows(c, glyph.Rows, lcd ? (element.X * 6) + 1 : element.X, lcd ? (element.Y * 9) + 1 : element.Y, lcd ? 1 : element.Size, text);
                break;
            case ElementKind.Input:
                string mask = ScreenSample.Mask(element);
                DrawText(c, element.Align == ElementAlign.Right ? mask.Replace('_', '0') : mask, element.X, TextTop(element, c.Display), element.Size, value);
                break;
            case ElementKind.Drawn:
                DrawHatch(c, BoundsOf(element, c.Display, c.Document), dim ? text : value);
                break;
            case ElementKind.Line when !lcd:
                DrawLine(c.Pixels, c.Width, c.Height, element.X, element.Y, element.Width, element.Height, text);
                break;
            case ElementKind.Frame when !lcd:
                DrawFrame(c, element.X, element.Y, element.Width, element.Height, text);
                break;
            case ElementKind.Box when !lcd:
                Fill(c.Pixels, c.Width, c.Height, element.X, element.Y, element.Width, element.Height, text);
                break;
            default:
                break;
        }
    }

    private static void DrawFrame(Canvas c, int x, int y, int width, int height, uint color)
    {
        Fill(c.Pixels, c.Width, c.Height, x, y, width, 1, color);
        Fill(c.Pixels, c.Width, c.Height, x, y + height - 1, width, 1, color);
        Fill(c.Pixels, c.Width, c.Height, x, y, 1, height, color);
        Fill(c.Pixels, c.Width, c.Height, x + width - 1, y, 1, height, color);
    }

    private static void DrawBig(Canvas c, ScreenElement element, uint color)
    {
        string text = BigText(element, c.Document!);
        if (!c.IsLcd)
        {
            DrawText(c, text, element.X, TextTop(element, c.Display), Math.Max(element.Size, 2), color);
            return;
        }

        int x = element.X;
        foreach (char digit in text)
        {
            if (char.IsAsciiDigit(digit))
            {
                string parts = ScreenFont.BigDigits[digit - '0'];
                for (int i = 0; i < 6; i++)
                {
                    int left = ((x + (i % 3)) * 6) + 1;
                    int top = ((element.Y + (i / 3)) * 9) + 1;
                    if (parts[i] == 'F')
                    {
                        Fill(c.Pixels, c.Width, c.Height, left, top, 5, 8, color);
                    }
                    else if (ScreenFont.BigPart(parts[i]) is { } rows)
                    {
                        DrawRows(c, rows, left, top, 1, color);
                    }
                }

                x += 4;
            }
            else
            {
                DrawText(c, ".", x, element.Y, 1, color);
                DrawText(c, ".", x, element.Y + 1, 1, color);
                x += 2;
            }
        }
    }

    private static void DrawBar(Canvas c, ScreenElement element, uint color)
    {
        double full = c.Document is { } document && element.Maximum.Length > 0 ? ScreenSample.Number(document, element.Maximum) : 100;
        double part = c.Document is { } known ? ScreenSample.Number(known, element.Text) : 50;
        double share = full > 0 ? Math.Clamp(part / full, 0, 1) : 0;
        if (c.IsLcd)
        {
            for (int i = 0; i < (int)(share * element.Width); i++)
            {
                Fill(c.Pixels, c.Width, c.Height, ((element.X + i) * 6) + 1, (element.Y * 9) + 1, 5, 8, color);
            }
        }
        else
        {
            DrawFrame(c, element.X, element.Y, Math.Max(element.Width, 1), Math.Max(element.Height, 1), color);
            Fill(c.Pixels, c.Width, c.Height, element.X + 1, element.Y + 1, (int)(share * (element.Width - 2)), element.Height - 2, color);
        }
    }

    // What the project's own code draws: there is no knowing what, only where.
    private static void DrawHatch(Canvas c, Rect bounds, uint color)
    {
        for (int y = (int)bounds.Top; y < (int)bounds.Bottom; y++)
        {
            for (int x = (int)bounds.Left; x < (int)bounds.Right; x++)
            {
                if ((x + y) % 4 == 0)
                {
                    Fill(c.Pixels, c.Width, c.Height, x, y, 1, 1, color);
                }
            }
        }
    }

    private static void DrawMenu(Canvas c, ScreenMenu menu)
    {
        bool lcd = c.IsLcd;
        uint text = lcd ? LcdText : OledText;
        int step = lcd ? 1 : 8;
        int across = lcd ? c.Display.Width : c.Display.Width / 6;
        int unit = lcd ? 1 : 6;
        string marker = menu.Marker.Length > 0 ? menu.Marker : ">";
        int markerCells = c.IsGenerated ? ScreenFont.CellsOf(c.Document!, marker) : marker.Length;
        if (menu.Counter && menu.Top - step >= 0)
        {
            string counter = "1/" + menu.Items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            DrawText(c, counter, (across - counter.Length) * unit, menu.Top - step, 1, text);
        }

        for (int row = 0; row < menu.Rows && row < menu.Items.Count; row++)
        {
            int y = menu.Top + (row * step);
            ScreenMenuItem item = menu.Items[row];
            DrawText(c, row == 0 ? marker : new string(' ', markerCells), 0, y, 1, text);
            DrawText(c, item.Text, markerCells * unit, y, 1, text);
            string? right = !c.IsGenerated ? null
                : item.Value.Length > 0 ? ScreenSample.Text(c.Document!, item.Value, item.Format, []) ?? ValueLook(item.Value)
                : item.Mark.Length > 0 && ScreenSample.Number(c.Document!, item.Mark) != 0 ? "*"
                : null;
            if (right is not null)
            {
                DrawText(c, right, (across - right.Length) * unit, y, 1, ValueColor);
            }
        }
    }

    private static void DrawRows(Canvas c, IReadOnlyList<byte> rows, int left, int top, int scale, uint color)
    {
        for (int dy = 0; dy < 8 && dy < rows.Count; dy++)
        {
            for (int dx = 0; dx < 5; dx++)
            {
                if ((rows[dy] & (0x10 >> dx)) != 0)
                {
                    Fill(c.Pixels, c.Width, c.Height, left + (dx * scale), top + (dy * scale), scale, scale, color);
                }
            }
        }
    }

    // A text, with the symbols it names in a generated project.
    private static void DrawText(Canvas c, string text, int x, int y, int size, uint color)
    {
        bool lcd = c.IsLcd;
        Span<byte> rows = stackalloc byte[8];
        int scale = lcd ? 1 : size;
        int top = lcd ? (y * 9) + 1 : y;
        int at = 0;
        foreach (ScreenFont.TextPart part in c.IsGenerated ? ScreenFont.Split(c.Document!, text) : [new ScreenFont.TextPart(text, null)])
        {
            foreach (char letter in part.Letters ?? "\0")
            {
                int left = lcd ? ((x + at) * 6) + 1 : x + (at * 6 * size);
                if (lcd && x + at >= c.Display.Width)
                {
                    return;
                }

                if (part.Symbol is { } symbol)
                {
                    DrawRows(c, symbol.Rows, left, top, scale, color);
                }
                else if (letter != ' ')
                {
                    Glyph(letter, c.HasFont, rows);
                    for (int dy = 0; dy < 8; dy++)
                    {
                        for (int dx = 0; dx < 5; dx++)
                        {
                            if ((rows[dy] & (0x10 >> dx)) != 0)
                            {
                                Fill(c.Pixels, c.Width, c.Height, left + (dx * scale), top + (dy * scale), scale, scale, color);
                            }
                        }
                    }
                }

                at++;
            }
        }
    }
    private static void Fill(uint[] pixels, int width, int height, int x, int y, int w, int h, uint color)
    {
        int right = Math.Min(x + w, width);
        int bottom = Math.Min(y + h, height);
        for (int py = Math.Max(y, 0); py < bottom; py++)
        {
            for (int px = Math.Max(x, 0); px < right; px++)
            {
                pixels[(py * width) + px] = color;
            }
        }
    }

    private static void DrawLine(uint[] pixels, int width, int height, int x0, int y0, int x1, int y1, uint color)
    {
        int dx = Math.Abs(x1 - x0);
        int dy = -Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int error = dx + dy;
        while (true)
        {
            Fill(pixels, width, height, x0, y0, 1, 1, color);
            if (x0 == x1 && y0 == y1)
            {
                return;
            }

            int twice = 2 * error;
            if (twice >= dy)
            {
                error += dy;
                x0 += sx;
            }

            if (twice <= dx)
            {
                error += dx;
                y0 += sy;
            }
        }
    }
}
