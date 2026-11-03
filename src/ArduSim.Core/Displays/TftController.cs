namespace ArduSim.Core.Displays;

public enum TftChip
{
    St7735,
    St7789,
    Ili9341,
}

/// <summary>
/// How a TFT module's glass sits on its controller's frame memory (GRAM): the GRAM size the chip is
/// strapped for, the glass size, the GRAM pixel behind the glass's top-left corner and the way the
/// glass runs through GRAM, the color order the panel is wired for, and whether it is an IPS panel
/// (normally black, so it needs the controller's inversion on to show true colors). The values come
/// from the rotation 0 the module's library uses (MADCTL mirror bits and column/row start), which
/// shows the picture upright on the real module.
/// </summary>
public sealed record TftPanel(
    TftChip Chip,
    int RamWidth,
    int RamHeight,
    int Width,
    int Height,
    int OriginColumn,
    int OriginRow,
    bool MirrorColumns,
    bool MirrorRows,
    bool Bgr,
    bool NormallyBlack)
{
    /// <summary>The modules, by the <c>module</c> property of the TFT parts.</summary>
    public static TftPanel For(string module) => module switch
    {
        "st7735-128x160-green" => Upright(TftChip.St7735, 132, 162, 128, 160, 2, 1, bgr: true),
        "st7735-128x128" => Upright(TftChip.St7735, 132, 132, 128, 128, 2, 3, bgr: true),
        "st7735-80x160" => Upright(TftChip.St7735, 128, 160, 80, 160, 24, 0, bgr: false),
        "st7735-128x160" or "st7735" => Upright(TftChip.St7735, 128, 160, 128, 160, 0, 0, bgr: false),
        "st7789-240x320" => Upright(TftChip.St7789, 240, 320, 240, 320, 0, 0, bgr: false, ips: true),
        "st7789-135x240" => Upright(TftChip.St7789, 240, 320, 135, 240, 53, 40, bgr: false, ips: true),
        "st7789-240x240" or "st7789" => Upright(TftChip.St7789, 240, 320, 240, 240, 0, 80, bgr: false, ips: true),
        _ => Upright(TftChip.Ili9341, 240, 320, 240, 320, 0, 0, bgr: true, mirrorRows: false),
    };

    /// <summary>The GRAM pixel (physical column and row) behind glass pixel (x, y).</summary>
    public (int Column, int Row) RamPixel(int x, int y) =>
        (MirrorColumns ? OriginColumn - x : OriginColumn + x, MirrorRows ? OriginRow - y : OriginRow + y);

    // Adafruit's rotation 0: MADCTL MX and MY (ILI9341: MX only) with its column and row start.
    private static TftPanel Upright(TftChip chip, int ramWidth, int ramHeight, int width, int height, int columnStart, int rowStart, bool bgr, bool ips = false, bool mirrorRows = true) =>
        new(chip, ramWidth, ramHeight, width, height, ramWidth - 1 - columnStart, mirrorRows ? ramHeight - 1 - rowStart : rowStart, true, mirrorRows, bgr, ips);
}

/// <summary>
/// The MIPI DCS controllers of small color TFTs: ST7735S (datasheet v1.1), ST7789V (v1.6) and
/// ILI9341 (v1.11), on the 4-wire serial interface, where D/C tells commands from their parameters.
/// GRAM of 18-bit pixels; sleep in/out, display on/off, normal/partial mode with its area, inversion,
/// idle (8 colors), MADCTL (MY, MX, MV and the RGB/BGR order), COLMOD 12/16/18-bit pixel writes,
/// CASET/RASET windows with RAMWR/RAMWRC, vertical scrolling (VSCRDEF/VSCSAD), the status reads
/// libraries print and the ILI9341 read index (0xD9). Gamma, power and frame-rate settings are
/// accepted and have no effect; multi-byte reads come byte-aligned (the dummy clock is not modelled).
/// </summary>
public sealed class TftController
{
    private const byte Madctl = 0x36;
    private const byte Colmod = 0x3A;

