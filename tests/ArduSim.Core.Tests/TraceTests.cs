using ArduSim.Core.Components.Parts;
using ArduSim.Core.Projects;
using ArduSim.Core.Simulation;
using ArduSim.Core.Tracing;

namespace ArduSim.Core.Tests;

/// <summary>The logic analyzer's recording, wire pictures and decoders (ADR-014).</summary>
public sealed class TraceTests : IDisposable
{
    private readonly SimulationEngine _engine = new();
    private readonly EepromMemory _eeprom = EepromMemory.InMemory(1024);

    public void Dispose() => _eeprom.Dispose();

    private BoardSimulation Build(IEnumerable<ComponentDocument> parts, params string[] wires) => Scenes.Create(Scenes.Scene(parts, wires), _engine, _eeprom);

    private static TraceStore StoreOf(Mcu mcu, ulong now, params BusDecoder[] decoders)
    {
        var store = new TraceStore();
        foreach (BusDecoder decoder in decoders)
        {
            store.AddDecoder(decoder);
        }

        (PinSample[] pins, BusEvent[] bus, bool overflowed) = mcu.Recorder.Drain();
        store.Append(pins, bus, overflowed, now);
        return store;
    }

    // ---- recording --------------------------------------------------------------------------------

    [Fact]
    public void Recording_starts_with_every_pin_and_follows_writes_pwm_and_tones()
    {
        Mcu mcu = Build([]).Mcu;
        mcu.PinMode(13, 1);
        mcu.SetRecording(true);
        (PinSample[] first, _, _) = mcu.Recorder.Drain();
        Assert.Contains(first, s => s.Pin == 13 && s.Level == SignalLevel.Low);
        Assert.Contains(first, s => s.Pin == 2 && s.Level == SignalLevel.Floating);

        mcu.DigitalWrite(13, 1);
        mcu.DigitalWrite(13, 1); // no change, no sample
        mcu.DigitalWrite(13, 0);
        mcu.PinMode(9, 1);
        mcu.AnalogWrite(9, 64);
        mcu.PinMode(8, 1);
        mcu.Tone(8, 440, 0);
        (PinSample[] pins, _, _) = mcu.Recorder.Drain();

        Assert.Equal([SignalLevel.High, SignalLevel.Low], pins.Where(s => s.Pin == 13).Select(s => s.Level));
        Assert.Contains(pins, s => s.Pin == 9 && s.Level == SignalLevel.Pwm && s.Value == 251);
        Assert.Contains(pins, s => s.Pin == 8 && s.Level == SignalLevel.Tone && s.Value == 440);
    }

    [Fact]
    public void Off_it_records_nothing()
    {
        Mcu mcu = Build([]).Mcu;
        mcu.PinMode(13, 1);
        mcu.DigitalWrite(13, 1);
        mcu.I2cBegin();
        mcu.I2cStart(0x3C, read: false);
        Assert.False(mcu.Recorder.HasData);

        mcu.SetRecording(true);
        mcu.SetRecording(false);
        mcu.Recorder.Drain();
        mcu.DigitalWrite(13, 0);
        Assert.False(mcu.Recorder.HasData);
    }

    [Fact]
    public void Hardware_i2c_bytes_decode_with_address_acks_and_stop()
    {
        BoardSimulation sim = Build(
            [Scenes.Part("oled1", Oled.TypeName)],
            "board.5V oled1.VCC", "board.GND oled1.GND", "board.A4 oled1.SDA", "board.A5 oled1.SCL");
        Mcu mcu = sim.Mcu;
        mcu.I2cBegin();
        mcu.SetRecording(true);
        mcu.I2cStart(0x3C, read: false);
        mcu.I2cWrite(0x40);
        mcu.I2cStop();
        mcu.I2cStart(0x50, read: false); // nobody home
        mcu.I2cStop();

        var decoder = new I2cDecoder(18, 19);
        StoreOf(mcu, _engine.Clock.Now, decoder);

        Assert.Equal(["S 0x3C W A", "0x40 A", "P", "S 0x50 W N", "P"], decoder.Frames.Select(f => f.Text));
        Assert.Equal(FrameKind.Error, decoder.Frames[3].Kind);
        Assert.Equal(100_000UL, decoder.Frames[0].End - decoder.Frames[0].Start); // 10 bit times at 100 kHz
    }

