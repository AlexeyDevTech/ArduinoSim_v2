using System.Text;
using ArduSim.Boards;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Simulation;
using ArduSim.Core.Timing;

namespace ArduSim.Core.Tests;

public sealed class McuTests : IDisposable
{
    private readonly SimulationEngine _engine = new();
    private readonly EepromMemory _eeprom = EepromMemory.InMemory(1024);
    private readonly Mcu _mcu;
    private readonly List<PinChange> _changes = [];

    public McuTests()
    {
        _mcu = new Mcu(Nano.Profile, _engine, _eeprom, seed: 1);
        _mcu.PinChanged += _changes.Add;
    }

    public void Dispose() => _eeprom.Dispose();

    private IEnumerable<SimDiagnostic> Warnings(string code) => _engine.Log.Entries.Where(e => e.Code == code);

    [Fact]
    public void Calls_charge_the_timing_table()
    {
        _mcu.PinMode(13, 1);
        _mcu.DigitalWrite(13, 1);
        _mcu.AnalogRead(14);

        Assert.Equal((4 + 4 + 112) * VirtualTime.Microsecond, _engine.Clock.Now);
    }

    [Fact]
    public void Output_changes_are_reported_with_the_time_they_happen()
    {
        _mcu.PinMode(13, 1);
        _mcu.DigitalWrite(13, 1);
        _mcu.DelayMicros(1000);
        _mcu.DigitalWrite(13, 0);

        Assert.Equal(
            [new PinChange(0, 13, new PinDrive(DriveKind.Low, 0)), new PinChange(4_000, 13, new PinDrive(DriveKind.High, 0)), new PinChange(1_008_000, 13, new PinDrive(DriveKind.Low, 0))],
            _changes);
    }

    [Fact]
    public void Output_after_input_pullup_starts_high_as_on_avr()
    {
        _mcu.PinMode(7, 2);
        _mcu.PinMode(7, 1);

        Assert.Equal(DriveKind.High, _mcu.DriveOf(7).Kind);
    }

    [Fact]
    public void Floating_input_warns_once_and_pullup_reads_high()
    {
        _mcu.PinMode(2, 0);
        _mcu.DigitalRead(2);
        _mcu.DigitalRead(2);
        _mcu.PinMode(3, 2);

        Assert.Single(Warnings("floating-input"));
        Assert.Contains("D2", Warnings("floating-input").Single().Message, StringComparison.Ordinal);
        Assert.Equal(1, _mcu.DigitalRead(3));
    }

    [Theory]
    [InlineData(9, 100, DriveKind.Pwm)]
    [InlineData(9, 0, DriveKind.Low)]
    [InlineData(9, 255, DriveKind.High)]
    [InlineData(7, 127, DriveKind.Low)]
    [InlineData(7, 128, DriveKind.High)]
    public void AnalogWrite_follows_avr_rules(int pin, int value, DriveKind expected)
    {
        _mcu.AnalogWrite(pin, value);

        Assert.Equal(expected, _mcu.DriveOf(pin).Kind);
    }

    [Fact]
    public void DigitalWrite_turns_pwm_off()
    {
        _mcu.AnalogWrite(10, 50);
        _mcu.DigitalWrite(10, 1);

        Assert.Equal(DriveKind.High, _mcu.DriveOf(10).Kind);
    }

    [Fact]
    public void Tone_with_duration_ends_by_itself()
    {
        _mcu.Tone(8, 440, 10);
        Assert.Equal(new PinDrive(DriveKind.Tone, 440), _mcu.DriveOf(8));

        _mcu.DelayMicros(10_000);

        Assert.Equal(DriveKind.Low, _mcu.DriveOf(8).Kind);
    }

    [Fact]
    public void Invalid_and_analog_only_pins_warn_and_are_ignored()
    {
        _mcu.DigitalWrite(30, 1);
        _mcu.DigitalWrite(20, 1);

        Assert.Single(Warnings("invalid-pin"));
        Assert.Contains("A6", Warnings("analog-only-pin").Single().Message, StringComparison.Ordinal);
        Assert.Empty(_changes);
    }

