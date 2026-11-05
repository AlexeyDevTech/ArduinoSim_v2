using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using ArduSim.App.Localization;
using ArduSim.App.Services;
using ArduSim.Boards;
using ArduSim.Core.Boards;
using ArduSim.Core.Editing;
using ArduSim.Core.Projects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArduSim.App.ViewModels;

/// <summary>A topic of the examples gallery.</summary>
public sealed record ExampleGroup(string Name, IReadOnlyList<ExampleItem> Items);

/// <summary>A board on the start view: a click creates a project for it.</summary>
public sealed partial class BoardTile(BoardProfile board, string summary, bool isLast) : ObservableObject
{
    public BoardProfile Board { get; } = board;

    public string Summary { get; } = summary;

    /// <summary>The board the last new project used.</summary>
    public bool IsLast { get; } = isLast;

    [ObservableProperty]
    public partial System.Windows.Media.ImageSource? Thumbnail { get; set; }
}

/// <summary>The start view (docs/UI.md §2.7): new projects by board tile, the examples gallery, and opening folders.</summary>
public sealed partial class MainViewModel
{
    private static readonly string[] Families = ["avr", "esp32", "esp8266"];
    private string? _noProjectFolder;

    public ObservableCollection<ExampleGroup> ExampleGroups { get; } = [];

    public IReadOnlyList<BoardTile> BoardTiles { get; private set; } = [];

    /// <summary>Все, Arduino, ESP32, ESP8266 with their counts.</summary>
    public IReadOnlyList<OptionItem> ExampleFamilies { get; private set; } = [];

    [ObservableProperty]
    public partial OptionItem? SelectedFamily { get; set; }

    [ObservableProperty]
    public partial string ExampleSearch { get; set; } = "";

    [ObservableProperty]
    public partial string NewProjectName { get; set; } = "";

    [ObservableProperty]
    public partial string NewProjectError { get; set; } = "";

    /// <summary>The open project is a copy of this bundled example: Properties says what to try.</summary>
    [ObservableProperty]
    public partial ExampleItem? ExampleInfo { get; set; }

    /// <summary>First launch: nothing was opened yet, so the start view suggests the first steps.</summary>
    public bool ShowFirstSteps => Recent.Count == 0;

    public bool NoExampleMatches => ExampleGroups.Count == 0;

    public ExampleItem? BlinkExample => Examples.FirstOrDefault(e => e.Name == "Blink");

    /// <summary>Tells a first-time user about the compiler download, so it can start early.</summary>
    public string? ToolchainNote => _builds.FindToolchain() is null ? Strings.Format("Start_ToolchainNote", _builds.Manifest.SizeBytes / (1024 * 1024)) : null;

    /// <summary>The example's block in Properties shows while nothing is selected.</summary>
    public bool ShowExampleInfo => ExampleInfo is not null && Properties?.Mode == PropertiesMode.Project;

    partial void OnExampleInfoChanged(ExampleItem? value) => OnPropertyChanged(nameof(ShowExampleInfo));

    public string NoExamplesText => Strings.Format("Start_NoExamples", ExampleSearch.Trim());

    partial void OnSelectedFamilyChanged(OptionItem? value) => RebuildExamples();

    partial void OnExampleSearchChanged(string value) => RebuildExamples();

    private void InitializeStartView()
    {
        ExampleFamilies =
        [
            new("", Strings.Format("Start_FamilyCount", Strings.Get("Start_FamilyAll"), Examples.Count), null),
            .. Families.Select(core => new OptionItem(core, Strings.Format("Start_FamilyCount", Strings.Get("Family_" + core), Examples.Count(e => e.Core == core)), null)),
        ];
        SelectedFamily = ExampleFamilies[0];
        BoardProfile? last = _state.LastBoard is { } id ? BoardCatalog.Find(id) : null;
        if (last is not null)
        {
            SelectedBoard = last;
        }

        BoardTiles = [.. Boards.Select(b => new BoardTile(b, BoardSummary(b), b.Id == last?.Id))];
        NewProjectName = NextFreeName(Strings.Get("Start_DefaultName"));
    }

    private static string BoardSummary(BoardProfile board)
    {
        string volts = board.LogicVoltage.ToString("0.#", CultureInfo.CurrentCulture);
        return board.HasWifi ? Strings.Format("Board_SummaryWifi", volts) : Strings.Format("Board_Summary", volts, board.Pins.Count);
    }

    private static string NextFreeName(string stem)
    {
        for (int n = 1; ; n++)
        {
            string name = string.Create(CultureInfo.CurrentCulture, $"{stem} {n}");
            if (!Directory.Exists(Path.Combine(AppEnvironment.ProjectsDirectory, name)))
            {
                return name;
            }
        }
    }