    [Fact]
    public void Hardware_i2c_draws_the_wires_of_its_bytes_and_holds_scl_low_between_them()
    {
        BoardSimulation sim = Build(
            [Scenes.Part("oled1", Oled.TypeName)],
            "board.5V oled1.VCC", "board.GND oled1.GND", "board.A4 oled1.SDA", "board.A5 oled1.SCL");
        Mcu mcu = sim.Mcu;
        mcu.I2cBegin();
        mcu.SetRecording(true);
        ulong start = _engine.Clock.Now;
        mcu.I2cStart(0x3C, read: false); // address byte 0x78 = 0111 1000
        mcu.DelayMicros(50);
        mcu.I2cStop();
        TraceStore store = StoreOf(mcu, _engine.Clock.Now + 1000);

        // One column per 2.5 µs: a quarter of a 10 µs bit.
        var scl = new ColumnLevel[100];
        var sda = new ColumnLevel[100];
        store.Render(19, start, 2500, scl);
        store.Render(18, start, 2500, sda);

        Assert.Equal(ColumnLevel.High, scl[0]);                        // START: SCL high
        Assert.Equal(ColumnLevel.High, sda[0]);                        // SDA high before its fall
        Assert.Equal(ColumnLevel.Low, sda[3]);                         // and low after it
        Assert.Equal(ColumnLevel.Low, sda[5]);                         // first address bit: 0
        Assert.Equal(ColumnLevel.High, sda[9]);                        // second address bit: 1
        Assert.Equal(ColumnLevel.Low, scl[4]);                         // first half of a bit
        Assert.Equal(ColumnLevel.High, scl[6]);                        // second half
        Assert.Equal(ColumnLevel.Low, scl[50]);                        // 25 µs into the 50 µs pause: the transaction holds SCL low
    }

    [Fact]
    public void Hardware_spi_decodes_both_directions_and_draws_the_clock()
    {
        BoardSimulation sim = Build(
            [Scenes.Part("oled1", Oled.TypeName, ("interface", "spi"))],
            "board.5V oled1.VCC", "board.GND oled1.GND", "board.D13 oled1.D0", "board.D11 oled1.D1", "board.D9 oled1.DC", "board.D10 oled1.CS", "board.D8 oled1.RES");
        Mcu mcu = sim.Mcu;
        mcu.SpiBegin();
        mcu.SpiSettings(1_000_000, 1, 0);
        mcu.SetRecording(true);
        ulong start = _engine.Clock.Now;
        mcu.SpiTransfer(0xA5);

        var decoder = new SpiDecoder(11, 12, 13);
        TraceStore store = StoreOf(mcu, _engine.Clock.Now, decoder);
        Assert.Equal(["0xA5"], decoder.Frames.Select(f => f.Text)); // nobody selected: MISO reads 0xFF

        var mosi = new ColumnLevel[8];
        store.Render(11, start, 1000, mosi); // one column per bit at 1 MHz
        int[] expected = [1, 0, 1, 0, 0, 1, 0, 1];
        Assert.Equal(expected, mosi.Select(c => c == ColumnLevel.High ? 1 : 0));
        var sck = new ColumnLevel[8];
        store.Render(13, start, 1000, sck);
        Assert.All(sck, c => Assert.Equal(ColumnLevel.Low | ColumnLevel.High, c));
    }

