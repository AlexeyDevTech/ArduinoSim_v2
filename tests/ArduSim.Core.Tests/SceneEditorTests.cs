using System.Text.Json;
using ArduSim.Boards;
using ArduSim.Core.Components;
using ArduSim.Core.Editing;
using ArduSim.Core.Projects;

namespace ArduSim.Core.Tests;

public sealed class SceneEditorTests
{
    private static SceneEditor NewEditor()
    {
        var scene = new SceneModel();
        scene.Parts.Add(new PartModel("board", Nano.Id) { X = 100, Y = 300 });
        return new SceneEditor(scene, Scenes.Registry);
    }

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public void Added_parts_snap_to_the_grid_and_get_numbered_ids()
    {
        SceneEditor editor = NewEditor();

        PartModel a = editor.AddPart("led", 123, 456);
        PartModel b = editor.AddPart("led", 0, 0);

        Assert.Equal(("led1", 120.0, 460.0), (a.Id, a.X, a.Y));
        Assert.Equal("led2", b.Id);
        Assert.Equal("r1", editor.AddPart("resistor", 0, 0).Id);
    }

    [Fact]
    public void Moving_parts_moves_the_corners_of_wires_between_them_only()
    {
        SceneEditor editor = NewEditor();
        PartModel led = editor.AddPart("led", 300, 100);
        PartModel r = editor.AddPart("resistor", 300, 200);
        WireModel inner = editor.AddWire(new(r.Id, "2"), new(led.Id, "A"), [(400, 210)])!;
        WireModel outer = editor.AddWire(new("board", "D13"), new(r.Id, "1"), [(120, 210)])!;

        editor.MoveParts([led.Id, r.Id], 20, -10);

        Assert.Equal((320.0, 90.0), (led.X, led.Y));
        Assert.Equal((420.0, 200.0), inner.Waypoints[0]);
        Assert.Equal((120.0, 210.0), outer.Waypoints[0]);
    }

    [Theory]
    [InlineData("led", true)]
    [InlineData("led", false)]
    [InlineData("resistor", true)]
    [InlineData("pushbutton", true)]
    public void Rotation_keeps_pins_on_the_grid(string type, bool clockwise)
    {
        SceneEditor editor = NewEditor();
        PartModel part = editor.AddPart(type, 300, 100);
        ComponentDefinition definition = Scenes.Registry.Find(type)!;

        editor.Rotate([part.Id], clockwise);

        Assert.Equal(clockwise ? 90 : 270, part.Rotation);
        Assert.All(definition.Pins, pin =>
        {
            (double x, double y) = editor.PinPosition(new PinReference(part.Id, pin.Name))!.Value;
            Assert.Equal((SceneEditor.Snap(x), SceneEditor.Snap(y)), (x, y));
        });
    }

    [Fact]
    public void Deleting_a_part_removes_its_wires_and_the_board_cannot_be_deleted()
    {
        SceneEditor editor = NewEditor();
        PartModel led = editor.AddPart("led", 300, 100);
        editor.AddWire(new("board", "D13"), new(led.Id, "A"), []);

        editor.Delete([led.Id, "board"], []);

        Assert.Equal(["board"], editor.Scene.Parts.Select(p => p.Id));
        Assert.Empty(editor.Scene.Wires);
    }

