namespace ArduSim.Core.Displays;

/// <summary>
/// The PCD8544 48x84 LCD controller of the Nokia 5110 module (Philips datasheet, 1999): DDRAM of six
/// banks of 84 vertical bytes (bit 0 at the top), the basic (H = 0) and extended (H = 1) instruction
/// sets with function set (power-down, vertical addressing), display control (blank, normal, all
/// segments on, inverse), X and Y addresses, and in the extended set temperature coefficient, bias
/// and V<sub>OP</sub>. RES clears the registers (power-down, blank, V<sub>OP</sub> 0); RAM keeps its
/// contents.
/// </summary>
public sealed class Pcd8544Controller
{
    public const int Columns = 84;
    public const int Banks = 6;

    private readonly byte[] _ram = new byte[Columns * Banks];

    public Pcd8544Controller() => Reset();

    /// <summary>Something visible may have changed.</summary>
    public event Action? Changed;

    public ReadOnlySpan<byte> Ram => _ram;

    public bool PowerDown { get; private set; }

    public bool VerticalAddressing { get; private set; }

    public bool ExtendedInstructions { get; private set; }

    /// <summary>Display control D and E bits: 0 blank, 1 all segments on, 2 normal, 3 inverse.</summary>
    public int Mode { get; private set; }

    /// <summary>Operating voltage setting, 0-127: V<sub>LCD</sub> = 3.06 V + 0.06 V x V<sub>OP</sub>.</summary>
    public int Vop { get; private set; }

    public int Bias { get; private set; }

    public int TemperatureCoefficient { get; private set; }

    public int X { get; private set; }

    public int Y { get; private set; }

    public void Reset()
    {
        PowerDown = true;
        VerticalAddressing = false;
        ExtendedInstructions = false;
        Mode = 0;
        Vop = 0;
        Bias = 0;
        TemperatureCoefficient = 0;
        X = 0;
        Y = 0;
        Changed?.Invoke();
    }

    public void Command(byte value)
    {
        if ((value & 0xF8) == 0x20)
        {
            PowerDown = (value & 0x04) != 0;
            VerticalAddressing = (value & 0x02) != 0;
            ExtendedInstructions = (value & 0x01) != 0;
        }
        else if (!ExtendedInstructions)
        {
            if ((value & 0x80) != 0)
            {
                X = Math.Min(value & 0x7F, Columns - 1);
            }
            else if ((value & 0xF8) == 0x40)
            {
                Y = Math.Min(value & 0x07, Banks - 1);
            }
            else if ((value & 0xF8) == 0x08)
            {
                Mode = ((value & 0x04) >> 1) | (value & 0x01);
            }
        }
        else if ((value & 0x80) != 0)
        {
            Vop = value & 0x7F;
        }
        else if ((value & 0xF8) == 0x10)
        {
            Bias = value & 0x07;
        }
        else if ((value & 0xFC) == 0x04)
        {
            TemperatureCoefficient = value & 0x03;
        }

        Changed?.Invoke();
    }

    public void Data(byte value)
    {
        _ram[(Y * Columns) + X] = value;
        if (VerticalAddressing)
        {
            if (++Y >= Banks)
            {
                Y = 0;
                X = (X + 1) % Columns;
            }
        }
        else if (++X >= Columns)
        {
            X = 0;
            Y = (Y + 1) % Banks;
        }

        Changed?.Invoke();
    }

    /// <summary>Whether the pixel at (x, y) of the 84x48 glass is driven dark, before the contrast.</summary>
    public bool Dark(int x, int y)
    {
        if (PowerDown)
        {
            return false;
        }

        bool bit = (_ram[((y / 8) * Columns) + x] & (1 << (y % 8))) != 0;
        return Mode switch
        {
            1 => true,
            2 => bit,
            3 => !bit,
            _ => false,
        };
    }
}