    private readonly byte[] _ram;
    private readonly byte[] _parameters = new byte[16];
    private readonly byte[] _pixel = new byte[3];
    private int _parameterCount;
    private byte _command;
    private bool _writing;
    private int _pixelBytes;
    private int _column;
    private int _row;
    private int _columnStart;
    private int _columnEnd;
    private int _rowStart;
    private int _rowEnd;
    private byte[] _response = [];
    private int _responseIndex;
    private int _readIndex;

    public TftController(TftChip chip, int ramWidth, int ramHeight)
    {
        Chip = chip;
        RamWidth = ramWidth;
        RamHeight = ramHeight;
        _ram = new byte[ramWidth * ramHeight * 3];
        Reset();
    }

    /// <summary>Something visible may have changed.</summary>
    public event Action? Changed;

    public TftChip Chip { get; }

    public int RamWidth { get; }

    public int RamHeight { get; }

    public bool Sleeping { get; private set; }

    public bool DisplayOn { get; private set; }

    public bool Inverted { get; private set; }

    public bool Idle { get; private set; }

    public bool Partial { get; private set; }

    public bool Scrolling { get; private set; }

    /// <summary>Memory access control: bit 7 MY, 6 MX, 5 MV, 4 ML, 3 BGR, 2 MH.</summary>
    public byte AccessControl { get; private set; }

    /// <summary>Interface pixel format; the low three bits give the bits per pixel of writes (3: 12, 5: 16, 6: 18).</summary>
    public byte PixelFormat { get; private set; }

    public int ScrollTop { get; private set; }

    public int ScrollHeight { get; private set; }

    public int ScrollStart { get; private set; }

    public int PartialStart { get; private set; }

    public int PartialEnd { get; private set; }

    /// <summary>Hardware reset (RESX low) or SWRESET: registers to their defaults; GRAM keeps its contents.</summary>
    public void Reset()
    {
        Sleeping = true;
        DisplayOn = false;
        Inverted = false;
        Idle = false;
        Partial = false;
        Scrolling = false;
        AccessControl = 0;
        PixelFormat = (byte)(Chip == TftChip.St7735 ? 0x06 : 0x66);
        ScrollTop = 0;
        ScrollHeight = RamHeight;
        ScrollStart = 0;
        PartialStart = 0;
        PartialEnd = RamHeight - 1;
        _columnStart = 0;
        _columnEnd = RamWidth - 1;
        _rowStart = 0;
        _rowEnd = RamHeight - 1;
        _column = 0;
        _row = 0;
        _writing = false;
        _parameterCount = 0;
        _pixelBytes = 0;
        _response = [];
        _readIndex = 0;
        Changed?.Invoke();
    }

    /// <summary>A byte with D/C low.</summary>
    public void Command(byte value)
    {
        _command = value;
        _parameterCount = 0;
        _pixelBytes = 0;
        _writing = false;
        _response = [];
        _responseIndex = 0;
        switch (value)
        {
            case 0x01: // SWRESET
                Reset();
                break;
            case 0x10 or 0x11: // SLPIN, SLPOUT
                Sleeping = value == 0x10;
                Changed?.Invoke();
                break;
            case 0x12: // PTLON
                Partial = true;
                Scrolling = false;
                Changed?.Invoke();
                break;
            case 0x13: // NORON
                Partial = false;
                Scrolling = false;
                Changed?.Invoke();
                break;
            case 0x20 or 0x21: // INVOFF, INVON
                Inverted = value == 0x21;
                Changed?.Invoke();
                break;
            case 0x28 or 0x29: // DISPOFF, DISPON
                DisplayOn = value == 0x29;
                Changed?.Invoke();
                break;
            case 0x2C: // RAMWR
                _column = _columnStart;
                _row = _rowStart;
                _writing = true;
                break;
            case 0x3C: // RAMWRC
                _writing = true;
                break;
            case 0x38 or 0x39: // IDMOFF, IDMON
                Idle = value == 0x39;
                Changed?.Invoke();
                break;
            default:
                PrepareRead(value);
                break;
        }
    }

