using System.IO;
using System.Windows.Threading;
using ArduSim.Core.Boards;
using ArduSim.Core.Projects;
using ArduSim.Core.Screens;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArduSim.App.ViewModels;

/// <summary>The Экраны tab: beside the schematic, when the project has a display (ADR-019).</summary>
public sealed class ScreensTab
{
    public static ScreensTab Instance { get; } = new();
}

/// <summary>The screen editor's place in the window, and what it asks of the rest of the app.</summary>
public sealed partial class MainViewModel : IScreensHost
{
    public const int ScreensPage = 2;

    private int _pageBeforeScreens = -1;
    private bool _sketchDrawsScreens;
    private bool _applyingScreenEdit;
    private DispatcherTimer? _screensRescan;

    /// <summary>The Экраны tab's contents, once it has been opened.</summary>
    [ObservableProperty]
    public partial ScreensViewModel? Screens { get; private set; }

    public bool IsScreensTab => SelectedTab is ScreensTab;

    public bool HasScreensTab => Tabs.Contains(ScreensTab.Instance);

    /// <summary>On project open: code that already draws screens brings the tab, whatever the schematic has.</summary>
    private void FindScreensInSketch()
    {
        try
        {
            _sketchDrawsScreens = ScreenCodeAnalyzer.Analyze(((IScreensHost)this).SketchSources()) is not null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _sketchDrawsScreens = false;
        }

        UpdateScreensTab();
    }

    /// <summary>The tab is there when the schematic has a display or the project already has screens.</summary>
    private void UpdateScreensTab()
    {
        if (_projectDirectory is null || Scene is null)
        {
            return;
        }

        bool wanted = _sketchDrawsScreens || ScreenHardware.DisplayOf(SceneDocument) is not null || File.Exists(Path.Combine(_projectDirectory, ScreenDocument.FileName));
        if (wanted && !HasScreensTab)
        {
            Tabs.Insert(Math.Min(1, Tabs.Count), ScreensTab.Instance);
            OnPropertyChanged(nameof(HasScreensTab));
        }
        else if (!wanted && HasScreensTab)
        {
            if (IsScreensTab)
            {
                SelectedTab = SceneTab.Instance;
            }

            Tabs.Remove(ScreensTab.Instance);
            OnPropertyChanged(nameof(HasScreensTab));
        }
    }

    private void CloseScreens()
    {
        _screensRescan?.Stop();
        Screens = null;
        _sketchDrawsScreens = false;
        _pageBeforeScreens = -1;
        OnPropertyChanged(nameof(HasScreensTab));
    }

    /// <summary>What the tab shows follows the selection: the left panel its list, the right panel its fields.</summary>
    private void OnScreensTabShown(bool shown)
    {
        OnPropertyChanged(nameof(IsScreensTab));
        if (shown)
        {
            EnsureScreens();
            if (_pageBeforeScreens < 0)
            {
                _pageBeforeScreens = LeftTab;
                LeftTab = ScreensPage;
            }

            Screens!.Reload();
        }
        else if (_pageBeforeScreens >= 0)
        {
            if (LeftTab == ScreensPage)
            {
                LeftTab = _pageBeforeScreens;
            }

            _pageBeforeScreens = -1;
            _screensRescan?.Stop();
            Properties?.EndExternal();
            OnPropertyChanged(nameof(ShowExampleInfo));
            OnPropertyChanged(nameof(ShowProjectActions));
        }
    }

    private void EnsureScreens()
    {
        if (Screens is null)
        {
            Screens = new ScreensViewModel(this);
            Screens.Changed += RaiseUndoState;
        }
    }

    /// <summary>Before a build: a generated project's code follows its model and the schematic, whether or not the tab was opened.</summary>
    private void GenerateScreens()
    {
        if (_projectDirectory is null || !File.Exists(Path.Combine(_projectDirectory, ScreenDocument.FileName)))
        {
            return;
        }

        if (Screens is null)
        {
            EnsureScreens();
            Screens!.Reload();
        }

        Screens.Generate();
    }

    /// <summary>The sketch was typed in with the tab on screen (the Рядом mode): what it shows follows, a moment after the typing stops.</summary>
    private void OnSketchTextChanged()
    {
        if (_applyingScreenEdit || !IsScreensTab || Screens is null or { IsGenerated: true })
        {
            return;
        }

        if (_screensRescan is null)
        {
            _screensRescan = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _screensRescan.Tick += (_, _) =>
            {
                _screensRescan.Stop();
                Screens?.Refresh();
            };
        }

        _screensRescan.Stop();
        _screensRescan.Start();
    }

    [RelayCommand]
    private void ShowScreens()
    {
        if (HasScreensTab)
        {
            SelectedTab = ScreensTab.Instance;
        }
    }

    // ---- IScreensHost ---------------------------------------------------------------------------------

    string IScreensHost.ProjectDirectory => _projectDirectory ?? "";

    string IScreensHost.ProjectTitle => ProjectName;

    string IScreensHost.SketchDirectory => _project is not null && _projectDirectory is not null ? _project.SketchDirectory(_projectDirectory) : "";

