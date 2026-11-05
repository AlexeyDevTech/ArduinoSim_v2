namespace ArduSim.Core.Tracing;

/// <summary>
/// The wire levels a hardware bus byte stands for, drawn at the bus's own bit time from the byte the
/// event carries: what a logic analyzer on the real pins would show. SPI is drawn in mode 0.
/// </summary>
public static class BusWaveform
{
    /// <summary>The level of <paramref name="pin"/> at <paramref name="time"/> inside <paramref name="e"/>.</summary>
    public static bool Level(in BusEvent e, int pin, ulong time)
    {
        ulong offset = time < e.Time ? 0 : Math.Min(time - e.Time, e.Duration == 0 ? 0 : e.Duration - 1UL);
        return e.Kind switch
        {
            BusEventKind.I2cStart => I2cStart(e, pin, offset),
            BusEventKind.I2cWrite or BusEventKind.I2cRead => I2cByte(e, pin, offset, 0, e.Duration / 9),
            BusEventKind.I2cStop => I2cStop(e, pin, offset),
            BusEventKind.SpiByte or BusEventKind.SpiByteLsbFirst => Spi(e, pin, offset),
            _ => Uart(e, offset),
        };
    }

    /// <summary>
    /// The level the pin rests at after the event, until the next byte or net change: inside an I2C
    /// transaction the master holds SCL low and the pull-up has SDA; a UART line idles high.
    /// </summary>
    public static bool RestLevel(in BusEvent e, int pin) => !e.IsI2c || pin != e.PinB;

    /// <summary>The times inside the event at which the pin's level may change, ascending.</summary>
    public static void Transitions(in BusEvent e, int pin, List<ulong> times)
    {
        ArgumentNullException.ThrowIfNull(times);
        int bits = Bits(e);
        double bit = bits == 0 ? 0 : e.Duration / (double)bits;
        int steps = e.IsUart ? 1 : 4; // the clock pins change within a bit
        for (int i = 0; i < bits * steps; i++)
        {
            times.Add(e.Time + (ulong)(i * bit / steps));
        }
    }

    private static int Bits(in BusEvent e) => e.Kind switch
    {
        BusEventKind.I2cStart => 10,
        BusEventKind.I2cWrite or BusEventKind.I2cRead => 9,
        BusEventKind.I2cStop => 1,
        BusEventKind.SpiByte or BusEventKind.SpiByteLsbFirst => 8,
        _ => e.Extra,
    };

    // START: SDA falls in the middle of the first bit time, SCL at its end; then the address byte.
    private static bool I2cStart(in BusEvent e, int pin, ulong offset)
    {
        ulong bit = e.Duration / 10;
        if (offset < bit)
        {
            return pin != e.PinA || offset < bit / 2;
        }

        return I2cByte(e, pin, offset, bit, bit);
    }

    // Nine bit times: the data line changes while SCL is low, SCL is high in the second half of each.
    private static bool I2cByte(in BusEvent e, int pin, ulong offset, ulong start, ulong bit)
    {
        if (bit == 0)
        {
            return true;
        }

        int index = (int)Math.Min((offset - start) / bit, 8);
        if (pin == e.PinB)
        {
            return (offset - start) % bit >= bit / 2;
        }

        return index == 8
            ? e.Extra == 0 // the ACK bit: SDA low on ACK
            : ((e.Value >> (7 - index)) & 1) != 0;
    }

    // STOP: SCL rises first, then SDA rises while SCL is high.
    private static bool I2cStop(in BusEvent e, int pin, ulong offset) =>
        pin == e.PinB ? offset >= e.Duration / 4 : offset >= e.Duration * 3 / 4;

    private static bool Spi(in BusEvent e, int pin, ulong offset)
    {
        ulong bit = e.Duration / 8;
        if (bit == 0)
        {
            return false;
        }

        if (pin == e.PinB)
        {
            return offset % bit >= bit / 2;
        }

        int index = (int)Math.Min(offset / bit, 7);
        int shift = e.Kind == BusEventKind.SpiByteLsbFirst ? index : 7 - index;
        byte value = pin == e.PinC ? e.Extra : e.Value;
        return ((value >> shift) & 1) != 0;
    }

    // Start bit, data LSB first, stop (and parity, drawn as stop) bits high.
    private static bool Uart(in BusEvent e, ulong offset)
    {
        int bits = e.Extra == 0 ? 10 : e.Extra;
        int index = (int)(offset * (ulong)bits / Math.Max(1UL, e.Duration));
        if (index == 0)
        {
            return false;
        }

        return index > 8 || ((e.Value >> (index - 1)) & 1) != 0;
    }
}
