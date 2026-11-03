using ArduSim.Core.Timing;

namespace ArduSim.Core.Displays;

public enum OledChip
{
    Ssd1306,
    Sh1106,
    Ssd1309,
    Ssd1327,
}

/// <summary>What the OLED part needs from its controller (SSD1306/SH1106/SSD1309, or the grayscale SSD1327).</summary>
public interface IOledController
{
    event Action? Changed;

    /// <summary>A command byte the controller does not have (reported once per byte by the part).</summary>
    event Action<byte>? UnknownCommand;

    /// <summary><see cref="DisplayKind.Oled"/> (packed bits) or <see cref="DisplayKind.GrayOled"/> (a level byte per pixel).</summary>
    DisplayKind Kind { get; }

    bool DisplayOn { get; }

    byte Contrast { get; }

    /// <summary>The panel's supply is on (SSD1306 charge pump, SH1106 DC-DC; always for chips fed from the module's boost converter).</summary>
    bool PanelSupply { get; }

    bool ScrollActive { get; }

    ulong ScrollStepInterval { get; }

    byte Status { get; }

    void Reset();

    void Command(byte value);

    void Data(byte value);

    void ScrollStep();

    /// <summary>The glass in the layout of <see cref="Kind"/>.</summary>
    byte[] Render(OledPanel panel);
}

/// <summary>
/// How a module's glass is wired to the controller. Modules are mounted so that segment remap
/// (A1) and reversed COM scan (C8) give an upright picture, as every library initializes them;
/// other settings mirror or flip it, as on the real part.
/// </summary>
/// <param name="ColumnOffset">First RAM column shown (SH1106 modules: 2 of 132; 64x48 SSD1306: 32).</param>
/// <param name="AlternativeComPins">The COM pin layout the glass is wired for (0xDA 0x12), else sequential (0x02).</param>
public sealed record OledPanel(int Width, int Height, int ColumnOffset, bool AlternativeComPins)
{
    public static OledPanel For(OledChip chip, string size) => chip switch
    {
        OledChip.Sh1106 => new OledPanel(128, 64, 2, true),
        OledChip.Ssd1309 => new OledPanel(128, 64, 0, true),
        OledChip.Ssd1327 => new OledPanel(128, 128, 0, true),
        _ => size switch
        {
            "128x32" => new OledPanel(128, 32, 0, false),
            "64x48" => new OledPanel(64, 48, 32, true),
            "96x16" => new OledPanel(96, 16, 0, false),
            _ => new OledPanel(128, 64, 0, true),
        },
    };
}

/// <summary>
/// SSD1306, SH1106 and SSD1309 OLED controllers (ADR-006, ADR-013; datasheets SSD1306 rev 1.5,
/// SH1106 v2.3, SSD1309 rev 1.1): GDDRAM of 8 pages, the command set with its parameter bytes,
/// page/horizontal/vertical addressing (SSD1306, SSD1309), segment remap, COM scan direction and pin
/// layout, multiplex ratio, display offset and start line, contrast, inverse, entire-display-on,
/// display on/off, the charge pump (SSD1306) or DC-DC converter (SH1106), the scroll commands
/// (horizontal, vertical-and-horizontal, vertical area; on the SSD1309 with a column range and the
/// one-column content scroll) driven by the frame clock, and the SSD1309 command lock. The SSD1309
/// has no charge pump: its panel supply comes from the module.
/// </summary>
public sealed class OledController : IOledController
{
    private const int Pages = 8;
    private static readonly int[] ScrollIntervals = [5, 64, 128, 256, 3, 4, 25, 2];
    private static readonly int[] Ssd1309ScrollIntervals = [5, 64, 128, 256, 2, 3, 4, 1];

    private readonly byte[] _ram;
    private readonly List<byte> _parameters = [];
    private byte _command;
    private int _needed = -1;

    private int _column;
    private int _page;
    private int _addressingMode;
    private int _columnStart;
    private int _columnEnd;
    private int _pageStart;
    private int _pageEnd;

    private int _scrollKind;
    private int _scrollStartPage;
    private int _scrollEndPage;
    private int _scrollInterval;
    private int _scrollVerticalStep;
    private int _verticalScroll;
    private int _scrollAreaTop;
    private int _scrollAreaRows;
    private int _scrollStartColumn;
    private int _scrollEndColumn;
    private bool _scrollHorizontal;

