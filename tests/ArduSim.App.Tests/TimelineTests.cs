using ArduSim.App.ViewModels;
using ArduSim.Boards;
using ArduSim.Core.Tracing;
using ArduSim.Ipc;

namespace ArduSim.App.Tests;

/// <summary>The Диаграмма tab's rows, window and data feed (ADR-014), without a window.</summary>
public sealed class TimelineTests
{
    private static TimelineViewModel NanoTimeline()
    {
        var timeline = new TimelineViewModel();
        timeline.SetBoard(Nano.Profile);
        timeline.SetViewWidth(1000);
        return timeline;
    }

    private static TraceBlock Block(params (ulong Time, byte Pin, SignalLevel Level)[] samples) =>
        new([.. samples.Select(s => s.Time)], [.. samples.SelectMany(s => new[] { s.Pin, (byte)s.Level })], new int[samples.Length], [], [], [], false);

    [Fact]
    public void The_menus_offer_the_boards_pins_and_buses_and_a_shown_pin_leaves_the_list()
    {
        TimelineViewModel timeline = NanoTimeline();

        Assert.Equal(["I2C (A4, A5)", "SPI (D11, D13)", "Serial TX (D1)", "Serial RX (D0)"], timeline.BusChoices.Select(c => c.Title));
        Assert.Contains(timeline.PinChoices, p => p.Name == "D13");

        timeline.AddPin(Nano.Profile.FindPin("D13")!);
        timeline.AddPin(Nano.Profile.FindPin("D13")!);

        Assert.Single(timeline.Rows);
        Assert.DoesNotContain(timeline.PinChoices, p => p.Name == "D13");
        Assert.False(timeline.IsEmpty);
    }

    [Fact]
    public void The_mega_offers_all_its_uarts()
    {
        var timeline = new TimelineViewModel();
        timeline.SetBoard(Mega2560.Profile);

        Assert.Contains(timeline.BusChoices, c => c.Title == "Serial3 TX (D14)");
        Assert.Contains(timeline.BusChoices, c => c.Title == "SPI (D51, D52)");
    }

    [Fact]
    public void A_bit_banged_bus_decodes_what_was_recorded_before_and_after_it_was_added()
    {
        TimelineViewModel timeline = NanoTimeline();
        ulong t = 0;
        var block = new List<(ulong, byte, SignalLevel)>();
        for (int bit = 7; bit >= 0; bit--)
        {
            block.Add((t++, 4, ((0xA5 >> bit) & 1) != 0 ? SignalLevel.High : SignalLevel.Low));
            block.Add((t++, 3, SignalLevel.High));
            block.Add((t++, 3, SignalLevel.Low));
        }

        timeline.Append(Block([.. block]), t);
        timeline.CustomKind = "SPI";
        timeline.CustomFirst = Nano.Profile.FindPin("D4");
        timeline.CustomSecond = Nano.Profile.FindPin("D3");
        timeline.AddCustomBus();

        TimelineRow row = Assert.Single(timeline.Rows);
        Assert.Equal("SPI (D4, D3)", row.Title);
        Assert.Equal("0xA5", Assert.Single(row.Decoder!.Frames).Text);
    }

    [Fact]
    public void A_uart_row_needs_only_its_line_and_the_form_refuses_a_missing_pin()
    {
        TimelineViewModel timeline = NanoTimeline();
        timeline.CustomKind = "SPI";
        timeline.AddCustomBus();
        Assert.Empty(timeline.Rows);

        timeline.CustomKind = "UART";
        Assert.False(timeline.CustomHasSecond);
        timeline.CustomFirst = Nano.Profile.FindPin("D7");
        timeline.CustomBaud = 115200;
        timeline.AddCustomBus();

        Assert.Equal("UART (D7, 115200)", Assert.Single(timeline.Rows).Title);
    }

    [Fact]
    public void The_window_follows_the_latest_time_until_the_user_zooms_or_pans()
    {
        TimelineViewModel timeline = NanoTimeline();
        double ns = timeline.NsPerPixel;

        timeline.Append(Block((0, 13, SignalLevel.Low)), 50_000_000);
        Assert.True(timeline.Following);
        Assert.Equal(50_000_000 - (ulong)(1000 * ns), timeline.ViewStart);

        timeline.ZoomAt(500, 0.5);
        Assert.False(timeline.Following);
        ulong start = timeline.ViewStart;
        timeline.Append(null, 60_000_000);
        Assert.Equal(start, timeline.ViewStart);

        timeline.PanBy(1e9); // drag to the end
        Assert.True(timeline.Following);
    }

    [Fact]
    public void Zooming_keeps_the_time_under_the_pointer_in_place()
    {
        TimelineViewModel timeline = NanoTimeline();
        timeline.Append(null, 100_000_000);
        double before = timeline.ViewStart + (300 * timeline.NsPerPixel);

        timeline.ZoomAt(300, 0.8);

        Assert.Equal(before, timeline.ViewStart + (300 * timeline.NsPerPixel), 1e3);
    }

    [Fact]
    public void The_readout_shows_the_scale_and_the_distance_from_the_marker()
    {
        TimelineViewModel timeline = NanoTimeline();
        timeline.Append(null, 100_000_000);
        Assert.DoesNotContain("Δ", timeline.Readout, StringComparison.Ordinal);

        timeline.SetMarker(100);
        timeline.SetCursor(200);

        Assert.Contains("Δ 1 ", timeline.Readout, StringComparison.Ordinal); // 100 pixels at 10 µs each: 1 ms
    }

    [Theory]
    [InlineData(10_000, 2_000_000)]
    [InlineData(1, 200)]
    [InlineData(3_000, 500_000)]
    public void Ruler_ticks_are_a_round_step_about_100_pixels_apart(double nsPerPixel, ulong expected)
    {
        Assert.Equal(expected, TimelineViewModel.TickStep(nsPerPixel));
    }

    [Fact]
    public void A_new_run_clears_the_recording_but_keeps_the_rows_and_another_board_clears_them()
    {
        TimelineViewModel timeline = NanoTimeline();
        timeline.AddPin(Nano.Profile.FindPin("D13")!);
        timeline.Append(Block((5, 13, SignalLevel.High)), 10);

        timeline.Reset();
        Assert.True(timeline.Store.IsEmpty);
        Assert.Single(timeline.Rows);
        Assert.True(timeline.Following);

        timeline.SetBoard(Uno.Profile);
        Assert.Empty(timeline.Rows);
    }

    [Fact]
    public void Bus_events_arrive_through_the_ipc_block()
    {
        TimelineViewModel timeline = NanoTimeline();
        timeline.AddBus(timeline.BusChoices[0]); // I2C
        byte[] data = [(byte)BusEventKind.I2cStart, 0x78, 1, 18, 19, BusEvent.NoPin, (byte)BusEventKind.I2cStop, 0, 0, 18, 19, BusEvent.NoPin];

        timeline.Append(new TraceBlock([], [], [], [1000, 200_000], [100_000, 10_000], data, Overflowed: true), 300_000);

        Assert.Equal(["S 0x3C W A", "P"], timeline.Rows[0].Decoder!.Frames.Select(f => f.Text));
        Assert.True(timeline.HasGaps);
    }
}
