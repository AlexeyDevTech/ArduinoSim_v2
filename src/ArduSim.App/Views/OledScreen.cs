using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArduSim.Ipc;

namespace ArduSim.App.Views;

/// <summary>
/// The glass of one OLED part (docs/UI.md §3.6): lit pixels in the panel's color (white, blue, or
/// yellow on the top 16 rows of a two-tone 128x64) at their gray level (SSD1327), contrast as brightness, black when off. Every
/// OLED pixel is a whole number of device pixels at the current zoom, so the WriteableBitmap is
/// shown 1:1 with nearest-neighbor sampling (brief §3.5); the glow is a blur effect in the template.
/// </summary>
public sealed class OledScreen
{
    /// <summary>Pixel pitch in canvas units (0.17 mm, as on a 0.96" 128x64 module).</summary>
    public const double PixelPitch = 0.67;

    private const int MaxPitch = 8;

    /// <summary>DisplayKind.GrayOled: one level byte per pixel.</summary>
    private const byte GrayKind = 6;

    private WriteableBitmap? _bitmap;
    private uint[] _pixels = [];

    public ImageSource? Source => _bitmap;

    /// <summary>Size of the picture in canvas units; it follows the zoom by whole pixels.</summary>
    public double Width { get; private set; }

    public double Height { get; private set; }

    /// <summary>Redraws the glass; true when <see cref="Source"/> is a new bitmap.</summary>
    /// <param name="panel"><c>white</c>, <c>blue</c> or <c>yellow-blue</c>.</param>
    public bool Render(DisplayData? frame, int columns, int rows, string panel, double pixelsPerUnit, double pitchUnits = PixelPitch)
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
        double upscale = Math.Max(1, Math.Round(pitchUnits * pixelsPerUnit / pitch));
        Width = width * upscale / pixelsPerUnit;
        Height = height * upscale / pixelsPerUnit;

        bool on = frame is not null && (frame.Status & 3) == 3;
        double brightness = on ? 0.35 + (0.65 * Math.Clamp(frame!.Contrast, 0, 1)) : 0;
        const uint dark = 0; // transparent: the glass shows through, and only lit pixels glow
        uint lower = Pixel(Scale(panel == "white" ? Rgb(0xF0F5FF) : Rgb(0x48AEFF), brightness));
        uint upper = panel == "yellow-blue" && rows == 64 ? Pixel(Scale(Rgb(0xFFD447), brightness)) : lower;

        if (_pixels.Length != width * height)
        {
            _pixels = new uint[width * height];
        }

        Array.Fill(_pixels, dark);
        if (on && frame!.Kind == GrayKind)
        {
            Color lowerColor = Scale(panel == "white" ? Rgb(0xF0F5FF) : Rgb(0x48AEFF), brightness);
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    int index = (y * columns) + x;
                    if (index < frame.Dots.Length && frame.Dots[index] > 0)
                    {
                        uint level = Pixel(Scale(lowerColor, frame.Dots[index] / 255.0));
                        for (int dy = 0; dy < pitch - gap; dy++)
                        {
                            Array.Fill(_pixels, level, (((y * pitch) + dy) * width) + (x * pitch), pitch - gap);
                        }
                    }
                }
            }
        }
        else if (on)
        {
            int stride = (columns + 7) / 8;
            for (int y = 0; y < rows; y++)
            {
                uint lit = y < 16 ? upper : lower;
                for (int x = 0; x < columns; x++)
                {
                    int index = (y * stride) + (x / 8);
                    if (index < frame!.Dots.Length && (frame.Dots[index] & (0x80 >> (x % 8))) != 0)
                    {
                        for (int dy = 0; dy < pitch - gap; dy++)
                        {
                            Array.Fill(_pixels, lit, (((y * pitch) + dy) * width) + (x * pitch), pitch - gap);
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

    private static Color Scale(Color c, double factor) =>
        Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));

    private static uint Pixel(Color c) => 0xFF000000u | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

    private static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
