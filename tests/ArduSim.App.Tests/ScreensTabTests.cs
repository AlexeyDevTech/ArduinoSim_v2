using System.IO;
using System.Text.Json;
using ArduSim.App.Services;
using ArduSim.App.ViewModels;
using ArduSim.Core.Projects;
using ArduSim.Core.Screens;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArduSim.App.Tests;

/// <summary>The Экраны tab (ADR-019) without a window: when it is there, what the panels show, and what an edit does to code and model.</summary>
[Collection("Main view model")]
public sealed class ScreensTabTests : IDisposable
{
    private const string Handwritten = """
        #include <LiquidCrystal_I2C.h>
        LiquidCrystal_I2C lcd(0x27, 16, 2);
        enum Page { FRONT, INFO };
        Page page = FRONT;

        void show() {
          switch (page) {
            case FRONT: lcd.setCursor(1, 0); lcd.print("Hello"); lcd.setCursor(0, 1); lcd.print(millis()); break;
            case INFO: lcd.setCursor(0, 0); lcd.print("Info"); break;
          }
        }

        void setup() { lcd.init(); }

        void loop() {
          char key = readKey();
          if (key == '#') { if (page == FRONT) page = INFO; }
          show();
        }
        """;

    private const string LcdScene = """
        { "components": [
            { "id": "board", "type": "arduino-nano-v3", "x": 0, "y": 0, "rotation": 0, "flip": false },
            { "id": "lcd1", "type": "lcd", "x": 280, "y": 0, "rotation": 0, "flip": false },
            { "id": "btn1", "type": "pushbutton", "x": 0, "y": 200, "rotation": 0, "flip": false },
            { "id": "btn2", "type": "pushbutton", "x": 100, "y": 200, "rotation": 0, "flip": false }
          ],
          "wires": [
            { "id": "w1", "from": { "component": "board", "pin": "A4" }, "to": { "component": "lcd1", "pin": "SDA" }, "waypoints": [], "color": "auto" },
            { "id": "w2", "from": { "component": "board", "pin": "A5" }, "to": { "component": "lcd1", "pin": "SCL" }, "waypoints": [], "color": "auto" },
            { "id": "w3", "from": { "component": "btn1", "pin": "1" }, "to": { "component": "board", "pin": "D2" }, "waypoints": [], "color": "auto" },
            { "id": "w4", "from": { "component": "btn1", "pin": "2" }, "to": { "component": "board", "pin": "GND" }, "waypoints": [], "color": "auto" },
            { "id": "w5", "from": { "component": "btn2", "pin": "1" }, "to": { "component": "board", "pin": "D3" }, "waypoints": [], "color": "auto" },
            { "id": "w6", "from": { "component": "btn2", "pin": "2" }, "to": { "component": "board", "pin": "GND" }, "waypoints": [], "color": "auto" }
          ] }
        """;

    private const string EmptyScene = """{ "components": [ { "id": "board", "type": "arduino-nano-v3", "x": 0, "y": 0, "rotation": 0, "flip": false } ], "wires": [] }""";

    private const string EmptySketch = "void setup() {\n}\n\nvoid loop() {\n}\n";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ardusim-screens-" + Guid.NewGuid().ToString("N")[..8]);

