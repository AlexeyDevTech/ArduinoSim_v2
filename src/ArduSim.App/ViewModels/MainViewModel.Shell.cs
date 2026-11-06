using ArduSim.App.Localization;
using ArduSim.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArduSim.App.ViewModels;

/// <summary>The shell around the project: tooltips, the Рядом mode, the bottom panel's folding and the log's filter (docs/UI.md §2).</summary>
public sealed partial class MainViewModel
{
    public const int PartsPage = 0;
    public const int ProjectPage = 1;

    private readonly List<LogItem> _log = [];
    private CodeDocumentViewModel? _lastDocument;
    private bool _compactHeight;
    private int _clangdGeneration;
    private (bool Palette, bool Properties, bool Bottom)? _panelsBeforeHide;

    /// <summary>A part should be brought into view (the log's Показать, the explorer).</summary>
    public event Action<PartViewModel>? ShowPartRequested;

    /// <summary>Focus should go to the Monitor's input line (Ctrl+Shift+M).</summary>
    public event Action? FocusSerialRequested;

    /// <summary>New text for the Monitor. Appending keeps the user's selection and scroll position.</summary>
    public event Action<string>? SerialAppended;

    /// <summary>The Monitor's whole text changed: cleared, another port, or its oldest lines gone.</summary>
    public event Action<string>? SerialReplaced;

    /// <summary>The new project's name box should take focus (Ctrl+N).</summary>
    public event Action? FocusNewProjectRequested;

    // ---- tooltips: what a control does, or why it cannot now (docs/UI.md principle 4) -------------

    private string? UnavailableReason =>
        !HasProject ? Strings.Get("Tip_NoProject")
        : Status == SimStatus.Building ? Strings.Get("Tip_Building")
        : Status == SimStatus.Starting ? Strings.Get("Tip_Starting")
        : null;

    public string BuildTip => UnavailableReason ?? Strings.Get(IsRunning ? "Tip_BuildRunning" : "Tip_Build");

    public string RunTip => UnavailableReason ?? Strings.Get(!IsRunning ? "Tip_Run" : IsRunStale ? "Tip_RestartStale" : "Tip_Restart");

    public string PauseTip => UnavailableReason ?? Strings.Get(
        IsPaused ? "Tip_Resume" : Status == SimStatus.Running ? "Tip_Pause" : Status == SimStatus.Busy ? "Tip_BusyNoPause" : "Tip_NotRunning");

    public string StopTip => Status == SimStatus.Building ? Strings.Get("Tip_StopBuild") : UnavailableReason ?? Strings.Get(IsRunning ? "Tip_Stop" : "Tip_NotRunning");

    private bool CanStop => IsRunning || Status == SimStatus.Building;

    /// <summary>The wheel moves the schematic and Ctrl+wheel zooms, for a touchpad (Параметры).</summary>
    [ObservableProperty]
    public partial bool WheelPans { get; set; }

    partial void OnWheelPansChanged(bool value)
    {
        if (Scene is not null)
        {
            Scene.WheelPans = value;
        }
    }

    private string StepTipFor(string key) => UnavailableReason ?? Strings.Get(
        Status is SimStatus.Running or SimStatus.Paused ? key : Status == SimStatus.Busy ? "Tip_BusyNoPause" : "Tip_NotRunning");

    public string StepTip => StepTipFor("Toolbar_StepLoopTip");

    public string Step1Tip => StepTipFor("Toolbar_Step1Tip");

    public string Step10Tip => StepTipFor("Toolbar_Step10Tip");

    public string Step100Tip => StepTipFor("Toolbar_Step100Tip");

    public string BoardTip => IsRunning ? Strings.Get("Tip_BoardRunning") : Strings.Get("Toolbar_Board");

    public string? StatusTip => !HasProject ? null : Strings.Get(StatusPanelTab switch
    {
        BuildTab => "Tip_StatusBuild",
        LogTab => "Tip_StatusLog",
        _ => "Tip_StatusSerial",
    });

    public string SendTip => UnavailableReason ?? Strings.Get(IsRunning ? "Serial_SendTip" : "Tip_NotRunning");

    /// <summary>The Monitor's input line says why nothing can be sent.</summary>
    public bool ShowSerialPlaceholder => !IsRunning && SerialInput.Length == 0;

    partial void OnSerialInputChanged(string value) => OnPropertyChanged(nameof(ShowSerialPlaceholder));

    public void ShowLockedToast() => ShowToast(Strings.Get("Canvas_LockedHint"));

    /// <summary>Something failed that nothing expected: the work stays open and the reason is shown.</summary>
    public void ShowInternalError(string details) => ShowBanner(BannerKind.Info, Strings.Get("Banner_InternalError"), Strings.Get("Banner_InternalErrorBody"), details);

