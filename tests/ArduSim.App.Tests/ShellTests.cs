using System.IO;
using ArduSim.App.Services;
using ArduSim.App.ViewModels;
using ArduSim.App.Views;
using ArduSim.Boards;
using ArduSim.Build.Compilation;
using ArduSim.Core.Components;
using ArduSim.Core.Projects;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArduSim.App.Tests;

/// <summary>The window's shell without a window: commands, tips, search, the explorer and the language server.</summary>
public sealed class ShellTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ardusim-shell-" + Guid.NewGuid().ToString("N")[..8]);

    public ShellTests() => Directory.CreateDirectory(_root);

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

    private string NewProject(string name = "proj")
    {
        string directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        ProjectTemplate.Create(directory, BoardCatalog.All[0].Id);
        return directory;
    }

    // The bug that greyed out Собрать and Запуск after the first project of a session.
    [Fact]
    public void BuildAndRunFollowTheProject() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        int runChanged = 0;
        int buildChanged = 0;
        vm.RunCommand.CanExecuteChanged += (_, _) => runChanged++;
        vm.BuildCommand.CanExecuteChanged += (_, _) => buildChanged++;

        Assert.True(await vm.OpenAsync(NewProject()));
        Assert.True(runChanged > 0);
        Assert.True(buildChanged > 0);
        Assert.True(vm.RunCommand.CanExecute(null));
        Assert.True(vm.BuildCommand.CanExecute(null));

        runChanged = buildChanged = 0;
        await vm.CloseProjectCommand.ExecuteAsync(null);
        Assert.True(runChanged > 0);
        Assert.True(buildChanged > 0);
        Assert.False(vm.RunCommand.CanExecute(null));
        Assert.False(vm.BuildCommand.CanExecute(null));
        await vm.DisposeAsync();
    });

    [Fact]
    public void DisabledCommandsSayWhy() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        Assert.Contains("откройте", vm.RunTip, StringComparison.OrdinalIgnoreCase);
        Assert.Null(vm.StatusTip);
        Assert.True(await vm.OpenAsync(NewProject()));
        Assert.Contains("F5", vm.RunTip, StringComparison.Ordinal);
        Assert.Contains("F5", vm.PauseTip, StringComparison.Ordinal);
        vm.Status = SimStatus.Building;
        Assert.Contains("сборка", vm.RunTip, StringComparison.OrdinalIgnoreCase);
        vm.Status = SimStatus.Running;
        Assert.Contains("F6", vm.PauseTip, StringComparison.Ordinal);
        Assert.Contains("Перезапустить", vm.RunTip, StringComparison.Ordinal);
        vm.IsRunStale = true;
        Assert.Contains("перезапуск", vm.RunTip, StringComparison.OrdinalIgnoreCase);
        vm.Status = SimStatus.Idle;
        await vm.DisposeAsync();
    });

    [Fact]
    public void SerialSendWaitsForARun() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        Assert.True(await vm.OpenAsync(NewProject()));
        Assert.False(vm.SendSerialCommand.CanExecute(null));
        Assert.True(vm.ShowSerialPlaceholder);
        vm.SerialInput = "x";
        Assert.False(vm.ShowSerialPlaceholder);
        Assert.Contains("F5", vm.SendTip, StringComparison.Ordinal);
        vm.Status = SimStatus.Running;
        Assert.Contains("Enter", vm.SendTip, StringComparison.Ordinal);
        vm.Status = SimStatus.Idle;
        await vm.DisposeAsync();
    });

    [Fact]
    public void TheMonitorGetsAppendedTextAndReplacesItOnlyOnClear() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        Assert.True(await vm.OpenAsync(NewProject()));
        var appended = new List<string>();
        var replaced = new List<string>();
        vm.SerialAppended += appended.Add;
        vm.SerialReplaced += replaced.Add;
        Assert.True(vm.SerialIsEmpty);

        byte[] letter = System.Text.Encoding.UTF8.GetBytes("Я\n");
        vm.AppendSerial(0, letter[..1]);
        Assert.True(vm.SerialIsEmpty); // half a letter is nothing to show yet
        vm.AppendSerial(0, letter[1..]);
        Assert.False(vm.SerialIsEmpty);
        Assert.Equal(["Я\n"], appended);
        Assert.Empty(replaced);

        vm.ClearSerialCommand.Execute(null);
        Assert.Equal([""], replaced);
        Assert.True(vm.SerialIsEmpty);
        await vm.DisposeAsync();
    });

    [Fact]
    public void StampsAreKeptPerPort() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        Assert.True(await vm.OpenAsync(NewProject()));
        var shown = new System.Text.StringBuilder();
        vm.SerialAppended += text => shown.Append(text);
        vm.TimestampSerial = true;

        vm.AppendSerial(0, "a"u8.ToArray());
        vm.AppendSerial(1, "other\n"u8.ToArray());
        vm.AppendSerial(0, "b\n"u8.ToArray());

        Assert.Equal(1, shown.ToString().Count(c => c == '['));
        Assert.EndsWith("ab\n", shown.ToString(), StringComparison.Ordinal);
        await vm.DisposeAsync();
    });

    [Fact]
    public void OpeningTheFoldedPanelOnTheLogClearsItsBadge() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        Assert.True(await vm.OpenAsync(NewProject()));
        vm.SelectedBottomTab = MainViewModel.LogTab;
        vm.BottomOpen = false;
        vm.UnseenWarnings = 3;

        vm.BottomOpen = true;
        Assert.Equal(0, vm.UnseenWarnings);

        vm.SelectedBottomTab = MainViewModel.SerialTab;
        vm.BottomOpen = false;
        vm.UnseenWarnings = 3;
        vm.BottomOpen = true;
        Assert.Equal(3, vm.UnseenWarnings);
        await vm.DisposeAsync();
    });

    [Fact]
    public void ANewRunUnpausesThePlotter() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        vm.Plotter.Paused = true;

        Assert.True(await vm.OpenAsync(NewProject()));

        Assert.False(vm.Plotter.Paused);
        await vm.DisposeAsync();
    });

    [Fact]
    public void EverySeverityHasItsOwnGlyph()
    {
        var converter = new SeverityToGlyphConverter();
        object[] glyphs = [.. Enumerable.Range(0, 3).Select(severity => converter.Convert(severity, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture))];

        Assert.Equal(3, glyphs.Distinct().Count());
    }

    [Fact]
    public void OpeningAFolderWithASketchMakesItAProject() => OnSta(async () =>
    {
        string folder = Path.Combine(_root, "MySketch");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "MySketch.ino"), "void setup() {}\nvoid loop() {}\n");
        MainViewModel vm = NewViewModel();

        Assert.True(await vm.OpenAsync(folder));
        Assert.True(File.Exists(Path.Combine(folder, ProjectFile.FileName)));
        Assert.Equal("MySketch.ino", vm.Documents.Single().Title);
        await vm.DisposeAsync();
    });

    [Fact]
    public void AFolderWithoutASketchOffersAProject() => OnSta(async () =>
    {
        string folder = Path.Combine(_root, "Empty");
        Directory.CreateDirectory(folder);
        MainViewModel vm = NewViewModel();

        Assert.False(await vm.OpenAsync(folder));
        Assert.Equal(BannerKind.NoProject, vm.Banner);
        await vm.CreateProjectHereCommand.ExecuteAsync(null);
        Assert.True(vm.HasProject);
        Assert.True(File.Exists(Path.Combine(folder, ProjectFile.FileName)));
        await vm.DisposeAsync();
    });

    [Fact]
    public void ADroppedSketchFileOpensItsFolder() => OnSta(async () =>
    {
        string directory = NewProject("Dropped");
        string sketch = Directory.EnumerateFiles(directory, "*.ino", SearchOption.AllDirectories).Single();
        MainViewModel vm = NewViewModel();

        await vm.OpenDroppedAsync(sketch);
        Assert.Equal(Path.GetFullPath(directory), vm.ProjectDirectory);
        await vm.DisposeAsync();
    });

    [Fact]
    public void TheRyadomModeKeepsCanvasAndCodeOnScreen() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        Assert.True(await vm.OpenAsync(NewProject()));
        CodeDocumentViewModel document = vm.Documents.First();

        vm.SelectedTab = document;
        Assert.False(vm.IsCanvasVisible);
        vm.SplitView = true;
        Assert.True(vm.IsCanvasVisible);
        Assert.Same(document, vm.VisibleDocument);

        vm.SelectedTab = SceneTab.Instance;
        Assert.Same(document, vm.VisibleDocument);

        vm.SplitView = false;
        Assert.Null(vm.VisibleDocument);
        vm.FlipCodeCommand.Execute(null);
        Assert.Same(document, vm.SelectedTab);
        vm.FlipCodeCommand.Execute(null);
        Assert.IsType<SceneTab>(vm.SelectedTab);
        await vm.DisposeAsync();
    });

    [Fact]
    public void TheExplorerWorksWithSketchFiles() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        Assert.True(await vm.OpenAsync(NewProject()));
        vm.LeftTab = MainViewModel.ProjectPage;
        ExplorerGroup sketch = vm.ExplorerGroups.Single(g => g.Key == "sketch");
        Assert.True(sketch.Items.Single().IsMain);

        Assert.NotNull(vm.CreateSketchFile("bad|name.h"));
        Assert.NotNull(vm.CreateSketchFile("notes.txt"));
        Assert.Null(vm.CreateSketchFile("config"));
        CodeDocumentViewModel created = Assert.IsType<CodeDocumentViewModel>(vm.SelectedTab);
        Assert.Equal("config.h", created.Title);
        Assert.NotNull(vm.CreateSketchFile("config.h"));

        ExplorerEntry entry = vm.ExplorerGroups.Single(g => g.Key == "sketch").Items.Single(i => i.Title == "config.h");
        Assert.Null(vm.RenameSketchFile(entry, "pins.h"));
        Assert.Equal("pins.h", Assert.IsType<CodeDocumentViewModel>(vm.SelectedTab).Title);

        Assert.Contains(vm.ExplorerGroups.Single(g => g.Key == "scene").Items, i => i.Kind == ExplorerKind.Part);
        await vm.DisposeAsync();
    });

    [Fact]
    public void AFailedBuildGivesThePanelsBack() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        if (new FirmwareBuildService().FindToolchain() is null)
        {
            Assert.Skip("The toolchain is not installed yet.");
        }

        Assert.True(await vm.OpenAsync(NewProject()));
        vm.SelectedBottomTab = MainViewModel.PlotterTab;
        CodeDocumentViewModel document = vm.Documents.First();
        string good = document.Document.Text;
        document.Document.Text = "void setup() { int broken = ; }\nvoid loop() {}\n";

        await vm.BuildCommand.ExecuteAsync(null);
        Assert.Equal(SimStatus.BuildFailed, vm.Status);
        Assert.Equal(MainViewModel.BuildTab, vm.SelectedBottomTab);
        Assert.Same(document, vm.SelectedTab);
        Assert.Equal(1, vm.BuildErrors);

        document.Document.Text = good;
        await vm.BuildCommand.ExecuteAsync(null);
        Assert.Equal(MainViewModel.PlotterTab, vm.SelectedBottomTab);
        Assert.IsType<SceneTab>(vm.SelectedTab);
        await vm.DisposeAsync();
    });

    [Fact]
    public void EveryBoardHasAName()
    {
        string[] missing = [.. BoardCatalog.All.Where(b => Localization.Strings.TryGet("Board_" + b.Id) is null).Select(b => b.Id)];
        Assert.Empty(missing);
    }

    [Fact]
    public void AFileInTheOldWindowsEncodingIsReadWithoutDamage() => OnSta(() =>
    {
        string path = Path.Combine(_root, "old.ino");
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        File.WriteAllBytes(path, System.Text.Encoding.GetEncoding(1251).GetBytes("// Привет\n"));

        var document = new CodeDocumentViewModel(path);

        Assert.True(document.LegacyEncoding);
        Assert.Equal("// Привет\n", document.Document.Text);
        return Task.CompletedTask;
    });

    [Fact]
    public void AConflictStopsSavingAtItsTab() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        string directory = NewProject();
        Assert.True(await vm.OpenAsync(directory));
        CodeDocumentViewModel document = vm.Documents.First();
        document.Document.Insert(0, "// mine\n");
        File.WriteAllText(document.Path, "// theirs\n");
        document.OnDiskChanged();
        Assert.True(document.HasConflict);

        vm.SaveCommand.Execute(null);

        Assert.Equal("// theirs\n", File.ReadAllText(document.Path));
        Assert.Same(document, vm.SelectedTab);
        Assert.Equal(BannerKind.Info, vm.Banner);
        await vm.DisposeAsync();
    });

    [Fact]
    public void SaveAsCopiesTheProjectAndOpensTheCopy() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        string directory = NewProject("Original");
        string target = Path.Combine(_root, "Copy");
        Directory.CreateDirectory(target);
        vm.PickFolder = _ => target;
        Assert.True(await vm.OpenAsync(directory));

        await vm.SaveProjectAsCommand.ExecuteAsync(null);

        Assert.Equal(Path.GetFullPath(target), vm.ProjectDirectory);
        Assert.True(File.Exists(Path.Combine(target, ProjectFile.FileName)));
        Assert.True(File.Exists(Path.Combine(directory, ProjectFile.FileName)));
        await vm.DisposeAsync();
    });

    [Fact]
    public void AllPanelsHideAndComeBackAsTheyWere() => OnSta(async () =>
    {
        MainViewModel vm = NewViewModel();
        vm.PaletteOpen = true;
        vm.PropertiesOpen = false;
        vm.BottomOpen = true;

        vm.ToggleAllPanelsCommand.Execute(null);
        Assert.False(vm.PaletteOpen || vm.PropertiesOpen || vm.BottomOpen);

        vm.ToggleAllPanelsCommand.Execute(null);
        Assert.True(vm.PaletteOpen);
        Assert.False(vm.PropertiesOpen);
        Assert.True(vm.BottomOpen);
        await vm.DisposeAsync();
    });

    // ---- search ------------------------------------------------------------------------------

    [Theory]
    [InlineData("датчик", "dht")]
    [InlineData("экран", "oled")]
    [InlineData("мотор", "servo")]
    [InlineData("кнопки", "pushbutton")]
    [InlineData("тумблер", "slide-switch")]
    [InlineData("hcsr", "hc-sr04")]
    [InlineData("hc-sr", "hc-sr04")]
    [InlineData("светодиодная", "led")]
    [InlineData("сетодиод", "led")]
    [InlineData("lfnxbr", "dht")] // "датчик" on the wrong keyboard layout
    public void ThePaletteFindsPartsByEverydayWords(string query, string type)
    {
        var palette = new PaletteViewModel(ComponentRegistry.CreateDefault(BoardCatalog.All)) { Search = query };
        Assert.Contains(palette.Groups.SelectMany(g => g.Items), i => i.Type == type);
        Assert.False(palette.HasNoMatches);
    }

    [Fact]
    public void ThePaletteSaysWhenNothingMatches()
    {
        var palette = new PaletteViewModel(ComponentRegistry.CreateDefault(BoardCatalog.All)) { Search = "zzzqq" };
        Assert.True(palette.HasNoMatches);
        Assert.Null(palette.Highlighted);
    }

    [Fact]
    public void LedDoesNotFindOleds()
    {
        var palette = new PaletteViewModel(ComponentRegistry.CreateDefault(BoardCatalog.All)) { Search = "led" };
        Assert.DoesNotContain(palette.Groups.SelectMany(g => g.Items), i => i.Type == "oled");
    }

    [Fact]
    public void ThePaletteKeyboardMovesTheHighlight()
    {
        var palette = new PaletteViewModel(ComponentRegistry.CreateDefault(BoardCatalog.All)) { Search = "светодиод" };
        PaletteItem first = Assert.IsType<PaletteItem>(palette.Highlighted);
        palette.MoveHighlight(1);
        Assert.NotEqual(first, palette.Highlighted);
        palette.MoveHighlight(-1);
        Assert.Equal(first, palette.Highlighted);
        palette.ClearSearchCommand.Execute(null);
        Assert.Null(palette.Highlighted);
    }

    [Fact]
    public void DisplaysStartFoldedAndTheChoiceIsRemembered()
    {
        var palette = new PaletteViewModel(ComponentRegistry.CreateDefault(BoardCatalog.All));
        Assert.Equal("output", palette.Groups[0].Key);
        Assert.Equal("passive", palette.Groups[1].Key);
        Assert.False(palette.Groups.Single(g => g.Key == "display").IsExpanded);
        Assert.Null(palette.CollapsedCategories);

        palette.Groups.Single(g => g.Key == "display").IsExpanded = true;
        palette.Groups.Single(g => g.Key == "sensor").IsExpanded = false;
        IReadOnlyList<string> remembered = Assert.IsAssignableFrom<IReadOnlyList<string>>(palette.CollapsedCategories);
        Assert.Equal(["sensor"], remembered);

        var again = new PaletteViewModel(ComponentRegistry.CreateDefault(BoardCatalog.All));
        again.SetCollapsed(remembered);
        Assert.True(again.Groups.Single(g => g.Key == "display").IsExpanded);
        Assert.False(again.Groups.Single(g => g.Key == "sensor").IsExpanded);
    }

    [Fact]
    public void EveryPartCanBeFoundByKeywords()
    {
        string[] missing = [.. ComponentRegistry.CreateDefault(BoardCatalog.All).Definitions
            .Where(d => d.Category != "board" && Localization.Strings.TryGet("Keywords_" + d.Type) is null)
            .Select(d => d.Type)];
        Assert.Empty(missing);
    }

    [Fact]
    public void EveryExampleHasItsTextsAndATopic()
    {
        string examples = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "examples"));
        string[] folders = [.. Directory.EnumerateDirectories(examples).Select(d => Path.GetFileName(d)!)];
        Assert.NotEmpty(folders);
        foreach (string name in folders)
        {
            Assert.Contains(name, ExampleCatalog.Names, StringComparer.OrdinalIgnoreCase);
            Assert.NotNull(Localization.Strings.TryGet("Example_" + name));
            Assert.NotNull(Localization.Strings.TryGet("Example_" + name + "_Desc"));
            Assert.NotNull(Localization.Strings.TryGet("Example_" + name + "_Try"));
        }
    }

    // Three examples once shipped corners with their numbers missing; the loader drops such points silently.
    [Fact]
    public void EveryExampleWaypointHasTwoNumbers()
    {
        string examples = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "examples"));
        foreach (string file in Directory.EnumerateFiles(examples, ProjectFile.FileName, SearchOption.AllDirectories))
        {
            Assert.DoesNotMatch(@"\[\s*\d*\s*,\s*\]", File.ReadAllText(file));
        }
    }

    // ---- property fields -------------------------------------------------------------------------

    [Theory]
    [InlineData("10k", 10000)]
    [InlineData("4,7к", 4700)]
    [InlineData("4.7k", 4700)]
    [InlineData("1М", 1000000)]
    [InlineData("220 Ом", 220)]
    [InlineData("100m", 0.1)]
    [InlineData("5", 5)]
    public void NumbersAreReadAsPeopleTypeThem(string text, double expected) =>
        Assert.Equal(expected, PropertyField.ParseNumber(text)!.Value, 6);

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    public void NonsenseIsNotANumber(string text) => Assert.Null(PropertyField.ParseNumber(text));

    // ---- compiler messages --------------------------------------------------------------------------

    [Fact]
    public void CommonCompilerMessagesAreExplainedAndFixed()
    {
        var semicolon = new BuildDiagnostic(DiagnosticSeverity.Error, "expected ';' after expression", "a.ino", 3, 10);
        Assert.Contains("точка с запятой", DiagnosticHints.Explain(semicolon)!, StringComparison.Ordinal);
        Assert.Equal(";", DiagnosticHints.FixFor(semicolon)!.Insert);

        var typo = new BuildDiagnostic(DiagnosticSeverity.Error, "use of undeclared identifier 'digitalWrit'; did you mean 'digitalWrite'?", "a.ino", 3, 3);
        Assert.Contains("digitalWrite", DiagnosticHints.Explain(typo)!, StringComparison.Ordinal);
        DiagnosticFix fix = DiagnosticHints.FixFor(typo)!;
        Assert.Equal(11, fix.Remove);
        Assert.Equal("digitalWrite", fix.Insert);

        var unknown = new BuildDiagnostic(DiagnosticSeverity.Error, "some message no rule knows", "a.ino", 3, 3);
        Assert.Null(DiagnosticHints.Explain(unknown));
        Assert.Null(DiagnosticHints.FixFor(unknown));
    }

    [Fact]
    public void AFixIsOneEditAndMarksFollowTheText() => OnSta(() =>
    {
        string path = Path.Combine(_root, "a.ino");
        File.WriteAllText(path, "int a = 1\nint b = 2;\nint c = ;\n");
        var document = new CodeDocumentViewModel(path);
        var missing = new BuildDiagnostic(DiagnosticSeverity.Error, "expected ';' after top level declarator", path, 1, 10);
        var later = new BuildDiagnostic(DiagnosticSeverity.Error, "expected expression", path, 3, 9);
        document.Diagnostics = [later];

        document.Apply(missing, DiagnosticHints.FixFor(missing)!);
        Assert.StartsWith("int a = 1;\n", document.Document.Text, StringComparison.Ordinal);

        // A line added above moves the mark; an edit on the marked line drops it.
        document.Document.Insert(0, "// note\n");
        Assert.Equal(4, document.Diagnostics.Single().Line);
        document.Document.Insert(document.Document.GetLineByNumber(4).Offset, "x");
        Assert.Empty(document.Diagnostics);
        return Task.CompletedTask;
    });

    [Fact]
    public void CompilerColumnsCountBytes() => OnSta(() =>
    {
        string path = Path.Combine(_root, "b.ino");
        File.WriteAllText(path, "// Привет\n");
        var document = new CodeDocumentViewModel(path);
        // "// " is 3 bytes, each Cyrillic letter 2: column 16 in bytes is character 10.
        Assert.Equal(10, document.CharColumn(new BuildDiagnostic(DiagnosticSeverity.Error, "x", path, 1, 16)));
        return Task.CompletedTask;
    });

    // ---- the language server -----------------------------------------------------------------------

    [Fact]
    public async Task ClangdKnowsTheArduinoApi()
    {
        if (new FirmwareBuildService().FindToolchain() is not { } toolchain)
        {
            Assert.Skip("The toolchain is not installed yet.");
            return;
        }

        string directory = NewProject("Lsp");
        ProjectFile project = ProjectFile.Load(directory);
        string sketch = Directory.EnumerateFiles(directory, "*.ino", SearchOption.AllDirectories).Single();
        const string Code = "void setup() {\n  digitalWrite(13, HIGH);\n  Serial.\n}\nvoid loop() {}\n";
        File.WriteAllText(sketch, Code);

        await using ClangdClient? client = await ClangdClient.StartAsync(
            Path.Combine(toolchain.Root, "bin", "clangd.exe"), directory, FirmwareBuildService.EditorFlags(project, directory, BoardCatalog.All[0]), NullLogger.Instance);
        Assert.NotNull(client);
        await client.UpdateAsync(sketch, Code);

        // The outline waits for clangd to have read the headers; until then completion only knows the file's words.
        IReadOnlyList<SymbolEntry> symbols = await client.SymbolsAsync(sketch);
        Assert.Contains(symbols, s => s.Name == "setup");

        IReadOnlyList<CompletionEntry> members = await client.CompleteAsync(sketch, 3, 10);
        Assert.Contains(members, e => e.Label.Contains("println", StringComparison.Ordinal));

        string? hover = await client.HoverAsync(sketch, 2, 5);
        Assert.Contains("digitalWrite", hover ?? "", StringComparison.Ordinal);

        SourceLocationEntry? definition = await client.DefinitionAsync(sketch, 2, 5);
        Assert.NotNull(definition);
        Assert.EndsWith(".h", definition.Path, StringComparison.OrdinalIgnoreCase);
    }
}
