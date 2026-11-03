namespace ArduSim.Core.Displays;

/// <summary>
/// A display frame as an 8-bit grayscale image, for the headless <c>--dump-display</c> and tests:
/// OLED and LED-matrix pixels 1:1 (lit 255, dark 0); a character LCD with every 5x8 dot as one pixel
/// and a one-pixel gap between cells (lit 255, unlit dot 64, background 24); seven-segment digits as
/// one row of 8 pixels per digit (a-g, dp) at their brightness; RGB LEDs 1:1 at their luminance.
/// </summary>
public sealed record DisplayImage(int Width, int Height, byte[] Pixels)
{
    public const byte Lit = 255;
    public const byte Unlit = 64;
    public const byte Background = 24;

    public byte this[int x, int y] => Pixels[(y * Width) + x];

    public static DisplayImage From(DisplayFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return frame.Kind switch
        {
            DisplayKind.Oled or DisplayKind.LedMatrix => Oled(frame),
            DisplayKind.Segments => new DisplayImage(frame.Columns * 8, 1, frame.Dots.ToArray()),
            DisplayKind.RgbLeds => Rgb(frame),
            _ => Lcd(frame),
        };
    }

    private static DisplayImage Rgb(DisplayFrame frame)
    {
        ReadOnlySpan<byte> rgb = frame.Dots.Span;
        var pixels = new byte[frame.Columns * frame.Rows];
        for (int i = 0; i < pixels.Length && (3 * i) + 2 < rgb.Length; i++)
        {
            pixels[i] = (byte)Math.Round((0.2126 * rgb[3 * i]) + (0.7152 * rgb[(3 * i) + 1]) + (0.0722 * rgb[(3 * i) + 2]));
        }

        return new DisplayImage(frame.Columns, frame.Rows, pixels);
    }

    private static DisplayImage Oled(DisplayFrame frame)
    {
        int stride = (frame.Columns + 7) / 8;
        var pixels = new byte[frame.Columns * frame.Rows];
        ReadOnlySpan<byte> dots = frame.Dots.Span;
        for (int y = 0; y < frame.Rows; y++)
        {
            for (int x = 0; x < frame.Columns; x++)
            {
                if ((dots[(y * stride) + (x / 8)] & (0x80 >> (x % 8))) != 0)
                {
                    pixels[(y * frame.Columns) + x] = Lit;
                }
            }
        }

        return new DisplayImage(frame.Columns, frame.Rows, pixels);
    }

    private static DisplayImage Lcd(DisplayFrame frame)
    {
        int width = (frame.Columns * 6) + 1;
        int height = (frame.Rows * 9) + 1;
        var pixels = new byte[width * height];
        Array.Fill(pixels, Background);
        bool powered = frame.Status.HasFlag(DisplayStatus.Powered);
        ReadOnlySpan<byte> dots = frame.Dots.Span;
        for (int row = 0; row < frame.Rows; row++)
        {
            for (int column = 0; column < frame.Columns; column++)
            {
                for (int line = 0; line < 8; line++)
                {
                    byte bits = dots[(((row * 8) + line) * frame.Columns) + column];
                    for (int dot = 0; dot < 5; dot++)
                    {
                        int x = 1 + (column * 6) + dot;
                        int y = 1 + (row * 9) + line;
                        pixels[(y * width) + x] = !powered ? Background : (bits & (0x10 >> dot)) != 0 ? Lit : Unlit;
                    }
                }
            }
        }

        return new DisplayImage(width, height, pixels);
    }
}
