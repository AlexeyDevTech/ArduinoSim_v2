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
        Editor.MouseHover += OnMouseHover;
        Editor.MouseHoverStopped += (_, _) => _tip.IsOpen = false;
        ApplyTheme();
        Loaded += (_, _) => Theme.Changed += ApplyTheme;
        Unloaded += (_, _) => Theme.Changed -= ApplyTheme;
        DataContextChanged += (_, e) => Attach(e.NewValue as CodeDocumentViewModel);
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _ = Dispatcher.BeginInvoke(() => Editor.TextArea.Focus(), DispatcherPriority.Input);
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
        }

        _document = document;
        if (document is not null)
        {
            document.NavigateRequested += OnNavigate;
            document.PropertyChanged += OnDocumentChanged;
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

    private void OnMouseHover(object sender, MouseEventArgs e)
    {
        if (Editor.GetPositionFromPoint(e.GetPosition(Editor)) is not { } position)
        {
            return;
        }

        string[] messages = [.. Diagnostics().Where(d => d.Line == position.Line).Select(d => d.Message)];
        if (messages.Length == 0)
        {
            return;
        }

        _tip.PlacementTarget = Editor;
        _tip.Content = string.Join(Environment.NewLine, messages);
        _tip.IsOpen = true;
        e.Handled = true;
    }
}
