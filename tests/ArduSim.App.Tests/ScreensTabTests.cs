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

    private const string DeviceScene = """
        { "components": [
            { "id": "board", "type": "arduino-nano-v3", "x": 0, "y": 0, "rotation": 0, "flip": false },
            { "id": "lcd1", "type": "lcd", "x": 280, "y": 0, "rotation": 0, "flip": false, "properties": { "size": "20x4" } },
            { "id": "bz1", "type": "buzzer", "x": 0, "y": 200, "rotation": 0, "flip": false },
            { "id": "led1", "type": "led", "x": 100, "y": 200, "rotation": 0, "flip": false },
            { "id": "r1", "type": "resistor", "x": 200, "y": 200, "rotation": 0, "flip": false }
          ],
          "wires": [
            { "id": "w1", "from": { "component": "board", "pin": "A4" }, "to": { "component": "lcd1", "pin": "SDA" }, "waypoints": [], "color": "auto" },
            { "id": "w2", "from": { "component": "board", "pin": "A5" }, "to": { "component": "lcd1", "pin": "SCL" }, "waypoints": [], "color": "auto" },
            { "id": "w3", "from": { "component": "bz1", "pin": "1" }, "to": { "component": "board", "pin": "D10" }, "waypoints": [], "color": "auto" },
            { "id": "w4", "from": { "component": "led1", "pin": "A" }, "to": { "component": "r1", "pin": "1" }, "waypoints": [], "color": "auto" },
            { "id": "w5", "from": { "component": "r1", "pin": "2" }, "to": { "component": "board", "pin": "D11" }, "waypoints": [], "color": "auto" }
          ] }
        """;

    private static async Task<ScreensViewModel> CreatedScreens(MainViewModel vm, string project)
    {
        await Opened(vm, project);
        ScreensViewModel screens = ShowScreens(vm);
        screens.CreateCommand.Execute(null);
        return screens;
    }

    private static PropertyField Field(MainViewModel vm, string name) => vm.Properties!.Fields.First(f => f.Name == name);

    private static ElementTool Tool(ScreensViewModel screens, ElementKind kind) => screens.ElementTools.First(t => t.Kind == kind);

    private const string OledScene = """{ "components": [ { "id": "board", "type": "arduino-nano-v3", "x": 0, "y": 0, "rotation": 0, "flip": false }, { "id": "oled1", "type": "oled", "x": 260, "y": 120, "rotation": 0, "flip": false } ], "wires": [] }""";

    [Fact]
    public void The_palette_is_there_on_the_map_too_and_a_click_opens_the_screen_with_the_element_on_it() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        ScreensViewModel screens = await CreatedScreens(vm, Project(DeviceScene, EmptySketch));
        Assert.False(screens.IsFace);
        Assert.All(screens.ElementTools, t => Assert.True(t.IsAvailable, t.Kind.ToString()));
        int before = screens.SelectedScreen!.Elements.Count;

        screens.PlaceElementCommand.Execute(Tool(screens, ElementKind.BigNumber));

        Assert.True(screens.IsFace);
        Assert.Equal(before + 1, screens.SelectedScreen!.Elements.Count);
        Assert.Equal(ElementKind.BigNumber, screens.SelectedElement!.Kind);

        await vm.DisposeAsync();
    });

    [Fact]
    public void A_tile_used_with_another_tab_in_front_brings_the_screens_back() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        ScreensViewModel screens = await CreatedScreens(vm, Project(DeviceScene, EmptySketch));
        ElementTool tool = Tool(screens, ElementKind.Text);
        vm.SelectedTab = SceneTab.Instance;

        screens.PlaceElementCommand.Execute(tool);

        Assert.Same(ScreensTab.Instance, vm.SelectedTab);

        await vm.DisposeAsync();
    });

    [Fact]
    public void A_click_on_a_tile_does_not_replace_the_tiles_under_it() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        ScreensViewModel screens = await CreatedScreens(vm, Project(DeviceScene, EmptySketch));
        IReadOnlyList<ElementTool> tools = screens.ElementTools;

        screens.PlaceElementCommand.Execute(Tool(screens, ElementKind.Text));

        Assert.Same(tools, screens.ElementTools);

        await vm.DisposeAsync();
    });

    [Fact]
    public void A_symbol_from_the_palette_is_the_one_selected_in_the_list() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        ScreensViewModel screens = await CreatedScreens(vm, Project(DeviceScene, EmptySketch));
        screens.SelectGlyph("heart");

        screens.PlaceElementCommand.Execute(Tool(screens, ElementKind.Symbol));

        Assert.Equal("heart", screens.SelectedElement!.Text);
        Assert.Null(screens.SelectedGlyphName);

        await vm.DisposeAsync();
    });

    [Fact]
    public void An_element_dropped_on_a_card_goes_on_that_screen() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        ScreensViewModel screens = await CreatedScreens(vm, Project(DeviceScene, EmptySketch));
        screens.AddScreenCommand.Execute(null);
        string other = screens.SelectedScreenId!;
        screens.SelectScreen("home");

        Assert.True(screens.CanPlaceOn(other, ElementKind.Bar));
        Assert.False(screens.CanPlaceOn(other, ElementKind.Line));
        Assert.False(screens.CanPlaceOn("nothing", ElementKind.Bar));
        screens.PlaceOn(other, ElementKind.Bar);

        Assert.Equal(other, screens.SelectedScreenId);
        Assert.True(screens.IsFace);
        Assert.Contains(screens.Document!.Find(other)!.Elements, e => e.Kind == ElementKind.Bar);

        await vm.DisposeAsync();
    });

    [Fact]
    public void An_oled_s_palette_has_shapes_too() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        ScreensViewModel screens = await CreatedScreens(vm, Project(OledScene, EmptySketch));

        Assert.True(Tool(screens, ElementKind.Line).IsAvailable);
        Assert.True(Tool(screens, ElementKind.Frame).IsAvailable);
        Assert.True(Tool(screens, ElementKind.Box).IsAvailable);

        await vm.DisposeAsync();
    });

    [Fact]
    public void In_a_project_from_code_what_needs_the_tab_s_data_stays_in_the_palette_and_says_why() => OnSta(async () =>
    {
        MainViewModel vm = await Opened(NewViewModel(), Project(LcdScene, Handwritten));
        ScreensViewModel screens = ShowScreens(vm);

        Assert.True(Tool(screens, ElementKind.Text).IsAvailable);
        Assert.True(Tool(screens, ElementKind.Value).IsAvailable);
        ElementTool big = Tool(screens, ElementKind.BigNumber);
        Assert.False(big.IsAvailable);
        Assert.Contains("Только в проекте", big.Tip, StringComparison.Ordinal);
        Assert.NotEmpty(screens.ToolsNote);

        // A click on what cannot be placed writes nothing.
        string before = File.ReadAllText(Path.Combine(vm.ProjectDirectory!, "sketch", "sketch.ino"));
        int elements = screens.SelectedScreen!.Elements.Count;
        screens.PlaceElementCommand.Execute(big);
        Assert.Equal(elements, screens.SelectedScreen!.Elements.Count);
        Assert.Equal(before, File.ReadAllText(Path.Combine(vm.ProjectDirectory!, "sketch", "sketch.ino")));

        // And what can be placed goes into the code.
        screens.PlaceElementCommand.Execute(Tool(screens, ElementKind.Text));
        Assert.Equal(elements + 1, screens.SelectedScreen!.Elements.Count);

        await vm.DisposeAsync();
    });

    // An expression's field is an editable list, and a template without this part shows nothing and takes no typing.
    [Fact]
    public void The_app_s_list_control_has_a_text_field_for_an_editable_list()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory!.FullName, "ArduSim.sln")))
        {
            directory = directory.Parent;
        }

        string controls = File.ReadAllText(Path.Combine(directory.FullName, "src", "ArduSim.App", "Themes", "Controls.xaml"));

        Assert.Contains("x:Name=\"PART_EditableTextBox\"", controls, StringComparison.Ordinal);
    }

    [Fact]
    public void With_a_variable_or_a_symbol_selected_the_panel_has_nothing_about_the_screen() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        ScreensViewModel screens = await CreatedScreens(vm, Project(DeviceScene, EmptySketch));
        screens.AddScreenCommand.Execute(null);
        Assert.True(screens.CanSetStart);

        screens.AddVariableCommand.Execute(null);
        Assert.False(screens.CanSetStart);
        Assert.False(screens.CanDuplicate);
        Assert.False(screens.ShowsElementList);

        screens.SelectGlyph("bell");
        Assert.False(screens.CanSetStart);
        Assert.False(screens.CanRemoveSelected);

        await vm.DisposeAsync();
    });

    [Fact]
    public void The_page_counts_what_is_in_its_groups() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        ScreensViewModel screens = await CreatedScreens(vm, Project(DeviceScene, EmptySketch));
        screens.AddVariableCommand.Execute(null);

        Assert.Contains("(1)", screens.ScreensHeader, StringComparison.Ordinal);
        Assert.Contains("(1)", screens.DataHeader, StringComparison.Ordinal);
        Assert.Contains("(" + screens.GlyphRows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")", screens.SymbolsHeader, StringComparison.Ordinal);

        await vm.DisposeAsync();
    });

    [Fact]
    public void Data_is_made_chosen_and_changed_from_the_panels_and_undoes() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        ScreensViewModel screens = await CreatedScreens(vm, Project(DeviceScene, EmptySketch));

        screens.AddVariableCommand.Execute(null);

        Assert.Equal("value", screens.SelectedVariableName);
        Assert.Single(screens.VariableRows);
        Assert.Null(screens.SelectedRow);
        Assert.False(screens.ShowsScreenPanel);

        Field(vm, "max").Text = "9";
        Field(vm, "max").Commit();
        Assert.Equal(9, screens.Document!.Variables[0].Max);

        Field(vm, "name").Text = "count";
        Field(vm, "name").Commit();
        Assert.Equal("count", screens.SelectedVariableName);

        Field(vm, "kind").SelectedOption = Field(vm, "kind").Options.First(o => o.Value == nameof(VariableKind.Flag));
        Assert.Equal(VariableKind.Flag, screens.Document.Variables[0].Kind);
        Assert.Equal(2, screens.Document.Variables[0].Options.Count);

        Assert.True(screens.RemoveSelectedCommand.CanExecute(null));
        screens.RemoveSelectedCommand.Execute(null);
        Assert.Empty(screens.VariableRows);
        screens.UndoEditCommand.Execute(null);
        Assert.Single(screens.VariableRows);

        screens.SelectedRow = screens.Rows[0];
        Assert.True(screens.ShowsScreenPanel);

        await vm.DisposeAsync();
    });

    [Fact]
    public void A_name_that_cannot_be_used_stays_out_and_says_why() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        ScreensViewModel screens = await CreatedScreens(vm, Project(DeviceScene, EmptySketch));
        screens.AddVariableCommand.Execute(null);

        Field(vm, "name").Text = "hour";
        Field(vm, "name").Commit();

        Assert.Equal("value", screens.Document!.Variables[0].Name);
        Assert.NotEmpty(Field(vm, "name").Error);

        await vm.DisposeAsync();
    });

    [Fact]
    public void An_element_s_expression_that_cannot_be_read_stays_out_and_says_why() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        ScreensViewModel screens = await CreatedScreens(vm, Project(DeviceScene, EmptySketch));
        screens.IsFace = true;
        screens.AddElementAt(ElementKind.Value, 0, 1);
        ScreenElement element = screens.SelectedElement!;

        Field(vm, "when").Text = "a +";
        Field(vm, "when").Commit();
        Assert.NotEmpty(Field(vm, "when").Error);
        Assert.Empty(screens.SelectedElement!.When);

        Field(vm, "when").Text = "uptime > 3";
        Field(vm, "when").Commit();
        Assert.Empty(Field(vm, "when").Error);
        Assert.Equal("uptime > 3", screens.SelectedElement!.When);
        Assert.Equal(element.Id, screens.SelectedElementId);

        await vm.DisposeAsync();
    });

    [Fact]
    public void Each_new_element_can_be_added_and_a_new_one_starts_from_the_first_variable() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        ScreensViewModel screens = await CreatedScreens(vm, Project(DeviceScene, EmptySketch));
        screens.AddVariableCommand.Execute(null);
        screens.SelectScreen("home");
        screens.IsFace = true;

        foreach (ElementKind kind in (ElementKind[])[ElementKind.BigNumber, ElementKind.Bar, ElementKind.Symbol, ElementKind.Input, ElementKind.Drawn])
        {
            screens.PlaceElementCommand.Execute(Tool(screens, kind));
            Assert.Equal(kind, screens.SelectedElement!.Kind);
            Assert.Contains(vm.Properties!.Fields, f => f.Name == "when");
        }

        Assert.Contains(screens.SelectedScreen!.Elements, e => e.Kind == ElementKind.BigNumber && e.Text == "value");
        Assert.Contains(screens.SelectedScreen.Elements, e => e.Kind == ElementKind.Symbol && e.Text == "bell");
        Assert.Equal(6, screens.ElementRows.Count);

        await vm.DisposeAsync();
    });

    [Fact]
    public void An_action_is_added_changed_and_taken_away_on_a_transition() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        ScreensViewModel screens = await CreatedScreens(vm, Project(DeviceScene, EmptySketch));
        screens.AddVariableCommand.Execute(null);
        screens.SelectScreen("home");
        screens.AddTransitionCommand.Execute(null);
        TransitionRow row = screens.TransitionRows[0];

        row.Actions.AddCommand.Execute(null);
        ActionRow action = screens.TransitionRows[0].Actions.Rows[0];
        Assert.Equal(ActionKind.Set, screens.SelectedScreen!.Transitions[0].Actions[0].Kind);
        Assert.Equal("value", action.SelectedTarget!.Value);

        action.Value = "5";
        Assert.Equal("5", screens.SelectedScreen!.Transitions[0].Actions[0].Value);

        action.SelectedKind = action.Kinds.First(k => k.Value == nameof(ActionKind.Beep));
        ScreenAction beep = screens.SelectedScreen!.Transitions[0].Actions[0];
        Assert.Equal(ActionKind.Beep, beep.Kind);
        Assert.Equal("bz1", beep.Target);

        screens.TransitionRows[0].Actions.RemoveCommand.Execute(screens.TransitionRows[0].Actions.Rows[0]);
        Assert.Empty(screens.SelectedScreen!.Transitions[0].Actions);

        await vm.DisposeAsync();
    });

    [Fact]
    public void A_transition_can_go_back_stay_or_wait_for_a_condition() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        ScreensViewModel screens = await CreatedScreens(vm, Project(DeviceScene, EmptySketch));
        screens.AddScreenCommand.Execute(null);
        screens.SelectScreen("home");
        screens.AddTransitionCommand.Execute(null);
        TransitionRow row = screens.TransitionRows[0];

        row.SelectedTarget = row.Targets.First(t => t.Value == ScreenDocument.Back);
        Assert.Equal(ScreenDocument.Back, screens.SelectedScreen!.Transitions[0].Target);

        row = screens.TransitionRows[0];
        row.SelectedTrigger = row.Triggers.First(o => o.Value == ScreensViewModel.ConditionOption);
        row = screens.TransitionRows[0];
        Assert.True(row.NeedsValue);
        row.TriggerValue = "uptime > 3";
        Assert.Equal(TriggerKind.Condition, screens.SelectedScreen!.Transitions[0].Trigger.Kind);
        Assert.Equal("uptime > 3", screens.SelectedScreen.Transitions[0].Trigger.Value);

        row = screens.TransitionRows[0];
        row.SelectedTarget = row.Targets.First(t => t.Value == "");
        Assert.Equal("", screens.SelectedScreen!.Transitions[0].Target);

        await vm.DisposeAsync();
    });

    [Fact]
    public void A_symbol_is_drawn_dot_by_dot_and_a_drag_undoes_as_one() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        ScreensViewModel screens = await CreatedScreens(vm, Project(DeviceScene, EmptySketch));
        screens.AddGlyphCommand.Execute(null);
        Assert.Equal("symbol", screens.SelectedGlyphName);
        Assert.True(screens.HasSelectedGlyph);

        screens.SetGlyphDot(0, 0, on: true, finished: false);
        screens.SetGlyphDot(1, 0, on: true, finished: false);
        screens.SetGlyphDot(4, 7, on: true, finished: true);

        Assert.Equal(0b11000, screens.SelectedGlyphRows[0]);
        Assert.Equal(0b00001, screens.SelectedGlyphRows[7]);
        Assert.Contains("\"symbol\"", File.ReadAllText(Path.Combine(vm.ProjectDirectory!, ScreenDocument.FileName)), StringComparison.Ordinal);

        screens.UndoEditCommand.Execute(null);
        Assert.Equal(0, screens.SelectedGlyphRows[0]);
        Assert.Equal(0, screens.SelectedGlyphRows[7]);

        await vm.DisposeAsync();
    });

    [Fact]
    public void What_will_not_work_shows_on_the_tab() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        ScreensViewModel screens = await CreatedScreens(vm, Project(DeviceScene, EmptySketch));
        screens.IsFace = true;
        Assert.Empty(screens.ProblemsText);

        screens.AddElementAt(ElementKind.Input, 0, 1);

        Assert.Contains("клавиатуры", screens.ProblemsText, StringComparison.Ordinal);

        await vm.DisposeAsync();
    });

    [Fact]
    public void What_actions_can_drive_comes_from_the_schematic() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        ScreensViewModel screens = await CreatedScreens(vm, Project(DeviceScene, EmptySketch));

        Assert.Equal(["bz1"], screens.ActionTargets(ActionKind.Beep).Select(o => o.Value));
        Assert.Equal(["led1"], screens.ActionTargets(ActionKind.Output).Select(o => o.Value));

        await vm.DisposeAsync();
    });

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
    public void A_value_is_its_expression_in_the_code() => OnSta(async () =>
    {
        MainViewModel vm = await Opened(NewViewModel(), Project(LcdScene, Handwritten));
        ScreensViewModel screens = ShowScreens(vm);
        screens.SelectScreen("FRONT");
        screens.SelectElement(screens.SelectedScreen!.Elements.Single(e => e.Kind == ElementKind.Value).Id);

        PropertyField field = vm.Properties!.Fields.Single(f => f.Name == "text");
        Assert.True(field.IsEnabled);
        field.Text = "millis() / 1000";
        field.Commit();

        Assert.Contains("lcd.print(millis() / 1000);", vm.Documents.First().Document.Text, StringComparison.Ordinal);

        await vm.DisposeAsync();
    });

    [Fact]
    public void What_a_shared_helper_draws_can_be_seen_but_not_taken_away_for_one_screen() => OnSta(async () =>
    {
        string code = Handwritten.Replace(
            "void show() {",
            "void title() { lcd.setCursor(0, 0); lcd.print(\"T\"); }\n\nvoid show() {",
            StringComparison.Ordinal).Replace("case FRONT: lcd", "case FRONT: title(); lcd", StringComparison.Ordinal).Replace("case INFO: lcd", "case INFO: title(); lcd", StringComparison.Ordinal);
        MainViewModel vm = await Opened(NewViewModel(), Project(LcdScene, code));
        ScreensViewModel screens = ShowScreens(vm);
        screens.SelectScreen("FRONT");

        screens.SelectElement(screens.SelectedScreen!.Elements.First(e => e.Text == "T").Id);

        Assert.False(screens.CanRemoveSelected);

        await vm.DisposeAsync();
    });

    [Fact]
    public void The_constructor_adds_text_to_a_screen_from_code_and_takes_it_away() => OnSta(async () =>
    {
        MainViewModel vm = await Opened(NewViewModel(), Project(LcdScene, Handwritten));
        ScreensViewModel screens = ShowScreens(vm);
        CodeDocumentViewModel document = vm.Documents.First();
        screens.SelectScreen("INFO");
        screens.IsFace = true;
        Assert.True(screens.CanAddElements);

        screens.AddElementAt(ElementKind.Text, 3, 1, "Added");

        Assert.Contains("lcd.setCursor(3, 1); lcd.print(F(\"Added\"));", document.Document.Text, StringComparison.Ordinal);
        Assert.Equal("Added", screens.SelectedElement!.Text);
        Assert.True(document.IsDirty);

        // In place, as a double-click does it, and along by a drag, then gone: one undo at a time.
        screens.SetElementText(screens.SelectedElementId!, "Changed");
        Assert.Contains("F(\"Changed\")", document.Document.Text, StringComparison.Ordinal);
        screens.MoveElement(screens.SelectedElementId!, 5, 0);
        Assert.Contains("lcd.setCursor(5, 0); lcd.print(F(\"Changed\"));", document.Document.Text, StringComparison.Ordinal);
        Assert.True(screens.CanRemoveSelected);
        screens.RemoveSelected();
        Assert.DoesNotContain("Changed", document.Document.Text, StringComparison.Ordinal);

        screens.UndoEditCommand.Execute(null);
        Assert.Contains("lcd.setCursor(5, 0); lcd.print(F(\"Changed\"));", document.Document.Text, StringComparison.Ordinal);
        screens.UndoEditCommand.Execute(null);
        screens.UndoEditCommand.Execute(null);
        screens.UndoEditCommand.Execute(null);
        Assert.Equal(Handwritten, document.Document.Text);
        screens.RedoEditCommand.Execute(null);
        Assert.Contains("F(\"Added\")", document.Document.Text, StringComparison.Ordinal);

        await vm.DisposeAsync();
    });

    [Fact]
    public void The_constructor_adds_screens_and_transitions_to_code() => OnSta(async () =>
    {
        string project = Project(LcdScene, Handwritten);
        MainViewModel vm = await Opened(NewViewModel(), project);
        ScreensViewModel screens = ShowScreens(vm);
        CodeDocumentViewModel document = vm.Documents.First();
        Assert.True(screens.CanAddScreens);
        Assert.True(screens.CanConnect);

        screens.AddScreenAt(700, 300);

        Assert.Equal("SCREEN_3", screens.SelectedScreenId);
        Assert.Contains("enum Page { FRONT, INFO, SCREEN_3 };", document.Document.Text, StringComparison.Ordinal);
        Assert.Contains("case SCREEN_3:", document.Document.Text, StringComparison.Ordinal);
        Assert.Equal(700, screens.SelectedScreen!.MapX);

        // A drag from card to card: a key that does nothing there yet, which its row can change.
        screens.Connect("FRONT", "SCREEN_3");
        screens.SelectScreen("FRONT");
        TransitionRow row = screens.TransitionRows.Single(r => r.Transition.Target == "SCREEN_3");
        Assert.True(row.CanEditTrigger);
        Assert.True(row.CanRemove);
        row.SelectedTrigger = row.Triggers.First(t => t.Value == "key::7");
        Assert.Contains("if (page == FRONT && key == '7') { page = SCREEN_3; return; }", document.Document.Text, StringComparison.Ordinal);

        // Renamed everywhere; the map keeps it where it was.
        screens.SelectScreen("SCREEN_3");
        PropertyField name = vm.Properties!.Fields.Single(f => f.Name == "title");
        name.Text = "ABOUT";
        name.Commit();
        Assert.Equal("ABOUT", screens.SelectedScreenId);
        Assert.DoesNotContain("case SCREEN_3", document.Document.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(", SCREEN_3", document.Document.Text, StringComparison.Ordinal);
        Assert.Equal(700, screens.SelectedScreen!.MapX);

        // The first screen comes from where the variable starts.
        Assert.True(screens.CanSetStart);
        screens.SetStartCommand.Execute(null);
        Assert.Contains("Page page = ABOUT;", document.Document.Text, StringComparison.Ordinal);
        Assert.Equal("ABOUT", screens.Document!.Start);
        screens.UndoEditCommand.Execute(null);

        // A transition from any screen; and one taken away with its row.
        int anywhere = screens.AnywhereRows.Count;
        screens.AddAnywhereCommand.Execute(null);
        Assert.Equal(anywhere + 1, screens.AnywhereRows.Count);
        screens.RemoveTransitionCommand.Execute(screens.AnywhereRows.First(r => r.Transition.Trigger.Value == "1"));
        Assert.Equal(anywhere, screens.AnywhereRows.Count);

        // A screen nothing else needs goes with all that names it.
        screens.SelectScreen("ABOUT");
        Assert.True(screens.CanRemoveSelected);
        screens.RemoveSelected();
        Assert.DoesNotContain("ABOUT", document.Document.Text, StringComparison.Ordinal);
        Assert.Null(screens.Document!.Find("ABOUT"));

        await vm.DisposeAsync();
    });

    [Fact]
    public void A_bad_name_for_a_screen_is_refused() => OnSta(async () =>
    {
        MainViewModel vm = await Opened(NewViewModel(), Project(LcdScene, Handwritten));
        ScreensViewModel screens = ShowScreens(vm);
        screens.SelectScreen("INFO");
        PropertyField name = vm.Properties!.Fields.Single(f => f.Name == "title");

        name.Text = "1 bad";
        name.Commit();

        Assert.Equal(Handwritten, vm.Documents.First().Document.Text);
        Assert.NotEmpty(screens.Notice);

        await vm.DisposeAsync();
    });

    [Fact]
    public void Elements_are_centered_duplicated_and_resized_in_a_generated_project() => OnSta(async () =>
    {
        MainViewModel vm = await Opened(NewViewModel(), Project(LcdScene, EmptySketch));
        ScreensViewModel screens = ShowScreens(vm);
        screens.CreateCommand.Execute(null);
        screens.IsFace = true;

        screens.AddElementAt(ElementKind.Text, 0, 1, "abcd");
        Assert.True(screens.CanCenter);
        screens.CenterSelectedCommand.Execute(null);
        Assert.Equal(6, screens.SelectedElement!.X);

        screens.DuplicateSelectedCommand.Execute(null);
        Assert.Equal(("abcd", 1), (screens.SelectedElement!.Text, screens.SelectedElement.Y));

        screens.SelectElement(null);
        screens.DuplicateSelectedCommand.Execute(null);
        Assert.Equal(2, screens.Rows.Count);
        Assert.Empty(screens.SelectedScreen!.Transitions);

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
        Assert.True(row.CanRemove);

        // Its key is compared in an if around more than this one statement.
        Assert.False(row.CanEditTrigger);
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
        screens.PlaceElementCommand.Execute(Tool(screens, ElementKind.Text));
        string added = screens.SelectedElementId!;
        PropertyField x = vm.Properties.Fields.Single(f => f.Name == "x");
        Assert.True(x.Step(1));
        Assert.Equal(1, screens.SelectedElement!.X);
        Assert.Same(x, vm.Properties.Fields.Single(f => f.Name == "x"));
        screens.RemoveSelectedCommand.Execute(null);
        Assert.DoesNotContain(screens.SelectedScreen!.Elements, e => e.Id == added);

        // A line has no business on a character display.
        Assert.DoesNotContain(screens.ElementTools, t => t.Kind == ElementKind.Line);
        Assert.True(Tool(screens, ElementKind.Text).IsAvailable);

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
        screens.PlaceElementCommand.Execute(Tool(screens, ElementKind.Value));
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
