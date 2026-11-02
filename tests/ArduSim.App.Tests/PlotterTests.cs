using ArduSim.App.ViewModels;

namespace ArduSim.App.Tests;

/// <summary>The Serial Plotter's line format (the Arduino IDE's): values split by spaces, tabs or commas, optional labels.</summary>
public sealed class PlotterTests
{
    [Fact]
    public void Unlabeled_values_are_named_by_position()
    {
        var plotter = new PlotterViewModel();

        plotter.Append("1 2.5\t-3,4e2\n");

        Assert.Equal(["1", "2", "3", "4"], plotter.Series.Select(s => s.Name));
        Assert.Equal([1, 2.5, -3, 400], plotter.Series.Select(s => s.Values.Single()));
        Assert.Equal(1, plotter.Count);
    }

    [Fact]
    public void Labeled_values_keep_their_series_and_a_missing_value_is_a_gap()
    {
        var plotter = new PlotterViewModel();

        plotter.Append("pot:512 led:128\r\n");
        plotter.Append("led:51\r\n");
        plotter.Append("pot:205 led:51\r\n");

        PlotSeries pot = plotter.Series.Single(s => s.Name == "pot");
        Assert.Equal(3, pot.Values.Count);
        Assert.Equal(512, pot.Values[0]);
        Assert.True(double.IsNaN(pot.Values[1]));
        Assert.Equal(205, pot.Values[2]);
        Assert.Equal([128, 51, 51], plotter.Series.Single(s => s.Name == "led").Values);
    }

    [Fact]
    public void A_series_that_appears_later_starts_with_gaps()
    {
        var plotter = new PlotterViewModel();

        plotter.Append("a:1\na:2\na:3 b:7\n");

        PlotSeries b = plotter.Series.Single(s => s.Name == "b");
        Assert.Equal(3, b.Values.Count);
        Assert.True(b.Values.Take(2).All(double.IsNaN));
        Assert.Equal(7, b.Values[2]);
    }

    [Fact]
    public void Lines_split_across_chunks_are_joined_and_text_lines_are_ignored()
    {
        var plotter = new PlotterViewModel();
        long version = plotter.Version;

        plotter.Append("Start\n12");
        Assert.Equal(version, plotter.Version);
        plotter.Append("34 5");
        plotter.Append("6\n");

        Assert.Equal([1234], plotter.Series[0].Values);
        Assert.Equal([56], plotter.Series[1].Values);
        Assert.True(plotter.Version > version);
    }

    [Fact]
    public void Only_the_last_capacity_lines_are_kept()
    {
        var plotter = new PlotterViewModel();

        plotter.Append(string.Concat(Enumerable.Range(0, PlotterViewModel.Capacity + 20).Select(i => $"{i}\n")));

        Assert.Equal(PlotterViewModel.Capacity, plotter.Count);
        Assert.Equal(20, plotter.Series[0].Values[0]);
        Assert.Equal(PlotterViewModel.Capacity + 19, plotter.Series[0].Values[^1]);
    }

    [Fact]
    public void Paused_plotter_skips_lines_and_clear_starts_over()
    {
        var plotter = new PlotterViewModel();
        plotter.Append("1\n");

        plotter.TogglePauseCommand.Execute(null);
        plotter.Append("2\n3\n");
        Assert.Equal([1], plotter.Series[0].Values);

        plotter.TogglePauseCommand.Execute(null);
        plotter.Append("4\n");
        Assert.Equal([1, 4], plotter.Series[0].Values);

        plotter.Clear();
        Assert.Empty(plotter.Series);
        Assert.Equal(0, plotter.Count);
    }
}
