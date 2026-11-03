using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Threading;
using ArduSim.App.Localization;
using ArduSim.App.Services;
using ArduSim.Boards;
using ArduSim.Build.Compilation;
using ArduSim.Build.Toolchains;
using ArduSim.Core.Boards;
using ArduSim.Core.Components;
using ArduSim.Core.Editing;
using ArduSim.Core.Projects;
using ArduSim.Ipc;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ArduSim.App.ViewModels;

public enum SimStatus
{
    Idle,
    Building,
    BuildFailed,
    Starting,
    Running,
    Paused,
    Busy,
    Crashed,
    Stopped,
}

public enum UnsavedChoice
{
    Save,
    Discard,
    Cancel,
}

/// <summary>A bundled example; the window renders its scene into <see cref="Thumbnail"/> for the start view.</summary>
public sealed partial class ExampleItem(string name, string path) : ObservableObject
{
    public string Name { get; } = name;

    public string Path { get; } = path;

    [ObservableProperty]
    public partial ImageSource? Thumbnail { get; set; }
}

public sealed record SpeedOption(string Label, double Value);

/// <param name="Setting">The value stored in the project (<c>settings.serial.lineEnding</c>).</param>
public sealed record LineEndingOption(string Label, string Text, string Setting);

public sealed record LogItem(string Time, int Severity, string Message, string? ComponentId);

/// <summary>A parsed compiler message in the Сборка tab.</summary>
public sealed record DiagnosticItem(BuildDiagnostic Diagnostic)
{
    public string Location => Diagnostic.File is null ? "" : $"{Path.GetFileName(Diagnostic.File)}:{Diagnostic.Line}";

    public string Message => Diagnostic.Message;

    public int Severity => Diagnostic.Severity == DiagnosticSeverity.Warning ? 1 : 2;
}

