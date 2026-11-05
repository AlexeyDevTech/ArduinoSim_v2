using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ArduSim.App.Localization;
using ArduSim.App.Services;
using ArduSim.App.ViewModels;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Search;

namespace ArduSim.App.Views;

/// <summary>The find bar's own messages, in Russian.</summary>
internal sealed class SearchTexts : ICSharpCode.AvalonEdit.Search.Localization
{
    public override string NoMatchesFoundText => Strings.Get("Editor_FindNoMatches");

    public override string ErrorText => Strings.Get("Editor_FindError");
}

/// <summary>A clangd completion in AvalonEdit's list.</summary>
internal sealed class ClangdCompletion(CompletionEntry entry) : ICompletionData
{
    public ImageSource? Image => null;

    public string Text { get; } = entry.Label.TrimEnd('(', ')');

    public object Content => string.IsNullOrEmpty(entry.Detail) ? entry.Label : $"{entry.Label}  :  {entry.Detail}";

    public object Description => string.IsNullOrEmpty(entry.Documentation) ? entry.Detail : $"{entry.Detail}\n{entry.Documentation}".Trim();

    public double Priority => 0;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        ArgumentNullException.ThrowIfNull(textArea);
        ArgumentNullException.ThrowIfNull(completionSegment);
        textArea.Document.Replace(completionSegment, entry.InsertText);
    }
}

/// <summary>Signature help for AvalonEdit's insight window.</summary>
internal sealed class SignatureProvider(IReadOnlyList<SignatureEntry> signatures) : IOverloadProvider
{
    private int _selected;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            _selected = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(null));
        }
    }

    public int Count => signatures.Count;

    public string CurrentIndexText => $"{_selected + 1} / {signatures.Count}";

    public object CurrentHeader => signatures[_selected].Label;

    public object CurrentContent => signatures[_selected].Documentation;
}

/// <summary>
/// The IDE side of the code editor (docs/UI.md §2.5): find and replace, go to line, editing commands
/// and typing help, and what clangd knows (completion, signatures, hover, go to definition).
/// </summary>
public partial class CodeEditor
{
    private static readonly string[] Closers = [")", "]", "}", "\"", "'"];
    private readonly DispatcherTimer _clangdSync = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private SearchPanel? _search;
    private CompletionWindow? _completion;
    private OverloadInsightWindow? _insight;
    private bool _textDirty = true;

    private MainViewModel? Main => Window.GetWindow(this)?.DataContext as MainViewModel;

    private ClangdClient? Clangd => Main?.Clangd;

    private void InstallAssist()
    {
        _search = SearchPanel.Install(Editor.TextArea);
        _search.MarkerBrush = CodeEditorBrushes.Selection;
        _search.Localization = new SearchTexts();
        _search.Unloaded += (_, _) => ReplaceBar.Visibility = Visibility.Collapsed;
        TextArea area = Editor.TextArea;
        area.TextEntering += OnTextEntering;
        area.TextEntered += OnTextEntered;
        area.Caret.PositionChanged += (_, _) => UpdateCaretText();
        Editor.TextChanged += (_, _) =>
        {
            _textDirty = true;
            _clangdSync.Stop();
            _clangdSync.Start();
        };
        _clangdSync.Tick += async (_, _) =>
        {
            _clangdSync.Stop();
            await SyncClangdAsync();
        };
        area.PreviewMouseLeftButtonDown += OnPreviewClick;
        area.PreviewKeyDown += OnAssistKey;
        Editor.ContextMenuOpening += OnContextMenuOpening;
        Editor.ContextMenu = new ContextMenu();
    }

