using System.IO;
using System.Text;
using ArduSim.Build.Compilation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ICSharpCode.AvalonEdit.Document;

namespace ArduSim.App.ViewModels;

/// <summary>The schematic tab; always first and not closable.</summary>
public sealed class SceneTab
{
    public static SceneTab Instance { get; } = new();
}

/// <summary>
/// One sketch file in an editor tab (docs/UI.md §2.5). The file on disk is the source of truth:
/// unchanged documents follow external edits silently; a document edited on both sides shows the
/// conflict bar.
/// </summary>
public sealed partial class CodeDocumentViewModel : ObservableObject
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private bool _loading;
    private string _diskText = "";

    public CodeDocumentViewModel(string path)
    {
        Path = path;
        Document.TextChanged += (_, _) =>
        {
            if (!_loading)
            {
                IsDirty = true;
            }
        };
        Load();
    }

    public string Path { get; }

    public string Title => System.IO.Path.GetFileName(Path);

    public TextDocument Document { get; } = new();

    [ObservableProperty]
    public partial bool IsDirty { get; private set; }

    [ObservableProperty]
    public partial bool HasConflict { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<BuildDiagnostic> Diagnostics { get; set; } = [];

    /// <summary>Raised to move the caret: (line, column), 1-based.</summary>
    public event Action<int, int>? NavigateRequested;

    public void Navigate(int line, int column) => NavigateRequested?.Invoke(line, column);

    public void Save()
    {
        if (!IsDirty)
        {
            return;
        }

        _diskText = Document.Text;
        File.WriteAllText(Path, _diskText, Utf8);
        IsDirty = false;
        HasConflict = false;
    }

    /// <summary>The file changed on disk (called on the UI thread). True when the editor reloaded it silently.</summary>
    public bool OnDiskChanged()
    {
        string text;
        try
        {
            text = File.ReadAllText(Path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false; // still being written or gone; the watcher fires again
        }

        if (text == _diskText || text == Document.Text)
        {
            _diskText = text;
            return false;
        }

        _diskText = text;
        if (IsDirty)
        {
            HasConflict = true;
            return false;
        }

        Replace(text);
        return true;
    }

    [RelayCommand]
    private void ReloadFromDisk()
    {
        Replace(_diskText);
        HasConflict = false;
    }

    [RelayCommand]
    private void KeepMine() => HasConflict = false;

    private void Load()
    {
        _diskText = File.Exists(Path) ? File.ReadAllText(Path) : "";
        Replace(_diskText);
    }

    private void Replace(string text)
    {
        _loading = true;
        Document.Text = text;
        Document.UndoStack.ClearAll();
        _loading = false;
        IsDirty = false;
    }
}
