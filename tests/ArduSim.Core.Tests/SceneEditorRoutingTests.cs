using System.Text.Json;
using ArduSim.Boards;
using ArduSim.Core.Editing;
using ArduSim.Core.Projects;

namespace ArduSim.Core.Tests;

public sealed class SceneEditorRoutingTests
{
    private static SceneEditor NewEditor()
    {
        var scene = new SceneModel();
        scene.Parts.Add(new PartModel("board", Nano.Id) { X = 100, Y = 300 });
        return new SceneEditor(scene, Scenes.Registry);
    }

    [Fact]
    public void A_wire_end_moves_to_another_pin_in_one_undo_step()
    {
        SceneEditor editor = NewEditor();
        PartModel led = editor.AddPart("led", 400, 0);
        var anode = new PinReference(led.Id, "A");
        WireModel wire = editor.AddWire(new("board", "D2"), anode, [(300, 100)])!;
        editor.SetWireColor(wire.Id, "blue");

        Assert.True(editor.ReconnectWire(wire.Id, fromEnd: true, new("board", "D3")));

        Assert.Equal(new PinReference("board", "D3"), wire.From);
        Assert.Equal(anode, wire.To);
        Assert.Equal("blue", wire.Color);
        Assert.Single(editor.Scene.Wires);

        editor.Undo();
        WireModel restored = editor.Scene.FindWire(wire.Id)!;
        Assert.Equal(new PinReference("board", "D2"), restored.From);
        Assert.Equal([(300.0, 100.0)], restored.Waypoints);
    }

    [Fact]
    public void A_wire_end_cannot_land_on_the_wires_own_pins_or_on_nothing()
    {
        SceneEditor editor = NewEditor();
        PartModel led = editor.AddPart("led", 400, 0);
        WireModel wire = editor.AddWire(new("board", "D2"), new(led.Id, "A"), [])!;
        bool changed = false;
        editor.Changed += () => changed = true;

        Assert.False(editor.ReconnectWire(wire.Id, fromEnd: true, new(led.Id, "A")));
        Assert.False(editor.ReconnectWire(wire.Id, fromEnd: true, new("board", "D2")));
        Assert.False(editor.ReconnectWire(wire.Id, fromEnd: false, new("board", "NoSuchPin")));
        Assert.False(editor.ReconnectWire("w99", fromEnd: false, new("board", "D3")));

        Assert.False(changed);
    }

    [Fact]
    public void A_new_wire_takes_the_bend_that_does_not_run_along_another_part_pin()
    {
        SceneEditor editor = NewEditor();
        PartModel first = editor.AddPart("led", 200, 0);
        PartModel second = editor.AddPart("led", 500, 200);

        // From the cathode, the outer pin: going right from the anode would run over the part's own cathode.
        var from = new PinReference(first.Id, "C");
        var to = new PinReference(second.Id, "A");
        (double X, double Y) a = editor.PinPosition(from)!.Value;
        (double X, double Y) b = editor.PinPosition(to)!.Value;
        Assert.Empty(editor.BestCorner(from, to));

        // A third LED whose anode sits on the horizontal leg that goes first by default.
        PartModel third = editor.AddPart("led", 0, 0);
        (double X, double Y) pin = editor.PinPosition(new(third.Id, "A"))!.Value;
        editor.MoveParts([third.Id], a.X + 100 - pin.X, a.Y - pin.Y);
        Assert.Equal((a.X + 100, a.Y), editor.PinPosition(new(third.Id, "A")));

        Assert.Equal([(a.X, b.Y)], editor.BestCorner(from, to));
    }

    [Fact]
    public void An_aligned_wire_needs_no_corner()
    {
        SceneEditor editor = NewEditor();
        Assert.Empty(editor.BestCorner(new("board", "D2"), new("board", "D3")));
    }

    [Fact]
    public void A_free_spot_is_the_center_until_a_part_covers_it()
    {
        SceneEditor editor = NewEditor();
        (double x, double y) = editor.FreeSpot(40, 40, 800, 800);
        Assert.Equal((800.0, 800.0), (x, y));

        PartModel placed = editor.AddPart("led", x - 20, y - 20);
        (double left, double top, double right, double bottom) = editor.Bounds(placed);
        (double nextX, double nextY) = editor.FreeSpot(40, 40, 800, 800);

        bool overlaps = nextX + 20 > left && nextX - 20 < right && nextY + 20 > top && nextY - 20 < bottom;
        Assert.False(overlaps);
    }

