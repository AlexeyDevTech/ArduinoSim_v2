using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using ArduSim.App.Localization;
using ArduSim.App.Services;
using ArduSim.Build.Libraries;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArduSim.App.ViewModels;

/// <summary>One library in the dialog's list.</summary>
public sealed partial class LibraryRow(CatalogLibrary library) : ObservableObject
{
    public CatalogLibrary Library { get; } = library;

    public string Name => Library.Name;

    public string Author => Library.Author;

    public string Sentence => Library.Sentence;

    public IReadOnlyList<string> Versions { get; } = [.. library.Releases.Select(r => r.Version)];

    /// <summary>The release chosen in the version list.</summary>
    public CatalogRelease Release => Library.Releases.FirstOrDefault(r => r.Version == SelectedVersion) ?? Library.Latest;

    public string Size => FormatBytes(Release.Size);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Release))]
    [NotifyPropertyChangedFor(nameof(Size))]
    public partial string SelectedVersion { get; set; } = library.Latest.Version;

    /// <summary>What is true of it in this project; empty when it is simply not here.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = "";

    /// <summary>What the main button says.</summary>
    [ObservableProperty]
    public partial string ActionLabel { get; set; } = "";

    [ObservableProperty]
    public partial bool CanInstall { get; set; }

    [ObservableProperty]
    public partial bool CanRemove { get; set; }

    /// <summary>A warning about this board, what it brings along.</summary>
    [ObservableProperty]
    public partial string Note { get; set; } = "";

    public string? InstalledDirectory { get; set; }

    public static string FormatBytes(long bytes) => bytes >= 1 << 20
        ? string.Create(CultureInfo.CurrentCulture, $"{bytes / (double)(1 << 20):0.0} {Strings.Get("Unit_MiB")}")
        : string.Create(CultureInfo.CurrentCulture, $"{Math.Max(bytes, 1024) / 1024:0} {Strings.Get("Unit_KiB")}");
}

/// <summary>The Библиотеки dialog (ADR-018): the catalog's search and installing, and adding from a folder or a ZIP.</summary>
public sealed partial class LibraryDialogViewModel : ObservableObject
{
    private const int ShownLimit = 100;

    private readonly LibraryService _service;
    private readonly Action _changed;
    private LibraryCatalog? _catalog;
    private LibraryInventory _inventory;
    private CancellationTokenSource? _work;

    public LibraryDialogViewModel(LibraryService service, Action changed, string? query = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _changed = changed;
        _inventory = service.Inventory();
        Query = query ?? "";
    }

    /// <summary>The folder picker, set by the window.</summary>
    public Func<string?>? PickFolder { get; set; }

    /// <summary>The file picker for a ZIP, set by the window.</summary>
    public Func<string?>? PickZip { get; set; }

    public ObservableCollection<LibraryRow> Rows { get; } = [];

    [ObservableProperty]
    public partial string Query { get; set; }

    /// <summary>The catalog is on its way, or a library is being installed.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand), nameof(RemoveCommand), nameof(AddFolderCommand), nameof(AddZipCommand), nameof(RefreshCatalogCommand), nameof(CancelCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>What is happening: a download's progress.</summary>
    [ObservableProperty]
    public partial string Progress { get; set; } = "";

    /// <summary>How big and how old the catalog is.</summary>
    [ObservableProperty]
    public partial string Summary { get; set; } = "";

    /// <summary>The result of the last action.</summary>
    [ObservableProperty]
    public partial string Message { get; set; } = "";

    /// <summary>What went wrong with the last action, or with the catalog.</summary>
    [ObservableProperty]
    public partial string Error { get; set; } = "";

    /// <summary>Why the list is empty, or that it is cut short.</summary>
    [ObservableProperty]
    public partial string ListNote { get; set; } = "";

    partial void OnQueryChanged(string value)
    {
        // A new search is a new start: the last failure is no longer news. Without a catalog it still is.
        if (_catalog is not null)
        {
            Error = "";
        }

        Search();
    }

    /// <summary>Called when the window opens.</summary>
    public Task LoadAsync() => LoadCatalogAsync(refresh: false);

