using ArduSim.Core.Timing;

namespace ArduSim.Core.Displays;

/// <summary>
/// The SSD1327 128x128 grayscale OLED controller (ADR-013; datasheet rev 1.8): GDDRAM of 128 rows of
/// 64 bytes, two 4-bit pixels each (the low nibble drives the first segment of the pair unless the
/// nibble remap swaps them), column and row windows with horizontal or vertical increment, the remap
/// bits (column, nibble, COM scan, COM split odd/even), start line, display offset, multiplex,
/// contrast, the four display modes, the gray scale table (B8, and the linear B9 table it starts
/// with), display on/off, the command lock and the horizontal scroll over a row and column range.
/// Commands and their parameters arrive with D/C low.
/// </summary>
public sealed class Ssd1327Controller : IOledController
{
    public const int Rows = 128;
    public const int ColumnBytes = 64;

    private static readonly int[] ScrollIntervals = [6, 32, 64, 256, 3, 4, 5, 2];

    private readonly byte[] _ram = new byte[ColumnBytes * Rows];
    private readonly int[] _grayTable = new int[16];
    private readonly List<byte> _parameters = [];
    private byte _command;
    private int _needed = -1;
    private int _column;
    private int _row;
    private int _columnStart;
    private int _columnEnd;
    private int _rowStart;
    private int _rowEnd;
    private int _scrollKind;
    private int _scrollStartRow;
    private int _scrollEndRow;
    private int _scrollStartColumn;
    private int _scrollEndColumn;
    private int _scrollInterval;
    private int _phase1;
    private int _phase2;
    private int _divide;
    private int _oscillator;

    public Ssd1327Controller() => Reset();

    public event Action? Changed;

    public event Action<byte>? UnknownCommand;

    public DisplayKind Kind => DisplayKind.GrayOled;

    public ReadOnlySpan<byte> Ram => _ram;

    public bool DisplayOn { get; private set; }

    public byte Contrast { get; private set; }

    /// <summary>The module supplies the panel's VCC.</summary>
    public bool PanelSupply => true;

    public bool ScrollActive { get; private set; }

    /// <summary>0xA0's value: bit 0 column remap, 1 nibble remap, 2 vertical increment, 4 COM remap, 6 COM split odd/even.</summary>
    public byte Remap { get; private set; }

    public int StartLine { get; private set; }

    public int DisplayOffset { get; private set; }

    public int Multiplex { get; private set; }

    /// <summary>0 normal, 1 all on (GS15), 2 all off (GS0), 3 inverse.</summary>
    public int Mode { get; private set; }

    public bool Locked { get; private set; }

    /// <summary>GS0-GS15 pulse widths in DCLKs (GS0 is always 0, off).</summary>
    public IReadOnlyList<int> GrayTable => _grayTable;

    public double FrameRate { get; private set; }

    public ulong ScrollStepInterval => (ulong)(ScrollIntervals[_scrollInterval] * (double)VirtualTime.Second / FrameRate);

    public byte Status => (byte)(DisplayOn ? 0x00 : 0x40);

    public void Reset()
    {
        DisplayOn = false;
        Contrast = 0x7F;
        ScrollActive = false;
        Remap = 0;
        StartLine = 0;
        DisplayOffset = 0;
        Multiplex = 128;
        Mode = 0;
        Locked = false;
        _columnStart = 0;
        _columnEnd = ColumnBytes - 1;
        _rowStart = 0;
        _rowEnd = Rows - 1;
        _column = 0;
        _row = 0;
        _scrollKind = 0;
        _needed = -1;
        _parameters.Clear();
        LinearGrayTable();
        SetTiming(phase1: 4, phase2: 7, divide: 1, oscillator: 0);
        Changed?.Invoke();
    }