    private void RebuildExamples()
    {
        string[] tokens = PartSearch.Normalize(ExampleSearch).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        ExampleGroups.Clear();
        foreach (string topic in ExampleCatalog.Topics)
        {
            List<ExampleItem> items = [.. Examples
                .Where(e => e.Topic == topic && (string.IsNullOrEmpty(SelectedFamily?.Value) || e.Core == SelectedFamily.Value))
                .Where(e => tokens.All(e.SearchText.Contains))
                .OrderBy(e => e.Order)];
            if (items.Count > 0)
            {
                ExampleGroups.Add(new ExampleGroup(Strings.Get("ExampleTopic_" + topic), items));
            }
        }

        OnPropertyChanged(nameof(NoExampleMatches));
        OnPropertyChanged(nameof(NoExamplesText));
    }

    /// <summary>Ctrl+N and Новый проект…: back to the start view, with the name box ready.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task NewProjectAsync()
    {
        if (!ConfirmDiscard())
        {
            return;
        }

        await StopSessionAsync();
        CloseProject();
        Status = SimStatus.Idle;
        NewProjectName = NextFreeName(Strings.Get("Start_DefaultName"));
        NewProjectError = "";
        FocusNewProjectRequested?.Invoke();
    }

    /// <summary>A board tile: creates the project under the projects folder and opens it.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task CreateProjectAsync(BoardTile? tile)
    {
        if (tile is null)
        {
            return;
        }

        string name = NewProjectName.Trim();
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            NewProjectError = Strings.Get("Start_NameInvalid");
            return;
        }

        string directory = Path.Combine(AppEnvironment.ProjectsDirectory, name);
        if (Directory.Exists(directory))
        {
            NewProjectError = Strings.Format("Start_NameTaken", name);
            return;
        }

