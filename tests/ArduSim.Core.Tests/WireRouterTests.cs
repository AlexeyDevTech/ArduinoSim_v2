using ArduSim.Boards;
using ArduSim.Core.Editing;
using ArduSim.Core.Projects;

namespace ArduSim.Core.Tests;

/// <summary>How wires are laid out (ADR-020): from a pin straight out, around parts, beside each other, and again when a part moves.</summary>
public sealed class WireRouterTests
{
    private static readonly string[] KeypadPins = ["R1", "R2", "R3", "R4", "C1", "C2", "C3", "C4"];
    private static readonly string[] BoardPins = ["D9", "D8", "D7", "D6", "D5", "D4", "D3", "D2"];

    private static SceneEditor NewEditor()
    {
        var scene = new SceneModel();
        scene.Parts.Add(new PartModel("board", Nano.Id) { X = 100, Y = 300 });
        return new SceneEditor(scene, Scenes.Registry);
    }

    // The keypad above the board, with its eight wires, the owner's own example of a mess.
    private static SceneEditor WithKeypad()
    {
        SceneEditor editor = NewEditor();
        PartModel keypad = editor.AddPart("keypad", 130, 20);
        for (int i = 0; i < KeypadPins.Length; i++)
        {
            editor.AddWire(new(keypad.Id, KeypadPins[i]), new("board", BoardPins[i]), []);
        }

        return editor;
    }

    private static IEnumerable<((double X, double Y) A, (double X, double Y) B)> Segments(IReadOnlyList<(double X, double Y)> route) =>
        route.Zip(route.Skip(1));

    private static void AssertOrthogonal(IReadOnlyList<(double X, double Y)> route)
    {
        foreach (((double X, double Y) a, (double X, double Y) b) in Segments(route))
        {
            Assert.True(a.X == b.X || a.Y == b.Y, $"{a} to {b} is not at a right angle");
        }
    }

    private static bool Overlap(((double X, double Y) A, (double X, double Y) B) one, ((double X, double Y) A, (double X, double Y) B) two)
    {
        bool bothVertical = one.A.X == one.B.X && two.A.X == two.B.X && one.A.X == two.A.X;
        bool bothHorizontal = one.A.Y == one.B.Y && two.A.Y == two.B.Y && one.A.Y == two.A.Y;
        if (bothVertical)
        {
            return Math.Max(Math.Min(one.A.Y, one.B.Y), Math.Min(two.A.Y, two.B.Y)) < Math.Min(Math.Max(one.A.Y, one.B.Y), Math.Max(two.A.Y, two.B.Y));
        }

        return bothHorizontal && Math.Max(Math.Min(one.A.X, one.B.X), Math.Min(two.A.X, two.B.X)) < Math.Min(Math.Max(one.A.X, one.B.X), Math.Max(two.A.X, two.B.X));
    }

    private static bool CrossesBody(SceneEditor editor, IReadOnlyList<(double X, double Y)> route, PartModel part)
    {
        (double left, double top, double right, double bottom) = editor.Bounds(part);
        return Segments(route).Any(s => Math.Max(s.A.X, s.B.X) > left && Math.Min(s.A.X, s.B.X) < right && Math.Max(s.A.Y, s.B.Y) > top && Math.Min(s.A.Y, s.B.Y) < bottom);
    }

    [Fact]
    public void A_wire_leaves_each_pin_straight_out_of_its_part()
    {
        SceneEditor editor = WithKeypad();
        PartModel board = editor.Scene.FindPart("board")!;
        (_, double top, _, _) = editor.Bounds(board);

        foreach (WireModel wire in editor.Scene.Wires)
        {
            IReadOnlyList<(double X, double Y)> route = editor.Route(wire);
            AssertOrthogonal(route);

            // The last leg comes straight down into the board's pin, from above its edge.
            Assert.Equal(route[^1].X, route[^2].X);
            Assert.True(route[^2].Y < top, $"{wire.To.Pin} turns at {route[^2].Y}, inside the board that starts at {top}");
        }
    }

    [Fact]
    public void Wires_of_different_nets_never_share_a_track()
    {
        SceneEditor editor = WithKeypad();

        var routes = editor.Scene.Wires.Select(editor.Route).ToList();
        for (int i = 0; i < routes.Count; i++)
        {
            for (int j = i + 1; j < routes.Count; j++)
            {
                foreach (((double X, double Y) A, (double X, double Y) B) one in Segments(routes[i]))
                {
                    Assert.DoesNotContain(Segments(routes[j]), two => Overlap(one, two));
                }
            }
        }
    }

    [Fact]
    public void A_wire_goes_around_a_part_in_its_way()
    {
        SceneEditor editor = NewEditor();
        PartModel left = editor.AddPart("led", 300, 100);
        PartModel right = editor.AddPart("led", 800, 100);
        PartModel between = editor.AddPart("lcd", 450, 0);

        WireModel wire = editor.AddWire(new(left.Id, "C"), new(right.Id, "A"), [])!;

        Assert.False(CrossesBody(editor, editor.Route(wire), between));
    }