    [Fact]
    public void Serial_bytes_leave_at_the_baud_rate_and_writes_block_when_the_ring_is_full()
    {
        var sent = new List<(ulong Time, byte Value)>();
        _mcu.Uarts[0].ByteTransmitted += (_, b) => sent.Add((_engine.Clock.Now, b));
        _mcu.SerialBegin(0, 9600, 0x06);
        ulong start = _engine.Clock.Now;

        _mcu.SerialWrite(0, new byte[100]);
        ulong afterWrite = _engine.Clock.Now;
        _mcu.SerialFlush(0);

        ulong frame = 10 * VirtualTime.Second / 9600;
        Assert.Equal(100, sent.Count);
        Assert.Equal(start + frame, sent[0].Time);
        Assert.Equal(frame, sent[1].Time - sent[0].Time);
        // 1 byte shifting + 64 queued fit without waiting; the other 35 wait for a slot each.
        Assert.InRange(afterWrite - start, 35 * frame, 36 * frame + VirtualTime.Microsecond);
        Assert.Equal(start + 100 * frame, sent[^1].Time);
    }

    [Fact]
    public void Serial_before_begin_drops_output_with_a_warning()
    {
        _mcu.SerialWrite(0, "hi"u8);

        Assert.Single(Warnings("serial-not-begun"));
    }

    [Fact]
    public void Serial_receive_buffers_64_bytes_and_reports_overflow()
    {
        _mcu.SerialBegin(0, 115200, 0x06);
        _mcu.Uarts[0].Receive(Encoding.ASCII.GetBytes(new string('x', 70)));
        _mcu.DelayMicros(20_000);

        Assert.Equal(64, _mcu.SerialAvailable(0));
        Assert.Single(Warnings("serial-rx-overflow"));
        Assert.Equal('x', _mcu.SerialRead(0));
    }

    [Fact]
    public void Eeprom_writes_take_programming_time_before_the_next_access()
    {
        Assert.Equal(0xFF, _mcu.EepromRead(5));
        _mcu.EepromWrite(5, 42);
        ulong afterWrite = _engine.Clock.Now;

        Assert.Equal(42, _mcu.EepromRead(5));
        Assert.True(_engine.Clock.Now - afterWrite >= 3_299 * VirtualTime.Microsecond);
    }

    [Fact]
    public void Eeprom_addresses_wrap_with_a_warning()
    {
        _mcu.EepromWrite(1024 + 7, 9);

        Assert.Equal(9, _eeprom.Read(7));
        Assert.Single(Warnings("eeprom-address"));
    }

    [Fact]
    public void External_interrupt_fires_on_the_configured_edge_and_waits_while_masked()
    {
        _mcu.PinMode(2, 1);
        _mcu.AttachInterrupt(0, 3); // RISING on pin 2
        _mcu.SetInterruptsEnabled(false);
        _mcu.DigitalWrite(2, 1);

        Assert.False(_mcu.Interrupts.TryBeginDispatch(out _));
        _mcu.SetInterruptsEnabled(true);
        Assert.True(_mcu.Interrupts.TryBeginDispatch(out int number));
        Assert.Equal(0, number);
        Assert.False(_mcu.Interrupts.TryBeginDispatch(out _)); // no nesting
        _mcu.Interrupts.EndDispatch();
        _mcu.DigitalWrite(2, 0);                                // falling edge: not RISING
        Assert.False(_mcu.Interrupts.TryBeginDispatch(out _));
    }

    [Fact]
    public void Same_seed_gives_the_same_noise()
    {
        using var otherEeprom = EepromMemory.InMemory(1024);
        var other = new Mcu(Nano.Profile, new SimulationEngine(), otherEeprom, seed: 1);

        int[] a = Enumerable.Range(0, 5).Select(_ => _mcu.AnalogRead(14)).ToArray();
        int[] b = Enumerable.Range(0, 5).Select(_ => other.AnalogRead(14)).ToArray();

        Assert.Equal(a, b);
    }
}

public sealed class EepromMemoryTests
{
    [Fact]
    public void File_backed_eeprom_starts_erased_and_persists()
    {
        string path = Path.Combine(Path.GetTempPath(), "ardusim-eeprom-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            using (var eeprom = EepromMemory.FileBacked(path, 1024))
            {
                Assert.Equal(0xFF, eeprom.Read(1023));
                eeprom.Write(3, 0x5A);
            }

            Assert.Equal(1024, new FileInfo(path).Length);
            using (var reopened = EepromMemory.FileBacked(path, 1024))
            {
                Assert.Equal(0x5A, reopened.Read(3));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
}
