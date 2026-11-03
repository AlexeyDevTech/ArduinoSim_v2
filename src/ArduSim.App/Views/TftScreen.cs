using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArduSim.Ipc;

namespace ArduSim.App.Views;

/// <summary>
/// The glass of a color TFT or of a graphic LCD (Nokia 5110) part (docs/UI.md §3.6). The bitmap has
/// a whole number of device pixels per display pixel at the current zoom (with a one-pixel gap from 4
/// on) and is stretched over the glass with nearest-neighbor sampling. A TFT shows its pixels scaled
/// by the backlight (dark without one); the LCD's pixels darken a gray-green glass, which the
/// backlight brightens.
/// </summary>
public sealed class TftScreen
{
    private const int MaxPitch = 6;

    private static readonly Color LcdGlass = Color.FromRgb(0x9C, 0xA6, 0x8E);
    private static readonly Color LcdLit = Color.FromRgb(0xC4, 0xDD, 0xEC);
    private static readonly Color LcdInk = Color.FromRgb(0x1C, 0x22, 0x1E);
    private static readonly Color TftOff = Color.FromRgb(0x0E, 0x10, 0x14);

    private WriteableBitmap? _bitmap;
    private uint[] _pixels = [];

    public ImageSource? Source => _bitmap;

    /// <summary>Redraws the glass; true when <see cref="Source"/> is a new bitmap.</summary>
    /// <param name="graphicLcd">A PCD8544 glass rather than a TFT.</param>
    /// <param name="pitchUnits">Display pixel pitch in canvas units.</param>
    public bool Render(DisplayData? frame, int columns, int rows, bool graphicLcd, double pixelsPerUnit, double pitchUnits)
    {
        if (frame is not null)
        {
            columns = frame.Columns;
            rows = frame.Rows;
        }

        int pitch = Math.Clamp((int)Math.Round(pitchUnits * pixelsPerUnit), 1, MaxPitch);
        int gap = pitch >= 4 ? 1 : 0;
        int width = columns * pitch;
        int height = rows * pitch;
        if (_pixels.Length != width * height)
        {
            _pixels = new uint[width * height];
        }

        double light = frame is not null && (frame.Status & 1) != 0 ? Math.Clamp(frame.Contrast, 0, 1) : 0;
        if (graphicLcd)
        {
            Color glass = Mix(LcdGlass, LcdLit, light);
            uint background = Pixel(glass);
            Array.Fill(_pixels, background);
            if (frame is not null)
            {
                for (int y = 0; y < rows; y++)
                {
                    for (int x = 0; x < columns; x++)
                    {
                        int index = (y * columns) + x;
                        if (index < frame.Dots.Length && frame.Dots[index] > 0)
                        {
                            Fill(x, y, pitch, gap, width, Pixel(Mix(glass, LcdInk, frame.Dots[index] / 255.0)));
                        }
                    }
                }
            }
        }
        else
        {
            uint off = Pixel(TftOff);
            Array.Fill(_pixels, off);
            if (frame is not null && light > 0)
            {
                for (int y = 0; y < rows; y++)
                {
                    for (int x = 0; x < columns; x++)
                    {
                        int index = ((y * columns) + x) * 3;
                        if (index + 2 < frame.Dots.Length)
                        {
                            Color shown = Color.FromRgb(frame.Dots[index], frame.Dots[index + 1], frame.Dots[index + 2]);
                            Fill(x, y, pitch, gap, width, Pixel(Mix(TftOff, shown, light)));
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

    private void Fill(int x, int y, int pitch, int gap, int width, uint color)
    {
        for (int dy = 0; dy < pitch - gap; dy++)
        {
            Array.Fill(_pixels, color, (((y * pitch) + dy) * width) + (x * pitch), pitch - gap);
        }
    }

    private static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
        (byte)Math.Round(from.R + ((to.R - from.R) * amount)),
        (byte)Math.Round(from.G + ((to.G - from.G) * amount)),
        (byte)Math.Round(from.B + ((to.B - from.B) * amount)));

    private static uint Pixel(Color c) => 0xFF000000u | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
}