    [Fact]
    public void Wires_need_two_different_existing_pins()
    {
        SceneEditor editor = NewEditor();

        Assert.Null(editor.AddWire(new("board", "D13"), new("board", "D13"), []));
        Assert.Null(editor.AddWire(new("board", "D13"), new("ghost", "1"), []));
        Assert.Null(editor.AddWire(new("board", "D13"), new("board", "D99"), []));
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void Undo_and_redo_cover_every_edit()
    {
        SceneEditor editor = NewEditor();
        PartModel led = editor.AddPart("led", 300, 100);
        editor.SetProperty(led.Id, "color", Json("blue"));
        editor.Rotate([led.Id], clockwise: true);

        editor.Undo();
        Assert.Equal(0, editor.Scene.FindPart("led1")!.Rotation);
        editor.Undo();
        Assert.False(editor.Scene.FindPart("led1")!.Properties.ContainsKey("color"));
        editor.Undo();
        Assert.Null(editor.Scene.FindPart("led1"));
        Assert.False(editor.CanUndo);

        editor.Redo();
        editor.Redo();
        Assert.Equal("blue", editor.Scene.FindPart("led1")!.Properties["color"].GetString());
        Assert.True(editor.CanRedo);
    }

    [Fact]
    public void Copy_and_paste_renumbers_parts_and_keeps_the_wires_between_them()
    {
        SceneEditor editor = NewEditor();
        PartModel led = editor.AddPart("led", 300, 100);
        PartModel r = editor.AddPart("resistor", 300, 200);
        editor.AddWire(new(r.Id, "2"), new(led.Id, "A"), [(400, 210)]);
        editor.AddWire(new("board", "D13"), new(r.Id, "1"), []);

        string text = editor.Copy([led.Id, r.Id, "board"])!;
        IReadOnlyList<string> pasted = editor.Paste(text, 500, 100);

        Assert.Equal(["led2", "r2"], pasted.Order());
        WireModel copy = editor.Scene.Wires.Single(w => w.From.Component == "r2");
        Assert.Equal(new PinReference("led2", "A"), copy.To);
        Assert.Equal((600.0, 210.0), copy.Waypoints[0]);
        Assert.Equal(3, editor.Scene.Wires.Count);
        Assert.Empty(editor.Paste("not json", 0, 0));
    }

    [Fact]
    public void Renaming_a_part_follows_its_wires_and_rejects_bad_ids()
    {
        SceneEditor editor = NewEditor();
        PartModel led = editor.AddPart("led", 300, 100);
        editor.AddWire(new("board", "D13"), new(led.Id, "A"), []);

        Assert.False(editor.RenamePart(led.Id, "board"));
        Assert.False(editor.RenamePart(led.Id, "Bad Id"));
        Assert.True(editor.RenamePart(led.Id, "status-led"));

        Assert.Equal("status-led", editor.Scene.Wires[0].To.Component);
    }

    [Fact]
    public void Nets_group_wires_through_shared_pins_and_know_what_they_carry()
    {
        SceneEditor editor = NewEditor();
        PartModel btn = editor.AddPart("pushbutton", 300, 100);
        PartModel led = editor.AddPart("led", 400, 100);
        editor.AddWire(new("board", "D2"), new(btn.Id, "1"), []);
        editor.AddWire(new(btn.Id, "2"), new("board", "GND"), []);
        editor.AddWire(new(led.Id, "C"), new(btn.Id, "2"), []);

        IReadOnlyList<IReadOnlyList<WireModel>> nets = editor.WireNets();

        Assert.Equal(2, nets.Count);
        Assert.Contains(nets, n => n.Count == 2 && SceneEditor.KindOf(n) == NetKind.Ground);
        Assert.Contains(nets, n => n.Count == 1 && SceneEditor.KindOf(n) == NetKind.Signal);
    }

    [Fact]
    public void Routes_are_orthogonal()
    {
        IReadOnlyList<(double X, double Y)> route = SceneEditor.Route((0, 0), [(50, 30)], (80, 80));

        Assert.Equal([(0.0, 0.0), (50.0, 0.0), (50.0, 30.0), (80.0, 30.0), (80.0, 80.0)], route);
    }

    [Fact]
    public void Saved_projects_omit_default_properties_and_keep_unknown_members()
    {
        string dir = Directory.CreateTempSubdirectory("ardusim-save-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, ProjectFile.FileName), """
                { "schemaVersion": 1, "board": "arduino-nano-v3", "future": 7,
                  "scene": { "components": [ { "id": "board", "type": "arduino-nano-v3", "x": 0, "y": 0, "note": "kept" } ], "wires": [] } }
                """);
            ProjectFile project = ProjectFile.Load(dir);
            var editor = new SceneEditor(SceneModel.FromDocument(project.Scene), Scenes.Registry);
            PartModel led = editor.AddPart("led", 300, 100);
            editor.SetProperty(led.Id, "color", Json("red"));   // the default
            PartModel r = editor.AddPart("resistor", 300, 200);
            editor.SetProperty(r.Id, "resistance", Json(1000));

            (project with { Scene = editor.Scene.ToDocument(Scenes.Registry) }).Save(dir);
            ProjectFile reopened = ProjectFile.Load(dir);

            Assert.Null(reopened.Scene.Components.Single(c => c.Id == "led1").Properties);
            Assert.Equal(1000, reopened.Scene.Components.Single(c => c.Id == "r1").Properties!["resistance"].GetDouble());
            Assert.True(reopened.Unknown!.ContainsKey("future"));
            Assert.Equal("kept", reopened.Scene.Components[0].Unknown!["note"].GetString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