    [Fact]
    public void A_part_set_down_on_a_wire_pushes_it_aside()
    {
        SceneEditor editor = NewEditor();
        PartModel left = editor.AddPart("led", 300, 100);
        PartModel right = editor.AddPart("led", 800, 100);
        WireModel wire = editor.AddWire(new(left.Id, "C"), new(right.Id, "A"), [])!;

        PartModel lcd = editor.AddPart("lcd", 450, 900);
        (double X, double Y) along = editor.Route(wire)[1];
        (double lcdLeft, double lcdTop, double _, double _) = editor.Bounds(lcd);
        editor.MoveParts([lcd.Id], along.X + 50 - lcdLeft, along.Y - 40 - lcdTop);

        Assert.False(CrossesBody(editor, editor.Route(wire), lcd));
    }

    [Fact]
    public void Moving_a_part_lays_out_its_wires_anew_and_they_stay_on_the_pins()
    {
        SceneEditor editor = WithKeypad();
        PartModel keypad = editor.Scene.Parts.Single(p => p.Type == "keypad");

        editor.MoveParts([keypad.Id], 370, 90);

        foreach (WireModel wire in editor.Scene.Wires)
        {
            IReadOnlyList<(double X, double Y)> route = editor.Route(wire);
            AssertOrthogonal(route);
            Assert.Equal(editor.PinPosition(wire.From), route[0]);
            Assert.Equal(editor.PinPosition(wire.To), route[^1]);
            Assert.False(CrossesBody(editor, route.Skip(1).SkipLast(1).ToList(), keypad));
        }

        // The same for the other end: the board is not special, or everything is.
        editor.MoveParts(["board"], -160, 40);
        foreach (WireModel wire in editor.Scene.Wires)
        {
            AssertOrthogonal(editor.Route(wire));
            Assert.Equal(editor.PinPosition(wire.To), editor.Route(wire)[^1]);
        }
    }

    [Fact]
    public void Corners_set_by_hand_stay_until_an_end_moves_alone()
    {
        SceneEditor editor = NewEditor();
        PartModel led = editor.AddPart("led", 300, 100);
        PartModel other = editor.AddPart("led", 600, 100);
        WireModel wire = editor.AddWire(new(led.Id, "C"), new(other.Id, "A"), [(400, 50), (500, 50)])!;
        Assert.False(wire.IsAuto);

        // Together, the shape goes along.
        editor.MoveParts([led.Id, other.Id], 10, 20);
        Assert.Equal([(410.0, 70.0), (510.0, 70.0)], wire.Waypoints);

        // An unrelated part leaves it alone, even on top of it: the route is the user's.
        PartModel third = editor.AddPart("led", 900, 900);
        editor.MoveParts([third.Id], -480, -850);
        Assert.Equal([(410.0, 70.0), (510.0, 70.0)], wire.Waypoints);

        // One end alone: its corners mean nothing any more.
        editor.MoveParts([led.Id], -100, 0);
        Assert.True(wire.IsAuto);
    }

    [Fact]
    public void Editing_a_route_by_hand_keeps_it_and_resetting_it_lays_it_out_again()
    {
        SceneEditor editor = NewEditor();
        PartModel led = editor.AddPart("led", 300, 100);
        WireModel wire = editor.AddWire(new("board", "D2"), new(led.Id, "A"), [])!;
        Assert.True(wire.IsAuto);
        List<(double X, double Y)> laidOut = [.. wire.Waypoints];

        editor.SetWaypoints(wire.Id, [(50, 50)]);
        Assert.False(wire.IsAuto);

        editor.ResetRoute(wire.Id);
        Assert.True(wire.IsAuto);
        Assert.Equal(laidOut, wire.Waypoints);
    }

    [Fact]
    public void Undo_brings_back_the_routes_that_were_on_screen()
    {
        SceneEditor editor = WithKeypad();
        PartModel keypad = editor.Scene.Parts.Single(p => p.Type == "keypad");
        var before = editor.Scene.Wires.ToDictionary(w => w.Id, w => w.Waypoints.ToList());

        editor.MoveParts([keypad.Id], 300, 0);
        Assert.NotEqual(before.Values.First(), editor.Scene.Wires[0].Waypoints);

        editor.Undo();

        foreach (WireModel wire in editor.Scene.Wires)
        {
            Assert.True(wire.IsAuto);
            Assert.Equal(before[wire.Id], wire.Waypoints);
        }
    }

