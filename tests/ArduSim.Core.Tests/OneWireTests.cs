using ArduSim.Core.Buses;
using ArduSim.Core.Components;
using ArduSim.Core.Components.Parts;
using ArduSim.Core.Diagnostics;
using ArduSim.Core.Projects;
using ArduSim.Core.Simulation;

namespace ArduSim.Core.Tests;

/// <summary>DS18B20 parts on a 1-Wire bus, driven through the MCU the way the OneWire library does with its Arduino-call fallback.</summary>
public sealed class OneWireTests : IDisposable
{
    private const int Dq = 2;

    private readonly SimulationEngine _engine = new();
    private readonly EepromMemory _eeprom = EepromMemory.InMemory(1024);

    public void Dispose() => _eeprom.Dispose();

    private IEnumerable<SimDiagnostic> Warnings(string code) => _engine.Log.Entries.Where(e => e.Code == code);

    private BoardSimulation Bus(int sensors, bool pullUp = true, bool parasite = false)
    {
        var parts = new List<ComponentDocument>();
        var wires = new List<string>();
        for (int i = 1; i <= sensors; i++)
        {
            parts.Add(Scenes.Part($"ds{i}", "ds18b20", ("temperature", 20.0 + i)));
            wires.AddRange([$"board.D2 ds{i}.DQ", $"board.GND ds{i}.GND", parasite ? $"board.GND ds{i}.VDD" : $"board.5V ds{i}.VDD"]);
        }

        if (pullUp)
        {
            parts.Add(Scenes.Part("r1", "resistor", ("resistance", 4700)));
            wires.AddRange(["board.D2 r1.1", "r1.2 board.5V"]);
        }

        return Scenes.Create(Scenes.Scene(parts, [.. wires]), _engine, _eeprom);
    }

    // ---- the master, as OneWire.cpp 2.3.8 with DIRECT_* = pinMode/digitalWrite/digitalRead ----------

    private static bool Reset(Mcu mcu)
    {
        mcu.PinMode(Dq, 0);
        int retries = 125;
        do
        {
            if (--retries == 0)
            {
                return false;
            }

            mcu.DelayMicros(2);
        }
        while (mcu.DigitalRead(Dq) == 0);

        mcu.DigitalWrite(Dq, 0);
        mcu.PinMode(Dq, 1);
        mcu.DelayMicros(480);
        mcu.PinMode(Dq, 0);
        mcu.DelayMicros(70);
        bool present = mcu.DigitalRead(Dq) == 0;
        mcu.DelayMicros(410);
        return present;
    }

    private static void WriteBit(Mcu mcu, bool bit)
    {
        mcu.SetInterruptsEnabled(false);
        mcu.DigitalWrite(Dq, 0);
        mcu.PinMode(Dq, 1);
        mcu.DelayMicros(bit ? 10u : 65u);
        mcu.DigitalWrite(Dq, 1);
        mcu.SetInterruptsEnabled(true);
        mcu.DelayMicros(bit ? 55u : 5u);
    }

    private static bool ReadBit(Mcu mcu)
    {
        mcu.SetInterruptsEnabled(false);
        mcu.PinMode(Dq, 1);
        mcu.DigitalWrite(Dq, 0);
        mcu.DelayMicros(3);
        mcu.PinMode(Dq, 0);
        mcu.DelayMicros(10);
        bool bit = mcu.DigitalRead(Dq) != 0;
        mcu.SetInterruptsEnabled(true);
        mcu.DelayMicros(53);
        return bit;
    }

    private static void Write(Mcu mcu, params byte[] bytes)
    {
        foreach (byte value in bytes)
        {
            for (int i = 0; i < 8; i++)
            {
                WriteBit(mcu, ((value >> i) & 1) != 0);
            }
        }

        mcu.PinMode(Dq, 0);
        mcu.DigitalWrite(Dq, 0);
    }

    private static byte Read(Mcu mcu)
    {
        int value = 0;
        for (int i = 0; i < 8; i++)
        {
            value |= (ReadBit(mcu) ? 1 : 0) << i;
        }

        return (byte)value;
    }

    // OneWire::search, the Maxim application note 187 algorithm.
    private static List<ulong> Search(Mcu mcu, byte command = 0xF0)
    {
        var found = new List<ulong>();
        int lastDiscrepancy = 0;
        bool lastDevice = false;
        ulong rom = 0;
        while (!lastDevice)
        {
            if (!Reset(mcu))
            {
                break;
            }

            Write(mcu, command);
            int lastZero = 0;
            bool complete = true;
            for (int bit = 1; bit <= 64; bit++)
            {
                bool id = ReadBit(mcu);
                bool complement = ReadBit(mcu);
                if (id && complement)
                {
                    complete = false;
                    break;
                }

                bool direction;
                if (id != complement)
                {
                    direction = id;
                }
                else
                {
                    direction = bit < lastDiscrepancy ? ((rom >> (bit - 1)) & 1) != 0 : bit == lastDiscrepancy;
                    if (!direction)
                    {
                        lastZero = bit;
                    }
                }

                rom = direction ? rom | (1UL << (bit - 1)) : rom & ~(1UL << (bit - 1));
                WriteBit(mcu, direction);
            }

            if (!complete)
            {
                break;
            }

            found.Add(rom);
            lastDiscrepancy = lastZero;
            lastDevice = lastDiscrepancy == 0;
        }

        return found;
    }

    private static byte[] Bytes(ulong rom) => BitConverter.GetBytes(rom);