    /// <summary>Hides all three panels for the schematic alone; again brings back what was open.</summary>
    [RelayCommand]
    private void ToggleAllPanels()
    {
        if (PaletteOpen || PropertiesOpen || BottomOpen)
        {
            _panelsBeforeHide = (PaletteOpen, PropertiesOpen, BottomOpen);
            PaletteOpen = PropertiesOpen = BottomOpen = false;
            BottomAutoFolded = false;
        }
        else
        {
            (PaletteOpen, PropertiesOpen, BottomOpen) = _panelsBeforeHide ?? (true, true, true);
            _panelsBeforeHide = null;
        }
    }

    /// <summary>Code was edited after the running firmware was built: Перезапуск applies it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunTip))]
    public partial bool IsRunStale { get; set; }

    [ObservableProperty]
    public partial int BuildErrors { get; set; }

    [ObservableProperty]
    public partial int BuildWarnings { get; set; }

    /// <summary>Lines in the Monitor start with the virtual time.</summary>
    [ObservableProperty]
    public partial bool TimestampSerial { get; set; }

    /// <summary>The one-line hint at the bottom of the schematic (Параметры).</summary>
    [ObservableProperty]
    public partial bool CanvasHints { get; set; }

    partial void OnCanvasHintsChanged(bool value)
    {
        if (Scene is not null)
        {
            Scene.HintsEnabled = value;
        }
    }

    /// <summary>Currents on LEDs and resistors while running (Параметры).</summary>
    [ObservableProperty]
    public partial bool CanvasCurrents { get; set; }

    partial void OnCanvasCurrentsChanged(bool value)
    {
        if (Scene is not null)
        {
            Scene.CurrentsShown = value;
        }
    }

    // ---- left panel, code beside the schematic ------------------------------------------------------

    /// <summary>The left panel's page: Детали or Проект.</summary>
    [ObservableProperty]
    public partial int LeftTab { get; set; }

    partial void OnLeftTabChanged(int value)
    {
        if (value == ProjectPage)
        {
            RefreshExplorer();
            _ = RefreshOutlineAsync();
        }
    }

    // ---- the language server ----------------------------------------------------------------------

    /// <summary>clangd for the open project; null without a toolchain, and the editor still edits (ADR-016).</summary>
    public ClangdClient? Clangd { get; private set; }

    /// <summary>The toolchain's formatter, when the toolchain is installed.</summary>
    public string? ClangFormatPath => _builds.FindToolchain() is { } toolchain && System.IO.File.Exists(System.IO.Path.Combine(toolchain.Root, "bin", "clang-format.exe"))
        ? System.IO.Path.Combine(toolchain.Root, "bin", "clang-format.exe")
        : null;

    public void ShowEditorMessage(string text) => ShowToast(text);

    private async Task StartClangdAsync()
    {
        int generation = ++_clangdGeneration;
        await StopClangdAsync();
        if (_builds.FindToolchain() is not { } toolchain || _project is not { } project || _board is not { } board || _projectDirectory is not { } directory)
        {
            return;
        }

        ClangdClient? client = await ClangdClient.StartAsync(System.IO.Path.Combine(toolchain.Root, "bin", "clangd.exe"), directory, FirmwareBuildService.EditorFlags(project, directory, board), _logger);
        if (generation == _clangdGeneration)
        {
            Clangd = client;

            // Reading the main file now means the first completion already knows the Arduino API.
            if (client is not null && Documents.FirstOrDefault() is { } main)
            {
                await client.UpdateAsync(main.Path, main.Document.Text);
                await client.SymbolsAsync(main.Path);
            }

            _ = RefreshOutlineAsync();
        }
        else if (client is not null)
        {
            await client.DisposeAsync();
        }
    }

    private async Task StopClangdAsync(bool forget = false)
    {
        if (forget)
        {
            _clangdGeneration++;
        }

        ClangdClient? client = Clangd;
        Clangd = null;
        if (client is not null)
        {
            await client.DisposeAsync();
        }
    }

    /// <summary>The Рядом mode: the schematic stays on screen and the code sits to its right.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleDocument), nameof(IsCanvasVisible))]
    public partial bool SplitView { get; set; }

    /// <summary>The file the editor area shows; in the Рядом mode the Схема tab keeps the last one on screen.</summary>
    public CodeDocumentViewModel? VisibleDocument =>
        SelectedTab as CodeDocumentViewModel ?? (SplitView ? _lastDocument ?? Documents.FirstOrDefault() : null);

    public bool IsCanvasVisible => IsSceneTab || SplitView;

    partial void OnSplitViewChanged(bool value) => RaiseUndoState();

