using System.Globalization;
using System.Text;
using ArduSim.Core.Timing;

namespace ArduSim.Core.Displays;

/// <summary>
/// The HD44780 dot-matrix LCD controller (ADR-006, datasheet HD44780U): the full instruction set,
/// 80 bytes of DDRAM with the 1- and 2-line address maps, 64 bytes of CGRAM (8 glyphs of 5x8),
/// 8-bit and 4-bit interfaces with nibble sequencing, and busy flag / address counter reads.
/// Execution times follow the datasheet at fosc = 270 kHz. Bus timing is the caller's: it hands
/// over what the data lines carried at the falling edge of E.
/// </summary>
public sealed class Hd44780
{
    public const ulong ShortExecution = 37 * VirtualTime.Microsecond;
    public const ulong LongExecution = 1_520 * VirtualTime.Microsecond;
    public const ulong DataExecution = ShortExecution + (4 * VirtualTime.Microsecond);

    private const int LineLength = 40;
    private const int OneLineLength = 80;

    private readonly Func<ulong> _clock;
    private readonly byte[] _ddram = new byte[128];
    private readonly byte[] _cgram = new byte[64];
    private bool _lowNibbleNext;
    private byte _highNibble;

    /// <param name="clock">Virtual time in nanoseconds, for the busy flag.</param>
    public Hd44780(Func<ulong> clock)
    {
        _clock = clock;
        Reset();
    }

    /// <summary>Something visible may have changed (RAM, display control, shift, cursor).</summary>
    public event Action? Changed;

    /// <summary>An instruction or data byte arrived while the busy flag was set; a real controller may drop it.</summary>
    public event Action? WrittenWhileBusy;

    /// <summary>Address counter: a DDRAM address (7 bits) or, after Set CGRAM address, a CGRAM address (6 bits).</summary>
    public int AddressCounter { get; private set; }

    public bool AddressesCgram { get; private set; }

    public bool Increment { get; private set; }

    public bool ShiftOnWrite { get; private set; }

    public bool DisplayOn { get; private set; }

    public bool CursorOn { get; private set; }

    public bool BlinkOn { get; private set; }

    public bool EightBitInterface { get; private set; }

    public bool TwoLines { get; private set; }

    public bool LargeFont { get; private set; }

    /// <summary>Display shift in positions (0..39 in 2-line mode, 0..79 in 1-line mode).</summary>
    public int DisplayShift { get; private set; }

    public ulong BusyUntil { get; private set; }

    public bool Busy => _clock() < BusyUntil;

    /// <summary>The 4-bit interface expects the low nibble next.</summary>
    public bool LowNibbleNext => _lowNibbleNext;

    public ReadOnlySpan<byte> Ddram => _ddram;

    public ReadOnlySpan<byte> Cgram => _cgram;

    /// <summary>Initialization by the internal reset circuit (power-on): 8-bit, 1 line, display off, increment, cleared.</summary>
    public void Reset()
    {
        Array.Fill(_ddram, (byte)0x20);
        Array.Clear(_cgram);
        AddressCounter = 0;
        AddressesCgram = false;
        Increment = true;
        ShiftOnWrite = false;
        DisplayOn = false;
        CursorOn = false;
        BlinkOn = false;
        EightBitInterface = true;
        TwoLines = false;
        LargeFont = false;
        DisplayShift = 0;
        BusyUntil = 0;
        _lowNibbleNext = false;
        Changed?.Invoke();
    }

    /// <summary>E fell with R/W low: <paramref name="lines"/> is DB7..DB0 (in 4-bit mode only DB7..DB4 count).</summary>
    public void Write(bool registerSelect, int lines)
    {
        byte value = (byte)lines;
        if (EightBitInterface)
        {
            _lowNibbleNext = false;
            Execute(registerSelect, value);
        }
        else if (!_lowNibbleNext)
        {
            _highNibble = (byte)(value & 0xF0);
            _lowNibbleNext = true;
        }
        else
        {
            _lowNibbleNext = false;
            Execute(registerSelect, (byte)(_highNibble | (value >> 4)));
        }
    }