    private static byte[] ReadScratchpad(Mcu mcu, ulong rom)
    {
        Assert.True(Reset(mcu));
        Write(mcu, [0x55, .. Bytes(rom), 0xBE]);
        return [.. Enumerable.Range(0, 9).Select(_ => Read(mcu))];
    }

    private static double Celsius(byte[] pad) => (short)(pad[0] | (pad[1] << 8)) / 16.0;

    [Fact]
    public void Reset_gets_a_presence_pulse_only_with_a_sensor_on_the_bus()
    {
        Assert.True(Reset(Bus(1).Mcu));
        Assert.False(Reset(Bus(0).Mcu));
    }

    [Fact]
    public void Read_rom_returns_family_serial_and_a_valid_crc()
    {
        BoardSimulation sim = Bus(1);
        Assert.True(Reset(sim.Mcu));
        Write(sim.Mcu, 0x33);
        byte[] rom = [.. Enumerable.Range(0, 8).Select(_ => Read(sim.Mcu))];

        Assert.Equal(0x28, rom[0]);
        Assert.Equal(OneWireSlave.Crc8(rom.AsSpan(0, 7)), rom[7]);
        Assert.Equal(((Ds18b20)sim.FindComponent("ds1")!).Rom, BitConverter.ToUInt64(rom));
    }

    [Fact]
    public void Search_finds_every_sensor_on_the_bus()
    {
        BoardSimulation sim = Bus(3);
        ulong[] roms = [.. Enumerable.Range(1, 3).Select(i => ((Ds18b20)sim.FindComponent($"ds{i}")!).Rom)];

        List<ulong> found = Search(sim.Mcu);

        Assert.Equal(roms.Order(), found.Order());
    }

    [Fact]
    public void Conversion_takes_750_ms_at_12_bits_and_the_scratchpad_holds_the_reading()
    {
        BoardSimulation sim = Bus(1);
        Mcu mcu = sim.Mcu;
        ulong rom = ((Ds18b20)sim.FindComponent("ds1")!).Rom;

        Assert.Equal(85.0, Celsius(ReadScratchpad(mcu, rom)));
        Assert.True(Reset(mcu));
        Write(mcu, 0xCC, 0x44);
        ulong start = _engine.Clock.Now;
        while (!ReadBit(mcu))
        {
        }

        Assert.InRange((_engine.Clock.Now - start) / 1e6, 749, 752);
        byte[] pad = ReadScratchpad(mcu, rom);
        Assert.Equal(OneWireSlave.Crc8(pad.AsSpan(0, 8)), pad[8]);
        Assert.Equal(21.0, Celsius(pad));
        Assert.Equal(0x7F, pad[4]);
    }

    [Fact]
    public void Nine_bit_resolution_converts_in_94_ms_and_rounds_to_half_degrees()
    {
        BoardSimulation sim = Bus(1);
        Mcu mcu = sim.Mcu;
        sim.FindComponent("ds1")!.ApplyInput("temperature", -10.3);
        Assert.True(Reset(mcu));
        Write(mcu, 0xCC, 0x4E, 0x4B, 0x46, 0x1F);
        Assert.True(Reset(mcu));
        Write(mcu, 0xCC, 0x44);
        ulong start = _engine.Clock.Now;
        while (!ReadBit(mcu))
        {
        }

        Assert.InRange((_engine.Clock.Now - start) / 1e6, 93, 95);
        byte[] pad = ReadScratchpad(mcu, ((Ds18b20)sim.FindComponent("ds1")!).Rom);
        Assert.Equal(-10.5, Celsius(pad));
        Assert.Equal(0x1F, pad[4]);
    }

    [Fact]
    public void Match_rom_selects_one_sensor_of_two()
    {
        BoardSimulation sim = Bus(2);
        Mcu mcu = sim.Mcu;
        Assert.True(Reset(mcu));
        Write(mcu, 0xCC, 0x44);
        mcu.DelayMicros(800_000);

        Assert.Equal(21.0, Celsius(ReadScratchpad(mcu, ((Ds18b20)sim.FindComponent("ds1")!).Rom)));
        Assert.Equal(22.0, Celsius(ReadScratchpad(mcu, ((Ds18b20)sim.FindComponent("ds2")!).Rom)));
    }

    [Fact]
    public void Read_power_supply_tells_parasite_sensors_apart()
    {
        Mcu external = Bus(1).Mcu;
        Assert.True(Reset(external));
        Write(external, 0xCC, 0xB4);
        Assert.True(ReadBit(external));

        Mcu parasite = Bus(1, parasite: true).Mcu;
        Assert.True(Reset(parasite));
        Write(parasite, 0xCC, 0xB4);
        Assert.False(ReadBit(parasite));
    }

    [Fact]
    public void A_bus_without_a_pull_up_warns_at_the_sensor()
    {
        BoardSimulation sim = Bus(1, pullUp: false);
        Reset(sim.Mcu);

        Assert.Equal("ds1", Assert.Single(Warnings("onewire-pullup")).ComponentId);
    }

    [Fact]
    public void Alarm_search_finds_only_sensors_beyond_their_limits()
    {
        BoardSimulation sim = Bus(2);
        Mcu mcu = sim.Mcu;
        sim.FindComponent("ds2")!.ApplyInput("temperature", 90);
        Assert.True(Reset(mcu));
        Write(mcu, 0xCC, 0x4E, 30, 0, 0x7F); // TH 30 °C, TL 0 °C (the factory TL of 70 °C alarms at room temperature)
        Assert.True(Reset(mcu));
        Write(mcu, 0xCC, 0x44);
        mcu.DelayMicros(800_000);

        Assert.Equal([((Ds18b20)sim.FindComponent("ds2")!).Rom], Search(mcu, 0xEC));
    }
}
