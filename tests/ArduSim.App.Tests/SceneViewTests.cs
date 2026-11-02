using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using ArduSim.App.Services;
using ArduSim.App.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArduSim.App.Tests;

public sealed class SceneViewTests
{
    [Fact]
    public void A_wire_with_the_same_route_and_color_does_not_redraw()
    {
        Brush stroke = WirePalette.Brush("green", Core.Editing.NetKind.Signal);
        var wire = new WireViewModel("w1", Points((0, 0), (10, 0)), stroke, ["w1"]);
        var changed = new List<string?>();
        wire.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        wire.Update([(0, 0), (10, 0)], WirePalette.Brush("green", Core.Editing.NetKind.Signal), ["w1"]);
        Assert.Empty(changed);

        wire.Update([(0, 0), (20, 0)], stroke, ["w1"]);
        Assert.Equal([nameof(WireViewModel.Points)], changed);
        Assert.Equal(20, wire.Points[1].X);
    }

    [Fact]
    public void The_theme_is_remembered_and_defaults_to_dark()
    {
        string path = Path.Combine(Path.GetTempPath(), "ardusim-state-" + Guid.NewGuid().ToString("N"), "ui-state.json");

        var state = new UserState(path, NullLogger<UserState>.Instance);
        Assert.Equal("dark", state.Theme);
        state.Theme = "light";

        Assert.Equal("light", new UserState(path, NullLogger<UserState>.Instance).Theme);
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }

    private static PointCollection Points(params (double X, double Y)[] points)
    {
        var collection = new PointCollection(points.Select(p => new System.Windows.Point(p.X, p.Y)));
        collection.Freeze();
        return collection;
    }
}