    private int _divide;
    private int _oscillator;
    private int _phase1;
    private int _phase2;

    public OledController(OledChip chip)
    {
        if (chip == OledChip.Ssd1327)
        {
            throw new ArgumentException("The SSD1327 has its own controller model.", nameof(chip));
        }

        Chip = chip;
        RamColumns = chip == OledChip.Sh1106 ? 132 : 128;
        _ram = new byte[RamColumns * Pages];
        Reset();
    }

    /// <summary>Something visible may have changed.</summary>
    public event Action? Changed;

    /// <summary>
    /// A command byte this controller does not know. SSD1306 commands sent to an SH1106 (U8g2 and
    /// others share init code) are ignored silently, as the chip does, and do not raise it.
    /// </summary>
    public event Action<byte>? UnknownCommand;

    public OledChip Chip { get; }

    public DisplayKind Kind => DisplayKind.Oled;

    /// <summary>SSD1309: commands and RAM are locked (0xFD 0x16) until 0xFD 0x12.</summary>
    public bool Locked { get; private set; }

    public int RamColumns { get; }

    public ReadOnlySpan<byte> Ram => _ram;

    public bool DisplayOn { get; private set; }

    public byte Contrast { get; private set; }

    public bool Inverse { get; private set; }

    public bool EntireDisplayOn { get; private set; }

    public bool SegmentRemap { get; private set; }

    public bool ComScanReversed { get; private set; }

    public bool AlternativeComPins { get; private set; }

    public int Multiplex { get; private set; }

    public int DisplayOffset { get; private set; }

    public int StartLine { get; private set; }

    /// <summary>SSD1306: charge pump enabled (0x8D 0x14); SH1106: DC-DC converter on (0xAD 0x8B).</summary>
    public bool PanelSupply { get; private set; }

    public bool ScrollActive { get; private set; }

    /// <summary>As set by 0x20: 0 horizontal, 1 vertical, 2 page (the reset value).</summary>
    public int AddressingMode => _addressingMode;

    public int Column => _column;

    public int Page => _page;

    /// <summary>Frame rate from the oscillator, clock divide ratio, pre-charge phases and multiplex (datasheet §8.3).</summary>
    public double FrameRate { get; private set; }

    /// <summary>Virtual time between two scroll steps.</summary>
    public ulong ScrollStepInterval => (ulong)((Chip == OledChip.Ssd1309 ? Ssd1309ScrollIntervals : ScrollIntervals)[_scrollInterval] * (double)VirtualTime.Second / FrameRate);

    /// <summary>Power-on or RES reset: registers to their defaults (RAM keeps its contents, as on the chip).</summary>
    public void Reset()
    {
        DisplayOn = false;
        Contrast = (byte)(Chip == OledChip.Sh1106 ? 0x80 : 0x7F);
        Inverse = false;
        EntireDisplayOn = false;
        SegmentRemap = false;
        ComScanReversed = false;
        AlternativeComPins = true;
        Multiplex = 64;
        DisplayOffset = 0;
        StartLine = 0;
        PanelSupply = Chip != OledChip.Ssd1306;
        ScrollActive = false;
        Locked = false;
        _addressingMode = 2;
        _columnStart = 0;
        _columnEnd = RamColumns - 1;
        _pageStart = 0;
        _pageEnd = Pages - 1;
        _column = 0;
        _page = 0;
        _verticalScroll = 0;
        _scrollAreaTop = 0;
        _scrollAreaRows = 64;
        _scrollStartColumn = 0;
        _scrollEndColumn = RamColumns - 1;
        _scrollHorizontal = true;
        _needed = -1;
        _parameters.Clear();
        SetTiming(divide: 1, oscillator: Chip == OledChip.Ssd1309 ? 7 : 8, phase1: 2, phase2: 2);
        Changed?.Invoke();
    }