    [Fact]
    public void Uart_frames_are_decoded_and_the_line_idles_high_after_them()
    {
        Mcu mcu = Build([]).Mcu;
        mcu.SerialBegin(0, 9600, 0x06);
        mcu.SetRecording(true);
        ulong start = _engine.Clock.Now;
        mcu.SerialWrite(0, "Hi\n"u8);
        mcu.DelayMicros(5_000);
        mcu.Uarts[0].Receive("k"u8);
        mcu.DelayMicros(5_000);

        var tx = new UartDecoder(1, 9600);
        var rx = new UartDecoder(0, 9600);
        TraceStore store = StoreOf(mcu, _engine.Clock.Now, tx, rx);

        Assert.Equal(["'H'", "'i'", "0x0A"], tx.Frames.Select(f => f.Text));
        Assert.Equal(["'k'"], rx.Frames.Select(f => f.Text));
        Assert.Equal(1_041_666UL, tx.Frames[0].End - tx.Frames[0].Start);

        var line = new int[10];
        var column = new ColumnLevel[1];
        for (int bit = 0; bit < line.Length; bit++)
        {
            store.Render(1, start + (ulong)bit * 104_166 + 50_000, 1000, column); // the middle of each bit
            line[bit] = column[0] == ColumnLevel.High ? 1 : 0;
        }

        int[] expected = [0, 0, 0, 0, 1, 0, 0, 1, 0, 1]; // start, then 'H' = 0x48 LSB first, stop
        Assert.Equal(expected, line);
        var after = new ColumnLevel[1];
        store.Render(1, _engine.Clock.Now - 1000, 500, after);
        Assert.Equal(ColumnLevel.High, after[0]);
    }

    // ---- decoders on real edges -------------------------------------------------------------------

    private static PinSample Sample(ulong time, int pin, bool high) => new(time, (byte)pin, high ? SignalLevel.High : SignalLevel.Low, 0);

    [Fact]
    public void A_bit_banged_spi_byte_is_read_on_rising_edges_while_selected()
    {
        var decoder = new SpiDecoder(mosi: 2, miso: 3, sck: 4, cs: 5);
        ulong t = 0;
        decoder.OnSample(Sample(t++, 5, true));
        decoder.OnSample(Sample(t++, 4, false));
        foreach (int bit in new[] { 1, 1, 1, 1, 1, 1, 1, 1 })
        {
            decoder.OnSample(Sample(t++, 2, bit == 1));
            decoder.OnSample(Sample(t++, 4, true)); // deselected: ignored
            decoder.OnSample(Sample(t++, 4, false));
        }

        Assert.Empty(decoder.Frames);
        decoder.OnSample(Sample(t++, 5, false));
        foreach (int bit in new[] { 1, 0, 1, 1, 0, 0, 1, 0 })
        {
            decoder.OnSample(Sample(t++, 2, bit == 1));
            decoder.OnSample(Sample(t++, 3, bit == 0));
            decoder.OnSample(Sample(t++, 4, true));
            decoder.OnSample(Sample(t++, 4, false));
        }

        Assert.Equal(["0xB2 / 0x4D"], decoder.Frames.Select(f => f.Text));
    }

    [Fact]
    public void A_bit_banged_uart_byte_is_read_in_the_middle_of_its_bits()
    {
        var decoder = new UartDecoder(line: 7, baud: 9600);
        const ulong Bit = 1_000_000_000UL / 9600;
        decoder.OnSample(Sample(0, 7, true));
        ulong start = 1000;
        decoder.OnSample(Sample(start, 7, false));
        int[] bits = [1, 0, 0, 0, 0, 0, 1, 0]; // 'A' = 0x41, LSB first
        for (int i = 0; i < 8; i++)
        {
            if (bits[i] != (i == 0 ? 0 : bits[i - 1]))
            {
                decoder.OnSample(Sample(start + (((ulong)i + 1) * Bit), 7, bits[i] == 1));
            }
        }

        decoder.OnSample(Sample(start + (9 * Bit), 7, true));
        decoder.Advance(start + (11 * Bit));

        Assert.Equal(["'A'"], decoder.Frames.Select(f => f.Text));
        Assert.Equal(FrameKind.Data, decoder.Frames[0].Kind);
    }