/// <summary>
/// The main window: one project at a time — its scene, sketch files and runs (docs/UI.md §2).
/// Talks to the simulation only through <see cref="ISimulationSession"/> (ADR-003).
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    public const int SerialTab = 0;
    public const int PlotterTab = 1;
    public const int BuildTab = 2;
    public const int LogTab = 3;

    private const int SerialLimit = 200_000;
    private static readonly string[] SourceExtensions = [".ino", ".h", ".hpp", ".c", ".cpp"];

    private readonly AppEnvironment _environment;
    private readonly FirmwareBuildService _builds;
    private readonly UserState _state;
    private readonly ILogger<MainViewModel> _logger;
    private readonly Dispatcher _dispatcher;
    private readonly ComponentRegistry _registry = ComponentRegistry.CreateDefault(BoardCatalog.All);
    private readonly StringBuilder _serial = new();
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private ISimulationSession? _session;
    private string? _projectDirectory;
    private ProjectFile? _project;
    private ProjectSettings _settings = new();
    private BoardProfile? _board;
    private string? _firmwarePath;
    private CancellationTokenSource? _toolchainCancel;
    private FileSystemWatcher? _watcher;
    private int _revision;
    private int _savedRevision;
    private bool _settingsDirty;
    private bool _loading;
    private ulong _timeNs;
    private (double Wall, ulong TimeNs)? _speedSample;
    private int _diagnosticIndex = -1;
    private SourceLocation? _crashLocation;

    public MainViewModel(AppEnvironment environment, FirmwareBuildService builds, UserState state, ILogger<MainViewModel> logger)
    {
        _environment = environment;
        _builds = builds;
        _state = state;
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;
        Examples = [.. AppEnvironment.Examples().Select(e => new ExampleItem(e.Name, e.Path))];
        Recent = [.. state.Recent];
        Palette = new PaletteViewModel(_registry);
        Speeds = [new("0.1×", 0.1), new("0.5×", 0.5), new("1×", 1), new("2×", 2), new("10×", 10), new(Strings.Get("Speed_Max"), double.PositiveInfinity)];
        LineEndings = [new(Strings.Get("Serial_NoEnding"), "", "none"), new("LF", "\n", "LF"), new("CR", "\r", "CR"), new("CR+LF", "\r\n", "CRLF")];
        SelectedSpeed = Speeds[2];
        SelectedLineEnding = LineEndings[1];
        SelectedBoard = Boards[0];
        Themes = [new("dark", Strings.Get("Theme_Dark"), null), new("light", Strings.Get("Theme_Light"), null)];
        SelectedTheme = Themes.FirstOrDefault(t => t.Value == state.Theme) ?? Themes[0];
        PanelLayout layout = state.Layout;
        PaletteOpen = layout.PaletteOpen;
        PropertiesOpen = layout.PropertiesOpen;
        BottomOpen = layout.BottomOpen;
        SelectedBottomTab = Math.Clamp(layout.BottomTab, SerialTab, LogTab);
        Status = SimStatus.Idle;
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            Toast = "";
        };
    }

    /// <summary>Asks Сохранить / Не сохранять / Отмена (set by the window).</summary>
    public Func<string, UnsavedChoice>? AskToSave { get; set; }

    /// <summary>Picks a folder (set by the window): (title) → path or null.</summary>
    public Func<string, string?>? PickFolder { get; set; }

    public ObservableCollection<ExampleItem> Examples { get; }

    public ObservableCollection<RecentProject> Recent { get; }

    public PaletteViewModel Palette { get; }

    public PlotterViewModel Plotter { get; } = new();

    public IReadOnlyList<BoardProfile> Boards { get; } = BoardCatalog.All;

    public IReadOnlyList<SpeedOption> Speeds { get; }

    public IReadOnlyList<LineEndingOption> LineEndings { get; }

    /// <summary>Тёмная / Светлая (Параметры, docs/UI.md §3.1).</summary>
    public IReadOnlyList<OptionItem> Themes { get; }

    /// <summary>The schematic tab first, then one tab per open sketch file.</summary>
    public ObservableCollection<object> Tabs { get; } = [];

    public IEnumerable<CodeDocumentViewModel> Documents => Tabs.OfType<CodeDocumentViewModel>();

    public ObservableCollection<LogItem> Log { get; } = [];

    public ObservableCollection<DiagnosticItem> Diagnostics { get; } = [];

    public bool InProcess => _environment.InProcess;

    public bool HasProject => _projectDirectory is not null;

    public string? ProjectDirectory => _projectDirectory;

    public string WindowTitle => HasProject ? $"{ProjectName}{(IsDirty ? " ●" : "")} — ArduSim" : "ArduSim";

    public string ToolchainText => _builds.FindToolchain() is null ? Strings.Get("Status_NoToolchain") : Strings.Format("Status_Toolchain", _builds.Manifest.Id);

    [ObservableProperty]
    public partial ExampleItem? SelectedExample { get; set; }

    [ObservableProperty]
    public partial SpeedOption SelectedSpeed { get; set; }

    [ObservableProperty]
    public partial LineEndingOption SelectedLineEnding { get; set; }

    [ObservableProperty]
    public partial OptionItem SelectedTheme { get; set; }

    [ObservableProperty]
    public partial BoardProfile SelectedBoard { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand), nameof(RedoCommand))]
    public partial SceneViewModel? Scene { get; set; }

    [ObservableProperty]
    public partial PropertiesViewModel? Properties { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSceneTab))]
    public partial object? SelectedTab { get; set; }

    public bool IsSceneTab => SelectedTab is SceneTab;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    public partial string ProjectName { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    public partial bool IsDirty { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsRunning), nameof(IsPaused), nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand), nameof(RunCommand), nameof(PauseCommand), nameof(StopCommand), nameof(StepLoopCommand), nameof(StepTimeCommand))]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand), nameof(RedoCommand), nameof(NewProjectCommand), nameof(OpenProjectCommand), nameof(OpenRecentCommand), nameof(OpenExampleCommand), nameof(CloseProjectCommand))]
    public partial SimStatus Status { get; set; }

    [ObservableProperty]
    public partial string ToolchainProgress { get; set; } = "";

    [ObservableProperty]
    public partial string VirtualTime { get; set; } = "";

    [ObservableProperty]
    public partial bool ClockInMilliseconds { get; set; }

    [ObservableProperty]
    public partial string ActualSpeed { get; set; } = "";

    [ObservableProperty]
    public partial string LastBuild { get; set; } = "";

    [ObservableProperty]
    public partial string SerialText { get; set; } = "";

    [ObservableProperty]
    public partial string SerialInput { get; set; } = "";

    [ObservableProperty]
    public partial bool AutoScroll { get; set; } = true;

    [ObservableProperty]
    public partial string BaudText { get; set; } = "";

    [ObservableProperty]
    public partial string BuildOutput { get; set; } = "";

    [ObservableProperty]
    public partial int SelectedBottomTab { get; set; }

    [ObservableProperty]
    public partial int UnseenWarnings { get; set; }

    [ObservableProperty]
    public partial bool PaletteOpen { get; set; }

    [ObservableProperty]
    public partial bool PropertiesOpen { get; set; }

    [ObservableProperty]
    public partial bool BottomOpen { get; set; }

    [ObservableProperty]
    public partial BannerKind Banner { get; set; }

    [ObservableProperty]
    public partial string BannerTitle { get; set; } = "";

    [ObservableProperty]
    public partial string BannerBody { get; set; } = "";

    [ObservableProperty]
    public partial string BannerDetails { get; set; } = "";

    [ObservableProperty]
    public partial bool HasCrashLocation { get; set; }

    [ObservableProperty]
    public partial string Toast { get; set; } = "";

    public string StatusText => Status switch
    {
        SimStatus.Building => ToolchainProgress.Length > 0 ? ToolchainProgress : Strings.Get("Status_Building"),
        SimStatus.BuildFailed => Strings.Get("Status_BuildFailed"),
        SimStatus.Starting => Strings.Get("Status_Starting"),
        SimStatus.Running => Strings.Get("Status_Running"),
        SimStatus.Paused => Strings.Get("Status_Paused"),
        SimStatus.Busy => Strings.Get("Status_Busy"),
        SimStatus.Crashed => Strings.Get("Status_Crashed"),
        SimStatus.Stopped => Strings.Get("Status_Stopped"),
        _ => Strings.Get("Status_Idle"),
    };

    public bool IsRunning => Status is SimStatus.Running or SimStatus.Paused or SimStatus.Busy;

    public bool IsPaused => Status == SimStatus.Paused;

    public bool CanEdit => !IsRunning && Status is not (SimStatus.Building or SimStatus.Starting);

    public async ValueTask DisposeAsync()
    {
        await StopSessionAsync();
        _watcher?.Dispose();
        _toolchainCancel?.Dispose();
    }

    /// <summary>Keyboard shortcuts of pushbuttons (part property <c>key</c>), live while a run is active.</summary>
    public bool OnKey(string key, bool down)
    {
        if (!IsRunning)
        {
            return false;
        }

        bool handled = false;
        foreach (PartViewModel part in Scene?.Parts.Where(p => p.Type == "pushbutton" && string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)) ?? [])
        {
            if (down && !part.Pressed)
            {
                part.Press();
            }
            else if (!down)
            {
                part.Release();
            }

            handled = true;
        }

        return handled;
    }

    /// <summary>Before anything replaces the project: offers to save. False when the user cancelled.</summary>
    public bool ConfirmDiscard()
    {
        if (!IsDirty)
        {
            return true;
        }

        UnsavedChoice choice = AskToSave?.Invoke(ProjectName) ?? UnsavedChoice.Discard;
        return choice switch
        {
            UnsavedChoice.Save => SaveAll(),
            UnsavedChoice.Discard => true,
            _ => false,
        };
    }

    public void SaveLayout(double paletteWidth, double propertiesWidth, double bottomHeight) =>
        _state.Layout = new PanelLayout
        {
            PaletteWidth = paletteWidth,
            PropertiesWidth = propertiesWidth,
            BottomHeight = bottomHeight,
            PaletteOpen = PaletteOpen,
            PropertiesOpen = PropertiesOpen,
            BottomOpen = BottomOpen,
            BottomTab = SelectedBottomTab,
        };

    public PanelLayout Layout => _state.Layout;

    /// <summary>Opens a project folder (start view, recent list, command line).</summary>
    public Task<bool> OpenAsync(string directory) => OpenProjectAsync(directory);

    // ---- projects ------------------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task NewProjectAsync()
    {
        if (!ConfirmDiscard() || PickFolder?.Invoke(Strings.Get("Dialog_NewProject")) is not { } directory)
        {
            return;
        }

        if (!File.Exists(Path.Combine(directory, ProjectFile.FileName)))
        {
            try
            {
                ProjectTemplate.Create(directory, SelectedBoard.Id);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                ShowBanner(BannerKind.Info, Strings.Get("Banner_CreateFailed"), e.Message, "");
                return;
            }
        }

        await OpenProjectAsync(directory);
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task OpenProjectAsync()
    {
        if (ConfirmDiscard() && PickFolder?.Invoke(Strings.Get("Dialog_OpenProject")) is { } directory)
        {
            await OpenProjectAsync(directory);
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task OpenRecentAsync(RecentProject? recent)
    {
        if (recent is null || !ConfirmDiscard())
        {
            return;
        }

        if (!File.Exists(Path.Combine(recent.Path, ProjectFile.FileName)))
        {
            _state.Forget(recent.Path);
            Recent.Remove(recent);
            ShowToast(Strings.Format("Toast_RecentMissing", recent.Path));
            return;
        }

        await OpenProjectAsync(recent.Path);
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task OpenExampleAsync(ExampleItem? example)
    {
        if (example is not null && ConfirmDiscard())
        {
            await OpenProjectAsync(AppEnvironment.OpenExample(example.Path));
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task CloseProjectAsync()
    {
        if (ConfirmDiscard())
        {
            await StopSessionAsync();
            CloseProject();
            Status = SimStatus.Idle;
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (HasProject && SaveAll())
        {
            ShowToast(Strings.Get("Toast_Saved"));
        }
    }

    [RelayCommand]
    private void RevealProject()
    {
        if (_projectDirectory is not null)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_projectDirectory}\"") { UseShellExecute = true })?.Dispose();
        }
    }

    /// <summary>The example's scene, not editable or runnable, for a gallery thumbnail; null when it cannot be read.</summary>
    public SceneViewModel? PreviewScene(ExampleItem example)
    {
        ArgumentNullException.ThrowIfNull(example);
        try
        {
            ProjectFile project = ProjectFile.Load(example.Path);
            return BoardCatalog.Find(project.Board) is { } board
                ? new SceneViewModel(new SceneEditor(SceneModel.FromDocument(project.Scene), _registry), board, (_, _, _) => { })
                : null;
        }
        catch (Exception e) when (e is ProjectFormatException or IOException)
        {
            return null;
        }
    }

    private async Task<bool> OpenProjectAsync(string directory)
    {
        await StopSessionAsync();
        ProjectFile project;
        BoardProfile board;
        try
        {
            project = ProjectFile.Load(directory);
            board = BoardCatalog.Find(project.Board) ?? throw new ProjectFormatException(Strings.Format("Banner_UnknownBoard", project.Board));
        }
        catch (ProjectFormatException e)
        {
            ShowBanner(BannerKind.Info, Strings.Get("Banner_OpenFailed"), e.Message, "");
            return false;
        }

        CloseProject();
        _loading = true;
        _projectDirectory = Path.GetFullPath(directory);
        _project = project;
        _settings = project.Settings;
        _board = board;
        SelectedBoard = board;
        ProjectName = Path.GetFileName(_projectDirectory);
        BaudText = Strings.Format("Serial_Baud", _settings.Serial.Baud);
        SelectedSpeed = Speeds.MinBy(s => Math.Abs(Math.Log(s.Value) - Math.Log(Math.Max(_settings.Speed, 0.01)))) ?? Speeds[2];
        SelectedLineEnding = LineEndings.FirstOrDefault(l => l.Setting == _settings.Serial.LineEnding) ?? LineEndings[1];

        var editor = new SceneEditor(SceneModel.FromDocument(project.Scene), _registry);
        editor.Changed += OnSceneChanged;
        Scene = new SceneViewModel(editor, board, (id, name, value) => _session?.Send(new ComponentInput(id, name, value)));
        Properties = new PropertiesViewModel(Scene, () => _settings, UpdateSettings);

        Tabs.Add(SceneTab.Instance);
        string sketch = project.SketchDirectory(_projectDirectory);
        if (Directory.Exists(sketch))
        {
            foreach (string file in Directory.EnumerateFiles(sketch)
                .Where(f => SourceExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .OrderBy(f => Path.GetExtension(f).Equals(".ino", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
            {
                AddDocument(file);
            }

            Watch(sketch);
        }

        SelectedTab = SceneTab.Instance;
        _revision = _savedRevision = 0;
        _settingsDirty = false;
        _loading = false;
        UpdateDirty();
        _state.Touch(_projectDirectory);
        RefreshRecent();
        HideBanner();
        ClearRunOutput();
        Diagnostics.Clear();
        BuildOutput = "";
        LastBuild = "";
        Status = SimStatus.Idle;
        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(ProjectDirectory));
        return true;
    }

    private void CloseProject()
    {
        _watcher?.Dispose();
        _watcher = null;
        if (Scene is not null)
        {
            Scene.Editor.Changed -= OnSceneChanged;
        }

        foreach (CodeDocumentViewModel document in Documents)
        {
            document.PropertyChanged -= OnDocumentChanged;
        }

        Tabs.Clear();
        SelectedTab = null;
        Properties = null;
        Scene = null;
        _projectDirectory = null;
        _project = null;
        _board = null;
        _firmwarePath = null;
        _settings = new ProjectSettings();
        ProjectName = "";
        _revision = _savedRevision = 0;
        _settingsDirty = false;
        UpdateDirty();
        HideBanner();
        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(ProjectDirectory));
    }

    /// <summary>Saves open files and, when changed, the scene and settings. False (with a banner) on failure.</summary>
    private bool SaveAll()
    {
        if (_project is null || _projectDirectory is null || Scene is null)
        {
            return false;
        }

        try
        {
            SaveDocuments();
            if (_revision != _savedRevision || _settingsDirty)
            {
                _project = _project with { Board = _board?.Id ?? _project.Board, Scene = Scene.Editor.Scene.ToDocument(_registry), Settings = _settings };
                _project.Save(_projectDirectory);
                _savedRevision = _revision;
                _settingsDirty = false;
            }

            UpdateDirty();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowBanner(BannerKind.Info, Strings.Get("Banner_SaveFailed"), e.Message, "");
            return false;
        }
    }

    private void SaveDocuments()
    {
        foreach (CodeDocumentViewModel document in Documents)
        {
            document.Save();
        }
    }

    private void UpdateSettings(ProjectSettings settings)
    {
        _settings = settings;
        _settingsDirty = true;
        BaudText = Strings.Format("Serial_Baud", settings.Serial.Baud);
        UpdateDirty();
    }

    private void OnSceneChanged()
    {
        // Undo and redo can bring the other board back: the toolbar and the build follow the scene.
        if (Scene?.Editor.Scene.Parts.FirstOrDefault(Scene.Editor.IsBoard) is { } boardPart && boardPart.Type != _board?.Id
            && BoardCatalog.Find(boardPart.Type) is { } sceneBoard)
        {
            _board = sceneBoard;
            Scene.UseBoard(sceneBoard);
            bool loading = _loading;
            _loading = true;
            SelectedBoard = sceneBoard;
            _loading = loading;
        }

        _revision++;
        UpdateDirty();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void OnDocumentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CodeDocumentViewModel.IsDirty))
        {
            UpdateDirty();
        }
    }

    private void UpdateDirty() => IsDirty = HasProject && (_revision != _savedRevision || _settingsDirty || Documents.Any(d => d.IsDirty));

    private void RefreshRecent()
    {
        Recent.Clear();
        foreach (RecentProject recent in _state.Recent)
        {
            Recent.Add(recent);
        }
    }

    private void ShowToast(string text)
    {
        Toast = text;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // The toolbar's board picker switches the open project's board (ADR-009): the scene keeps its
    // parts and the wires to pins the new board also has; the change is undoable.
    partial void OnSelectedBoardChanged(BoardProfile value)
    {
        if (_loading || Scene is null || _board is null || value.Id == _board.Id)
        {
            return;
        }

        if (Scene.Editor.ChangeBoard(value.Id))
        {
            _board = value;
            Scene.UseBoard(value);
            ShowToast(Strings.Format("Toast_BoardChanged", value.DisplayName));
        }
    }

    partial void OnSelectedSpeedChanged(SpeedOption value)
    {
        _session?.Send(new SetSpeed(value.Value));
        if (!_loading && HasProject && value.Value != _settings.Speed)
        {
            UpdateSettings(_settings with { Speed = double.IsInfinity(value.Value) ? 1000 : value.Value });
        }
    }

    partial void OnSelectedLineEndingChanged(LineEndingOption value)
    {
        if (!_loading && HasProject && value.Setting != _settings.Serial.LineEnding)
        {
            UpdateSettings(_settings with { Serial = _settings.Serial with { LineEnding = value.Setting } });
        }
    }

    partial void OnSelectedThemeChanged(OptionItem value)
    {
        Theme.Apply(value.Value == "light");
        Scene?.RecolorWires();
        if (_state.Theme != value.Value)
        {
            _state.Theme = value.Value;
        }
    }

    partial void OnToolchainProgressChanged(string value) => OnPropertyChanged(nameof(StatusText));

    partial void OnStatusChanged(SimStatus value)
    {
        if (Scene is not null)
        {
            Scene.IsLocked = IsRunning || value == SimStatus.Starting;
        }
    }

    partial void OnSelectedBottomTabChanged(int value)
    {
        if (value == LogTab)
        {
            UnseenWarnings = 0;
        }
    }

    partial void OnClockInMillisecondsChanged(bool value) => UpdateClock();

    // ---- code tabs -----------------------------------------------------------------------------

    /// <summary>The tab for <paramref name="path"/>, opened if needed; null when the file does not exist.</summary>
    public CodeDocumentViewModel? OpenFile(string path)
    {
        string full = Path.GetFullPath(path);
        CodeDocumentViewModel? document = Documents.FirstOrDefault(d => string.Equals(d.Path, full, StringComparison.OrdinalIgnoreCase))
            ?? (File.Exists(full) ? AddDocument(full) : null);
        if (document is not null)
        {
            SelectedTab = document;
        }

        return document;
    }

    /// <summary>Sketch files that have no open tab (the "+" menu).</summary>
    public IReadOnlyList<string> ClosedFiles()
    {
        if (_project is null || _projectDirectory is null || !Directory.Exists(_project.SketchDirectory(_projectDirectory)))
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(_project.SketchDirectory(_projectDirectory))
            .Where(f => SourceExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Where(f => !Documents.Any(d => string.Equals(d.Path, f, StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.OrdinalIgnoreCase)];
    }

    [RelayCommand]
    private void OpenSketchFile(string? path)
    {
        if (path is not null)
        {
            OpenFile(path);
        }
    }

    [RelayCommand]
    private void CloseTab(object? tab)
    {
        if ((tab ?? SelectedTab) is not CodeDocumentViewModel document)
        {
            return;
        }

        if (document.IsDirty)
        {
            switch (AskToSave?.Invoke(document.Title) ?? UnsavedChoice.Discard)
            {
                case UnsavedChoice.Save:
                    document.Save();
                    break;
                case UnsavedChoice.Cancel:
                    return;
            }
        }

        int index = Tabs.IndexOf(document);
        document.PropertyChanged -= OnDocumentChanged;
        Tabs.Remove(document);
        if (SelectedTab is null || SelectedTab == document)
        {
            SelectedTab = Tabs[Math.Clamp(index - 1, 0, Tabs.Count - 1)];
        }

        UpdateDirty();
    }

    [RelayCommand]
    private void NextTab()
    {
        if (Tabs.Count > 0)
        {
            SelectedTab = Tabs[(Tabs.IndexOf(SelectedTab ?? Tabs[0]) + 1) % Tabs.Count];
        }
    }

    private CodeDocumentViewModel AddDocument(string path)
    {
        var document = new CodeDocumentViewModel(path);
        document.PropertyChanged += OnDocumentChanged;
        Tabs.Add(document);
        return document;
    }

    private void Watch(string directory)
    {
        _watcher = new FileSystemWatcher(directory)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            IncludeSubdirectories = false,
        };
        FileSystemWatcher watcher = _watcher;
        void OnChange(string path) => _dispatcher.InvokeAsync(() =>
        {
            if (_watcher == watcher && Documents.FirstOrDefault(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase)) is { } document && document.OnDiskChanged())
            {
                ShowToast(Strings.Format("Toast_Reloaded", document.Title));
            }
        });
        watcher.Changed += (_, e) => OnChange(e.FullPath);
        watcher.Created += (_, e) => OnChange(e.FullPath);
        watcher.Renamed += (_, e) => OnChange(e.FullPath);
        watcher.EnableRaisingEvents = true;
    }

    // ---- diagnostics -----------------------------------------------------------------------------

    [RelayCommand]
    private void OpenDiagnostic(DiagnosticItem? item)
    {
        if (item?.Diagnostic is { File: { } file } diagnostic && OpenFile(file) is { } document)
        {
            _diagnosticIndex = Diagnostics.IndexOf(item);
            document.Navigate(Math.Max(1, diagnostic.Line), Math.Max(1, diagnostic.Column));
        }
    }

    [RelayCommand]
    private void NextDiagnostic() => StepDiagnostic(+1);

    [RelayCommand]
    private void PreviousDiagnostic() => StepDiagnostic(-1);

    [RelayCommand]
    private void OpenCrashLocation()
    {
        if (_crashLocation is { File: { } file, Line: var line } && OpenFile(file) is { } document)
        {
            document.Navigate(Math.Max(1, line), 1);
        }
    }

    private void StepDiagnostic(int direction)
    {
        List<DiagnosticItem> located = [.. Diagnostics.Where(d => d.Diagnostic.File is not null)];
        if (located.Count == 0)
        {
            return;
        }

        int current = _diagnosticIndex >= 0 && _diagnosticIndex < Diagnostics.Count ? located.IndexOf(Diagnostics[_diagnosticIndex]) : -1;
        int next = current < 0 ? (direction > 0 ? 0 : located.Count - 1) : (current + direction + located.Count) % located.Count;
        OpenDiagnostic(located[next]);
    }

    // ---- scene -----------------------------------------------------------------------------------

    private bool CanUndo() => Scene is { IsLocked: false } scene && scene.Editor.CanUndo && CanEdit;

    private bool CanRedo() => Scene is { IsLocked: false } scene && scene.Editor.CanRedo && CanEdit;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => Scene?.Editor.Undo();

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => Scene?.Editor.Redo();

    [RelayCommand]
    private void TogglePalette() => PaletteOpen = !PaletteOpen;

    [RelayCommand]
    private void ToggleProperties() => PropertiesOpen = !PropertiesOpen;

    [RelayCommand]
    private void ToggleBottom() => BottomOpen = !BottomOpen;

    [RelayCommand]
    private void ToggleClock() => ClockInMilliseconds = !ClockInMilliseconds;

    [RelayCommand]
    private void ShowStatusPanel()
    {
        BottomOpen = true;
        SelectedBottomTab = Status == SimStatus.BuildFailed ? BuildTab : Log.Any(l => l.Severity > 0) ? LogTab : SerialTab;
    }

    // ---- build and run ---------------------------------------------------------------------------

    private bool CanBuild() => _project is not null && CanEdit;

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private async Task BuildAsync() => await BuildFirmwareAsync();

    private async Task<bool> BuildFirmwareAsync()
    {
        if (_project is null || _board is null || _projectDirectory is null)
        {
            return false;
        }

        Toolchain? toolchain = _builds.FindToolchain();
        if (toolchain is null)
        {
            ShowBanner(
                BannerKind.Toolchain,
                Strings.Get("Banner_ToolchainTitle"),
                Strings.Format("Banner_ToolchainBody", _builds.Manifest.SizeBytes / (1024 * 1024), FirmwareBuildService.ToolchainDirectory),
                "");
            return false;
        }

        try
        {
            SaveDocuments();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowBanner(BannerKind.Info, Strings.Get("Banner_SaveFailed"), e.Message, "");
            return false;
        }

        UpdateDirty();
        Status = SimStatus.Building;
        Diagnostics.Clear();
        _diagnosticIndex = -1;
        BuildResult result = await FirmwareBuildService.BuildAsync(toolchain, _projectDirectory, _project, _board, CancellationToken.None);
        foreach (BuildDiagnostic d in result.Diagnostics.Where(d => d.Severity != DiagnosticSeverity.Note))
        {
            Diagnostics.Add(new DiagnosticItem(d));
        }

        foreach (CodeDocumentViewModel document in Documents)
        {
            document.Diagnostics = [.. result.Diagnostics.Where(d => d.Severity != DiagnosticSeverity.Note && d.File is not null && SamePath(d.File, document.Path))];
        }

        BuildOutput = Strings.Format("Build_Summary", result.Statistics.Elapsed.TotalSeconds, result.Statistics.Units, result.Statistics.CacheHits)
            + Environment.NewLine + result.Output;
        LastBuild = Strings.Format("Status_LastBuild", result.Statistics.Elapsed.TotalSeconds);
        if (!result.Succeeded)
        {
            Status = SimStatus.BuildFailed;
            BottomOpen = true;
            SelectedBottomTab = BuildTab;
            if (Diagnostics.FirstOrDefault(d => d.Severity == 2 && d.Diagnostic.File is not null) is { } first)
            {
                OpenDiagnostic(first);
            }

            return false;
        }

        _firmwarePath = result.FirmwarePath;
        Status = SimStatus.Idle;
        return true;
    }

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    [RelayCommand]
    private async Task DownloadToolchainAsync()
    {
        HideBanner();
        Status = SimStatus.Building;
        _toolchainCancel = new CancellationTokenSource();
        var progress = new Progress<ToolchainProgress>(p =>
        {
            string stage = Strings.Get("Toolchain_" + p.Stage);
            int percent = p.Total > 0 ? (int)(p.Completed * 100 / p.Total) : 0;
            ToolchainProgress = Strings.Format("Status_ToolchainProgress", stage, percent);
        });
        try
        {
            await _builds.InstallToolchainAsync(progress, _toolchainCancel.Token);
            OnPropertyChanged(nameof(ToolchainText));
            ToolchainProgress = "";
            Status = SimStatus.Idle;
            await RunAsync();
        }
        catch (ToolchainException e)
        {
            ToolchainProgress = "";
            Status = SimStatus.Idle;
            ShowBanner(BannerKind.Toolchain, Strings.Get("Banner_ToolchainFailed"), e.Message, "");
        }
    }

    private bool CanRun() => _project is not null && Status is not (SimStatus.Building or SimStatus.Starting);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        await StopSessionAsync();
        Status = SimStatus.Stopped;

        // Run saves everything first: the Runner reads the scene from the project file (docs/UI.md §4.4).
        if (!SaveAll() || !await BuildFirmwareAsync() || _firmwarePath is null || _projectDirectory is null)
        {
            return;
        }

        HideBanner();
        ClearRunOutput();
        Scene?.ResetRuntime(clearWarnings: true);
        Status = SimStatus.Starting;
        ISimulationSession session = _environment.CreateSession();
        session.MessageReceived += m => Observe(_dispatcher.InvokeAsync(() => OnMessage(session, m)));
        session.Ended += e => Observe(_dispatcher.InvokeAsync(() => OnEnded(session, e)));
        _session = session;
        string firmware = _firmwarePath;
        await session.StartAsync(new SessionStartInfo(_projectDirectory, firmware, SelectedSpeed.Value, 1), CancellationToken.None);
        if (_session == session && Status == SimStatus.Starting)
        {
            Status = SimStatus.Running;
        }
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Pause() => _session?.Send(IsPaused ? new ResumeRun() : new PauseRun());

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private async Task StopAsync()
    {
        await StopSessionAsync();
        Status = SimStatus.Stopped;
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void StepLoop() => _session?.Send(new StepRun(StepKind.Loop, 0));

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void StepTime(string milliseconds) =>
        _session?.Send(new StepRun(StepKind.Time, ulong.Parse(milliseconds, CultureInfo.InvariantCulture) * Core.Timing.VirtualTime.Millisecond));

    [RelayCommand]
    private void SendSerial()
    {
        if (_session is null)
        {
            return;
        }

        _session.Send(new SerialInput(0, Encoding.UTF8.GetBytes(SerialInput + SelectedLineEnding.Text)));
        SerialInput = "";
    }

    [RelayCommand]
    private void ClearSerial()
    {
        _serial.Clear();
        SerialText = "";
    }

    [RelayCommand]
    private void CloseBanner() => HideBanner();

    [RelayCommand]
    private async Task ShowPartAsync(string? id)
    {
        if (id is null || Scene?.Find(id) is not { } part)
        {
            return;
        }

        SelectedTab = SceneTab.Instance;
        Scene.Select(id, additive: false);
        part.Highlighted = true;
        await Task.Delay(TimeSpan.FromSeconds(2));
        part.Highlighted = false;
    }

    /// <summary>Exceptions in dispatcher callbacks would vanish in their task; log them instead.</summary>
    private void Observe(DispatcherOperation operation) =>
        operation.Task.ContinueWith(t => LogUiFailure(_logger, t.Exception!.GetBaseException()), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    private void ClearRunOutput()
    {
        Log.Clear();
        UnseenWarnings = 0;
        _serial.Clear();
        SerialText = "";
        Plotter.Clear();
        _timeNs = 0;
        _speedSample = null;
        UpdateClock();
        ActualSpeed = "";
    }

    private async Task StopSessionAsync()
    {
        ISimulationSession? session = _session;
        _session = null;
        if (session is not null)
        {
            await session.StopAsync();
            await session.DisposeAsync();
            Scene?.ResetRuntime(clearWarnings: false);
        }
    }

    private void OnMessage(ISimulationSession session, IpcMessage message)
    {
        if (session != _session)
        {
            return;
        }

        switch (message)
        {
            case Snapshot snapshot:
                Apply(snapshot);
                break;
            case FirmwareBusy:
                Status = SimStatus.Busy;
                ShowBanner(BannerKind.Busy, Strings.Get("Banner_BusyTitle"), Strings.Get("Banner_BusyBody"), "");
                break;
        }
    }

    private void Apply(Snapshot snapshot)
    {
        _timeNs = snapshot.TimeNs;
        UpdateClock();
        // The actual speed over the last second of wall time, so a speed change shows within a second.
        if (snapshot.State != RunState.Running || _speedSample is null)
        {
            _speedSample = snapshot.State == RunState.Running ? (snapshot.WallSeconds, snapshot.TimeNs) : null;
        }
        else if (snapshot.WallSeconds - _speedSample.Value.Wall >= 1)
        {
            double rate = (snapshot.TimeNs - _speedSample.Value.TimeNs) / 1e9 / (snapshot.WallSeconds - _speedSample.Value.Wall);
            ActualSpeed = rate.ToString("0.0", CultureInfo.CurrentCulture) + "×";
            _speedSample = (snapshot.WallSeconds, snapshot.TimeNs);
        }

        if (Status != SimStatus.Busy || snapshot.State == RunState.Paused)
        {
            Status = snapshot.State == RunState.Paused ? SimStatus.Paused : SimStatus.Running;
        }

        Scene?.ApplyPins(snapshot.Pins.Select(p => (p.Pin, (int)p.Kind, p.Value)));
        foreach (ComponentUpdate update in snapshot.Components)
        {
            Scene?.ApplyComponent(update.Id, update.State, update.Display);
        }

        if (snapshot.Serial.Length > 0)
        {
            string text = Encoding.UTF8.GetString(snapshot.Serial).Replace("\r\n", "\n", StringComparison.Ordinal);
            Plotter.Append(text);
            _serial.Append(text);
            if (_serial.Length > SerialLimit)
            {
                _serial.Remove(0, _serial.Length - SerialLimit);
            }

            SerialText = _serial.ToString();
        }

        foreach (LogEntry entry in snapshot.Logs)
        {
            Log.Add(new LogItem((entry.TimeNs / 1e9).ToString("0.000000", CultureInfo.CurrentCulture), entry.Severity, LogText.Localize(entry), entry.ComponentId));
            if (entry.Severity > 0)
            {
                if (entry.ComponentId is not null && Scene?.Find(entry.ComponentId) is { } part)
                {
                    part.HasWarning = true;
                }

                if (!BottomOpen || SelectedBottomTab != LogTab)
                {
                    UnseenWarnings++;
                }
            }
        }
    }

    private void UpdateClock() =>
        VirtualTime = ClockInMilliseconds
            ? (_timeNs / 1e6).ToString("0", CultureInfo.CurrentCulture) + " " + Strings.Get("Unit_Milliseconds")
            : (_timeNs / 1e9).ToString("0.000", CultureInfo.CurrentCulture) + " " + Strings.Get("Unit_Seconds");

    private void OnEnded(ISimulationSession session, SessionEnd end)
    {
        if (session != _session && end.Kind == SessionEndKind.Stopped)
        {
            return;
        }

        _session = null;
        Scene?.ResetRuntime(clearWarnings: false);
        LogRunEnded(_logger, end.Kind, end.Message);
        switch (end.Kind)
        {
            case SessionEndKind.Stopped:
                Status = SimStatus.Stopped;
                break;
            case SessionEndKind.Crashed:
                Status = SimStatus.Crashed;
                ShowCrash(end.Crash!);
                break;
            default:
                Status = SimStatus.Stopped;
                ShowBanner(BannerKind.Exited, Strings.Get("Banner_ExitedTitle"), end.Message, end.Output);
                break;
        }
    }

    private void ShowCrash(CrashDetails crash)
    {
        string what = crash.ExceptionCode switch
        {
            0xC0000005 => Strings.Get("Crash_AccessViolation") + (crash.AccessAddress < 0x10000
                ? " (" + Strings.Get(crash.AccessKind == 1 ? "Crash_NullWrite" : "Crash_NullRead") + ")"
                : string.Create(CultureInfo.InvariantCulture, $" (0x{crash.AccessAddress:X})")),
            0xC0000094 => Strings.Get("Crash_DivideByZero"),
            0xC00000FD => Strings.Get("Crash_StackOverflow"),
            _ => Strings.Format("Crash_Other", crash.ExceptionName),
        };

        string where = Strings.Get("Banner_CrashUnknownPlace");
        SourceLocation? location = null;
        if (_firmwarePath is not null)
        {
            SourceLocation? at = FirmwareSymbols.Resolve(_firmwarePath, crash.FirmwareBase, crash.ExceptionAddress);
            SourceLocation? caller = crash.ReturnAddress != 0 ? FirmwareSymbols.Resolve(_firmwarePath, crash.FirmwareBase, crash.ReturnAddress) : null;
            if (at?.File is not null)
            {
                where = Place(at);
                location = at;
            }
            else if (caller is not null)
            {
                where = Strings.Format("Banner_CrashCalledFrom", at?.Function ?? "?", Place(caller));
                location = caller.File is not null ? caller : null;
            }
        }

        string calls = Strings.Get("Banner_LastCalls") + Environment.NewLine + string.Join(Environment.NewLine, crash.LastCalls);
        ShowBanner(BannerKind.Crash, Strings.Get("Banner_CrashTitle"), Strings.Format("Banner_CrashBody", what, where), calls);
        _crashLocation = location;
        HasCrashLocation = location is not null;
    }

    private static string Place(SourceLocation location) =>
        location.File is null ? location.Function ?? "?" : Strings.Format("Crash_Location", location.Function ?? "?", Path.GetFileName(location.File), location.Line);

    private void ShowBanner(BannerKind kind, string title, string body, string details)
    {
        BannerTitle = title;
        BannerBody = body;
        BannerDetails = details;
        _crashLocation = null;
        HasCrashLocation = false;
        Banner = kind;
    }

    private void HideBanner() => Banner = BannerKind.None;

    [LoggerMessage(Level = LogLevel.Error, Message = "Handling a message from the simulation failed")]
    private static partial void LogUiFailure(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Run ended: {Kind} {Message}")]
    private static partial void LogRunEnded(ILogger logger, SessionEndKind kind, string message);
}

public enum BannerKind
{
    None,
    Info,
    Toolchain,
    Crash,
    Busy,
    Exited,
}