    public ScreensTabTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A language server may still hold a file; the temp folder is not worth failing a test.
        }
    }

    private static void OnSta(Func<Task> body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            _ = System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(Run);
            System.Windows.Threading.Dispatcher.PushFrame(frame);

            async Task Run()
            {
                try
                {
                    await body();
                }
                catch (Exception e)
                {
                    failure = e;
                }

                frame.Continue = false;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private MainViewModel NewViewModel() =>
        new(new AppEnvironment(false), new FirmwareBuildService(), new UserState(Path.Combine(_root, "ui-state.json"), NullLogger<UserState>.Instance), NullLogger<MainViewModel>.Instance);

    private string Project(string scene, string sketch, string name = "proj")
    {
        string directory = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(directory, "sketch"));
        File.WriteAllText(Path.Combine(directory, "sketch", "sketch.ino"), sketch);
        File.WriteAllText(Path.Combine(directory, ProjectFile.FileName), $$"""{ "schemaVersion": 1, "board": "arduino-nano-v3", "sketchPath": "sketch", "scene": {{scene}}, "settings": { "speed": 1.0, "serial": { "baud": 9600, "lineEnding": "LF" }, "libraryPaths": [] } }""");
        return directory;
    }

    private static string Sketch(string project) => Path.Combine(project, "sketch", "sketch.ino");

    private static async Task<MainViewModel> Opened(MainViewModel vm, string project)
    {
        Assert.True(await vm.OpenAsync(project));
        return vm;
    }

    private static ScreensViewModel ShowScreens(MainViewModel vm)
    {
        vm.SelectedTab = ScreensTab.Instance;
        return vm.Screens!;
    }

    [Fact]
    public void The_tab_is_there_with_a_display_on_the_schematic_and_not_without() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();

        await Opened(vm, Project(EmptyScene, EmptySketch));
        Assert.False(vm.HasScreensTab);

        await Opened(vm, Project(LcdScene, EmptySketch, "with"));
        Assert.True(vm.HasScreensTab);
        Assert.Equal(1, vm.Tabs.IndexOf(ScreensTab.Instance));

        await vm.DisposeAsync();
    });

    [Fact]
    public void The_tab_is_there_when_code_draws_screens_on_a_display_the_schematic_lacks() => OnSta(async () =>
    {
        MainViewModel vm = await Opened(NewViewModel(), Project(EmptyScene, Handwritten));

        Assert.True(vm.HasScreensTab);

        await vm.DisposeAsync();
    });

    [Fact]
    public void The_panels_follow_the_tab_and_come_back() => OnSta(async () =>
    {
        MainViewModel vm = await Opened(NewViewModel(), Project(LcdScene, Handwritten));
        vm.LeftTab = MainViewModel.ProjectPage;

        ScreensViewModel screens = ShowScreens(vm);

        Assert.Equal(MainViewModel.ScreensPage, vm.LeftTab);
        Assert.False(vm.IsCanvasVisible);
        Assert.Equal(PropertiesMode.External, vm.Properties!.Mode);
        Assert.Equal(screens.SelectedRow!.Title, vm.Properties.Title);
        Assert.False(vm.ShowProjectActions);

        vm.SelectedTab = SceneTab.Instance;

        Assert.Equal(MainViewModel.ProjectPage, vm.LeftTab);
        Assert.True(vm.IsCanvasVisible);
        Assert.Equal(PropertiesMode.Project, vm.Properties.Mode);
        Assert.Empty(vm.Properties.Fields);
        Assert.True(vm.ShowProjectActions);

        await vm.DisposeAsync();
    });

    [Fact]
    public void The_list_page_opens_the_tab() => OnSta(async () =>
    {
        MainViewModel vm = await Opened(NewViewModel(), Project(LcdScene, Handwritten));

        vm.LeftTab = MainViewModel.ScreensPage;

        Assert.True(vm.IsScreensTab);
        Assert.NotNull(vm.Screens);

        await vm.DisposeAsync();
    });

    [Fact]
    public void In_the_side_by_side_mode_the_tab_keeps_its_place_beside_the_code() => OnSta(async () =>
    {
        MainViewModel vm = await Opened(NewViewModel(), Project(LcdScene, Handwritten));
        ScreensViewModel screens = ShowScreens(vm);
        vm.SplitView = true;

        Assert.False(vm.IsCanvasVisible);
        Assert.NotNull(vm.VisibleDocument);

        screens.SelectScreen("INFO");
        screens.GoToCodeCommand.Execute(null);

        Assert.True(vm.IsScreensTab);

        await vm.DisposeAsync();
    });

    [Fact]
    public void Text_edited_in_a_field_changes_exactly_that_literal_and_undo_brings_it_back() => OnSta(async () =>
    {
        MainViewModel vm = await Opened(NewViewModel(), Project(LcdScene, Handwritten));
        ScreensViewModel screens = ShowScreens(vm);
        CodeDocumentViewModel document = vm.Documents.First();
        Assert.True(screens.IsFromCode);
        screens.SelectScreen("FRONT");
        screens.SelectElement(screens.SelectedScreen!.Elements.Single(e => e.Text == "Hello").Id);

        PropertyField field = vm.Properties!.Fields.Single(f => f.Name == "text");
        field.Text = "Hi \"there\"";
        field.Commit();

        Assert.Equal(Handwritten.Replace("\"Hello\"", "\"Hi \\\"there\\\"\"", StringComparison.Ordinal), document.Document.Text);
        Assert.Contains(screens.SelectedScreen!.Elements, e => e.Text == "Hi \"there\"");
        Assert.True(document.IsDirty);

        // The window's Ctrl+Z follows the tab, not the schematic.
        Assert.True(vm.UndoCommand.CanExecute(null));
        vm.UndoCommand.Execute(null);

        Assert.Equal(Handwritten, document.Document.Text);
        vm.RedoCommand.Execute(null);
        Assert.Contains("Hi \\\"there\\\"", document.Document.Text, StringComparison.Ordinal);

        await vm.DisposeAsync();
    });

    [Fact]
    public void A_drag_writes_only_the_numbers_that_changed() => OnSta(async () =>
    {
        MainViewModel vm = await Opened(NewViewModel(), Project(LcdScene, Handwritten));
        ScreensViewModel screens = ShowScreens(vm);
        screens.SelectScreen("FRONT");
        ScreenElement hello = screens.SelectedScreen!.Elements.Single(e => e.Text == "Hello");

        screens.MoveElement(hello.Id, 4, 1);

        Assert.Contains("lcd.setCursor(4, 1); lcd.print(\"Hello\");", vm.Documents.First().Document.Text, StringComparison.Ordinal);
        Assert.Equal(1, vm.Documents.First().Document.Text.Split("lcd.setCursor(0, 1)").Length - 1);

        await vm.DisposeAsync();
    });

    [Fact]
    public void What_is_computed_at_run_time_cannot_be_edited() => OnSta(async () =>
    {
        MainViewModel vm = await Opened(NewViewModel(), Project(LcdScene, Handwritten));
        ScreensViewModel screens = ShowScreens(vm);
        screens.SelectScreen("FRONT");

        screens.SelectElement(screens.SelectedScreen!.Elements.Single(e => e.Kind == ElementKind.Value).Id);

        Assert.False(vm.Properties!.Fields.Single(f => f.Name == "text").IsEnabled);
        Assert.NotEmpty(screens.ElementNote);

        await vm.DisposeAsync();
    });

    [Fact]
    public void A_transition_is_retargeted_in_the_code() => OnSta(async () =>
    {
        MainViewModel vm = await Opened(NewViewModel(), Project(LcdScene, Handwritten.Replace("page = INFO; }", "page = INFO; }\n  if (key == '*') { if (page == INFO) page = FRONT; }", StringComparison.Ordinal)));
        ScreensViewModel screens = ShowScreens(vm);
        screens.SelectScreen("INFO");

        TransitionRow row = screens.TransitionRows.Single();
        Assert.True(row.CanEditTarget);
        Assert.False(row.CanEditTrigger);
        Assert.False(row.CanRemove);
        row.SelectedTarget = row.Targets.Single(t => t.Value == "INFO");

        Assert.Contains("if (page == INFO) page = INFO;", vm.Documents.First().Document.Text, StringComparison.Ordinal);

        await vm.DisposeAsync();
    });

    [Fact]
    public void An_edit_is_refused_when_the_code_moved_on_and_the_screens_are_read_again() => OnSta(async () =>
    {
        MainViewModel vm = await Opened(NewViewModel(), Project(LcdScene, Handwritten));
        ScreensViewModel screens = ShowScreens(vm);
        screens.SelectScreen("FRONT");
        screens.SelectElement(screens.SelectedScreen!.Elements.Single(e => e.Text == "Hello").Id);
        PropertyField field = vm.Properties!.Fields.Single(f => f.Name == "text");

        // Typed in the code tab, and the half-second rescan has not come yet.
        CodeDocumentViewModel document = vm.Documents.First();
        document.Document.Insert(0, "// a comment\n");
        string typed = document.Document.Text;
        field.Text = "Other";
        field.Commit();

        Assert.Equal(typed, document.Document.Text);
        Assert.NotEmpty(screens.Notice);

        await vm.DisposeAsync();
    });

    [Fact]
    public void Map_positions_of_code_are_kept_beside_it_and_nothing_else() => OnSta(async () =>
    {
        string project = Project(LcdScene, Handwritten);
        MainViewModel vm = await Opened(NewViewModel(), project);
        ScreensViewModel screens = ShowScreens(vm);

        screens.MoveOnMap("INFO", 500, 300, finished: true);

        Assert.Equal(Handwritten, vm.Documents.First().Document.Text);
        ScreenDocument saved = ScreenDocument.Load(project)!;
        Assert.Equal(ScreenSource.Code, saved.Source);
        Assert.Equal(500, saved.Find("INFO")!.MapX);
        Assert.Empty(saved.Find("INFO")!.Elements);
        await vm.CloseProjectCommand.ExecuteAsync(null);

        await Opened(vm, project);
        screens = ShowScreens(vm);
        Assert.Equal(500, screens.Document!.Find("INFO")!.MapX);
        Assert.NotEmpty(screens.Document.Find("INFO")!.Elements);

        await vm.DisposeAsync();
    });

    [Fact]
    public void Creating_screens_writes_the_model_and_the_code_and_connects_the_sketch() => OnSta(async () =>
    {
        string project = Project(LcdScene, EmptySketch);
        MainViewModel vm = await Opened(NewViewModel(), project);
        ScreensViewModel screens = ShowScreens(vm);
        Assert.True(screens.IsEmptyProject);
        Assert.True(screens.CreateCommand.CanExecute(null));

        screens.CreateCommand.Execute(null);

        Assert.True(screens.IsGenerated);
        Assert.False(screens.CreateCommand.CanExecute(null));
        string sketch = vm.Documents.First().Document.Text;
        Assert.StartsWith("#include \"ui_screens.h\"", sketch, StringComparison.Ordinal);
        Assert.Contains("uiBegin();", sketch, StringComparison.Ordinal);
        Assert.Contains("uiUpdate();", sketch, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(project, ScreenDocument.FileName)));
        Assert.True(File.Exists(Path.Combine(project, "sketch", ScreenCodeGenerator.SourceFile)));
        Assert.True(File.Exists(Path.Combine(project, "sketch", ScreenCodeGenerator.HooksFile)));

        await vm.DisposeAsync();
    });

    [Fact]
    public void A_sketch_with_its_own_display_keeps_it_and_begins_the_screens_after_it() => OnSta(async () =>
    {
        const string Own = "#include <LiquidCrystal_I2C.h>\nLiquidCrystal_I2C lcd(0x27, 16, 2);\n\nvoid setup() {\n  lcd.init();\n}\n\nvoid loop() {\n}\n";
        MainViewModel vm = await Opened(NewViewModel(), Project(LcdScene, Own));
        ScreensViewModel screens = ShowScreens(vm);

        screens.CreateCommand.Execute(null);

        Assert.Equal("lcd", screens.Document!.Display.Variable);
        Assert.True(screens.Document.Display.Existing);
        Assert.Contains("  lcd.init();\n  uiBegin();\n}", vm.Documents.First().Document.Text, StringComparison.Ordinal);

        await vm.DisposeAsync();
    });

    [Fact]
    public void Building_a_model_of_screens_from_the_panels() => OnSta(async () =>
    {
        string project = Project(LcdScene, EmptySketch);
        MainViewModel vm = await Opened(NewViewModel(), project);
        ScreensViewModel screens = ShowScreens(vm);
        screens.CreateCommand.Execute(null);

        // A menu made now already works with what is on the schematic.
        screens.AddMenuScreenCommand.Execute(null);
        string menu = screens.SelectedScreenId!;
        Assert.Equal("btn1", screens.SelectedScreen!.Menu!.Down!.Part);
        Assert.Equal("btn2", screens.SelectedScreen.Menu.Select!.Part);
        Assert.Contains(vm.Properties!.Fields, f => f is { Name: "select", Editor: "dropdown" });

        screens.AddMenuItemCommand.Execute(null);
        Assert.Equal(2, screens.MenuRows.Count);
        screens.MenuRows[1].Text = "Second";
        Assert.Equal("Second", screens.SelectedScreen!.Menu!.Items[1].Text);
        screens.MenuRows[1].SelectedTarget = screens.MenuRows[1].Targets.Single(t => t.Value == "home");
        Assert.Equal("home", screens.SelectedScreen!.Menu!.Items[1].Target);
        Assert.Equal(1, screens.Rows.Single(r => r.Id == menu).Transitions);
        Assert.Contains(ScreensViewModel.LinksOf(screens.SelectedScreen), l => l is { Target: "home", Label: "Second", Index: -1 });
        screens.MenuRows[0].Action = "reset now";
        Assert.Equal("reset_now", screens.SelectedScreen!.Menu!.Items[0].Action);

        // Dragging a card onto another makes a transition with the first input; its row changes it.
        screens.Connect("home", menu);
        screens.SelectScreen("home");
        TransitionRow row = screens.TransitionRows.Single();
        Assert.Equal(menu, row.SelectedTarget!.Value);
        row.SelectedTrigger = row.Triggers.Single(t => t.Value == "button:btn2");
        Assert.Equal("btn2", screens.SelectedScreen!.Transitions[0].Trigger.Part);
        Assert.Same(row, screens.TransitionRows.Single());
        row.SelectedTrigger = row.Triggers.Single(t => t.Value == ScreensViewModel.TimeoutOption);
        Assert.True(row.NeedsValue);
        row.TriggerValue = "2500";
        Assert.Equal("2500", screens.SelectedScreen!.Transitions[0].Trigger.Value);

        // Everything is on disk at once, and undo goes back through it.
        ScreenDocument saved = ScreenDocument.Load(project)!;
        Assert.Equal("2500", saved.Find("home")!.Transitions[0].Trigger.Value);
        vm.UndoCommand.Execute(null);
        Assert.Equal("1000", screens.SelectedScreen!.Transitions[0].Trigger.Value);

        // A transition from any screen; a number field moves by one; Delete on a selected element.
        screens.AddAnywhereCommand.Execute(null);
        Assert.Single(screens.AnywhereRows);
        screens.RemoveTransitionCommand.Execute(screens.AnywhereRows[0]);
        Assert.Empty(screens.AnywhereRows);
        screens.IsFace = true;
        screens.AddElementCommand.Execute("Text");
        string added = screens.SelectedElementId!;
        PropertyField x = vm.Properties.Fields.Single(f => f.Name == "x");
        Assert.True(x.Step(1));
        Assert.Equal(1, screens.SelectedElement!.X);
        Assert.Same(x, vm.Properties.Fields.Single(f => f.Name == "x"));
        screens.RemoveSelectedCommand.Execute(null);
        Assert.DoesNotContain(screens.SelectedScreen!.Elements, e => e.Id == added);

        // A line has no business on a character display.
        Assert.False(screens.AddElementCommand.CanExecute("Line"));
        Assert.True(screens.AddElementCommand.CanExecute("Text"));

        await vm.DisposeAsync();
    });

    [Fact]
    public void The_generated_files_follow_the_schematic_and_hooks_written_by_hand_stay() => OnSta(async () =>
    {
        string project = Project(LcdScene, EmptySketch);
        MainViewModel vm = await Opened(NewViewModel(), project);
        ScreensViewModel screens = ShowScreens(vm);
        screens.CreateCommand.Execute(null);
        screens.IsFace = true;
        screens.AddElementCommand.Execute("Value");
        screens.Generate();
        string hooks = Path.Combine(project, "sketch", ScreenCodeGenerator.HooksFile);
        string source = Path.Combine(project, "sketch", ScreenCodeGenerator.SourceFile);
        Assert.Contains("(0x27, 16, 2)", File.ReadAllText(source), StringComparison.Ordinal);
        File.WriteAllText(hooks, File.ReadAllText(hooks) + "// mine\n");

        // The schematic's display grows.
        vm.Scene!.Editor.SetProperty("lcd1", "size", JsonSerializer.SerializeToElement("20x4"));
        screens.Generate();

        Assert.Contains("(0x27, 20, 4)", File.ReadAllText(source), StringComparison.Ordinal);
        Assert.Equal(20, screens.Document!.Display.Width);
        Assert.Contains("// mine", File.ReadAllText(hooks), StringComparison.Ordinal);

        await vm.DisposeAsync();
    });

    [Fact]
    public void Generated_code_opens_read_only_and_follows_the_model() => OnSta(async () =>
    {
        string project = Project(LcdScene, EmptySketch);
        MainViewModel vm = await Opened(NewViewModel(), project);
        ScreensViewModel screens = ShowScreens(vm);
        screens.CreateCommand.Execute(null);
        string source = Path.Combine(project, "sketch", ScreenCodeGenerator.SourceFile);

        CodeDocumentViewModel opened = vm.OpenFile(source)!;
        Assert.True(opened.IsReadOnly);
        Assert.False(vm.OpenFile(Path.Combine(project, "sketch", ScreenCodeGenerator.HooksFile))!.IsReadOnly);

        vm.SelectedTab = ScreensTab.Instance;
        screens.AddScreenCommand.Execute(null);
        screens.Generate();

        Assert.Equal(File.ReadAllText(source), opened.Document.Text);
        Assert.False(opened.IsDirty);

        await vm.DisposeAsync();
    });

    [Fact]
    public void Going_back_to_the_tab_keeps_the_undo_history() => OnSta(async () =>
    {
        MainViewModel vm = await Opened(NewViewModel(), Project(LcdScene, EmptySketch));
        ScreensViewModel screens = ShowScreens(vm);
        screens.CreateCommand.Execute(null);
        screens.AddScreenCommand.Execute(null);
        Assert.True(screens.CanUndoEdit);

        vm.SelectedTab = SceneTab.Instance;
        ShowScreens(vm);

        Assert.True(screens.CanUndoEdit);
        Assert.Equal(2, screens.Rows.Count);

        await vm.DisposeAsync();
    });

    [Fact]
    public void A_build_generates_even_if_the_tab_was_never_opened() => OnSta(async () =>
    {
        string project = Project(LcdScene, EmptySketch);
        MainViewModel vm = await Opened(NewViewModel(), project);
        ShowScreens(vm).CreateCommand.Execute(null);
        vm.SelectedTab = SceneTab.Instance;
        vm.SaveCommand.Execute(null);
        await vm.CloseProjectCommand.ExecuteAsync(null);
        string source = Path.Combine(project, "sketch", ScreenCodeGenerator.SourceFile);
        File.Delete(source);

        await Opened(vm, project);
        Assert.Null(vm.Screens);
        await vm.BuildCommand.ExecuteAsync(null);

        Assert.True(File.Exists(source));

        await vm.DisposeAsync();
    });
}