    /// <summary>What the controller drives on DB7..DB0 while E is high with R/W high (in 4-bit mode the current nibble on DB7..DB4).</summary>
    public int ReadLines(bool registerSelect)
    {
        byte value = registerSelect ? PeekData() : Status;
        if (EightBitInterface)
        {
            return value;
        }

        return _lowNibbleNext ? (value << 4) & 0xF0 : value & 0xF0;
    }

    /// <summary>E fell with R/W high: completes the read; a data read moves the address counter.</summary>
    public void EndRead(bool registerSelect)
    {
        if (!EightBitInterface)
        {
            _lowNibbleNext = !_lowNibbleNext;
            if (_lowNibbleNext)
            {
                return;
            }
        }

        if (registerSelect)
        {
            MoveAddress(Increment ? 1 : -1);
            BusyUntil = _clock() + DataExecution;
        }
    }

    /// <summary>Busy flag and address counter, as read with RS low.</summary>
    public byte Status => (byte)((Busy ? 0x80 : 0) | (AddressCounter & 0x7F));

    /// <summary>The 5-bit dot rows of character <paramref name="code"/> (CGRAM for 0x00-0x0F, else the A00 ROM).</summary>
    public void Glyph(byte code, Span<byte> rows)
    {
        if (code < 0x10)
        {
            for (int r = 0; r < 8; r++)
            {
                rows[r] = (byte)(_cgram[((code & 7) * 8) + r] & 0x1F);
            }
        }
        else
        {
            Hd44780Font.A00(code, rows);
        }
    }

    /// <summary>The DDRAM address shown in a cell of a <paramref name="columns"/> x <paramref name="rows"/> glass, or -1 for a blank cell.</summary>
    public int CellAddress(int row, int column, int columns, int rows)
    {
        if (!TwoLines)
        {
            // 1-line mode drives only the first row of the glass.
            return row == 0 ? (column + DisplayShift) % OneLineLength : -1;
        }

        int line = row % 2;
        int position = column + (row >= 2 ? columns : 0);
        if (position >= LineLength)
        {
            return -1;
        }

        return (line * 0x40) + ((position + DisplayShift) % LineLength);
    }