    private void OnAssistKey(object sender, KeyEventArgs e)
    {
        ModifierKeys modifiers = Keyboard.Modifiers;
        bool ctrl = modifiers == ModifierKeys.Control;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        switch (key)
        {
            case Key.H when ctrl:
                ShowReplace();
                break;
            case Key.G or Key.L when ctrl:
                ShowGoToLine();
                break;
            case Key.OemQuestion or Key.Divide when ctrl:
            case Key.Oem2 when ctrl:
                ToggleComment();
                break;
            case Key.T when ctrl:
            case Key.F when modifiers == (ModifierKeys.Shift | ModifierKeys.Alt):
                _ = FormatAsync();
                break;
            case Key.Up when modifiers == ModifierKeys.Alt:
                MoveLines(-1);
                break;
            case Key.Down when modifiers == ModifierKeys.Alt:
                MoveLines(1);
                break;
            case Key.D when modifiers == (ModifierKeys.Control | ModifierKeys.Shift):
            case Key.D when ctrl:
                DuplicateLine();
                break;
            case Key.Z when modifiers == (ModifierKeys.Control | ModifierKeys.Shift):
                Editor.Redo();
                break;
            case Key.Back when modifiers == ModifierKeys.None && RemoveEmptyPair():
                break;
            case Key.Space when ctrl:
                _ = ShowCompletionAsync();
                break;
            case Key.F12 when modifiers == ModifierKeys.None:
                _ = GoToDefinitionAsync(Editor.TextArea.Caret.Line, Editor.TextArea.Caret.Column);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    // ---- find, replace, go to line -----------------------------------------------------------------

    private void OpenFind()
    {
        Editor.TextArea.Focus();
        if (_search is { } search)
        {
            string selected = Editor.SelectedText;
            if (selected.Length > 0 && !selected.Contains('\n', StringComparison.Ordinal))
            {
                search.SearchPattern = selected;
            }

            search.Open();
            search.Reactivate();
        }
    }

    private void ShowReplace()
    {
        OpenFind();
        ReplaceBar.Visibility = Visibility.Visible;
        ReplaceBox.Focus();
        ReplaceBox.SelectAll();
    }

    private void OnReplaceKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ReplaceBar.Visibility = Visibility.Collapsed;
            _search?.Close();
            Editor.TextArea.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            ReplaceCurrent();
            e.Handled = true;
        }
    }

    private void OnReplaceOne(object sender, RoutedEventArgs e) => ReplaceCurrent();

    private void OnReplaceAll(object sender, RoutedEventArgs e)
    {
        if (_search is not { } search || Editor.Document is not { } document || string.IsNullOrEmpty(search.SearchPattern))
        {
            return;
        }

        List<ISearchResult> matches;
        try
        {
            matches = [.. Strategy(search).FindAll(document, 0, document.TextLength)];
        }
        catch (SearchPatternException failure)
        {
            ReplacedText.Text = failure.Message;
            return;
        }

        using (document.RunUpdate())
        {
            foreach (ISearchResult match in Enumerable.Reverse(matches))
            {
                document.Replace(match.Offset, match.Length, ReplaceBox.Text);
            }
        }

        ReplacedText.Text = Strings.Format("Editor_Replaced", matches.Count);
    }

    private void ReplaceCurrent()
    {
        if (_search is not { } search || Editor.Document is not { } document || string.IsNullOrEmpty(search.SearchPattern))
        {
            return;
        }

        try
        {
            if (Editor.SelectionLength > 0 && Strategy(search).FindNext(document, Editor.SelectionStart, Editor.SelectionLength) is { } selected && selected.Offset == Editor.SelectionStart && selected.Length == Editor.SelectionLength)
            {
                document.Replace(Editor.SelectionStart, Editor.SelectionLength, ReplaceBox.Text);
            }
        }
        catch (SearchPatternException failure)
        {
            ReplacedText.Text = failure.Message;
            return;
        }

        search.FindNext();
    }

    private static ISearchStrategy Strategy(SearchPanel search) =>
        SearchStrategyFactory.Create(search.SearchPattern ?? "", !search.MatchCase, search.WholeWords, search.UseRegex ? SearchMode.RegEx : SearchMode.Normal);