    public void Command(byte value)
    {
        if (_needed > 0)
        {
            _parameters.Add(value);
            if (--_needed == 0)
            {
                _needed = -1;
                if (!Locked || _command == 0xFD)
                {
                    Execute(_command, _parameters);
                    Changed?.Invoke();
                }
            }

            return;
        }

        int count = ParameterCount(value);
        if (count < 0)
        {
            if (!Locked)
            {
                UnknownCommand?.Invoke(value);
            }

            return;
        }

        if (count == 0)
        {
            if (!Locked)
            {
                Execute(value, []);
                Changed?.Invoke();
            }

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

        _ram[(_row * ColumnBytes) + _column] = value;
        if ((Remap & 0x04) != 0)
        {
            if (++_row > _rowEnd)
            {
                _row = _rowStart;
                if (++_column > _columnEnd)
                {
                    _column = _columnStart;
                }
            }
        }
        else if (++_column > _columnEnd)
        {
            _column = _columnStart;
            if (++_row > _rowEnd)
            {
                _row = _rowStart;
            }
        }

        Changed?.Invoke();
    }

    /// <summary>One scroll step: the bytes of the scrolled rows move one column inside the column range.</summary>
    public void ScrollStep()
    {
        if (!ScrollActive)
        {
            return;
        }

        int first = _scrollStartColumn;
        int last = Math.Min(_scrollEndColumn, ColumnBytes - 1);
        if (last > first)
        {
            for (int row = _scrollStartRow; row <= Math.Min(_scrollEndRow, Rows - 1); row++)
            {
                Span<byte> bytes = _ram.AsSpan((row * ColumnBytes) + first, last - first + 1);
                if (_scrollKind == 0x26)
                {
                    byte end = bytes[^1];
                    bytes[..^1].CopyTo(bytes[1..]);
                    bytes[0] = end;
                }
                else
                {
                    byte start = bytes[0];
                    bytes[1..].CopyTo(bytes);
                    bytes[^1] = start;
                }
            }
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// The glass, <c>Height</c> lines of <c>Width</c> bytes: each pixel's light at full contrast,
    /// 0-255, from its gray level's pulse width relative to GS15 (GS1 at width 0 still gets its
    /// pre-charge and glows faintly). Off, or on a line outside the multiplex ratio, it is 0.
    /// </summary>
    public byte[] Render(OledPanel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        var pixels = new byte[panel.Width * panel.Height];
        if (!DisplayOn)
        {
            return pixels;
        }

        Span<byte> light = stackalloc byte[16];
        double full = _grayTable[15] + 1;
        for (int level = 1; level < 16; level++)
        {
            light[level] = (byte)Math.Round(Math.Clamp((_grayTable[level] + 1) / full, 0, 1) * 255);
        }

        for (int y = 0; y < panel.Height; y++)
        {
            int row = RamRowForGlassLine(y);
            if (row < 0)
            {
                continue;
            }

            for (int x = 0; x < panel.Width; x++)
            {
                int level = Mode switch
                {
                    1 => 15,
                    2 => 0,
                    3 => 15 - Level(row, x),
                    _ => Level(row, x),
                };
                pixels[(y * panel.Width) + x] = light[level];
            }
        }

        return pixels;
    }

    // The module wires glass column x to SEG (127 - x) and glass line y to the COM pad that carries
    // COM (127 - y) with COM split on, so that the libraries' setting (A0 0x51: column remap, COM
    // remap, COM split) shows RAM upright, as on the real modules.
    private int Level(int row, int x)
    {
        int segment = 127 - x;
        int column = (Remap & 0x01) != 0 ? ColumnBytes - 1 - (segment / 2) : segment / 2;
        bool high = (segment % 2 == 1) != ((Remap & 0x02) != 0);
        byte value = _ram[(row * ColumnBytes) + column];
        return high ? value >> 4 : value & 0x0F;
    }

    private int RamRowForGlassLine(int y)
    {
        int pad = PadForCom(127 - y, split: true);
        int com = ComForPad(pad, (Remap & 0x40) != 0);
        int scan = (Remap & 0x10) != 0 ? 127 - com : com;
        int line = (scan + DisplayOffset) % Rows;
        return line >= Multiplex ? -1 : (StartLine + line) % Rows;
    }

    // COM split odd/even: COM0-63 drive the even pad rows, COM64-127 the odd ones.
    private static int PadForCom(int com, bool split) => !split ? com : com < 64 ? 2 * com : (2 * (com - 64)) + 1;

    private static int ComForPad(int pad, bool split) => !split ? pad : pad % 2 == 0 ? pad / 2 : 64 + (pad / 2);

    private static int ParameterCount(byte command) => command switch
    {
        0x15 or 0x75 => 2,
        0x81 or 0xA0 or 0xA1 or 0xA2 or 0xA8 or 0xAB or 0xB1 or 0xB3 or 0xB5 or 0xB6 or 0xBC or 0xBE or 0xD5 or 0xFD => 1,
        0xB8 => 15,
        0x26 or 0x27 => 7,
        0x2E or 0x2F or (>= 0xA4 and <= 0xA7) or 0xAE or 0xAF or 0xB9 or (>= 0x84 and <= 0x86) or 0xB2 or 0xBB => 0,
        _ => -1,
    };

    private void Execute(byte command, List<byte> p)
    {
        switch (command)
        {
            case 0x15:
                _columnStart = p[0] & 0x3F;
                _columnEnd = p[1] & 0x3F;
                _column = _columnStart;
                break;
            case 0x75:
                _rowStart = p[0] & 0x7F;
                _rowEnd = p[1] & 0x7F;
                _row = _rowStart;
                break;
            case 0x26 or 0x27:
                ScrollActive = false;
                _scrollKind = command;
                _scrollStartRow = p[1] & 0x7F;
                _scrollInterval = p[2] & 0x07;
                _scrollEndRow = p[3] & 0x7F;
                _scrollStartColumn = p[4] & 0x3F;
                _scrollEndColumn = p[5] & 0x3F;
                break;
            case 0x2E:
                ScrollActive = false;
                break;
            case 0x2F:
                ScrollActive = _scrollKind != 0;
                break;
            case 0x81:
                Contrast = p[0];
                break;
            case 0xA0:
                Remap = p[0];
                break;
            case 0xA1:
                StartLine = p[0] & 0x7F;
                break;
            case 0xA2:
                DisplayOffset = p[0] & 0x7F;
                break;
            case >= 0xA4 and <= 0xA7:
                Mode = command - 0xA4;
                break;
            case 0xA8:
                Multiplex = Math.Clamp((p[0] & 0x7F) + 1, 16, 128);
                UpdateFrameRate();
                break;
            case 0xAE or 0xAF:
                DisplayOn = command == 0xAF;
                break;
            case 0xB1:
                SetTiming(Math.Max(1, p[0] & 0x0F), Math.Max(1, p[0] >> 4), _divide, _oscillator);
                break;
            case 0xB3:
                SetTiming(_phase1, _phase2, (p[0] & 0x0F) + 1, p[0] >> 4);
                break;
            case 0xB8:
                for (int level = 1; level < 16; level++)
                {
                    _grayTable[level] = p[level - 1] & 0x3F;
                }

                UpdateFrameRate();
                break;
            case 0xB9:
                LinearGrayTable();
                UpdateFrameRate();
                break;
            case 0xFD:
                Locked = (p[0] & 0x04) != 0;
                break;
        }
    }

    // B9: GS0 = GS1 = 0, then 2, 4, ... 28.
    private void LinearGrayTable()
    {
        _grayTable[0] = 0;
        for (int level = 1; level < 16; level++)
        {
            _grayTable[level] = 2 * (level - 1);
        }
    }

    private void SetTiming(int phase1, int phase2, int divide, int oscillator)
    {
        _phase1 = phase1;
        _phase2 = phase2;
        _divide = divide;
        _oscillator = oscillator;
        UpdateFrameRate();
    }

    // Ffrm = Fosc / (D x K x MUX), K = phase 1 + phase 2 + 2 + GS15 (datasheet §7.3). Fosc is an
    // estimate: about 410 kHz at setting 0 (Adafruit's 0xB3 0x00 with 0xB1 0x11 is "100 Hz").
    private void UpdateFrameRate()
    {
        double fosc = 410_000 * (1 + (0.05 * _oscillator));
        FrameRate = fosc / (_divide * (_phase1 + _phase2 + 2 + _grayTable[15]) * (double)Multiplex);
    }
}