    /// <summary>
    /// Renders the glass: <paramref name="rows"/> x 8 dot rows of <paramref name="columns"/> cells, each a
    /// 5-bit mask (bit 4 = leftmost dot), index <c>(row * 8 + line) * columns + column</c>. The cursor is the
    /// 8th dot row; a blinking cursor shows all dots while <paramref name="blinkPhase"/> is true.
    /// </summary>
    public byte[] Render(int columns, int rows, bool blinkPhase)
    {
        var dots = new byte[rows * 8 * columns];
        if (!DisplayOn)
        {
            return dots;
        }

        Span<byte> glyph = stackalloc byte[8];
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int address = CellAddress(row, column, columns, rows);
                if (address < 0)
                {
                    continue;
                }

                Glyph(_ddram[address], glyph);
                bool atCursor = !AddressesCgram && address == AddressCounter;
                if (atCursor && CursorOn)
                {
                    glyph[7] = 0x1F;
                }

                if (atCursor && BlinkOn && blinkPhase)
                {
                    glyph.Fill(0x1F);
                }

                for (int line = 0; line < 8; line++)
                {
                    dots[(((row * 8) + line) * columns) + column] = glyph[line];
                }
            }
        }

        return dots;
    }

    /// <summary>
    /// The visible characters of one row as text: printable ASCII as is, CGRAM glyphs as <c>\0</c>..<c>\7</c>,
    /// other codes as <c>\xNN</c> (0x5C, which is ¥ in the A00 ROM, and the double quote included). Blank rows are spaces.
    /// </summary>
    public string RowText(int row, int columns, int rows)
    {
        var text = new StringBuilder(columns);
        for (int column = 0; column < columns; column++)
        {
            int address = DisplayOn ? CellAddress(row, column, columns, rows) : -1;
            byte code = address < 0 ? (byte)0x20 : _ddram[address];
            if (code < 0x10)
            {
                text.Append('\\').Append((char)('0' + (code & 7)));
            }
            else if (code is >= 0x20 and < 0x7E && code != 0x5C && code != 0x22)
            {
                text.Append((char)code);
            }
            else
            {
                text.Append(CultureInfo.InvariantCulture, $"\\x{code:X2}");
            }
        }

        return text.ToString();
    }

    private byte PeekData() => AddressesCgram ? _cgram[AddressCounter & 0x3F] : _ddram[AddressCounter & 0x7F];

    private void Execute(bool registerSelect, byte value)
    {
        if (Busy)
        {
            WrittenWhileBusy?.Invoke();
        }

        ulong now = _clock();
        if (registerSelect)
        {
            WriteData(value);
            BusyUntil = now + DataExecution;
        }
        else
        {
            BusyUntil = now + Command(value);
        }

        Changed?.Invoke();
    }

    private ulong Command(byte value)
    {
        if ((value & 0x80) != 0)
        {
            // Set DDRAM address.
            AddressCounter = value & 0x7F;
            AddressesCgram = false;
        }
        else if ((value & 0x40) != 0)
        {
            // Set CGRAM address.
            AddressCounter = value & 0x3F;
            AddressesCgram = true;
        }
        else if ((value & 0x20) != 0)
        {
            // Function set: DL, N, F.
            EightBitInterface = (value & 0x10) != 0;
            TwoLines = (value & 0x08) != 0;
            LargeFont = (value & 0x04) != 0;
            _lowNibbleNext = false;
            DisplayShift %= TwoLines ? LineLength : OneLineLength;
        }
        else if ((value & 0x10) != 0)
        {
            // Cursor or display shift: S/C, R/L.
            int direction = (value & 0x04) != 0 ? 1 : -1;
            if ((value & 0x08) != 0)
            {
                ShiftDisplay(-direction);
            }
            else
            {
                MoveAddress(direction);
            }
        }
        else if ((value & 0x08) != 0)
        {
            // Display on/off control: D, C, B.
            DisplayOn = (value & 0x04) != 0;
            CursorOn = (value & 0x02) != 0;
            BlinkOn = (value & 0x01) != 0;
        }
        else if ((value & 0x04) != 0)
        {
            // Entry mode set: I/D, S.
            Increment = (value & 0x02) != 0;
            ShiftOnWrite = (value & 0x01) != 0;
        }
        else if ((value & 0x02) != 0)
        {
            // Return home.
            AddressCounter = 0;
            AddressesCgram = false;
            DisplayShift = 0;
            return LongExecution;
        }
        else if (value == 0x01)
        {
            // Clear display: spaces everywhere, address 0, increment mode, no shift.
            Array.Fill(_ddram, (byte)0x20);
            AddressCounter = 0;
            AddressesCgram = false;
            Increment = true;
            DisplayShift = 0;
            return LongExecution;
        }

        return ShortExecution;
    }

    private void WriteData(byte value)
    {
        if (AddressesCgram)
        {
            _cgram[AddressCounter & 0x3F] = value;
        }
        else
        {
            _ddram[AddressCounter & 0x7F] = value;
        }

        MoveAddress(Increment ? 1 : -1);
        if (ShiftOnWrite && !AddressesCgram)
        {
            // With S set the display moves with the cursor: left when incrementing, right when decrementing.
            ShiftDisplay(Increment ? 1 : -1);
        }
    }

    /// <summary>Shifts the display: +1 moves the contents left (the window right), -1 right.</summary>
    private void ShiftDisplay(int amount)
    {
        int length = TwoLines ? LineLength : OneLineLength;
        DisplayShift = (((DisplayShift + amount) % length) + length) % length;
    }

    private void MoveAddress(int delta)
    {
        if (AddressesCgram)
        {
            AddressCounter = (AddressCounter + delta) & 0x3F;
            return;
        }

        if (!TwoLines)
        {
            int position = AddressCounter >= OneLineLength ? 0 : AddressCounter;
            AddressCounter = (((position + delta) % OneLineLength) + OneLineLength) % OneLineLength;
            return;
        }

        // 2-line mode: 0x00-0x27 and 0x40-0x67; running off one line enters the other.
        int line = AddressCounter >= 0x40 ? 1 : 0;
        int column = AddressCounter & 0x3F;
        column += delta;
        if (column >= LineLength)
        {
            column = 0;
            line ^= 1;
        }
        else if (column < 0)
        {
            column = LineLength - 1;
            line ^= 1;
        }

        AddressCounter = (line * 0x40) + column;
    }
}