    [Fact]
    public void Lay_out_all_takes_corners_set_by_hand_too_in_one_undo_step()
    {
        SceneEditor editor = NewEditor();
        PartModel led = editor.AddPart("led", 300, 100);
        WireModel wire = editor.AddWire(new("board", "D2"), new(led.Id, "A"), [(700, 700), (800, 700)])!;

        editor.LayOutWires();

        Assert.True(editor.Scene.Wires.Single().IsAuto);
        Assert.DoesNotContain((700.0, 700.0), editor.Scene.Wires.Single().Waypoints);

        editor.Undo();
        Assert.Equal([(700.0, 700.0), (800.0, 700.0)], editor.Scene.FindWire(wire.Id)!.Waypoints);
        Assert.False(editor.Scene.FindWire(wire.Id)!.IsAuto);
    }

    [Fact]
    public void A_file_keeps_only_corners_set_by_hand()
    {
        SceneEditor editor = NewEditor();
        PartModel led = editor.AddPart("led", 300, 100);
        WireModel auto = editor.AddWire(new("board", "D2"), new(led.Id, "A"), [])!;
        WireModel manual = editor.AddWire(new("board", "D3"), new(led.Id, "C"), [(400, 50)])!;
        Assert.NotEmpty(auto.Waypoints);

        SceneDocument document = editor.Scene.ToDocument(Scenes.Registry);

        Assert.Empty(document.Wires.Single(w => w.Id == auto.Id).Waypoints);
        Assert.Single(document.Wires.Single(w => w.Id == manual.Id).Waypoints);

        var reopened = new SceneEditor(SceneModel.FromDocument(document), Scenes.Registry);
        Assert.Equal(editor.Route(auto), reopened.Route(reopened.Scene.FindWire(auto.Id)!));
        Assert.False(reopened.Scene.FindWire(manual.Id)!.IsAuto);
    }

    [Fact]
    public void Corners_off_the_grid_or_the_old_single_bend_are_laid_out_on_opening()
    {
        SceneEditor editor = NewEditor();
        PartModel led = editor.AddPart("led", 300, 100);
        (double X, double Y) a = editor.PinPosition(new("board", "D2"))!.Value;
        (double X, double Y) b = editor.PinPosition(new(led.Id, "A"))!.Value;
        SceneDocument document = editor.Scene.ToDocument(Scenes.Registry) with
        {
            Wires =
            [
                new WireDocument { Id = "w1", From = new("board", "D2"), To = new(led.Id, "A"), Waypoints = [[a.X, b.Y]] },
                new WireDocument { Id = "w2", From = new("board", "D3"), To = new(led.Id, "A"), Waypoints = [[55, -80], [55, -70]] },
                new WireDocument { Id = "w3", From = new("board", "D4"), To = new(led.Id, "A"), Waypoints = [[50, -80], [60, -80]] },
            ],
        };

        var opened = new SceneEditor(SceneModel.FromDocument(document), Scenes.Registry);

        Assert.True(opened.Scene.FindWire("w1")!.IsAuto);
        Assert.True(opened.Scene.FindWire("w2")!.IsAuto);
        Assert.False(opened.Scene.FindWire("w3")!.IsAuto);
    }

    [Fact]
    public void The_preview_is_the_wire_that_results()
    {
        SceneEditor editor = NewEditor();
        PartModel led = editor.AddPart("led", 300, 100);
        var from = new PinReference("board", "D2");
        var to = new PinReference(led.Id, "A");

        IReadOnlyList<(double X, double Y)> preview = editor.PreviewRoute(from, to);
        WireModel wire = editor.AddWire(from, to, [])!;

        Assert.Equal(editor.Route(wire), preview);
    }

    [Fact]
    public void Pins_on_each_other_need_no_route()
    {
        SceneEditor editor = NewEditor();
        PartModel first = editor.AddPart("led", 200, 0);
        PartModel second = editor.AddPart("led", 400, 0);
        (double X, double Y) target = editor.PinPosition(new(first.Id, "C"))!.Value;
        (double X, double Y) moving = editor.PinPosition(new(second.Id, "A"))!.Value;
        editor.MoveParts([second.Id], target.X - moving.X, target.Y - moving.Y);
        editor.JoinCoincidingPins([second.Id]);

        WireModel wire = editor.Scene.Wires.Single();
        editor.LayOutWires();

        Assert.Empty(editor.Scene.FindWire(wire.Id)!.Waypoints);
    }

    [Fact]
    public void A_boxed_in_pin_still_gets_a_route()
    {
        SceneEditor editor = NewEditor();
        PartModel led = editor.AddPart("led", 300, 100);
        foreach ((double x, double y) in new[] { (240.0, 100.0), (360.0, 100.0), (300.0, 30.0), (300.0, 170.0) })
        {
            editor.AddPart("lcd", x, y);
        }

        WireModel wire = editor.AddWire(new("board", "D2"), new(led.Id, "A"), [])!;

        AssertOrthogonal(editor.Route(wire));
        Assert.Equal(editor.PinPosition(wire.To), editor.Route(wire)[^1]);
    }
}
