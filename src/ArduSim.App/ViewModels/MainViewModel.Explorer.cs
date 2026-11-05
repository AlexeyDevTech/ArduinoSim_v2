using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using ArduSim.App.Localization;
using ArduSim.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArduSim.App.ViewModels;

public enum ExplorerKind
{
    File,
    Part,
    Library,
    Symbol,
    Note,
}

/// <summary>One row of the project explorer: a sketch file, a part on the schematic or a library of the last build.</summary>
public sealed partial class ExplorerEntry(ExplorerKind kind, string title, string detail, string? path = null, string? partId = null, bool isMain = false) : ObservableObject
{
    public ExplorerKind Kind { get; } = kind;

    public string Title { get; } = title;

    public string Detail { get; } = detail;

    public string? Path { get; } = path;

    public string? PartId { get; } = partId;

    /// <summary>The sketch's main file: named after the project, so it cannot be renamed or deleted here.</summary>
    public bool IsMain { get; } = isMain;

    public bool CanOpen => Kind is ExplorerKind.File or ExplorerKind.Part or ExplorerKind.Symbol;

    /// <summary>The line of a function or variable in the open file.</summary>
    public int Line { get; init; }

    /// <summary>The open tab or the selected part.</summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; set; }
}

public sealed partial class ExplorerGroup(string key, string name) : ObservableObject
{
    public string Key { get; } = key;

    public string Name { get; } = name;

    public ObservableCollection<ExplorerEntry> Items { get; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;
}

/// <summary>The Проект page of the left panel: sketch files, parts and libraries, with file commands.</summary>
public sealed partial class MainViewModel
{
    private static readonly string[] IgnoredFolders = [".pio", ".git", ".vscode", "build", "bin", "obj"];
    private string? _explorerSignature;
    private IReadOnlyList<SymbolEntry> _outline = [];
    private string? _outlineOf;

    public ObservableCollection<ExplorerGroup> ExplorerGroups { get; } = [];

