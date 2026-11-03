using System.Windows;
using System.Windows.Media;
using ArduSim.Ipc;

namespace ArduSim.App.Views;

/// <summary>
/// Vector pictures of LED displays from their frames (ADR-012): seven-segment digits (direct, multiplexed
/// or TM1637), MAX7219 LED matrices and WS2812 LEDs. Each picture covers the part's whole box in part
/// units, so the template shows it 1:1 at any zoom.
/// </summary>
internal static class LedScreens
{
    private const double CellWidth = 24;
    private const double CellHeight = 36;

    // Segment bars a-g in a 24 x 36 cell, and the decimal point.
    private static readonly Rect[] Bars =
    [
        new(5, 1, 12, 3.4), new(16.6, 4, 3.4, 13), new(16.6, 19, 3.4, 13), new(5, 31.6, 12, 3.4),
        new(2, 19, 3.4, 13), new(2, 4, 3.4, 13), new(5, 16.3, 12, 3.4),
    ];

    /// <summary>Seven-segment digits drawn into <paramref name="area"/>; <paramref name="colon"/> puts TM1637's colon after the second digit.</summary>
    public static DrawingImage Segments(DisplayData? frame, int digits, Color color, bool colon, Size part, Rect area)
    {
        var group = Canvas(part);
        double scale = Math.Min(area.Width / (digits * CellWidth), area.Height / CellHeight);
        double left = area.Left + ((area.Width - (digits * CellWidth * scale)) / 2);
        double top = area.Top + ((area.Height - (CellHeight * scale)) / 2);
        byte[] levels = frame?.Dots ?? [];
        int brightest = levels.Length == 0 ? 0 : levels.Max();
        for (int digit = 0; digit < digits; digit++)
        {
            for (int segment = 0; segment < 8; segment++)
            {
                int index = (digit * 8) + segment;
                byte level = index < levels.Length ? levels[index] : (byte)0;
                Brush brush = Lit(color, level, brightest);
                double x = left + (digit * CellWidth * scale);
                if (segment < 7)
                {
                    Rect bar = Bars[segment];
                    var rect = new Rect(x + (bar.X * scale), top + (bar.Y * scale), bar.Width * scale, bar.Height * scale);
                    group.Children.Add(new GeometryDrawing(brush, null, new RectangleGeometry(rect, 1.2 * scale, 1.2 * scale)));
                }
                else if (colon && digit == 1)
                {
                    double cx = x + (CellWidth * scale);
                    group.Children.Add(new GeometryDrawing(brush, null, new EllipseGeometry(new Point(cx, top + (11 * scale)), 1.8 * scale, 1.8 * scale)));
                    group.Children.Add(new GeometryDrawing(brush, null, new EllipseGeometry(new Point(cx, top + (25 * scale)), 1.8 * scale, 1.8 * scale)));
                }
                else if (!colon)
                {
                    group.Children.Add(new GeometryDrawing(brush, null, new EllipseGeometry(new Point(x + (22 * scale), top + (33.3 * scale)), 1.7 * scale, 1.7 * scale)));
                }
            }
        }

        return Freeze(group);
    }

    /// <summary>MAX7219 modules: 8 x 8 round LEDs each at a 10-unit pitch, starting at (5, 5).</summary>
    public static DrawingImage Matrix(DisplayData? frame, int columns, Color color, Size part)
    {
        var group = Canvas(part);
        int stride = (columns + 7) / 8;
        byte[] dots = frame?.Dots ?? [];
        double intensity = frame is null ? 0 : frame.Contrast;
        bool on = frame is not null && (frame.Status & 2) != 0;
        var off = new SolidColorBrush(Color.FromArgb(0xFF, (byte)(color.R / 6), (byte)(color.G / 6), (byte)(color.B / 6)));
        off.Freeze();
        Brush lit = Glow(color, 0.45 + (0.55 * intensity));
        for (int row = 0; row < 8; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int at = (row * stride) + (column / 8);
                bool isLit = on && at < dots.Length && (dots[at] & (0x80 >> (column % 8))) != 0;
                group.Children.Add(new GeometryDrawing(isLit ? lit : off, null, new EllipseGeometry(new Point(10 + (column * 10), 10 + (row * 10)), 3.7, 3.7)));
            }
        }