    /// <summary>The window closes while something is running.</summary>
    public void CancelWork() => _work?.Cancel();

    private bool CanAct() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task RefreshCatalog() => LoadCatalogAsync(refresh: true);

    private async Task LoadCatalogAsync(bool refresh)
    {
        using CancellationTokenSource work = Begin();
        Progress = Strings.Get("Library_Loading");
        Error = "";
        try
        {
            var progress = new Progress<CatalogProgress>(p => Progress = p.Total is { } total
                ? Strings.Format("Library_LoadingProgress", LibraryRow.FormatBytes(p.Done), LibraryRow.FormatBytes(total))
                : Strings.Get("Library_Loading"));
            CatalogLoad load = await _service.LoadCatalogAsync(refresh, progress, work.Token);
            _catalog = load.Catalog;
            string date = load.Fetched.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
            Summary = Strings.Format(load.IsStale ? "Library_SummaryStale" : "Library_Summary", _catalog.Libraries.Count, date);
            Search();
        }
        catch (LibraryException e)
        {
            Error = Explain(e);
        }
        catch (OperationCanceledException)
        {
            Progress = "";
        }
        finally
        {
            End();
        }
    }

    private void Search()
    {
        Rows.Clear();
        if (_catalog is null)
        {
            return;
        }

        IReadOnlyList<CatalogLibrary> found = _catalog.Search(Query);
        foreach (CatalogLibrary library in found.Take(ShownLimit))
        {
            var row = new LibraryRow(library);
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(LibraryRow.SelectedVersion))
                {
                    Describe(row);
                }
            };
            Describe(row);
            Rows.Add(row);
        }

        ListNote = found.Count == 0 ? Strings.Get("Library_NoMatches")
            : found.Count > ShownLimit ? Strings.Format("Library_More", ShownLimit, found.Count)
            : "";
    }

    // What the row says follows from the release chosen and from what the project can already see.
    private void Describe(LibraryRow row)
    {
        CatalogRelease release = row.Release;
        KnownLibrary? known = _inventory.Find(row.Name);
        string note = "";
        string[] architectures = [.. release.Architectures];
        if (architectures.Length > 0 && !architectures.Contains("*") && !architectures.Contains(_service.Architecture, StringComparer.OrdinalIgnoreCase))
        {
            note = Strings.Format("Library_Arch", string.Join(", ", architectures.Take(4)));
        }

        InstallPlan plan = _catalog!.Plan(row.Library, release, name => _inventory.Find(name) is not null);
        string[] alongside = [.. plan.Items.Skip(1).Select(i => i.Library.Name)];
        if (alongside.Length > 0)
        {
            note = Joined(note, Strings.Format("Library_Depends", string.Join(", ", alongside)));
        }

        if (plan.Unknown.Count > 0)
        {
            note = Joined(note, Strings.Format("Library_DependsMissing", string.Join(", ", plan.Unknown)));
        }

        row.Note = note;
        row.InstalledDirectory = known?.Origin == LibraryOrigin.Project ? known.Info.Directory : null;
        row.CanRemove = row.InstalledDirectory is not null;
        switch (known?.Origin)
        {
            case LibraryOrigin.BuiltIn:
                row.Status = Strings.Get("Library_StatusBuiltIn");
                row.ActionLabel = Strings.Get("Library_Install");
                row.CanInstall = false;
                break;
            case LibraryOrigin.Project:
                int order = LibraryCatalog.CompareVersions(release.Version, known.Info.Version);
                row.Status = Strings.Format("Library_StatusProject", known.Info.Version);
                row.ActionLabel = Strings.Get(order > 0 ? "Library_Update" : order < 0 ? "Library_Replace" : "Library_Installed");
                row.CanInstall = order != 0;
                break;
            case LibraryOrigin.Documents or LibraryOrigin.Folder:
                row.Status = Strings.Format("Library_StatusDocuments", known.Info.Version);
                row.ActionLabel = Strings.Get("Library_Install");
                row.CanInstall = true;
                break;
            default:
                row.Status = "";
                row.ActionLabel = Strings.Get("Library_Install");
                row.CanInstall = true;
                break;
        }
    }

    private static string Joined(string a, string b) => a.Length == 0 ? b : a + " " + b;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task Install(LibraryRow? row)
    {
        if (row is null || _catalog is null)
        {
            return;
        }

        using CancellationTokenSource work = Begin();
        Error = "";
        Message = "";
        try
        {
            InstallPlan plan = _catalog.Plan(row.Library, row.Release, name => _inventory.Find(name) is not null);
            var progress = new Progress<LibraryDownloadProgress>(p => Progress = Strings.Format("Library_Downloading", p.Name, LibraryRow.FormatBytes(p.Done), LibraryRow.FormatBytes(p.Total)));
            var installed = new List<string>();
            foreach ((CatalogLibrary library, CatalogRelease release) in plan.Items)
            {
                await Task.Run(() => _service.Installer.InstallAsync(library, release, progress, work.Token), work.Token);
                installed.Add(library.Name);
            }

            Report(installed, plan.Unknown);
        }
        catch (LibraryException e)
        {
            Error = Explain(e);
            AfterChange();
        }
        catch (OperationCanceledException)
        {
            Message = Strings.Get("Library_Cancelled");
            AfterChange();
        }
        finally
        {
            End();
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void Remove(LibraryRow? row)
    {
        if (row?.InstalledDirectory is not { } directory)
        {
            return;
        }

        Error = "";
        try
        {
            _service.Remove(directory);
            Message = Strings.Format("Library_Removed", row.Name);
            AfterChange();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            Error = e.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task AddFolder() =>
        PickFolder?.Invoke() is { } folder ? AddAsync(token => _service.Installer.AddFolderAsync(folder, token)) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task AddZip() =>
        PickZip?.Invoke() is { } zip ? AddAsync(token => _service.Installer.AddZipAsync(zip, token)) : Task.CompletedTask;

    /// <summary>The same two ways, for a path already chosen (and for tests).</summary>
    public Task AddPathAsync(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase)
            ? AddAsync(token => _service.Installer.AddZipAsync(path, token))
            : AddAsync(token => _service.Installer.AddFolderAsync(path, token));
    }

    private async Task AddAsync(Func<CancellationToken, Task<IReadOnlyList<LibraryInfo>>> add)
    {
        using CancellationTokenSource work = Begin();
        Error = "";
        Message = "";
        Progress = Strings.Get("Library_Copying");
        try
        {
            IReadOnlyList<LibraryInfo> added = await add(work.Token);
            Report([.. added.Select(l => l.Name)], []);
        }
        catch (LibraryException e)
        {
            Error = Explain(e);
        }
        catch (OperationCanceledException)
        {
            Message = Strings.Get("Library_Cancelled");
        }
        finally
        {
            End();
        }
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _work?.Cancel();

    private void Report(IReadOnlyList<string> installed, IReadOnlyList<string> unknown)
    {
        Message = Strings.Format("Library_Done", string.Join(", ", installed));
        if (unknown.Count > 0)
        {
            Error = Strings.Format("Library_DependsMissing", string.Join(", ", unknown));
        }

        AfterChange();
    }

    private void AfterChange()
    {
        _inventory = _service.Inventory();
        foreach (LibraryRow row in Rows)
        {
            Describe(row);
        }

        _changed();
    }

    private CancellationTokenSource Begin()
    {
        var work = new CancellationTokenSource();
        _work = work;
        IsBusy = true;
        return work;
    }

    private void End()
    {
        _work = null;
        IsBusy = false;
        Progress = "";
    }

    private static string Explain(LibraryException e) => e.Failure switch
    {
        LibraryFailure.CatalogUnavailable => Strings.Get("Library_FailCatalog"),
        LibraryFailure.Download => Strings.Format("Library_FailDownload", e.Message),
        LibraryFailure.Checksum => Strings.Get("Library_FailChecksum"),
        LibraryFailure.BadArchive => Strings.Format("Library_FailArchive", e.Message),
        LibraryFailure.NotALibrary => Strings.Get("Library_FailNotALibrary"),
        LibraryFailure.AlreadyInProject => Strings.Get("Library_FailInProject"),
        _ => Strings.Format("Library_FailDisk", e.Message),
    };
}