    /// <summary>A byte with D/C high: a parameter of the last command, or pixel data after RAMWR.</summary>
    public void Data(byte value)
    {
        if (_writing)
        {
            WritePixelByte(value);
            return;
        }

        if (_parameterCount < _parameters.Length)
        {
            _parameters[_parameterCount] = value;
        }

        _parameterCount++;
        switch (_command)
        {
            case 0x2A when _parameterCount == 4: // CASET
                _columnStart = Word(0);
                _columnEnd = Word(2);
                break;
            case 0x2B when _parameterCount == 4: // RASET
                _rowStart = Word(0);
                _rowEnd = Word(2);
                break;
            case 0x30 when _parameterCount == 4: // PTLAR
                PartialStart = Word(0);
                PartialEnd = Word(2);
                Changed?.Invoke();
                break;
            case 0x33 when _parameterCount == 6: // VSCRDEF: top fixed area, scroll area, bottom fixed area
                ScrollTop = Math.Min(Word(0), RamHeight);
                ScrollHeight = Math.Min(Word(2), RamHeight - ScrollTop);
                Changed?.Invoke();
                break;
            case 0x37 when _parameterCount == 2: // VSCSAD
                ScrollStart = Word(0);
                Scrolling = true;
                Changed?.Invoke();
                break;
            case Madctl when _parameterCount == 1:
                AccessControl = value;
                Changed?.Invoke();
                break;
            case Colmod when _parameterCount == 1:
                PixelFormat = value;
                break;
            case 0xD9 when _parameterCount == 1 && Chip == TftChip.Ili9341: // SPI read index (undocumented)
                _readIndex = (value & 0x10) != 0 ? value & 0x0F : 0;
                break;
        }
    }

    /// <summary>What the controller shifts out on SDO for the next byte of a read command.</summary>
    public byte Read()
    {
        if (_responseIndex < _response.Length)
        {
            return _response[_responseIndex++];
        }

        return 0x00;
    }

    /// <summary>
    /// The glass as it looks with the backlight on: <paramref name="rgb"/> receives 3 bytes per pixel,
    /// row by row. Sleeping, off or outside the partial area, a TN panel is white and an IPS panel black.
    /// </summary>
    public void Render(TftPanel panel, Span<byte> rgb)
    {
        ArgumentNullException.ThrowIfNull(panel);
        bool blank = Sleeping || !DisplayOn;
        byte blankLevel = panel.NormallyBlack ? (byte)0 : (byte)255;
        bool swap = ((AccessControl & 0x08) != 0) != panel.Bgr;
        bool invert = Inverted != panel.NormallyBlack;
        for (int y = 0; y < panel.Height; y++)
        {
            for (int x = 0; x < panel.Width; x++)
            {
                int at = ((y * panel.Width) + x) * 3;
                (int column, int row) = panel.RamPixel(x, y);
                if (blank || column < 0 || column >= RamWidth || row < 0 || row >= RamHeight || (Partial && !InPartialArea(row)))
                {
                    rgb[at] = rgb[at + 1] = rgb[at + 2] = blankLevel;
                    continue;
                }

                int source = ((ScrolledRow(row) * RamWidth) + column) * 3;
                byte r = _ram[source], g = _ram[source + 1], b = _ram[source + 2];
                if (swap)
                {
                    (r, b) = (b, r);
                }

                if (Idle)
                {
                    r = (byte)(r >= 0x80 ? 255 : 0);
                    g = (byte)(g >= 0x80 ? 255 : 0);
                    b = (byte)(b >= 0x80 ? 255 : 0);
                }

                if (invert)
                {
                    r = (byte)~r;
                    g = (byte)~g;
                    b = (byte)~b;
                }

                rgb[at] = r;
                rgb[at + 1] = g;
                rgb[at + 2] = b;
            }
        }
    }

    private int Word(int index) => (_parameters[index] << 8) | _parameters[index + 1];

    private bool InPartialArea(int row) =>
        PartialStart <= PartialEnd ? row >= PartialStart && row <= PartialEnd : row >= PartialStart || row <= PartialEnd;

    // The scroll area shows memory from VSP on, wrapping inside the area (datasheet: vertical scrolling).
    private int ScrolledRow(int row)
    {
        if (!Scrolling || ScrollHeight <= 0 || row < ScrollTop || row >= ScrollTop + ScrollHeight)
        {
            return row;
        }

        int offset = ((row - ScrollTop) + (ScrollStart - ScrollTop)) % ScrollHeight;
        return ScrollTop + (offset < 0 ? offset + ScrollHeight : offset);
    }

