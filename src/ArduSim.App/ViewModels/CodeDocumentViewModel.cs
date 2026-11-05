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
        Document.Changed += (_, e) => FollowEdit(e);
        Load();
    }

    /// <summary>Marks stay on their code: an edited line drops its marks, later lines' marks move with the text.</summary>
    private void FollowEdit(DocumentChangeEventArgs change)
    {
        if (_loading || Diagnostics.Count == 0)
        {
            return;
        }

        int line = Document.GetLineByOffset(Math.Min(change.Offset, Document.TextLength)).LineNumber;
        int delta = change.InsertedText.Text.Count(c => c == '\n') - change.RemovedText.Text.Count(c => c == '\n');
        Diagnostics = [.. Diagnostics.Where(d => d.Line != line).Select(d => d.Line > line ? d with { Line = d.Line + delta } : d)];
    }

    /// <summary>The compiler counts columns in bytes of UTF-8; the editor in characters. They differ on a line with Cyrillic.</summary>
    public int CharColumn(BuildDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        if (diagnostic.Line < 1 || diagnostic.Line > Document.LineCount)
        {
            return diagnostic.Column;
        }

        string text = Document.GetText(Document.GetLineByNumber(diagnostic.Line));
        int bytes = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (bytes >= diagnostic.Column - 1)
            {
                return i + 1;
            }

            bytes += char.IsSurrogate(text[i]) ? 2 : Utf8.GetByteCount(text.AsSpan(i, 1));
        }

        return text.Length + 1;
    }

    /// <summary>Carries out a message's fix: one edit, one undo step.</summary>
    public void Apply(BuildDiagnostic diagnostic, DiagnosticFix fix)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        ArgumentNullException.ThrowIfNull(fix);
        if (diagnostic.Line > Document.LineCount)
        {
            return;
        }

        ICSharpCode.AvalonEdit.Document.DocumentLine line = Document.GetLineByNumber(diagnostic.Line);
        int offset = line.Offset + Math.Min(CharColumn(diagnostic) - 1, line.Length);
        Document.Replace(offset, Math.Min(fix.Remove, Document.TextLength - offset), fix.Insert);
        Navigate(diagnostic.Line, offset - line.Offset + fix.Insert.Length + 1);
    }

    /// <summary>The file was not UTF-8 (a sketch from an old Arduino IDE): it was read as Windows-1251 and will be saved as UTF-8.</summary>
    public bool LegacyEncoding { get; private set; }

    private static string ReadText(string path, out bool legacy)
    {
        byte[] bytes = File.ReadAllBytes(path);
        try
        {
            legacy = false;
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException)
        {
            legacy = true;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1251).GetString(bytes);
        }
    }

    /// <summary>The conflict bar's text, naming the file.</summary>
    public string ConflictText => Localization.Strings.Format("Editor_Conflict", Title);

    /// <summary>The caret, for the status bar.</summary>
    [ObservableProperty]
    public partial string CaretText { get; set; } = "";

    /// <summary>Raised to move the keyboard into this editor.</summary>
    public event Action? FocusRequested;

    public void RequestFocus() => FocusRequested?.Invoke();

    /// <summary>Raised to open the find bar (Ctrl+F while the focus is elsewhere).</summary>
    public event Action? FindRequested;

    public void RequestFind() => FindRequested?.Invoke();

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

    /// <summary>A file outside the project, such as a library header reached by go to definition: it can be read, not changed.</summary>
    public bool IsReadOnly { get; init; }

    public void Save()
    {
        if (!IsDirty || IsReadOnly)
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
            text = ReadText(Path, out _);
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

        ReplaceMiddle(text);
        return true;
    }

    [RelayCommand]
    private void ReloadFromDisk()
    {
        ReplaceMiddle(_diskText);
        HasConflict = false;
    }

    /// <summary>Takes the file's new text: only what differs is replaced, so the caret stays and Ctrl+Z brings the text back.</summary>
    private void ReplaceMiddle(string text)
    {
        string old = Document.Text;
        int prefix = 0;
        while (prefix < old.Length && prefix < text.Length && old[prefix] == text[prefix])
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < old.Length - prefix && suffix < text.Length - prefix && old[old.Length - 1 - suffix] == text[text.Length - 1 - suffix])
        {
            suffix++;
        }

        _loading = true;
        Document.Replace(prefix, old.Length - prefix - suffix, text.Substring(prefix, text.Length - prefix - suffix));
        _loading = false;
        IsDirty = false;
    }

    [RelayCommand]
    private void KeepMine() => HasConflict = false;

    private void Load()
    {
        bool legacy = false;
        _diskText = File.Exists(Path) ? ReadText(Path, out legacy) : "";
        LegacyEncoding = legacy;
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
