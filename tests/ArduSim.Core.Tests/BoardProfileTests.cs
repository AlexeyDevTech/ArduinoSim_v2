using ArduSim.Boards;
using ArduSim.Core.Boards;
using ArduSim.Core.Simulation;

namespace ArduSim.Core.Tests;

/// <summary>Every board's profile is consistent, and the M11 boards have the facts of their datasheets (ADR-015).</summary>
public sealed class BoardProfileTests
{
    public static TheoryData<string> Boards => [.. BoardCatalog.All.Select(b => b.Id)];

    [Theory]
    [MemberData(nameof(Boards))]
    public void Pins_are_unique_on_the_grid_and_on_the_board(string id)
    {
        BoardProfile board = BoardCatalog.Find(id)!;
        var names = board.Pins.Select(p => p.Name).Concat(board.PowerPins.Select(p => p.Name)).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Equal(board.Pins.Count, board.Pins.Select(p => p.Number).Distinct().Count());

        var positions = board.Pins.Select(p => (p.X, p.Y)).Concat(board.PowerPins.Select(p => (p.X, p.Y))).ToList();
        Assert.Equal(positions.Count, positions.Distinct().Count());
        Assert.All(positions, p =>
        {
            Assert.Equal(0, p.X % 10);
            Assert.Equal(0, p.Y % 10);
            Assert.InRange(p.X, 0, board.Width);
            Assert.InRange(p.Y, 0, board.Height);
        });
    }

    [Theory]
    [MemberData(nameof(Boards))]
    public void Bus_uart_and_interrupt_pins_exist_on_the_board(string id)
    {
        BoardProfile board = BoardCatalog.Find(id)!;
        Assert.All(board.I2c.Where(p => p.Sda >= 0), p =>
        {
            Assert.NotNull(board.FindPin(p.Sda));
            Assert.NotNull(board.FindPin(p.Scl));
        });
        Assert.All(board.Spi.Where(p => p.Mosi >= 0), p =>
        {
            Assert.NotNull(board.FindPin(p.Mosi));
            Assert.NotNull(board.FindPin(p.Miso));
            Assert.NotNull(board.FindPin(p.Sck));
        });
        Assert.Equal(board.SerialPorts, board.Uarts!.Count);
        Assert.All(board.Uarts, u =>
        {
            Assert.True(u.Rx < 0 || board.FindPin(u.Rx) is not null);
            Assert.True(u.Tx < 0 || board.FindPin(u.Tx) is not null);
        });
        Assert.All(board.ExternalInterrupts.Values, pin => Assert.NotNull(board.FindPin(pin)));
    }

    [Fact]
    public void The_uno_has_the_nanos_mcu_without_a6_and_a7()
    {
        Assert.Equal(20, Uno.Profile.Pins.Count);
        Assert.Null(Uno.Profile.FindPin("A6"));
        Assert.Equal(Nano.Profile.Timing, Uno.Profile.Timing);
        Assert.True(Uno.Profile.FindPin("D9")!.Has(PinCapabilities.Pwm));
        Assert.False(Uno.Profile.FindPin("D8")!.Has(PinCapabilities.Pwm));
    }

    [Fact]
    public void The_mega_has_its_pins_buses_and_memory()
    {
        BoardProfile mega = Mega2560.Profile;
        Assert.Equal(70, mega.Pins.Count);
        Assert.Equal(69, mega.FindPin("A15")!.Number);
        Assert.Equal(15, mega.Pins.Count(p => p.Has(PinCapabilities.Pwm)));
        Assert.Equal(new I2cPins(20, 21), mega.I2c[0]);
        Assert.Equal(new SpiPins(51, 50, 52, 53), mega.Spi[0]);
        Assert.Equal(4, mega.SerialPorts);
        Assert.Equal(new UartPins(15, 14), mega.Uarts![3]);
        Assert.Equal(4096, mega.EepromSize);
        Assert.Equal(21, mega.ExternalInterrupts[2]);
    }

    [Fact]
    public void The_3v3_pro_mini_runs_at_half_speed_on_3_3_volts()
    {
        BoardProfile slow = ProMini.Profile3V3;
        Assert.Equal(3.3, slow.LogicVoltage);
        Assert.Equal(8_000_000, slow.CpuHz);
        Assert.Contains("F_CPU=8000000L", slow.Defines);
        Assert.Equal(2 * Nano.Timing.DigitalWrite, slow.Timing.DigitalWrite);
        Assert.Equal(5.0, ProMini.Profile5V.LogicVoltage);
        Assert.Equal(Nano.Timing.DigitalWrite, ProMini.Profile5V.Timing.DigitalWrite);
    }

    [Fact]
    public void A_3v3_board_divides_its_spi_clock_from_8_mhz()
    {
        using EepromMemory eeprom = EepromMemory.InMemory(1024);
        var engine = new SimulationEngine();
        var scene = new Projects.SceneDocument { Components = [new Projects.ComponentDocument { Id = "board", Type = ProMini.Id3V3 }] };
        Mcu mcu = BoardSimulation.Create(scene, ProMini.Profile3V3, Components.ComponentRegistry.CreateDefault(BoardCatalog.All), engine, eeprom, seed: 1).Mcu;
        mcu.SpiBegin();
        mcu.SpiSettings(8_000_000, 1, 0);

        Assert.Equal(4_000_000u, mcu.SpiClock); // F_CPU / 2
    }
}