    [Fact]
    public void A_free_spot_stays_in_the_view_when_it_can_and_always_answers()
    {
        SceneEditor editor = NewEditor();
        PartModel placed = editor.AddPart("led", 780, 780);
        (double left, _, double right, _) = editor.Bounds(placed);

        (double x, double y) = editor.FreeSpot(40, 40, 800, 800, (600, 600, 1000, 1000));
        Assert.InRange(x - 20, 600, 1000);
        Assert.InRange(y - 20, 600, 1000);
        Assert.False(x + 20 > left && x - 20 < right && Math.Abs(y - 800) < 40);

        // A view too small for the part: the search goes on outside it instead of giving up.
        (double outX, double outY) = editor.FreeSpot(40, 40, 800, 800, (790, 790, 800, 800));
        Assert.False(double.IsNaN(outX) || double.IsNaN(outY));
    }

    [Fact]
    public void A_reconnected_wire_is_no_obstacle_to_itself()
    {
        SceneEditor editor = NewEditor();
        PartModel first = editor.AddPart("led", 200, 0);
        PartModel second = editor.AddPart("led", 500, 200);
        WireModel wire = editor.AddWire(new("board", "D2"), new(second.Id, "A"), [])!;

        Assert.True(editor.ReconnectWire(wire.Id, fromEnd: true, new(first.Id, "C")));

        // Nothing lies on the default route, so the wire keeps the plain L with no corner.
        Assert.Empty(wire.Waypoints);
    }

    [Fact]
    public void A_cancelled_step_leaves_nothing_to_redo()
    {
        SceneEditor editor = NewEditor();
        PartModel led = editor.AddPart("led", 200, 0);
        (double x, double y) = (led.X, led.Y);

        editor.Checkpoint();
        editor.MoveParts([led.Id], 50, 50, checkpoint: false);
        editor.CancelStep();

        Assert.Equal((x, y), (editor.Scene.FindPart(led.Id)!.X, editor.Scene.FindPart(led.Id)!.Y));
        Assert.False(editor.CanRedo);
        Assert.True(editor.CanUndo);
    }

    [Fact]
    public void Pins_that_land_on_each_other_join_in_the_same_undo_step()
    {
        SceneEditor editor = NewEditor();
        PartModel first = editor.AddPart("led", 200, 0);
        PartModel second = editor.AddPart("led", 400, 0);
        (double X, double Y) target = editor.PinPosition(new(first.Id, "C"))!.Value;
        (double X, double Y) moving = editor.PinPosition(new(second.Id, "A"))!.Value;

        editor.Checkpoint();
        editor.MoveParts([second.Id], target.X - moving.X, target.Y - moving.Y, checkpoint: false);
        Assert.Equal(1, editor.JoinCoincidingPins([second.Id]));

        WireModel wire = editor.Scene.Wires.Single();
        Assert.Equal(new PinReference(second.Id, "A"), wire.From);
        Assert.Equal(new PinReference(first.Id, "C"), wire.To);

        // Already joined: nothing more; and undo takes the move and the wire back together.
        Assert.Equal(0, editor.JoinCoincidingPins([second.Id]));
        editor.Undo();
        Assert.Empty(editor.Scene.Wires);
        Assert.Equal(moving, editor.PinPosition(new(second.Id, "A")));
    }

    [Fact]
    public void Several_property_edits_can_share_one_undo_step()
    {
        SceneEditor editor = NewEditor();
        PartModel a = editor.AddPart("led", 200, 0);
        PartModel b = editor.AddPart("led", 300, 0);
        JsonElement green = JsonSerializer.SerializeToElement("green");

        editor.Checkpoint();
        editor.SetProperty(a.Id, "color", green, checkpoint: false);
        editor.SetProperty(b.Id, "color", green, checkpoint: false);
        Assert.Equal("green", editor.Scene.FindPart(a.Id)!.Properties["color"].GetString());

        editor.Undo();
        Assert.False(editor.Scene.FindPart(a.Id)!.Properties.ContainsKey("color"));
        Assert.False(editor.Scene.FindPart(b.Id)!.Properties.ContainsKey("color"));
    }
}