    /// <summary>All sources of the sketch, with their subfolders (a PlatformIO <c>src</c> tree).</summary>
    private List<string> SketchFiles()
    {
        if (_project is null || _projectDirectory is null || !Directory.Exists(_project.SketchDirectory(_projectDirectory)))
        {
            return [];
        }

        string root = _project.SketchDirectory(_projectDirectory);
        return [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => SourceExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Where(f => !Path.GetRelativePath(root, f).Split(Path.DirectorySeparatorChar).SkipLast(1).Any(d => IgnoredFolders.Contains(d, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(f => Path.GetExtension(f).Equals(".ino", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)];
    }

    private bool IsMainFile(string path)
    {
        if (_project is null || _projectDirectory is null || !Path.GetExtension(path).Equals(".ino", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string name = Path.GetFileNameWithoutExtension(path);
        return string.Equals(name, ProjectName, StringComparison.OrdinalIgnoreCase)
            || SketchFiles().Count(f => Path.GetExtension(f).Equals(".ino", StringComparison.OrdinalIgnoreCase)) == 1;
    }

    /// <summary>Rebuilds the rows when what they show changed; the page is not touched while it is hidden.</summary>
    private void RefreshExplorer()
    {
        if (LeftTab != ProjectPage && _explorerSignature is not null)
        {
            _explorerSignature = "stale";
            return;
        }

        if (_project is null || _projectDirectory is null)
        {
            ExplorerGroups.Clear();
            _explorerSignature = null;
            return;
        }

        string root = _project.SketchDirectory(_projectDirectory);
        List<string> files = SketchFiles();
        List<PartViewModel> parts = Scene is { } scene ? [.. scene.Parts] : [];
        string signature = string.Join('|', files) + "#" + string.Join('|', parts.Select(p => p.Id + ":" + p.DisplayName)) + "#" + string.Join('|', _buildLibraries)
            + "#" + _outlineOf + ":" + string.Join('|', _outline.Select(s => s.Name + s.Line.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (signature == _explorerSignature)
        {
            return;
        }

        _explorerSignature = signature;
        Dictionary<string, bool> expanded = ExplorerGroups.ToDictionary(g => g.Key, g => g.IsExpanded);
        ExplorerGroups.Clear();

        var sketch = new ExplorerGroup("sketch", Strings.Get("Explorer_Sketch"));
        foreach (string file in files)
        {
            sketch.Items.Add(new ExplorerEntry(ExplorerKind.File, Path.GetRelativePath(root, file), "", file, isMain: IsMainFile(file)));
        }

        var scheme = new ExplorerGroup("scene", Strings.Format("Explorer_Scene", parts.Count));
        foreach (PartViewModel part in parts)
        {
            scheme.Items.Add(new ExplorerEntry(ExplorerKind.Part, part.Id, part.DisplayName, partId: part.Id));
        }

        var libraries = new ExplorerGroup("libraries", Strings.Get("Explorer_Libraries"));
        foreach (string library in _buildLibraries)
        {
            libraries.Items.Add(new ExplorerEntry(ExplorerKind.Library, Path.GetFileName(library), ""));
        }

        if (libraries.Items.Count == 0)
        {
            libraries.Items.Add(new ExplorerEntry(ExplorerKind.Note, Strings.Get("Explorer_LibrariesHint"), ""));
        }

        var outline = new ExplorerGroup("outline", Strings.Format("Explorer_Outline", Path.GetFileName(_outlineOf ?? "")));
        foreach (SymbolEntry symbol in _outline)
        {
            outline.Items.Add(new ExplorerEntry(ExplorerKind.Symbol, symbol.Name, symbol.Detail, _outlineOf) { Line = symbol.Line });
        }

        foreach (ExplorerGroup group in _outline.Count > 0 ? new[] { sketch, outline, scheme, libraries } : [sketch, scheme, libraries])
        {
            group.IsExpanded = !expanded.TryGetValue(group.Key, out bool open) || open;
            ExplorerGroups.Add(group);
        }

        UpdateExplorerCurrent();
    }

    private void UpdateExplorerCurrent()
    {
        foreach (ExplorerEntry entry in ExplorerGroups.SelectMany(g => g.Items))
        {
            entry.IsCurrent = entry.Kind switch
            {
                ExplorerKind.File => SelectedTab is CodeDocumentViewModel d && entry.Path is { } path && SamePath(d.Path, path),
                ExplorerKind.Part => Scene?.SelectedParts.Contains(entry.PartId ?? "") == true,
                _ => false,
            };
        }
    }

    private void OnSceneSelectionChanged()
    {
        UpdateExplorerCurrent();
        ClearEepromArmed = false;
        OnPropertyChanged(nameof(ShowExampleInfo));
        OnPropertyChanged(nameof(ShowProjectActions));
    }

    /// <summary>Asks clangd for the shown file's functions and variables, for the Структура group.</summary>
    public async Task RefreshOutlineAsync()
    {
        if (Clangd is not { } clangd || LeftTab != ProjectPage || VisibleDocument is not { } document)
        {
            return;
        }

        await clangd.UpdateAsync(document.Path, document.Document.Text);
        IReadOnlyList<SymbolEntry> symbols = await clangd.SymbolsAsync(document.Path);
        if (VisibleDocument == document)
        {
            _outline = symbols;
            _outlineOf = document.Path;
            RefreshExplorer();
        }
    }

    /// <summary>A click on a row: the file opens, the part is selected and brought into view.</summary>
    [RelayCommand]
    private void OpenExplorerEntry(ExplorerEntry? entry)
    {
        if (entry?.Path is { } path && entry.Kind == ExplorerKind.File)
        {
            OpenFile(path);
        }
        else if (entry?.Path is { } file && entry.Kind == ExplorerKind.Symbol)
        {
            OpenFile(file)?.Navigate(entry.Line, 1);
        }
        else if (entry?.PartId is { } id && Scene?.Find(id) is { } part)
        {
            if (!SplitView)
            {
                SelectedTab = SceneTab.Instance;
            }

            Scene.Select(id, additive: false);
            ShowPartRequested?.Invoke(part);
        }
    }

    /// <summary>The file name for a new or renamed file, checked: the full path, or the reason it cannot be used.</summary>
    public (string? Path, string? Error) CheckFileName(string name, string? renaming = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        name = name.Trim();
        if (_project is null || _projectDirectory is null)
        {
            return (null, Strings.Get("Tip_NoProject"));
        }

        if (name.Length == 0)
        {
            return (null, Strings.Get("File_NameEmpty"));
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 && !name.Contains('/', StringComparison.Ordinal) && !name.Contains('\\', StringComparison.Ordinal))
        {
            return (null, Strings.Get("File_NameInvalid"));
        }

        if (Path.GetExtension(name).Length == 0)
        {
            name += ".h";
        }

        if (!SourceExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))
        {
            return (null, Strings.Get("File_NameExtension"));
        }

        string folder = renaming is null ? _project.SketchDirectory(_projectDirectory) : Path.GetDirectoryName(renaming)!;
        string path = Path.GetFullPath(Path.Combine(folder, name));
        if (File.Exists(path) && !string.Equals(path, renaming, StringComparison.OrdinalIgnoreCase))
        {
            return (null, Strings.Format("File_NameTaken", name));
        }

        return (path, null);
    }

    /// <summary>Creates a file in the sketch folder and opens it; the reason on failure.</summary>
    public string? CreateSketchFile(string name)
    {
        (string? path, string? error) = CheckFileName(name);
        if (path is null)
        {
            return error;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }

        OpenFile(path);
        RefreshExplorer();
        return null;
    }

    /// <summary>Renames a sketch file, keeping an open tab's edits by saving them first.</summary>
    public string? RenameSketchFile(ExplorerEntry entry, string name)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Path is not { } old || entry.IsMain)
        {
            return Strings.Get("File_MainFixed");
        }

        (string? path, string? error) = CheckFileName(name, old);
        if (path is null)
        {
            return error;
        }

        CodeDocumentViewModel? open = Documents.FirstOrDefault(d => SamePath(d.Path, old));
        try
        {
            open?.Save();
            File.Move(old, path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }

        if (open is not null)
        {
            bool selected = SelectedTab == open;
            ForgetDocument(open);
            CodeDocumentViewModel renamed = AddDocument(path);
            if (selected)
            {
                SelectedTab = renamed;
            }
        }

        RefreshExplorer();
        return null;
    }

    /// <summary>Moves a sketch file to the Recycle Bin, so nothing is lost and no question is needed.</summary>
    public void DeleteSketchFile(ExplorerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Path is not { } path || entry.IsMain)
        {
            return;
        }

        try
        {
            CodeDocumentViewModel? open = Documents.FirstOrDefault(d => SamePath(d.Path, path));
            open?.Save();
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            if (open is not null)
            {
                ForgetDocument(open);
            }

            ShowToast(Strings.Format("Toast_FileDeleted", entry.Title));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            ShowBanner(BannerKind.Info, Strings.Get("Banner_DeleteFailed"), e.Message, "");
        }

        RefreshExplorer();
    }

    public static void RevealFile(ExplorerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Path is { } path)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true })?.Dispose();
        }
    }

    private void ForgetDocument(CodeDocumentViewModel document)
    {
        int index = Tabs.IndexOf(document);
        document.PropertyChanged -= OnDocumentChanged;
        Tabs.Remove(document);
        if (SelectedTab is null || SelectedTab == document)
        {
            SelectedTab = Tabs[Math.Clamp(index - 1, 0, Tabs.Count - 1)];
        }

        if (_lastDocument == document)
        {
            _lastDocument = Documents.FirstOrDefault();
        }

        OnPropertyChanged(nameof(VisibleDocument));
        UpdateDirty();
    }

    /// <summary>A file deleted outside the app: an unedited tab closes, an edited one stays so its text can be saved again.</summary>
    private void OnFileDeleted(string path, Action refresh) => _dispatcher.InvokeAsync(async () =>
    {
        await Task.Delay(TimeSpan.FromMilliseconds(400));
        if (Documents.FirstOrDefault(d => SamePath(d.Path, path)) is { } document)
        {
            if (File.Exists(path))
            {
                if (document.OnDiskChanged())
                {
                    IsRunStale = true;
                    ShowToast(Strings.Format("Toast_Reloaded", document.Title));
                }
            }
            else if (document.IsDirty)
            {
                ShowToast(Strings.Format("Toast_FileGoneEdited", document.Title));
            }
            else
            {
                ForgetDocument(document);
                ShowToast(Strings.Format("Toast_FileGone", document.Title));
            }
        }

        refresh();
    }, DispatcherPriority.Normal);
}