    BoardProfile IScreensHost.ScreenBoard => _board!;

    /// <summary>The schematic as it stands.</summary>
    public SceneDocument SceneDocument => Scene is { } scene ? scene.Editor.Scene.ToDocument(_registry) : new SceneDocument();

    IReadOnlyList<(string Path, string Text)> IScreensHost.SketchSources() =>
        [.. SketchFiles().Select(f => (f, Documents.FirstOrDefault(d => SamePath(d.Path, f)) is { } open ? open.Document.Text : CodeDocumentViewModel.ReadFile(f)))];

    string? IScreensHost.ReadSketchFile(string path) =>
        Documents.FirstOrDefault(d => SamePath(d.Path, path)) is { } open ? open.Document.Text : File.Exists(path) ? CodeDocumentViewModel.ReadFile(path) : null;

    void IScreensHost.WriteSketchFile(string path, string text)
    {
        CodeDocumentViewModel? open = Documents.FirstOrDefault(d => SamePath(d.Path, path));
        if (open is { IsReadOnly: false })
        {
            if (open.Document.Text != text)
            {
                _applyingScreenEdit = true;
                open.Document.Text = text;
                _applyingScreenEdit = false;
                open.Save();
            }
        }
        else if (!File.Exists(path) || CodeDocumentViewModel.ReadFile(path) != text)
        {
            File.WriteAllText(path, text);
            open?.OnDiskChanged();
            RefreshExplorer();
        }
    }

    /// <summary>What is generated anew from the model is read, not edited: a change there would be lost at the next build.</summary>
    private bool IsGeneratedScreenCode(string path) =>
        _projectDirectory is { } project && Path.GetFileName(path) is ScreenCodeGenerator.HeaderFile or ScreenCodeGenerator.SourceFile && File.Exists(Path.Combine(project, ScreenDocument.FileName));

    bool IScreensHost.ApplySourceEdits(IReadOnlyList<SourceEdit> edits)
    {
        // All or nothing: every file is looked at before any of them changes.
        var byFile = new List<(CodeDocumentViewModel Document, List<SourceEdit> Edits)>();
        foreach (IGrouping<string, SourceEdit> file in edits.GroupBy(e => e.Span.File, StringComparer.OrdinalIgnoreCase))
        {
            CodeDocumentViewModel document = Documents.FirstOrDefault(d => SamePath(d.Path, file.Key)) ?? AddDocument(file.Key);
            string text = document.Document.Text;
            if (document.IsReadOnly || !file.All(e => e.Fits(text)))
            {
                return false;
            }

            byFile.Add((document, [.. file]));
        }

        _applyingScreenEdit = true;
        foreach ((CodeDocumentViewModel document, List<SourceEdit> list) in byFile)
        {
            using (document.Document.RunUpdate())
            {
                foreach (SourceEdit edit in list.OrderByDescending(e => e.Span.Offset))
                {
                    document.Document.Replace(edit.Span.Offset, edit.Span.Length, edit.NewText);
                }
            }
        }

        _applyingScreenEdit = false;
        return true;
    }

    private CodeDocumentViewModel? MainSketchDocument()
    {
        List<string> files = SketchFiles();
        string? main = files.FirstOrDefault(IsMainFile) ?? files.FirstOrDefault();
        return main is null ? null : Documents.FirstOrDefault(d => SamePath(d.Path, main)) ?? AddDocument(main);
    }

    string IScreensHost.MainSketchText() => MainSketchDocument()?.Document.Text ?? "";

    void IScreensHost.InsertIntoMainSketch(IReadOnlyList<(int Offset, string Text)> insertions)
    {
        if (MainSketchDocument() is not { } document)
        {
            return;
        }

        // From the end, so that earlier offsets stay right.
        _applyingScreenEdit = true;
        foreach ((int offset, string text) in insertions.OrderByDescending(i => i.Offset))
        {
            document.Document.Insert(Math.Min(offset, document.Document.TextLength), text);
        }

        _applyingScreenEdit = false;
    }

    /// <summary>In the Рядом mode the code is already beside the tab: it shows the line there, and the tab stays.</summary>
    void IScreensHost.GoToSource(SourceLocation location)
    {
        if (SplitView && IsScreensTab)
        {
            CodeDocumentViewModel? document = Documents.FirstOrDefault(d => SamePath(d.Path, location.File)) ?? (File.Exists(location.File) ? AddDocument(location.File) : null);
            if (document is not null)
            {
                _lastDocument = document;
                OnPropertyChanged(nameof(VisibleDocument));
                document.Navigate(location.Line, 1);
            }

            return;
        }

        OpenFile(location.File)?.Navigate(location.Line, 1);
    }

    void IScreensHost.OpenSketchFile(string path) => OpenFile(path);

    void IScreensHost.ShowProperties(object key, string title, string subtitle, IReadOnlyList<PropertyField> fields)
    {
        if (IsScreensTab)
        {
            Properties?.ShowExternal(key, title, subtitle, fields);
            OnPropertyChanged(nameof(ShowExampleInfo));
            OnPropertyChanged(nameof(ShowProjectActions));
        }
    }
}