    private void RaiseUndoState()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedTabChanged(object? value)
    {
        RaiseUndoState();
        if (value is CodeDocumentViewModel document)
        {
            _lastDocument = document;
            document.RequestFocus();
        }

        OnPropertyChanged(nameof(VisibleDocument));
        OnPropertyChanged(nameof(IsCanvasVisible));
        UpdateExplorerCurrent();
        _ = RefreshOutlineAsync();
    }

    [RelayCommand]
    private void ToggleSplit() => SplitView = !SplitView;

    /// <summary>F7: between the schematic and the last code tab.</summary>
    [RelayCommand]
    private void FlipCode()
    {
        if (!HasProject)
        {
            return;
        }

        if (SelectedTab is SceneTab)
        {
            if ((_lastDocument ?? Documents.FirstOrDefault()) is { } document)
            {
                SelectedTab = document;
                document.RequestFocus();
            }
        }
        else
        {
            SelectedTab = SceneTab.Instance;
        }
    }

    [RelayCommand]
    private void PreviousTab()
    {
        if (Tabs.Count > 0)
        {
            SelectedTab = Tabs[(Tabs.IndexOf(SelectedTab ?? Tabs[0]) + Tabs.Count - 1) % Tabs.Count];
        }
    }

    /// <summary>Ctrl+Shift+E: the Проект page, opening the panel if it was folded.</summary>
    [RelayCommand]
    private void ShowExplorer()
    {
        PaletteOpen = true;
        LeftTab = ProjectPage;
    }

    /// <summary>Ctrl+Shift+M: the Monitor with focus on its input line.</summary>
    [RelayCommand]
    private void ShowMonitor()
    {
        BottomAutoFolded = false;
        BottomOpen = true;
        SelectedBottomTab = SerialTab;
        FocusSerialRequested?.Invoke();
    }

    // ---- bottom panel on a low window ---------------------------------------------------------------

    /// <summary>The panel folded itself for a low window; it opens again on the first output.</summary>
    [ObservableProperty]
    public partial bool BottomAutoFolded { get; set; }

    /// <summary>Called by the window when its height crosses the compact limit; the user's own toggle wins afterwards.</summary>
    public void SetCompactHeight(bool compact)
    {
        if (compact == _compactHeight)
        {
            return;
        }

        _compactHeight = compact;
        if (compact && BottomOpen)
        {
            BottomOpen = false;
            BottomAutoFolded = true;
        }
        else if (!compact && BottomAutoFolded)
        {
            BottomAutoFolded = false;
            BottomOpen = true;
        }
    }

    private void OpenBottomIfFolded()
    {
        if (BottomAutoFolded)
        {
            BottomAutoFolded = false;
            BottomOpen = true;
        }
    }

    // ---- the log's filter ---------------------------------------------------------------------------

    /// <summary>Все, Предупреждения, Ошибки (docs/UI.md §2.6).</summary>
    public IReadOnlyList<OptionItem> LogFilters { get; } =
    [
        new("0", Strings.Get("Log_FilterAll"), null),
        new("1", Strings.Get("Log_FilterWarnings"), null),
        new("2", Strings.Get("Log_FilterErrors"), null),
    ];

    [ObservableProperty]
    public partial OptionItem? SelectedLogFilter { get; set; }

    [ObservableProperty]
    public partial LogItem? SelectedLogItem { get; set; }

    public string LogEmptyText => Strings.Get(_log.Count == 0 ? "Log_EmptyNone" : "Log_EmptyFiltered");

    public bool LogIsEmpty => Log.Count == 0;

    private int LogMinimumSeverity => SelectedLogFilter?.Value == "2" ? 2 : SelectedLogFilter?.Value == "1" ? 1 : 0;

    partial void OnSelectedLogFilterChanged(OptionItem? value)
    {
        Log.Clear();
        foreach (LogItem item in _log.Where(i => i.Severity >= LogMinimumSeverity))
        {
            Log.Add(item);
        }

        OnPropertyChanged(nameof(LogIsEmpty));
        OnPropertyChanged(nameof(LogEmptyText));
    }

    private void AddLog(LogItem item)
    {
        _log.Add(item);
        if (item.Severity >= LogMinimumSeverity)
        {
            Log.Add(item);
        }

        OnPropertyChanged(nameof(LogIsEmpty));
        OnPropertyChanged(nameof(LogEmptyText));
        OnPropertyChanged(nameof(StatusTip));
    }

    /// <summary>The ⚠ on a part: the log opens at that part's first message.</summary>
    private void OnWarningClicked(PartViewModel part)
    {
        BottomAutoFolded = false;
        BottomOpen = true;
        SelectedBottomTab = LogTab;
        SelectedLogFilter = LogFilters[0];
        SelectedLogItem = _log.FirstOrDefault(l => l.ComponentId == part.Id && l.Severity > 0);
    }
}