        return Freeze(group);
    }

    /// <summary>WS2812 LEDs at the positions of a strip, a ring or an 8 x 8 matrix (see <see cref="LedCenters"/>).</summary>
    public static DrawingImage Rgb(DisplayData? frame, IReadOnlyList<Point> centers, double radius, Size part)
    {
        var group = Canvas(part);
        byte[] rgb = frame?.Dots ?? [];
        var dark = new SolidColorBrush(Color.FromRgb(0xE9, 0xEC, 0xF1));
        dark.Freeze();
        for (int i = 0; i < centers.Count; i++)
        {
            Color color = (3 * i) + 2 < rgb.Length ? Color.FromRgb(rgb[3 * i], rgb[(3 * i) + 1], rgb[(3 * i) + 2]) : Colors.Black;
            int peak = Math.Max(color.R, Math.Max(color.G, color.B));
            group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(centers[i].X - radius, centers[i].Y - radius, 2 * radius, 2 * radius), 1, 1)));
            if (peak > 0)
            {
                // The eye sees a dim LED as a small bright spot: full color, scaled by the square root of its level.
                double level = Math.Sqrt(peak / 255.0);
                var full = Color.FromRgb((byte)(color.R * 255 / peak), (byte)(color.G * 255 / peak), (byte)(color.B * 255 / peak));
                group.Children.Add(new GeometryDrawing(Glow(full, level), null, new EllipseGeometry(centers[i], radius * (0.6 + (0.9 * level)), radius * (0.6 + (0.9 * level)))));
            }
        }

        return Freeze(group);
    }

    /// <summary>Where a WS2812 part's LEDs sit, in part units, in data order (matrix row by row as seen).</summary>
    public static IReadOnlyList<Point> LedCenters(string layout, int count, Size part)
    {
        switch (layout)
        {
            case "ring":
                double radius = (part.Width / 2) - 12;
                return [.. Enumerable.Range(0, count).Select(i =>
                {
                    double angle = (-90 + (360.0 * i / count)) * Math.PI / 180;
                    return new Point((part.Width / 2) + (radius * Math.Cos(angle)), (part.Width / 2) + (radius * Math.Sin(angle)));
                })];
            case "matrix":
                return [.. Enumerable.Range(0, 64).Select(i => new Point(20 + (20 * (i % 8)), 20 + (20 * (i / 8))))];
            default:
                return [.. Enumerable.Range(0, count).Select(i => new Point(20 + (20 * i), 20))];
        }
    }

    private static SolidColorBrush Lit(Color color, byte level, int brightest)
    {
        if (level < 8 || brightest == 0)
        {
            return Fixed(Color.FromArgb(0x26, color.R, color.G, color.B));
        }

        double share = 0.3 + (0.7 * level / (double)brightest);
        return Fixed(Color.FromArgb((byte)(share * 255), color.R, color.G, color.B));
    }

    private static RadialGradientBrush Glow(Color color, double strength)
    {
        var brush = new RadialGradientBrush(
            [
                new GradientStop(Color.FromArgb(0xFF, (byte)Math.Min(255, color.R + 90), (byte)Math.Min(255, color.G + 90), (byte)Math.Min(255, color.B + 90)), 0),
                new GradientStop(Color.FromArgb((byte)(255 * Math.Clamp(strength, 0, 1)), color.R, color.G, color.B), 0.55),
                new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1),
            ]);
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Fixed(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    // A transparent box the size of the part, so the picture maps 1:1 onto part units.
    private static DrawingGroup Canvas(Size part)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, part.Width, part.Height))));
        return group;
    }

    private static DrawingImage Freeze(DrawingGroup group)
    {
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
}
