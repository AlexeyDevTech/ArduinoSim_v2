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

    /// <summary>Dots across and down for a display.</summary>
    public static (int Width, int Height) DotSize(ScreenDisplay display)
    {
        ArgumentNullException.ThrowIfNull(display);
        return display.Family == DisplayFamily.CharLcd ? ((display.Width * 6) + 1, (display.Height * 9) + 1) : (display.Width, display.Height);
    }

    /// <summary>Where an element lies, in dots of the bitmap.</summary>
    public static Rect BoundsOf(ScreenElement element, ScreenDisplay display)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(display);
        int characters = element.Kind == ElementKind.Value ? Math.Max(element.Width, 1) : Math.Max(element.Text.Length, 1);
        if (display.Family == DisplayFamily.CharLcd)
        {
            return new Rect((element.X * 6) + 1, (element.Y * 9) + 1, Math.Max((characters * 6) - 1, 5), 8);
        }

        return element.Kind switch
        {
            ElementKind.Line => new Rect(new Point(element.X, element.Y), new Point(element.Width, element.Height)),
            ElementKind.Frame or ElementKind.Box => new Rect(element.X, element.Y, Math.Max(element.Width, 1), Math.Max(element.Height, 1)),
            _ => new Rect(element.X, TextTop(element, display), characters * 6 * element.Size, 8 * element.Size),
        };
    }

    private static int TextTop(ScreenElement element, ScreenDisplay display) => display.TextFromBaseline ? element.Y - 7 : element.Y;

    /// <summary>One screen, frozen so that it can be kept and drawn at any scale.</summary>
    public static BitmapSource Render(ScreenNode screen, ScreenDisplay display)
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

        foreach (ScreenElement element in screen.Elements.Where(e => !e.IsAlternative))
        {
            Draw(pixels, width, height, element, display);
        }

        if (screen.Menu is { } menu)
        {
            DrawMenu(pixels, width, height, menu, display);
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static void Draw(uint[] pixels, int width, int height, ScreenElement element, ScreenDisplay display)
    {
        bool lcd = display.Family == DisplayFamily.CharLcd;
        uint text = element.IsConditional ? (lcd ? DimLcd : DimOled) : (lcd ? LcdText : OledText);
        switch (element.Kind)
        {
            case ElementKind.Text:
                DrawText(pixels, width, height, element.Text, element.X, TextTop(element, display), element.Size, text, display);
                break;
            case ElementKind.Value:
                string sample = element.Sample ?? element.Text;
                int field = Math.Max(element.Width, 1);
                string shown = sample.Length >= field ? sample[..field] : sample.PadRight(field);
                DrawText(pixels, width, height, shown, element.X, TextTop(element, display), element.Size, ValueColor, display);
                break;
            case ElementKind.Line when !lcd:
                DrawLine(pixels, width, height, element.X, element.Y, element.Width, element.Height, text);
                break;
            case ElementKind.Frame when !lcd:
                Fill(pixels, width, height, element.X, element.Y, element.Width, 1, text);
                Fill(pixels, width, height, element.X, element.Y + element.Height - 1, element.Width, 1, text);
                Fill(pixels, width, height, element.X, element.Y, 1, element.Height, text);
                Fill(pixels, width, height, element.X + element.Width - 1, element.Y, 1, element.Height, text);
                break;
            case ElementKind.Box when !lcd:
                Fill(pixels, width, height, element.X, element.Y, element.Width, element.Height, text);
                break;
            default:
                break;
        }
    }

    private static void DrawMenu(uint[] pixels, int width, int height, ScreenMenu menu, ScreenDisplay display)
    {
        bool lcd = display.Family == DisplayFamily.CharLcd;
        uint text = lcd ? LcdText : OledText;
        for (int row = 0; row < menu.Rows && row < menu.Items.Count; row++)
        {
            int y = menu.Top + (row * (lcd ? 1 : 8));
            DrawText(pixels, width, height, (row == 0 ? ">" : " ") + menu.Items[row].Text, 0, y, 1, text, display);
        }
    }

    private static void DrawText(uint[] pixels, int width, int height, string text, int x, int y, int size, uint color, ScreenDisplay display)
    {
        bool lcd = display.Family == DisplayFamily.CharLcd;
        Span<byte> rows = stackalloc byte[8];
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == ' ')
            {
                continue;
            }

            Hd44780Font.A00(c < 256 ? (byte)c : (byte)'?', rows);
            int left = lcd ? ((x + i) * 6) + 1 : x + (i * 6 * size);
            int top = lcd ? (y * 9) + 1 : y;
            int scale = lcd ? 1 : size;
            if (lcd && x + i >= display.Width)
            {
                break;
            }

            for (int dy = 0; dy < 8; dy++)
            {
                for (int dx = 0; dx < 5; dx++)
                {
                    if ((rows[dy] & (0x10 >> dx)) != 0)
                    {
                        Fill(pixels, width, height, left + (dx * scale), top + (dy * scale), scale, scale, color);
                    }
                }
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
