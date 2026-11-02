using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArduSim.Ipc;

namespace ArduSim.App.Views;

/// <summary>
/// The HD44780 glass of one LCD part (docs/UI.md §3.6): every dot of every 5x8 cell is a square,
/// unlit dots stay faintly visible, colors follow the backlight variant and contrast. The
/// WriteableBitmap is drawn for the current zoom with whole-pixel dots no larger than they appear,
/// and shown at the glass size with nearest-neighbor sampling: it is only ever scaled up, by less
/// than one dot, so no dot row is lost at any zoom (brief §3.5).
/// </summary>
public sealed class LcdScreen
{
    /// <summary>Dot pitch in canvas units (about 0.7 mm, as on a 1602 module).</summary>
    public const double DotPitch = 2.8;

    /// <summary>Above this many device pixels per canvas unit the bitmap is scaled up instead of drawn larger.</summary>
    private const double MaxRenderScale = 4;

    private WriteableBitmap? _bitmap;
    private uint[] _pixels = [];

    public ImageSource? Source => _bitmap;

    /// <summary>Size of the glass in canvas units.</summary>
    public double Width { get; private set; }

    public double Height { get; private set; }

    /// <summary>Redraws the glass; true when <see cref="Source"/> is a new bitmap.</summary>
    /// <param name="frame">The picture, or null for a module without power (before a run).</param>
    /// <param name="backlight">The part's variant: <c>blue</c>, <c>green</c> or <c>none</c>.</param>
    /// <param name="pixelsPerUnit">Device pixels per canvas unit at the current zoom.</param>
    public bool Render(DisplayData? frame, int columns, int rows, string backlight, double pixelsPerUnit)
    {
        if (frame is not null)
        {
            columns = frame.Columns;
            rows = frame.Rows;
        }

        int pitch = Math.Max(1, (int)Math.Floor(DotPitch * Math.Min(pixelsPerUnit, MaxRenderScale)));
        int gap = pitch switch
        {
            1 => 0,
            2 => 1,
            _ => Math.Max(1, (int)Math.Round(pitch * 0.12)),
        };
        int dot = pitch - gap;
        int width = ((6 * columns) + 1) * pitch;
        int height = ((9 * rows) + 1) * pitch;
        Width = ((6 * columns) + 1) * DotPitch;
        Height = ((9 * rows) + 1) * DotPitch;

        bool powered = frame is not null && (frame.Status & 1) != 0;
        bool on = powered && (frame!.Status & 2) != 0;
        bool lit = powered && (frame!.Status & 4) != 0;
        (Color background, Color text) = Palette(backlight, lit);
        double contrast = powered ? frame!.Contrast : 0;
        uint backgroundPixel = Pixel(background);
        uint offPixel = Pixel(Mix(background, text, 0.06 + (1.4 * Math.Max(0, contrast - 0.8))));
        uint onPixel = Pixel(Mix(Mix(background, text, 0.06), text, contrast));

        if (_pixels.Length != width * height)
        {
            _pixels = new uint[width * height];
        }

        Array.Fill(_pixels, backgroundPixel);
        if (powered)
        {
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int left = (1 + (column * 6)) * pitch;
                    int top = (1 + (row * 9)) * pitch;
                    for (int line = 0; line < 8; line++)
                    {
                        int index = (((row * 8) + line) * columns) + column;
                        byte bits = on && index < frame!.Dots.Length ? frame.Dots[index] : (byte)0;
                        for (int x = 0; x < 5; x++)
                        {
                            uint pixel = (bits & (0x10 >> x)) != 0 ? onPixel : offPixel;
                            Fill(left + (x * pitch), top + (line * pitch), dot, width, pixel);
                        }
                    }
                }
            }
        }

        bool created = _bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height;
        if (created)
        {
            _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        }

        _bitmap!.WritePixels(new Int32Rect(0, 0, width, height), _pixels, width * 4, 0);
        return created;
    }

    /// <summary>Glass and dot colors: blue modules show light dots on blue, green ones dark dots on yellow-green.</summary>
    private static (Color Background, Color Text) Palette(string backlight, bool lit) => backlight switch
    {
        "green" => lit ? (Rgb(0xA8D13C), Rgb(0x18220C)) : (Rgb(0x6B7A3A), Rgb(0x18220C)),
        "none" => (Rgb(0xA2AA92), Rgb(0x20241A)),
        _ => lit ? (Rgb(0x2A55EA), Rgb(0xEEF4FF)) : (Rgb(0x111C46), Rgb(0x6F7CB0)),
    };

    private void Fill(int x, int y, int size, int width, uint pixel)
    {
        for (int dy = 0; dy < size; dy++)
        {
            Array.Fill(_pixels, pixel, ((y + dy) * width) + x, size);
        }
    }

    private static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb((byte)(a.R + ((b.R - a.R) * t)), (byte)(a.G + ((b.G - a.G) * t)), (byte)(a.B + ((b.B - a.B) * t)));
    }

    private static uint Pixel(Color c) => 0xFF000000u | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

    private static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
