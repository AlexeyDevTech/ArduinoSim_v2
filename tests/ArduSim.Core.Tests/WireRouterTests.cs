using ArduSim.Boards;
using ArduSim.Core.Components;
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
        Assert.False(document.Wires.Single(w => w.Id == auto.Id).ByHand);
        Assert.Single(document.Wires.Single(w => w.Id == manual.Id).Waypoints);
        Assert.True(document.Wires.Single(w => w.Id == manual.Id).ByHand);

        var reopened = new SceneEditor(SceneModel.FromDocument(document), Scenes.Registry);
        Assert.Equal(editor.Route(auto), reopened.Route(reopened.Scene.FindWire(auto.Id)!));
        Assert.False(reopened.Scene.FindWire(manual.Id)!.IsAuto);
    }

    [Fact]
    public void Corners_in_a_file_stay_only_if_marked_as_set_by_hand()
    {
        SceneEditor editor = NewEditor();
        PartModel led = editor.AddPart("led", 300, 100);
        SceneDocument document = editor.Scene.ToDocument(Scenes.Registry) with
        {
            Wires =
            [
                new WireDocument { Id = "old", From = new("board", "D3"), To = new(led.Id, "A"), Waypoints = [[50, -80], [60, -80]] },
                new WireDocument { Id = "hand", From = new("board", "D4"), To = new(led.Id, "C"), Waypoints = [[50, -90], [60, -90]], ByHand = true },
            ],
        };

        var opened = new SceneEditor(SceneModel.FromDocument(document), Scenes.Registry);

        Assert.True(opened.Scene.FindWire("old")!.IsAuto);
        Assert.DoesNotContain((50.0, -80.0), opened.Scene.FindWire("old")!.Waypoints);
        Assert.False(opened.Scene.FindWire("hand")!.IsAuto);
        Assert.Equal([(50.0, -90.0), (60.0, -90.0)], opened.Scene.FindWire("hand")!.Waypoints);

        // And the mark goes back into the file with them, only for them.
        SceneDocument saved = opened.Scene.ToDocument(Scenes.Registry);
        Assert.True(saved.Wires.Single(w => w.Id == "hand").ByHand);
        Assert.False(saved.Wires.Single(w => w.Id == "old").ByHand);
        Assert.Empty(saved.Wires.Single(w => w.Id == "old").Waypoints);
    }

    [Fact]
    public void A_corner_set_by_hand_off_a_pins_line_still_leaves_the_pin_straight()
    {
        SceneEditor editor = NewEditor();
        PartModel led = editor.AddPart("led", 500, 100);
        WireModel wire = editor.AddWire(new("board", "5V"), new(led.Id, "A"), [(400, 450)])!;

        IReadOnlyList<(double X, double Y)> route = editor.Route(wire);

        // The board's bottom row: down, not along the row over the pins beside it. The LED's lead: from below.
        AssertOrthogonal(route);
        Assert.Equal(route[0].X, route[1].X);
        Assert.Equal(route[^1].X, route[^2].X);
    }

    // The owner's project: a keypad above the board, wired in reverse order, R1 to D2 and so on to C4 to D9.
    private static SceneEditor KeypadOverBoard(double keypadX, params string[] boardPins)
    {
        var scene = new SceneModel();
        scene.Parts.Add(new PartModel("board", Nano.Id) { X = 0, Y = -20 });
        var editor = new SceneEditor(scene, Scenes.Registry);
        PartModel keypad = editor.AddPart("keypad", keypadX, -220);
        for (int i = 0; i < KeypadPins.Length; i++)
        {
            editor.AddWire(new(keypad.Id, KeypadPins[i]), new("board", boardPins[i]), []);
        }

        return editor;
    }

    private static int Crossings(IReadOnlyList<(double X, double Y)> one, IReadOnlyList<(double X, double Y)> two)
    {
        int crossings = 0;
        foreach (((double X, double Y) A, (double X, double Y) B) a in Segments(one))
        {
            foreach (((double X, double Y) A, (double X, double Y) B) b in Segments(two))
            {
                ((double X, double Y) A, (double X, double Y) B) across = a.A.Y == a.B.Y ? a : b;
                ((double X, double Y) A, (double X, double Y) B) down = a.A.Y == a.B.Y ? b : a;
                if (across.A.Y == across.B.Y && down.A.X == down.B.X
                    && down.A.X > Math.Min(across.A.X, across.B.X) && down.A.X < Math.Max(across.A.X, across.B.X)
                    && across.A.Y > Math.Min(down.A.Y, down.B.Y) && across.A.Y < Math.Max(down.A.Y, down.B.Y))
                {
                    crossings++;
                }
            }
        }

        return crossings;
    }

    [Fact]
    public void A_bundle_wired_in_reverse_order_gets_a_track_for_each_wire()
    {
        SceneEditor editor = KeypadOverBoard(30, "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9");
        var routes = editor.Scene.Wires.Select(editor.Route).ToList();

        foreach (IReadOnlyList<(double X, double Y)> route in routes)
        {
            AssertOrthogonal(route);
            Assert.True(route.Count <= 6, $"{route.Count - 2} corners");

            // Straight out of both pins, and between the parts all the way.
            Assert.Equal(route[0].X, route[1].X);
            Assert.Equal(route[^1].X, route[^2].X);
            Assert.All(route, p => Assert.InRange(p.Y, -70, -10));
        }

        for (int i = 0; i < routes.Count; i++)
        {
            for (int j = i + 1; j < routes.Count; j++)
            {
                foreach (((double X, double Y) A, (double X, double Y) B) one in Segments(routes[i]))
                {
                    Assert.DoesNotContain(Segments(routes[j]), two => Overlap(one, two));
                }

                // Every pair has to cross once, and none more.
                Assert.Equal(1, Crossings(routes[i], routes[j]));
            }
        }
    }

    [Fact]
    public void A_bundle_in_the_same_order_runs_side_by_side_without_a_crossing()
    {
        SceneEditor editor = KeypadOverBoard(50, BoardPins);
        var routes = editor.Scene.Wires.Select(editor.Route).ToList();

        foreach (IReadOnlyList<(double X, double Y)> route in routes)
        {
            Assert.Equal(4, route.Count);
            Assert.All(routes, other => Assert.Equal(0, Crossings(route, other)));
        }
    }

    [Fact]
    public void A_bundle_with_its_pins_in_line_runs_straight()
    {
        SceneEditor editor = KeypadOverBoard(30, BoardPins);

        Assert.All(editor.Scene.Wires, wire => Assert.Empty(wire.Waypoints));
    }

    [Fact]
    public void A_part_in_the_channel_leaves_the_bundle_to_the_search()
    {
        SceneEditor editor = KeypadOverBoard(30, "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9");
        editor.MoveParts([editor.Scene.Parts.Single(p => p.Type == "keypad").Id], 0, -200);
        PartModel resistor = editor.AddPart("resistor", 60, -140);

        foreach (WireModel wire in editor.Scene.Wires)
        {
            AssertOrthogonal(editor.Route(wire));
            Assert.False(CrossesBody(editor, editor.Route(wire), resistor), wire.Id);
        }
    }

    [Fact]
    public void What_is_laid_out_does_not_depend_on_the_way_there()
    {
        SceneEditor editor = WithKeypad();
        PartModel keypad = editor.Scene.Parts.Single(p => p.Type == "keypad");

        // A drag there and back through other places, and its end.
        editor.Checkpoint();
        editor.MoveParts([keypad.Id], 300, 40, checkpoint: false);
        editor.MoveParts([keypad.Id], -500, -20, checkpoint: false);
        editor.MoveParts([keypad.Id], 200, -20, checkpoint: false);
        editor.SettleWires();

        var opened = new SceneEditor(SceneModel.FromDocument(editor.Scene.ToDocument(Scenes.Registry)), Scenes.Registry);
        foreach (WireModel wire in editor.Scene.Wires)
        {
            Assert.Equal(opened.Route(opened.Scene.FindWire(wire.Id)!), editor.Route(wire));
        }
    }

    [Fact]
    public void Every_example_comes_out_clean()
    {
        string examples = Path.Combine(RepositoryRoot(), "examples");
        var problems = new List<string>();
        foreach (string folder in Directory.EnumerateDirectories(examples))
        {
            var editor = new SceneEditor(SceneModel.FromDocument(ProjectFile.Load(folder).Scene), Scenes.Registry);
            string name = Path.GetFileName(folder);
            var net = new Dictionary<PinReference, int>();
            int number = 0;
            foreach (IReadOnlyList<WireModel> group in editor.WireNets())
            {
                number++;
                foreach (WireModel wire in group)
                {
                    net[wire.From] = number;
                    net[wire.To] = number;
                }
            }

            List<(WireModel Wire, IReadOnlyList<(double X, double Y)> Route)> routes = [.. editor.Scene.Wires.Select(w => (w, editor.Route(w)))];
            foreach ((WireModel wire, IReadOnlyList<(double X, double Y)> route) in routes)
            {
                int own = net[wire.From];
                if (Segments(route).Any(s => s.A.X != s.B.X && s.A.Y != s.B.Y))
                {
                    problems.Add($"{name}: {wire.Id} is not at right angles");
                }

                foreach ((WireModel other, IReadOnlyList<(double X, double Y)> theirs) in routes)
                {
                    if (net[other.From] != own && string.CompareOrdinal(wire.Id, other.Id) < 0
                        && Segments(route).Any(one => Segments(theirs).Any(two => Overlap(one, two))))
                    {
                        problems.Add($"{name}: {wire.Id} runs along {other.Id}");
                    }
                }

                foreach (PartModel part in editor.Scene.Parts)
                {
                    foreach (PinDefinition pin in editor.Definition(part)?.Pins ?? [])
                    {
                        var reference = new PinReference(part.Id, pin.Name);
                        if (net.GetValueOrDefault(reference) != own && editor.PinPosition(reference) is { } at
                            && Segments(route).Any(s => Passes(s, at)))
                        {
                            problems.Add($"{name}: {wire.Id} runs over {part.Id}.{pin.Name}");
                        }
                    }
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private static bool Passes(((double X, double Y) A, (double X, double Y) B) segment, (double X, double Y) point)
    {
        double dx = Math.Max(Math.Max(Math.Min(segment.A.X, segment.B.X) - point.X, point.X - Math.Max(segment.A.X, segment.B.X)), 0);
        double dy = Math.Max(Math.Max(Math.Min(segment.A.Y, segment.B.Y) - point.Y, point.Y - Math.Max(segment.A.Y, segment.B.Y)), 0);
        return (dx * dx) + (dy * dy) < 9;
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ArduSim.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("ArduSim.sln not found above " + AppContext.BaseDirectory);
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