        NewProjectError = "";
        _state.LastBoard = tile.Board.Id;
        SelectedBoard = tile.Board;
        await CreateAndOpenAsync(directory, tile.Board.Id);
    }

    /// <summary>Другая папка…: an empty folder gets the project, one with a project or a sketch simply opens.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task CreateInFolderAsync()
    {
        if (PickFolder?.Invoke(Strings.Get("Dialog_NewProject")) is not { } directory)
        {
            return;
        }

        bool empty = !Directory.EnumerateFileSystemEntries(directory).Any();
        bool holdsProject = File.Exists(Path.Combine(directory, ProjectFile.FileName)) || PlatformIoOrSketch(directory);
        if (holdsProject)
        {
            await OpenProjectAsync(directory);
        }
        else
        {
            await CreateAndOpenAsync(empty ? directory : Path.Combine(directory, NewProjectName.Trim()), SelectedBoard.Id);
        }
    }

    private static bool PlatformIoOrSketch(string directory) =>
        Build.Import.PlatformIoProject.ExistsIn(directory) || Directory.EnumerateFiles(directory, "*.ino").Any();

    /// <summary>The banner's Создать проект здесь, for a folder without a project or a sketch.</summary>
    [RelayCommand]
    private async Task CreateProjectHereAsync()
    {
        if (_noProjectFolder is { } folder)
        {
            _noProjectFolder = null;
            HideBanner();
            await CreateAndOpenAsync(folder, SelectedBoard.Id);
        }
    }

    private async Task CreateAndOpenAsync(string directory, string board)
    {
        try
        {
            Directory.CreateDirectory(directory);
            ProjectTemplate.Create(directory, board);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowBanner(BannerKind.Info, Strings.Get("Banner_CreateFailed"), e.Message, "");
            return;
        }

        await OpenProjectAsync(directory);
    }

    /// <summary>A folder or a file dropped on the window, or passed on the command line.</summary>
    public async Task OpenDroppedAsync(string path)
    {
        if (CanEdit && ConfirmDiscard())
        {
            await OpenProjectAsync(path);
        }
    }

    /// <summary>The folder to open for a dropped path: an .ino's folder, or the project above a <c>sketch</c> folder.</summary>
    private static string ProjectFolderFor(string path)
    {
        string directory = File.Exists(path) ? Path.GetDirectoryName(Path.GetFullPath(path))! : path;
        if (!File.Exists(Path.Combine(directory, ProjectFile.FileName))
            && Path.GetDirectoryName(Path.GetFullPath(directory)) is { } parent
            && File.Exists(Path.Combine(parent, ProjectFile.FileName)))
        {
            return parent;
        }

        return directory;
    }

    /// <summary>A plain Arduino sketch gets a project file beside it, and stays where it is. False, with a banner, for a folder with no sketch.</summary>
    private bool OpenSketchFolder(string directory)
    {
        if (!Directory.Exists(directory) || !Directory.EnumerateFiles(directory, "*.ino").Any())
        {
            _noProjectFolder = Directory.Exists(directory) ? directory : null;
            ShowBanner(BannerKind.NoProject, Strings.Get("Banner_NoProjectTitle"), Strings.Format("Banner_NoProjectBody", directory), "");
            return false;
        }

        try
        {
            ProjectTemplate.CreateAround(directory, SelectedBoard.Id, ".", new ProjectSettings());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowBanner(BannerKind.Info, Strings.Get("Banner_OpenFailed"), e.Message, "");
            return false;
        }

        ShowToast(Strings.Format("Toast_SketchOpened", SelectedBoard.DisplayName));
        return true;
    }

    /// <summary>After a project opened: the hint setting, and for an example what to try and which panel reports.</summary>
    private void OnProjectOpened()
    {
        OnPropertyChanged(nameof(ShowFirstSteps));
        _ = StartClangdAsync();
        if (Scene is not null)
        {
            Scene.HintsEnabled = CanvasHints;
            Scene.WheelPans = WheelPans;
        }

        ExampleInfo = FindExampleOf(_projectDirectory);
        if (ExampleInfo is not { } example)
        {
            return;
        }

        SelectedBottomTab = example.Panel;
        if (example.Name == "LogicAnalyzerDemo")
        {
            Timeline.AddDemoRows();
        }
    }

    private ExampleItem? FindExampleOf(string? directory)
    {
        if (directory is null || !string.Equals(Path.GetDirectoryName(directory), Path.GetFullPath(AppEnvironment.ProjectsDirectory), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string name = CopyNumber().Replace(Path.GetFileName(directory), "");
        return Examples.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@" \d+$")]
    private static partial Regex CopyNumber();

    /// <summary>Показать код in the example's block: the sketch's main file.</summary>
    [RelayCommand]
    private void ShowMainCode()
    {
        if ((_lastDocument ?? Documents.FirstOrDefault()) is { } document)
        {
            SelectedTab = document;
            document.RequestFocus();
        }
    }

    /// <summary>Начать заново: a fresh copy of the bundled example under the next free name; the edited copy stays.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task StartExampleOverAsync()
    {
        if (ExampleInfo is not { } example || !ConfirmDiscard())
        {
            return;
        }

        string target;
        try
        {
            target = AppEnvironment.CopyExample(example.Path, NextFreeName(example.Name));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowBanner(BannerKind.Info, Strings.Get("Banner_CreateFailed"), e.Message, "");
            return;
        }

        await OpenProjectAsync(target);
    }

    /// <summary>The project's block in Properties shows while nothing is selected.</summary>
    public bool ShowProjectActions => Properties?.Mode == PropertiesMode.Project;

    /// <summary>The first click asks, as nothing can bring the data back.</summary>
    [ObservableProperty]
    public partial bool ClearEepromArmed { get; set; }

    public string ClearEepromLabel => Strings.Get(ClearEepromArmed ? "Properties_ClearEepromSure" : "Properties_ClearEeprom");

    partial void OnClearEepromArmedChanged(bool value) => OnPropertyChanged(nameof(ClearEepromLabel));

    partial void OnPropertiesChanged(PropertiesViewModel? value)
    {
        OnPropertyChanged(nameof(ShowExampleInfo));
        OnPropertyChanged(nameof(ShowProjectActions));
    }

    /// <summary>Очистить EEPROM: removes the saved EEPROM and Preferences of the project; the next run starts blank.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void ClearEeprom()
    {
        if (_projectDirectory is not { } directory)
        {
            return;
        }

        if (!ClearEepromArmed)
        {
            ClearEepromArmed = true;
            return;
        }

        ClearEepromArmed = false;
        try
        {
            bool any = false;
            foreach (string name in (string[])[ProjectFile.EepromFileName, ProjectFile.PreferencesFileName])
            {
                string file = Path.Combine(directory, name);
                if (File.Exists(file))
                {
                    File.Delete(file);
                    any = true;
                }
            }

            ShowToast(Strings.Get(any ? "Toast_EepromCleared" : "Toast_EepromEmpty"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowBanner(BannerKind.Info, Strings.Get("Banner_DeleteFailed"), e.Message, "");
        }
    }

    /// <summary>Сохранить как…: the project with its files in another folder, which then stays open.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task SaveProjectAsAsync()
    {
        if (_projectDirectory is not { } source || !SaveAll() || PickFolder?.Invoke(Strings.Get("Dialog_SaveAs")) is not { } picked)
        {
            return;
        }

        // An empty folder is the copy itself; otherwise the copy gets its own folder inside it.
        string target = Directory.EnumerateFileSystemEntries(picked).Any() ? Path.Combine(picked, Path.GetFileName(source)) : picked;
        string from = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if ((Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).StartsWith(from, StringComparison.OrdinalIgnoreCase)
            || File.Exists(Path.Combine(target, ProjectFile.FileName)))
        {
            ShowBanner(BannerKind.Info, Strings.Get("Banner_CreateFailed"), Strings.Format("Banner_SaveAsTaken", target), "");
            return;
        }

        try
        {
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(target, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowBanner(BannerKind.Info, Strings.Get("Banner_CreateFailed"), e.Message, "");
            return;
        }

        await OpenProjectAsync(target);
    }

    /// <summary>The bare board on the canvas, for its tile on the start view.</summary>
    public SceneViewModel? PreviewBoard(BoardProfile board)
    {
        ArgumentNullException.ThrowIfNull(board);
        var document = new SceneDocument { Components = [new ComponentDocument { Id = ProjectTemplate.BoardId, Type = board.Id, X = 0, Y = 0 }] };
        return new SceneViewModel(new SceneEditor(SceneModel.FromDocument(document), _registry), board, (_, _, _) => { });
    }
}
