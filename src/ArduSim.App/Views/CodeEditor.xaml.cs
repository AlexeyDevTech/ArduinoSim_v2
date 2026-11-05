using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ArduSim.App.Services;
using ArduSim.App.ViewModels;
using ArduSim.Build.Compilation;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Indentation.CSharp;
using ICSharpCode.AvalonEdit.Rendering;

namespace ArduSim.App.Views;

/// <summary>One sketch file in AvalonEdit: highlighting, diagnostics, bracket matching, go-to (docs/UI.md §2.5).</summary>
public partial class CodeEditor : UserControl
{
    private readonly SquiggleRenderer _squiggles;
    private readonly DiagnosticMargin _margin;
    private readonly BracketRenderer _brackets = new();
    private readonly ToolTip _tip = new();
    private CodeDocumentViewModel? _document;

    public CodeEditor()
    {
        InitializeComponent();
        _squiggles = new SquiggleRenderer(Diagnostics);
        _margin = new DiagnosticMargin(Diagnostics);
        TextEditorOptions options = Editor.Options;
        options.IndentationSize = 2;
        options.ConvertTabsToSpaces = true;
        options.HighlightCurrentLine = true;
        options.EnableHyperlinks = false;
        options.EnableEmailHyperlinks = false;
        options.EnableRectangularSelection = true;
        Editor.TextArea.IndentationStrategy = new CSharpIndentationStrategy(options);
        Editor.TextArea.TextView.CurrentLineBorder = null;
        Editor.TextArea.SelectionBorder = null;
        Editor.TextArea.SelectionForeground = null;
        Editor.TextArea.TextView.BackgroundRenderers.Add(_squiggles);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_brackets);
        Editor.TextArea.LeftMargins.Insert(0, _margin);
        Editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (Editor.Document is { } document && _brackets.Update(document, Editor.CaretOffset))
            {
                Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
            }
        };
        InstallAssist();
        Editor.MouseHover += OnMouseHover;
        Editor.MouseHoverStopped += (_, _) => _tip.IsOpen = false;
        ApplyTheme();
        Loaded += (_, _) => Theme.Changed += ApplyTheme;
        Unloaded += (_, _) => Theme.Changed -= ApplyTheme;
        DataContextChanged += (_, e) => Attach(e.NewValue as CodeDocumentViewModel);

        // A file opened in its tab just now: the tab's selection came before this editor existed.
        Loaded += (_, _) =>
        {
            if (_document is not null && Main?.SelectedTab == _document)
            {
                OnFocusRequested();
            }
        };
    }

    private IReadOnlyList<BuildDiagnostic> Diagnostics() => _document?.Diagnostics ?? [];

    private void ApplyTheme()
    {
        Editor.SyntaxHighlighting = CppHighlighting.Definition;
        Editor.TextArea.TextView.CurrentLineBackground = CodeEditorBrushes.CurrentLine;
        Editor.TextArea.SelectionBrush = CodeEditorBrushes.Selection;
        Editor.TextArea.TextView.Redraw();
        _margin.InvalidateVisual();
    }

    private void Attach(CodeDocumentViewModel? document)
    {
        if (_document is not null)
        {
            _document.NavigateRequested -= OnNavigate;
            _document.PropertyChanged -= OnDocumentChanged;
            _document.FocusRequested -= OnFocusRequested;
            _document.FindRequested -= OpenFind;
        }

        _document = document;
        if (document is not null)
        {
            document.NavigateRequested += OnNavigate;
            document.PropertyChanged += OnDocumentChanged;
            document.FocusRequested += OnFocusRequested;
            document.FindRequested += OpenFind;
            _textDirty = true;
            UpdateCaretText();
        }

        Redraw();
    }

    private void OnDocumentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CodeDocumentViewModel.Diagnostics))
        {
            Redraw();
        }
    }

    private void Redraw()
    {
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        _margin.Refresh();
    }

    private void OnFocusRequested() => _ = Dispatcher.BeginInvoke(() => Editor.TextArea.Focus(), DispatcherPriority.Input);

    private void OnNavigate(int line, int column)
    {
        // The tab may have just become visible; move the caret once layout has run.
        _ = Dispatcher.BeginInvoke(
            () =>
            {
                if (Editor.Document is not { } document || line > document.LineCount)
                {
                    return;
                }

                int length = document.GetLineByNumber(line).Length;
                Editor.TextArea.Caret.Location = new TextLocation(line, Math.Min(column, length + 1));
                Editor.ScrollTo(line, column);
                Editor.TextArea.Focus();
            },
            DispatcherPriority.Loaded);
    }

    private async void OnMouseHover(object sender, MouseEventArgs e)
    {
        if (Editor.GetPositionFromPoint(e.GetPosition(Editor)) is not { } position)
        {
            return;
        }

        // A compiler message first, with its plain Russian; otherwise what clangd knows of the word.
        string[] messages = [.. Diagnostics().Where(d => d.Line == position.Line).Select(d => DiagnosticHints.Explain(d) is { } hint ? hint + Environment.NewLine + d.Message : d.Message)];
        string? text = messages.Length > 0 ? string.Join(Environment.NewLine, messages) : null;
        e.Handled = true;
        text ??= await HoverTextAsync(position.Line, position.Column);
        if (text is null || !Editor.IsMouseOver)
        {
            return;
        }

        _tip.PlacementTarget = Editor;
        _tip.Content = text;
        _tip.IsOpen = true;
    }
}
