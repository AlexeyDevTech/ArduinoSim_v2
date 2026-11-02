namespace ArduSim.Core.Displays;

/// <summary>
/// The PCF8574 8-bit I2C expander (datasheet NXP PCF8574): a write sets the eight quasi-
/// bidirectional outputs, a read returns the pin levels. A pin written 1 is only weakly pulled
/// up, so whatever drives it low from outside reads 0.
/// </summary>
public sealed class Pcf8574
{
    /// <summary>The output latch; all ones at power-on.</summary>
    public byte Latch { get; private set; } = 0xFF;

    public event Action<byte>? Written;

    public void Write(byte value)
    {
        Latch = value;
        Written?.Invoke(value);
    }

    /// <param name="externalLevels">What the pins would read from outside alone (1 = not pulled low).</param>
    public byte Read(byte externalLevels) => (byte)(Latch & externalLevels);

    public void Reset() => Latch = 0xFF;
}

/// <summary>Which PCF8574 bit drives which HD44780 line on an LCD backpack.</summary>
/// <param name="BacklightActiveLow">The backlight is on when its bit is 0.</param>
public sealed record LcdBackpackMap(int Rs, int Rw, int E, int Backlight, int D4, int D5, int D6, int D7, bool BacklightActiveLow)
{
    /// <summary>The common YwRobot/DFRobot backpack (LiquidCrystal_I2C's wiring): P0=RS, P1=RW, P2=E, P3=backlight, P4-P7=D4-D7.</summary>
    public static LcdBackpackMap Standard { get; } = new(0, 1, 2, 3, 4, 5, 6, 7, false);

    /// <summary>The mjkdz backpack: P0-P3=D4-D7, P4=E, P5=RW, P6=RS, P7=backlight (active low).</summary>
    public static LcdBackpackMap Mjkdz { get; } = new(6, 5, 4, 7, 0, 1, 2, 3, true);

    public bool RegisterSelect(byte pins) => Bit(pins, Rs);

    public bool ReadWrite(byte pins) => Bit(pins, Rw);

    public bool Enable(byte pins) => Bit(pins, E);

    public bool BacklightOn(byte pins) => Bit(pins, Backlight) != BacklightActiveLow;

    /// <summary>The HD44780 data lines DB7..DB4 as the high nibble (DB3..DB0 unconnected, so 1).</summary>
    public int DataLines(byte pins) =>
        (Bit(pins, D7) ? 0x80 : 0) | (Bit(pins, D6) ? 0x40 : 0) | (Bit(pins, D5) ? 0x20 : 0) | (Bit(pins, D4) ? 0x10 : 0) | 0x0F;

    /// <summary>Expander pin levels with the data pins pulled low where the controller drives DB7..DB4 low.</summary>
    public byte WithDataDriven(int lines)
    {
        int levels = 0xFF;
        foreach ((int pin, int line) in new[] { (D4, 0x10), (D5, 0x20), (D6, 0x40), (D7, 0x80) })
        {
            if ((lines & line) == 0)
            {
                levels &= ~(1 << pin);
            }
        }

        return (byte)levels;
    }

    private static bool Bit(byte pins, int bit) => (pins & (1 << bit)) != 0;
}