    [Fact]
    public void A_bit_banged_i2c_transaction_is_read_from_start_to_stop()
    {
        var decoder = new I2cDecoder(sda: 2, scl: 3);
        ulong t = 0;
        void Byte(int value, bool nack)
        {
            for (int bit = 7; bit >= -1; bit--)
            {
                bool level = bit < 0 ? nack : ((value >> bit) & 1) != 0;
                decoder.OnSample(Sample(t++, 2, level));
                decoder.OnSample(Sample(t++, 3, true));
                decoder.OnSample(Sample(t++, 3, false));
            }
        }

        decoder.OnSample(Sample(t++, 2, false)); // START: SDA falls while SCL is high
        decoder.OnSample(Sample(t++, 3, false));
        Byte(0x78, nack: false);
        Byte(0x40, nack: true);
        decoder.OnSample(Sample(t++, 2, false));
        decoder.OnSample(Sample(t++, 3, true));
        decoder.OnSample(Sample(t++, 2, true)); // STOP: SDA rises while SCL is high

        Assert.Equal(["S 0x3C W A", "0x40 N", "P"], decoder.Frames.Select(f => f.Text));
    }

    // ---- the store --------------------------------------------------------------------------------

    [Fact]
    public void A_column_shows_toggling_when_edges_are_finer_than_it()
    {
        var store = new TraceStore();
        store.Append([Sample(0, 1, false), Sample(1000, 1, true), Sample(1100, 1, false), Sample(1200, 1, true), Sample(5000, 1, false)], [], false, 6000);

        var columns = new ColumnLevel[6];
        store.Render(1, 0, 1000, columns);

        Assert.Equal(ColumnLevel.Low, columns[0]);
        Assert.Equal(ColumnLevel.Low | ColumnLevel.High, columns[1]);
        Assert.Equal(ColumnLevel.High, columns[2]);
        Assert.Equal(ColumnLevel.High, columns[4]);
        Assert.Equal(ColumnLevel.Low, columns[5]);
        Assert.Equal(SignalLevel.High, store.SampleAt(1, 3000)!.Value.Level);
    }

    [Fact]
    public void Far_zoomed_out_a_million_bus_bytes_paint_quickly_as_a_band()
    {
        var store = new TraceStore();
        BusEvent[] events = [.. Enumerable.Range(0, 1_000_000).Select(i => new BusEvent((ulong)i * 1000, 1000, BusEventKind.SpiByte, 0x55, 0xFF, 11, 13, 12))];
        store.Append([], events, false, 1_000_000_000);

        var columns = new ColumnLevel[1000];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        store.Render(13, 0, 1_000_000, columns);

        Assert.True(clock.ElapsedMilliseconds < 200, $"{clock.ElapsedMilliseconds} ms");
        Assert.All(columns, c => Assert.Equal(ColumnLevel.Low | ColumnLevel.High, c));
    }

    [Fact]
    public void The_oldest_samples_go_first_and_a_decoder_added_late_reads_what_is_there()
    {
        var store = new TraceStore(sampleLimit: 100);
        for (int i = 0; i < 400; i++)
        {
            store.Append([Sample((ulong)i * 10, 1, i % 2 == 0)], [], false, (ulong)i * 10);
        }

        Assert.True(store.Start > 0);
        Assert.Equal(SignalLevel.Low, store.SampleAt(1, 3990)!.Value.Level);
        Assert.Null(new TraceStore().SampleAt(1, 0));

        store.Clear();
        Assert.True(store.IsEmpty);
    }

    [Fact]
    public void Frames_in_a_window_are_found_by_time()
    {
        var decoder = new UartDecoder(1, 9600);
        foreach (ulong start in new ulong[] { 0, 2_000_000, 4_000_000, 6_000_000 })
        {
            decoder.OnEvent(new BusEvent(start, 1_000_000, BusEventKind.UartTx, 0x41, 10, 1, BusEvent.NoPin, BusEvent.NoPin));
        }

        Assert.Equal((1, 2), TraceStore.FramesIn(decoder, 2_500_000, 4_500_000));
        Assert.Equal((4, 0), TraceStore.FramesIn(decoder, 8_000_000, 9_000_000));
    }
}
