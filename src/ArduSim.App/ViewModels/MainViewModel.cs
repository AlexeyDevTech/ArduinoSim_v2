using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Threading;
using ArduSim.App.Localization;
using ArduSim.App.Services;
using ArduSim.Boards;
using ArduSim.Build.Compilation;
using ArduSim.Build.Toolchains;
using ArduSim.Core.Boards;
using ArduSim.Core.Components;
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

public sealed record ExampleItem(string Name, string Path);

public sealed record SpeedOption(string Label, double Value);

public sealed record LineEndingOption(string Label, string Text);

public sealed record LogItem(string Time, int Severity, string Message, string? ComponentId);

/// <summary>The M2 shell: one project at a time, build and run it, watch it live (ADR-003, ADR-004).</summary>
public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private const int SerialLimit = 200_000;

    private readonly AppEnvironment _environment;
    private readonly FirmwareBuildService _builds;
    private readonly ILogger<MainViewModel> _logger;
    private readonly Dispatcher _dispatcher;
    private readonly ComponentRegistry _registry = ComponentRegistry.CreateDefault(BoardCatalog.All);
    private readonly StringBuilder _serial = new();
    private ISimulationSession? _session;
    private string? _projectDirectory;
    private ProjectFile? _project;
    private BoardProfile? _board;
    private string? _firmwarePath;
    private CancellationTokenSource? _toolchainCancel;

    public MainViewModel(AppEnvironment environment, FirmwareBuildService builds, ILogger<MainViewModel> logger)
    {
        _environment = environment;
        _builds = builds;
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;
        Examples = [.. AppEnvironment.Examples().Select(e => new ExampleItem(e.Name, e.Path))];
        Speeds = [new("0.1×", 0.1), new("0.5×", 0.5), new("1×", 1), new("2×", 2), new("10×", 10), new(Strings.Get("Speed_Max"), double.PositiveInfinity)];
        LineEndings = [new(Strings.Get("Serial_NoEnding"), ""), new("LF", "\n"), new("CR", "\r"), new("CR+LF", "\r\n")];
        SelectedSpeed = Speeds[2];
        SelectedLineEnding = LineEndings[1];
        Status = SimStatus.Idle;
        if (Examples.Count > 0)
        {
            SelectedExample = Examples.FirstOrDefault(e => e.Name == "Blink") ?? Examples[0];
        }
    }

    public ObservableCollection<ExampleItem> Examples { get; }

    public IReadOnlyList<SpeedOption> Speeds { get; }

    public IReadOnlyList<LineEndingOption> LineEndings { get; }

    public ObservableCollection<LogItem> Log { get; } = [];

    public ObservableCollection<string> Diagnostics { get; } = [];

    public bool InProcess => _environment.InProcess;

    [ObservableProperty]
    public partial ExampleItem? SelectedExample { get; set; }

    [ObservableProperty]
    public partial SpeedOption SelectedSpeed { get; set; }

    [ObservableProperty]
    public partial LineEndingOption SelectedLineEnding { get; set; }

    [ObservableProperty]
    public partial SceneViewModel? Scene { get; set; }

    [ObservableProperty]
    public partial string ProjectName { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsRunning), nameof(IsPaused), nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand), nameof(RunCommand), nameof(PauseCommand), nameof(StopCommand), nameof(StepLoopCommand), nameof(StepTimeCommand))]
    public partial SimStatus Status { get; set; }

    [ObservableProperty]
    public partial string ToolchainProgress { get; set; } = "";

    [ObservableProperty]
    public partial string VirtualTime { get; set; } = "0.000 с";

    [ObservableProperty]
    public partial string ActualSpeed { get; set; } = "";

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
    public partial int SelectedTab { get; set; }

    [ObservableProperty]
    public partial BannerKind Banner { get; set; }

    [ObservableProperty]
    public partial string BannerTitle { get; set; } = "";

    [ObservableProperty]
    public partial string BannerBody { get; set; } = "";

    [ObservableProperty]
    public partial string BannerDetails { get; set; } = "";

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
        _toolchainCancel?.Dispose();
    }

    /// <summary>Keyboard shortcuts of pushbuttons (part property <c>key</c>).</summary>
    public bool OnKey(string key, bool down)
    {
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

    partial void OnSelectedExampleChanged(ExampleItem? value)
    {
        if (value is not null)
        {
            _ = OpenProjectAsync(AppEnvironment.OpenExample(value.Path));
        }
    }

    partial void OnSelectedSpeedChanged(SpeedOption value) => _session?.Send(new SetSpeed(value.Value));

    partial void OnToolchainProgressChanged(string value) => OnPropertyChanged(nameof(StatusText));

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task OpenFolderAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog();
        return dialog.ShowDialog() == true ? OpenProjectAsync(dialog.FolderName) : Task.CompletedTask;
    }

    private async Task OpenProjectAsync(string directory)
    {
        await StopSessionAsync();
        try
        {
            ProjectFile project = ProjectFile.Load(directory);
            BoardProfile board = BoardCatalog.Find(project.Board) ?? throw new ProjectFormatException($"Unknown board '{project.Board}'.");
            _projectDirectory = directory;
            _project = project;
            _board = board;
            _firmwarePath = null;
            ProjectName = Path.GetFileName(directory);
            BaudText = Strings.Format("Serial_Baud", project.Settings.Serial.Baud);
            Scene = new SceneViewModel(project, board, _registry, (id, name, value) => _session?.Send(new ComponentInput(id, name, value)));
            HideBanner();
            Status = SimStatus.Idle;
        }
        catch (ProjectFormatException e)
        {
            ShowBanner(BannerKind.Info, Strings.Get("Banner_ExitedTitle"), e.Message, "");
        }
    }

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

        Status = SimStatus.Building;
        Diagnostics.Clear();
        BuildResult result = await FirmwareBuildService.BuildAsync(toolchain, _projectDirectory, _project, _board, CancellationToken.None);
        foreach (BuildDiagnostic d in result.Diagnostics.Where(d => d.Severity != DiagnosticSeverity.Note))
        {
            Diagnostics.Add(d.File is null ? $"{d.Severity}: {d.Message}" : $"{Path.GetFileName(d.File)}({d.Line},{d.Column}): {d.Severity}: {d.Message}");
        }

        BuildOutput = Strings.Format("Build_Summary", result.Statistics.Elapsed.TotalSeconds, result.Statistics.Units, result.Statistics.CacheHits)
            + Environment.NewLine + result.Output;
        if (!result.Succeeded)
        {
            Status = SimStatus.BuildFailed;
            SelectedTab = 1;
            return false;
        }

        _firmwarePath = result.FirmwarePath;
        Status = SimStatus.Idle;
        return true;
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
            ToolchainProgress = Strings.Format("Status_Toolchain", stage, percent);
        });
        try
        {
            await _builds.InstallToolchainAsync(progress, _toolchainCancel.Token);
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
        if (!await BuildFirmwareAsync() || _firmwarePath is null || _projectDirectory is null)
        {
            return;
        }

        HideBanner();
        Log.Clear();
        _serial.Clear();
        SerialText = "";
        Scene?.ResetState();
        Status = SimStatus.Starting;
        ISimulationSession session = _environment.CreateSession();
        session.MessageReceived += m => _dispatcher.InvokeAsync(() => OnMessage(session, m));
        session.Ended += e => _dispatcher.InvokeAsync(() => OnEnded(session, e));
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

        part.Highlighted = true;
        await Task.Delay(TimeSpan.FromSeconds(2));
        part.Highlighted = false;
    }

    private async Task StopSessionAsync()
    {
        ISimulationSession? session = _session;
        _session = null;
        if (session is not null)
        {
            await session.StopAsync();
            await session.DisposeAsync();
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
        VirtualTime = (snapshot.TimeNs / 1e9).ToString("0.000", CultureInfo.CurrentCulture) + " с";
        if (snapshot.WallSeconds > 0.5 && snapshot.State == RunState.Running)
        {
            ActualSpeed = (snapshot.TimeNs / 1e9 / snapshot.WallSeconds).ToString("0.0", CultureInfo.CurrentCulture) + "×";
        }

        if (Status != SimStatus.Busy || snapshot.State == RunState.Paused)
        {
            Status = snapshot.State == RunState.Paused ? SimStatus.Paused : SimStatus.Running;
        }

        foreach (ComponentUpdate update in snapshot.Components)
        {
            Scene?.Find(update.Id)?.Apply(update.State);
        }

        if (snapshot.Serial.Length > 0)
        {
            _serial.Append(Encoding.UTF8.GetString(snapshot.Serial).Replace("\r\n", "\n", StringComparison.Ordinal));
            if (_serial.Length > SerialLimit)
            {
                _serial.Remove(0, _serial.Length - SerialLimit);
            }

            SerialText = _serial.ToString();
        }

        foreach (LogEntry entry in snapshot.Logs)
        {
            Log.Add(new LogItem((entry.TimeNs / 1e9).ToString("0.000000", CultureInfo.CurrentCulture), entry.Severity, entry.Message, entry.ComponentId));
            if (entry.Severity > 0)
            {
                SelectedTab = 2;
            }
        }
    }

    private void OnEnded(ISimulationSession session, SessionEnd end)
    {
        if (session != _session && end.Kind == SessionEndKind.Stopped)
        {
            return;
        }

        _session = null;
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
        if (_firmwarePath is not null)
        {
            SourceLocation? at = FirmwareSymbols.Resolve(_firmwarePath, crash.FirmwareBase, crash.ExceptionAddress);
            SourceLocation? caller = crash.ReturnAddress != 0 ? FirmwareSymbols.Resolve(_firmwarePath, crash.FirmwareBase, crash.ReturnAddress) : null;
            if (at?.File is not null)
            {
                where = Place(at);
            }
            else if (caller is not null)
            {
                where = Strings.Format("Banner_CrashCalledFrom", at?.Function ?? "?", Place(caller));
            }
        }

        string calls = Strings.Get("Banner_LastCalls") + Environment.NewLine + string.Join(Environment.NewLine, crash.LastCalls);
        ShowBanner(BannerKind.Crash, Strings.Get("Banner_CrashTitle"), Strings.Format("Banner_CrashBody", what, where), calls);
    }

    private static string Place(SourceLocation location) =>
        location.File is null ? location.Function ?? "?" : Strings.Format("Crash_Location", location.Function ?? "?", Path.GetFileName(location.File), location.Line);

    private void ShowBanner(BannerKind kind, string title, string body, string details)
    {
        BannerTitle = title;
        BannerBody = body;
        BannerDetails = details;
        Banner = kind;
    }

    private void HideBanner() => Banner = BannerKind.None;

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