    private void WritePixelByte(byte value)
    {
        _pixel[_pixelBytes++] = value;
        switch (PixelFormat & 0x07)
        {
            case 0x03 when _pixelBytes == 3: // 12-bit: two pixels in three bytes
                Store(Expand4(_pixel[0] >> 4), Expand4(_pixel[0] & 0x0F), Expand4(_pixel[1] >> 4));
                Store(Expand4(_pixel[1] & 0x0F), Expand4(_pixel[2] >> 4), Expand4(_pixel[2] & 0x0F));
                _pixelBytes = 0;
                break;
            case 0x05 when _pixelBytes == 2: // 16-bit RGB565, high byte first
                Store(Expand5(_pixel[0] >> 3), Expand6(((_pixel[0] & 0x07) << 3) | (_pixel[1] >> 5)), Expand5(_pixel[1] & 0x1F));
                _pixelBytes = 0;
                break;
            case not (0x03 or 0x05) when _pixelBytes == 3: // 18-bit (and 24-bit, truncated): six bits of each byte
                Store(Expand6(_pixel[0] >> 2), Expand6(_pixel[1] >> 2), Expand6(_pixel[2] >> 2));
                _pixelBytes = 0;
                break;
        }
    }

    private void Store(byte r, byte g, byte b)
    {
        (int column, int row) = Physical(_column, _row);
        if (column >= 0 && column < RamWidth && row >= 0 && row < RamHeight)
        {
            int at = ((row * RamWidth) + column) * 3;
            _ram[at] = r;
            _ram[at + 1] = g;
            _ram[at + 2] = b;
            Changed?.Invoke();
        }

        if (++_column > _columnEnd)
        {
            _column = _columnStart;
            if (++_row > _rowEnd)
            {
                _row = _rowStart;
            }
        }
    }

    // MV exchanges the logical column and row first; MX then mirrors the physical column, MY the physical row.
    private (int Column, int Row) Physical(int column, int row)
    {
        if ((AccessControl & 0x20) != 0)
        {
            (column, row) = (row, column);
        }

        if ((AccessControl & 0x40) != 0)
        {
            column = RamWidth - 1 - column;
        }

        if ((AccessControl & 0x80) != 0)
        {
            row = RamHeight - 1 - row;
        }

        return (column, row);
    }

    private void PrepareRead(byte command)
    {
        byte powerMode = (byte)((Sleeping ? 0 : 0x90) | (Idle ? 0x40 : 0) | (Partial ? 0x20 : 0x08) | (DisplayOn ? 0x04 : 0));
        byte[] answer = command switch
        {
            0x04 => Chip switch // RDDID: a dummy byte, then the three ID bytes
            {
                TftChip.St7735 => [0x00, 0x7C, 0x89, 0xF0],
                TftChip.St7789 => [0x00, 0x85, 0x85, 0x52],
                _ => [0x00, 0x00, 0x00, 0x00],
            },
            0x09 => [0x00, powerMode, AccessControl, PixelFormat, (byte)(Inverted ? 0x20 : 0)], // RDDST
            0x0A => [powerMode],                                   // RDDPM
            0x0B => [AccessControl],                               // RDDMADCTL
            0x0C => [PixelFormat],                                 // RDDCOLMOD
            0x0D => [(byte)((Scrolling ? 0x80 : 0) | (Inverted ? 0x20 : 0))], // RDDIM
            0x0E => [0x00],                                        // RDDSM
            0x0F => [(byte)(Sleeping ? 0x00 : 0xC0)],             // RDDSDR: register loading and functionality checks passed
            0xD3 when Chip == TftChip.Ili9341 => [0x00, 0x00, 0x93, 0x41], // ID4
            _ => [],
        };
        _response = answer;
        _responseIndex = Math.Min(_readIndex, answer.Length);
    }

    private static byte Expand4(int value) => (byte)((value << 4) | value);

    private static byte Expand5(int value) => (byte)((value << 3) | (value >> 2));

    private static byte Expand6(int value) => (byte)((value << 2) | (value >> 4));
}