    private void ShowGoToLine()
    {
        GoToHint.Text = Strings.Format("Editor_GoToLine", Editor.Document?.LineCount ?? 1);
        GoToBox.Text = "";
        GoToError.Visibility = Visibility.Collapsed;
        GoToBar.Visibility = Visibility.Visible;
        GoToBox.Focus();
    }

    private void OnGoToKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            GoToBar.Visibility = Visibility.Collapsed;
            Editor.TextArea.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            string[] parts = GoToBox.Text.Trim().Split(':', ',');
            if (Editor.Document is { } document && int.TryParse(parts[0], out int line) && line >= 1 && line <= document.LineCount)
            {
                int column = parts.Length > 1 && int.TryParse(parts[1], out int c) ? c : 1;
                Editor.TextArea.Caret.Location = new TextLocation(line, Math.Clamp(column, 1, document.GetLineByNumber(line).Length + 1));
                Editor.ScrollTo(line, column);
                GoToBar.Visibility = Visibility.Collapsed;
                Editor.TextArea.Focus();
            }
            else
            {
                GoToError.Visibility = Visibility.Visible;
            }

            e.Handled = true;
        }
    }

    private void UpdateCaretText()
    {
        if (_document is { } document)
        {
            document.CaretText = Strings.Format("Editor_Caret", Editor.TextArea.Caret.Line, Editor.TextArea.Caret.Column);
        }
    }

    // ---- editing commands ----------------------------------------------------------------------------

    private (DocumentLine First, DocumentLine Last) SelectedLines()
    {
        TextDocument document = Editor.Document;
        if (Editor.SelectionLength == 0)
        {
            DocumentLine line = document.GetLineByOffset(Editor.CaretOffset);
            return (line, line);
        }

        DocumentLine first = document.GetLineByOffset(Editor.SelectionStart);
        int endOffset = Editor.SelectionStart + Editor.SelectionLength;
        DocumentLine last = document.GetLineByOffset(endOffset);

        // A selection that ends at the start of a line does not include that line.
        if (last.Offset == endOffset && last != first)
        {
            last = last.PreviousLine;
        }

        return (first, last);
    }

    private void ToggleComment()
    {
        (DocumentLine first, DocumentLine last) = SelectedLines();
        TextDocument document = Editor.Document;
        var lines = new List<DocumentLine>();
        for (DocumentLine? line = first; line is not null; line = line == last ? null : line.NextLine)
        {
            lines.Add(line);
        }

        bool allCommented = lines.Where(l => document.GetText(l).Trim().Length > 0).All(l => document.GetText(l).TrimStart().StartsWith("//", StringComparison.Ordinal));
        int indent = lines.Where(l => document.GetText(l).Trim().Length > 0).Select(l => document.GetText(l).TakeWhile(char.IsWhiteSpace).Count()).DefaultIfEmpty(0).Min();
        using (document.RunUpdate())
        {
            foreach (DocumentLine line in Enumerable.Reverse(lines))
            {
                string text = document.GetText(line);
                if (allCommented)
                {
                    int at = text.IndexOf("//", StringComparison.Ordinal);
                    if (at >= 0)
                    {
                        int length = at + 2 < text.Length && text[at + 2] == ' ' ? 3 : 2;
                        document.Remove(line.Offset + at, length);
                    }
                }
                else if (text.Trim().Length > 0)
                {
                    document.Insert(line.Offset + indent, "// ");
                }
            }
        }
    }

    private void MoveLines(int direction)
    {
        (DocumentLine first, DocumentLine last) = SelectedLines();
        TextDocument document = Editor.Document;
        DocumentLine? neighbour = direction < 0 ? first.PreviousLine : last.NextLine;
        if (neighbour is null)
        {
            return;
        }

        int caretLine = Editor.TextArea.Caret.Line;
        int caretColumn = Editor.TextArea.Caret.Column;
        string block = document.GetText(first.Offset, last.EndOffset - first.Offset);
        string other = document.GetText(neighbour);
        int start = direction < 0 ? neighbour.Offset : first.Offset;
        int end = direction < 0 ? last.EndOffset : neighbour.EndOffset;
        string newline = TextUtilities.GetNewLineFromDocument(document, first.LineNumber);
        document.Replace(start, end - start, direction < 0 ? block + newline + other : other + newline + block);
        Editor.TextArea.Caret.Location = new TextLocation(caretLine + direction, caretColumn);
    }

    private void DuplicateLine()
    {
        (DocumentLine first, DocumentLine last) = SelectedLines();
        string block = Editor.Document.GetText(first.Offset, last.EndOffset - first.Offset);
        Editor.Document.Insert(last.EndOffset, TextUtilities.GetNewLineFromDocument(Editor.Document, first.LineNumber) + block);
    }

    /// <summary>The whole file through the toolchain's clang-format, in the Arduino style of two spaces.</summary>
    private async Task FormatAsync()
    {
        if (Main?.ClangFormatPath is not { } tool)
        {
            Main?.ShowEditorMessage(Strings.Get("Editor_FormatNoTool"));
            return;
        }

        string before = Editor.Text;
        try
        {
            var start = new ProcessStartInfo(tool)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new System.Text.UTF8Encoding(false),
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            };
            start.ArgumentList.Add("--assume-filename=sketch.cpp");
            start.ArgumentList.Add("--style={BasedOnStyle: Google, IndentWidth: 2, ColumnLimit: 0, AllowShortFunctionsOnASingleLine: Empty, SortIncludes: false}");
            using Process process = Process.Start(start) ?? throw new IOException("clang-format did not start");
            await process.StandardInput.WriteAsync(before);
            process.StandardInput.Close();
            string after = await process.StandardOutput.ReadToEndAsync();
            string errors = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                Main?.ShowEditorMessage(Strings.Format("Editor_FormatFailed", errors.Trim()));
                return;
            }

            if (after != before && Editor.Text == before)
            {
                int line = Editor.TextArea.Caret.Line;
                ReplaceKeepingCaret(after, line);
            }
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception)
        {
            Main?.ShowEditorMessage(Strings.Format("Editor_FormatFailed", e.Message));
        }
    }

    /// <summary>Replaces only what differs, so Ctrl+Z brings the text back and the view stays where it was.</summary>
    private void ReplaceKeepingCaret(string text, int line)
    {
        TextDocument document = Editor.Document;
        string old = document.Text;
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

        document.Replace(prefix, old.Length - prefix - suffix, text.Substring(prefix, text.Length - prefix - suffix));
        Editor.TextArea.Caret.Line = Math.Min(line, document.LineCount);
    }

    // ---- typing help -----------------------------------------------------------------------------

    private void OnTextEntering(object sender, TextCompositionEventArgs e)
    {
        // A closer typed where one already stands steps over it.
        if (e.Text.Length == 1 && Closers.Contains(e.Text) && Editor.CaretOffset < Editor.Document.TextLength
            && Editor.Document.GetCharAt(Editor.CaretOffset).ToString() == e.Text && Editor.SelectionLength == 0)
        {
            Editor.CaretOffset++;
            e.Handled = true;
            return;
        }

        // Only ( and . take the list's choice: a name being typed must not turn into the nearest match.
        if (e.Text.Length > 0 && _completion is not null && !IsIdentifierChar(e.Text[0]))
        {
            if (e.Text[0] is '(' or '.')
            {
                _completion.CompletionList.RequestInsertion(e);
            }
            else
            {
                _completion.Close();
            }
        }
    }

    private void OnTextEntered(object sender, TextCompositionEventArgs e)
    {
        if (e.Text is "\n" or "\r" or "\r\n")
        {
            ExpandBraces();
            return;
        }

        if (e.Text.Length != 1)
        {
            return;
        }

        char c = e.Text[0];
        TextDocument document = Editor.Document;
        int caret = Editor.CaretOffset;
        char next = caret < document.TextLength ? document.GetCharAt(caret) : '\n';
        char before = caret >= 2 ? document.GetCharAt(caret - 2) : ' ';
        if (c is '(' or '[' && (char.IsWhiteSpace(next) || next is ')' or ']' or ';' or ','))
        {
            document.Insert(caret, c == '(' ? ")" : "]");
            Editor.CaretOffset = caret;
        }
        else if (c == '{' && (char.IsWhiteSpace(next) || next == ')'))
        {
            document.Insert(caret, "}");
            Editor.CaretOffset = caret;
        }
        else if (c is '"' or '\'' && !IsIncludeLine(caret) && !IsIdentifierChar(before) && before != c && before != '\\' && (char.IsWhiteSpace(next) || next is ')' or ';' or ',' or ']'))
        {
            document.Insert(caret, c.ToString());
            Editor.CaretOffset = caret;
        }
        else if (c == '}')
        {
            AlignClosingBrace(caret);
        }

        if (c is '.' || (c == '>' && before == '-') || (c == ':' && before == ':') || ((c is '<' or '"') && IsIncludeLine(caret)))
        {
            _ = ShowCompletionAsync();
        }
        else if (IsIdentifierChar(c) && _completion is null && IsIdentifierChar(before) && (caret < 3 || !IsIdentifierChar(document.GetCharAt(caret - 3))) && !InCommentOrString(caret))
        {
            _ = ShowCompletionAsync();
        }
        else if (c is '(' or ',')
        {
            _ = ShowSignaturesAsync();
        }
        else if (c == ')')
        {
            _insight?.Close();
        }
    }

    /// <summary>Enter between { and }: the closer gets its own line, and the caret an indented one between them.</summary>
    private void ExpandBraces()
    {
        TextDocument document = Editor.Document;
        int caret = Editor.CaretOffset;
        if (caret >= document.TextLength || document.GetCharAt(caret) != '}')
        {
            return;
        }

        DocumentLine line = document.GetLineByOffset(caret);
        if (line.PreviousLine is not { } previous || !document.GetText(previous).TrimEnd().EndsWith('{'))
        {
            return;
        }

        string indent = new([.. document.GetText(previous).TakeWhile(char.IsWhiteSpace)]);
        string newline = TextUtilities.GetNewLineFromDocument(document, line.LineNumber);
        document.Replace(line.Offset, caret - line.Offset, indent + Editor.Options.IndentationString + newline + indent);
        Editor.CaretOffset = line.EndOffset;
    }

    /// <summary>Backspace inside an empty pair takes the closer along.</summary>
    private bool RemoveEmptyPair()
    {
        TextDocument document = Editor.Document;
        int caret = Editor.CaretOffset;
        if (Editor.SelectionLength > 0 || caret == 0 || caret >= document.TextLength)
        {
            return false;
        }

        string pair = document.GetText(caret - 1, 2);
        if (pair is not ("()" or "[]" or "{}" or "\"\"" or "''"))
        {
            return false;
        }

        document.Remove(caret - 1, 2);
        return true;
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>Whether the caret sits in a line comment or a string: nothing to complete there.</summary>
    private bool InCommentOrString(int offset)
    {
        DocumentLine line = Editor.Document.GetLineByOffset(offset);
        string before = Editor.Document.GetText(line.Offset, offset - line.Offset);
        bool inString = false;
        for (int i = 0; i < before.Length; i++)
        {
            if (before[i] == '"' && (i == 0 || before[i - 1] != '\\'))
            {
                inString = !inString;
            }
            else if (!inString && before[i] == '/' && i + 1 < before.Length && before[i + 1] == '/')
            {
                return true;
            }
        }

        return inString;
    }

    private bool IsIncludeLine(int offset)
    {
        DocumentLine line = Editor.Document.GetLineByOffset(offset);
        return Editor.Document.GetText(line.Offset, offset - line.Offset).TrimStart().StartsWith("#include", StringComparison.Ordinal);
    }

    /// <summary>A } alone on its line lines up with the line of its {.</summary>
    private void AlignClosingBrace(int caret)
    {
        TextDocument document = Editor.Document;
        DocumentLine line = document.GetLineByOffset(caret);
        string typed = document.GetText(line.Offset, caret - line.Offset);
        if (typed.Trim() != "}")
        {
            return;
        }

        int depth = 0;
        for (int i = caret - 2; i >= 0; i--)
        {
            char c = document.GetCharAt(i);
            if (c == '}')
            {
                depth++;
            }
            else if (c == '{' && depth-- == 0)
            {
                DocumentLine open = document.GetLineByOffset(i);
                string indent = new([.. document.GetText(open).TakeWhile(char.IsWhiteSpace)]);
                document.Replace(line.Offset, typed.Length - 1, indent);
                return;
            }
        }
    }

    // ---- what clangd knows -----------------------------------------------------------------------

    private async Task SyncClangdAsync()
    {
        if (_document is { } document && Clangd is { } clangd && (_textDirty || !clangd.Knows(document.Path)))
        {
            _textDirty = false;
            await clangd.UpdateAsync(document.Path, Editor.Text);
        }
    }

    private async Task ShowCompletionAsync()
    {
        if (Clangd is not { } clangd || _document is not { } document)
        {
            return;
        }

        int line = Editor.TextArea.Caret.Line;
        int column = Editor.TextArea.Caret.Column;
        _clangdSync.Stop();
        _textDirty = true;
        await SyncClangdAsync();
        IReadOnlyList<CompletionEntry> entries = await clangd.CompleteAsync(document.Path, line, column);
        if (entries.Count == 0 || Editor.TextArea.Caret.Line != line || !Editor.IsKeyboardFocusWithin)
        {
            return;
        }

        _completion?.Close();
        var window = new CompletionWindow(Editor.TextArea)
        {
            Background = (Brush)FindResource("Brush.SurfaceRaised"),
            BorderBrush = (Brush)FindResource("Brush.BorderStrong"),
            Foreground = (Brush)FindResource("Brush.Text.Primary"),
            FontFamily = (FontFamily)FindResource("Font.Mono"),
            FontSize = 12,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CloseWhenCaretAtBeginning = true,
        };
        window.CompletionList.ListBox.Background = window.Background;
        window.CompletionList.ListBox.Foreground = window.Foreground;
        window.CompletionList.ListBox.BorderThickness = new Thickness(0);
        foreach (CompletionEntry entry in entries.OrderBy(e => e.SortText, StringComparer.Ordinal))
        {
            window.CompletionList.CompletionData.Add(new ClangdCompletion(entry));
        }

        // The word typed so far is what the list replaces and filters on.
        TextDocument text = Editor.Document;
        int start = Editor.CaretOffset;
        while (start > 0 && IsIdentifierChar(text.GetCharAt(start - 1)))
        {
            start--;
        }

        window.StartOffset = start;
        window.EndOffset = Editor.CaretOffset;
        window.Closed += (_, _) =>
        {
            if (_completion == window)
            {
                _completion = null;
            }
        };
        _completion = window;
        window.Show();
        string typed = text.GetText(start, Editor.CaretOffset - start);
        if (typed.Length > 0)
        {
            window.CompletionList.SelectItem(typed);
        }

    }

    private async Task ShowSignaturesAsync()
    {
        if (Clangd is not { } clangd || _document is not { } document)
        {
            return;
        }

        int line = Editor.TextArea.Caret.Line;
        int column = Editor.TextArea.Caret.Column;
        _textDirty = true;
        await SyncClangdAsync();
        IReadOnlyList<SignatureEntry> signatures = await clangd.SignaturesAsync(document.Path, line, column);
        if (signatures.Count == 0 || Editor.TextArea.Caret.Line != line)
        {
            _insight?.Close();
            return;
        }

        _insight?.Close();
        _insight = new OverloadInsightWindow(Editor.TextArea)
        {
            Provider = new SignatureProvider(signatures),
            Background = (Brush)FindResource("Brush.SurfaceRaised"),
            Foreground = (Brush)FindResource("Brush.Text.Primary"),
        };
        _insight.Closed += (_, _) => _insight = null;
        _insight.Show();
    }

    private async Task GoToDefinitionAsync(int line, int column)
    {
        if (Clangd is not { } clangd || _document is not { } document || Main is not { } main)
        {
            return;
        }

        _textDirty = true;
        await SyncClangdAsync();
        if (await clangd.DefinitionAsync(document.Path, line, column) is { } target && main.OpenFile(target.Path) is { } opened)
        {
            opened.Navigate(target.Line, target.Column);
        }
        else
        {
            main.ShowEditorMessage(Strings.Get("Editor_DefinitionNotFound"));
        }
    }

    private void OnPreviewClick(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && Editor.GetPositionFromPoint(e.GetPosition(Editor)) is { } position)
        {
            _ = GoToDefinitionAsync(position.Line, position.Column);
            e.Handled = true;
        }
    }

    private async Task<string?> HoverTextAsync(int line, int column)
    {
        if (Clangd is not { } clangd || _document is not { } document)
        {
            return null;
        }

        await SyncClangdAsync();
        return await clangd.HoverAsync(document.Path, line, column);
    }

    // ---- right click ------------------------------------------------------------------------------

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // A right click outside the selection moves the caret there first.
        Point at = Mouse.GetPosition(Editor);
        if (Editor.GetPositionFromPoint(at) is { } position)
        {
            int offset = Editor.Document.GetOffset(position.Location);
            if (offset < Editor.SelectionStart || offset > Editor.SelectionStart + Editor.SelectionLength)
            {
                Editor.SelectionLength = 0;
                Editor.TextArea.Caret.Position = position;
            }
        }

        ContextMenu menu = Editor.ContextMenu;
        menu.Items.Clear();
        void Add(string key, string gesture, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = Strings.Get(key), InputGestureText = gesture, IsEnabled = enabled };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        bool editable = !Editor.IsReadOnly;
        bool selection = Editor.SelectionLength > 0;
        Add("Menu_Cut", "Ctrl+X", Editor.Cut, editable && selection);
        Add("Menu_Copy", "Ctrl+C", Editor.Copy, selection);
        Add("Menu_Paste", "Ctrl+V", Editor.Paste, editable && Clipboard.ContainsText());
        Add("Menu_SelectAll", "Ctrl+A", Editor.SelectAll);
        menu.Items.Add(new Separator());
        Add("Menu_Find", "Ctrl+F", OpenFind);
        Add("Menu_ReplaceText", "Ctrl+H", ShowReplace, editable);
        Add("Menu_GoToLine", "Ctrl+G", ShowGoToLine);
        if (Clangd is not null)
        {
            Add("Menu_GoToDefinition", "F12", () => _ = GoToDefinitionAsync(Editor.TextArea.Caret.Line, Editor.TextArea.Caret.Column));
        }

        menu.Items.Add(new Separator());
        Add("Menu_ToggleComment", "Ctrl+/", ToggleComment, editable);
        Add("Menu_Format", "Ctrl+T", () => _ = FormatAsync(), editable && Main?.ClangFormatPath is not null);
        if (Diagnostics().Count > 0 && Main is { } main)
        {
            menu.Items.Add(new Separator());
            Add("Menu_NextError", "F8", () => main.NextDiagnosticCommand.Execute(null));
            Add("Menu_PreviousError", "Shift+F8", () => main.PreviousDiagnosticCommand.Execute(null));
        }
    }
}