    public void Command(byte value)
    {
        if (_needed > 0)
        {
            _parameters.Add(value);
            if (--_needed == 0)
            {
                Execute(_command, _parameters);
                _needed = -1;
                Changed?.Invoke();
            }

            return;
        }

        if (Locked && value != 0xFD)
        {
            return; // a locked SSD1309 takes nothing but the unlock command
        }

        int count = ParameterCount(value);
        if (count < 0)
        {
            if (Chip != OledChip.Sh1106 || !IsSsd1306Command(value))
            {
                UnknownCommand?.Invoke(value);
            }

            return;
        }

        if (count == 0)
        {
            Execute(value, []);
            Changed?.Invoke();
            return;
        }

        _command = value;
        _needed = count;
        _parameters.Clear();
    }

    public void Data(byte value)
    {
        if (Locked)
        {
            return;
        }

        if (_page < Pages && _column < RamColumns)
        {
            _ram[(_page * RamColumns) + _column] = value;
        }

        Advance();
        Changed?.Invoke();
    }

    /// <summary>One scroll step: the scrolled pages move one column, and the vertical scroll moves on.</summary>
    public void ScrollStep()
    {
        if (!ScrollActive)
        {
            return;
        }

        if (_scrollHorizontal)
        {
            ShiftColumns(_scrollKind is 0x26 or 0x29);
        }

        if (_scrollKind is 0x29 or 0x2A && _scrollAreaRows > 0)
        {
            _verticalScroll = (_verticalScroll + _scrollVerticalStep) % _scrollAreaRows;
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// The glass, <c>Height</c> rows of <c>Width</c> pixels, packed MSB-left, <c>(Width + 7) / 8</c>
    /// bytes per row. Off, unpowered or out-of-multiplex rows are dark.
    /// </summary>
    public byte[] Render(OledPanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        int stride = (panel.Width + 7) / 8;
        var pixels = new byte[stride * panel.Height];
        if (!DisplayOn || !PanelSupply)
        {
            return pixels;
        }

        for (int y = 0; y < panel.Height; y++)
        {
            int ramRow = RamRowForGlassLine(panel, y);
            for (int x = 0; x < panel.Width; x++)
            {
                bool lit;
                if (EntireDisplayOn)
                {
                    lit = ramRow >= 0;
                }
                else
                {
                    lit = ramRow >= 0 && Pixel(RamColumnForGlassColumn(panel, x), ramRow);
                    lit ^= Inverse && ramRow >= 0;
                }

                if (lit)
                {
                    pixels[(y * stride) + (x / 8)] |= (byte)(0x80 >> (x % 8));
                }
            }
        }

        return pixels;
    }

    // The scrolled pages move one column inside the scroll's column range (the whole row but on the SSD1309).
    private void ShiftColumns(bool right)
    {
        int first = Math.Min(_scrollStartColumn, RamColumns - 1);
        int last = Math.Min(_scrollEndColumn, RamColumns - 1);
        if (last <= first)
        {
            return;
        }

        for (int page = _scrollStartPage; page <= Math.Min(_scrollEndPage, Pages - 1); page++)
        {
            Span<byte> row = _ram.AsSpan((page * RamColumns) + first, last - first + 1);
            if (right)
            {
                byte end = row[^1];
                row[..^1].CopyTo(row[1..]);
                row[0] = end;
            }
            else
            {
                byte start = row[0];
                row[1..].CopyTo(row);
                row[^1] = start;
            }
        }
    }

    /// <summary>Status byte for a read (bit 6 set while the display is off).</summary>
    public byte Status => (byte)(DisplayOn ? 0x00 : 0x40);

    private bool Pixel(int column, int row) =>
        column >= 0 && column < RamColumns && (_ram[((row / 8) * RamColumns) + column] & (1 << (row % 8))) != 0;

    private int RamColumnForGlassColumn(OledPanel panel, int x)
    {
        int segment = RamColumns - 1 - (x + panel.ColumnOffset);
        return SegmentRemap ? RamColumns - 1 - segment : segment;
    }

    /// <summary>The RAM row a glass line shows, or -1 when its COM is outside the multiplex ratio.</summary>
    private int RamRowForGlassLine(OledPanel panel, int y)
    {
        // The module wires glass line y to the pad that carries COM (Height - 1 - y) in its intended pin layout.
        int pad = PadForCom(panel.Height - 1 - y, panel.AlternativeComPins);
        int com = ComForPad(pad, AlternativeComPins);
        if (com >= Multiplex)
        {
            return -1;
        }

        int slot = ComScanReversed ? Multiplex - 1 - com : com;
        int line = (slot + DisplayOffset) % 64;
        if (_verticalScroll != 0 && line >= _scrollAreaTop && line < _scrollAreaTop + _scrollAreaRows)
        {
            line = _scrollAreaTop + ((line - _scrollAreaTop + _verticalScroll) % _scrollAreaRows);
        }

        return (StartLine + line) % 64;
    }

    // Alternative COM pins: COM0-31 on the even pad rows, COM32-63 on the odd ones.
    private static int PadForCom(int com, bool alternative) => !alternative ? com : com < 32 ? 2 * com : (2 * (com - 32)) + 1;

    private static int ComForPad(int pad, bool alternative) => !alternative ? pad : pad % 2 == 0 ? pad / 2 : 32 + (pad / 2);

    private static bool IsSsd1306Command(byte command) => command is 0x20 or 0x21 or 0x22 or 0x8D or 0xA3 or (>= 0x26 and <= 0x2F) or 0xD6;

    private int ParameterCount(byte command)
    {
        if (command <= 0x1F || (command >= 0x40 && command <= 0x7F) || (command >= 0xB0 && command <= 0xB7))
        {
            return 0;
        }

        switch (command)
        {
            case 0x81 or 0xA8 or 0xD3 or 0xD5 or 0xD9 or 0xDA or 0xDB:
                return 1;
            case 0xA0 or 0xA1 or 0xA4 or 0xA5 or 0xA6 or 0xA7 or 0xAE or 0xAF or 0xC0 or 0xC8 or 0xE3:
                return 0;
        }

        if (Chip == OledChip.Sh1106)
        {
            return command switch
            {
                0xAD => 1,
                >= 0x30 and <= 0x33 => 0, // pump voltage
                0xE0 or 0xEE => 0,        // read-modify-write
                _ => -1,
            };
        }

        if (Chip == OledChip.Ssd1309)
        {
            return command switch
            {
                0x20 or 0xDC or 0xFD => 1,
                0x21 or 0x22 or 0xA3 => 2,
                0x26 or 0x27 or 0x29 or 0x2A or 0x2C or 0x2D => 7,
                0x2E or 0x2F => 0,
                _ => -1, // no charge pump (0x8D) on the SSD1309
            };
        }

        return command switch
        {
            0x20 or 0x8D => 1,
            0x21 or 0x22 or 0xA3 => 2,
            0x26 or 0x27 => 6,
            0x29 or 0x2A => 5,
            0x2E or 0x2F => 0,
            _ => -1,
        };
    }

    private void Execute(byte command, List<byte> p)
    {
        switch (command)
        {
            case <= 0x0F:
                _column = (_column & 0xF0) | command;
                break;
            case <= 0x1F:
                _column = (_column & 0x0F) | ((command & 0x0F) << 4);
                break;
            case 0x20:
                _addressingMode = p[0] & 0x03;
                break;
            case 0x21:
                _columnStart = Math.Min(p[0] & 0x7F, RamColumns - 1);
                _columnEnd = Math.Min(p[1] & 0x7F, RamColumns - 1);
                _column = _columnStart;
                break;
            case 0x22:
                _pageStart = p[0] & 0x07;
                _pageEnd = p[1] & 0x07;
                _page = _pageStart;
                break;
            case 0x26 or 0x27:
                ScrollActive = false;
                _scrollKind = command;
                _scrollStartPage = p[1] & 0x07;
                _scrollInterval = p[2] & 0x07;
                _scrollEndPage = p[3] & 0x07;
                _scrollVerticalStep = 0;
                _scrollHorizontal = true;
                (_scrollStartColumn, _scrollEndColumn) = Chip == OledChip.Ssd1309 ? (p[5] & 0x7F, p[6] & 0x7F) : (0, RamColumns - 1);
                break;
            case 0x29 or 0x2A:
                ScrollActive = false;
                _scrollKind = command;
                _scrollStartPage = p[1] & 0x07;
                _scrollInterval = p[2] & 0x07;
                _scrollEndPage = p[3] & 0x07;
                _scrollVerticalStep = p[4] & 0x3F;
                _scrollHorizontal = Chip != OledChip.Ssd1309 || (p[0] & 0x01) != 0;
                (_scrollStartColumn, _scrollEndColumn) = Chip == OledChip.Ssd1309 ? (p[5] & 0x7F, p[6] & 0x7F) : (0, RamColumns - 1);
                break;
            case 0x2C or 0x2D: // SSD1309 content scroll: one column now
                _scrollStartPage = p[1] & 0x07;
                _scrollEndPage = p[3] & 0x07;
                (_scrollStartColumn, _scrollEndColumn) = (p[5] & 0x7F, p[6] & 0x7F);
                ShiftColumns(command == 0x2C);
                break;
            case 0x2E:
                ScrollActive = false;
                _verticalScroll = 0;
                break;
            case 0x2F:
                ScrollActive = _scrollKind != 0;
                break;
            case >= 0x30 and <= 0x33:
                break;
            case >= 0x40 and <= 0x7F:
                StartLine = command & 0x3F;
                break;
            case 0x81:
                Contrast = p[0];
                break;
            case 0x8D:
                PanelSupply = (p[0] & 0x04) != 0;
                break;
            case 0xA0 or 0xA1:
                SegmentRemap = command == 0xA1;
                break;
            case 0xA3:
                _scrollAreaTop = p[0] & 0x3F;
                _scrollAreaRows = Math.Clamp(p[1] & 0x7F, 0, 64 - _scrollAreaTop);
                break;
            case 0xA4 or 0xA5:
                EntireDisplayOn = command == 0xA5;
                break;
            case 0xA6 or 0xA7:
                Inverse = command == 0xA7;
                break;
            case 0xA8:
                Multiplex = Math.Clamp((p[0] & 0x3F) + 1, 16, 64);
                UpdateFrameRate();
                break;
            case 0xAD:
                PanelSupply = (p[0] & 0x01) != 0;
                break;
            case 0xAE or 0xAF:
                DisplayOn = command == 0xAF;
                break;
            case >= 0xB0 and <= 0xB7:
                _page = command & 0x07;
                break;
            case 0xC0 or 0xC8:
                ComScanReversed = command == 0xC8;
                break;
            case 0xD3:
                DisplayOffset = p[0] & 0x3F;
                break;
            case 0xD5:
                SetTiming((p[0] & 0x0F) + 1, p[0] >> 4, _phase1, _phase2);
                break;
            case 0xD9:
                SetTiming(_divide, _oscillator, Math.Max(1, p[0] & 0x0F), Math.Max(1, p[0] >> 4));
                break;
            case 0xDA:
                AlternativeComPins = (p[0] & 0x10) != 0;
                break;
            case 0xFD:
                Locked = (p[0] & 0x04) != 0;
                break;
        }
    }

    private void SetTiming(int divide, int oscillator, int phase1, int phase2)
    {
        _divide = divide;
        _oscillator = oscillator;
        _phase1 = phase1;
        _phase2 = phase2;
        UpdateFrameRate();
    }

    // Fosc is about 370 kHz at the reset setting 8 and grows with it; K = phase 1 + phase 2 + 50 DCLKs.
    private void UpdateFrameRate() =>
        FrameRate = (300_000 + (_oscillator * 8_750.0)) / (_divide * (_phase1 + _phase2 + 50) * (double)Multiplex);

    private void Advance()
    {
        switch (_addressingMode)
        {
            case 0: // horizontal
                if (++_column > _columnEnd)
                {
                    _column = _columnStart;
                    if (++_page > _pageEnd)
                    {
                        _page = _pageStart;
                    }
                }

                break;
            case 1: // vertical
                if (++_page > _pageEnd)
                {
                    _page = _pageStart;
                    if (++_column > _columnEnd)
                    {
                        _column = _columnStart;
                    }
                }

                break;
            default: // page: the column stops wrapping at the end of the RAM row
                if (++_column >= RamColumns)
                {
                    _column = Chip == OledChip.Sh1106 ? RamColumns - 1 : 0;
                }

                break;
        }
    }
}
