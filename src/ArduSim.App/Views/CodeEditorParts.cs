using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using ArduSim.Build.Compilation;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Rendering;

namespace ArduSim.App.Views;

/// <summary>AvalonEdit's C++ highlighting with colors that read on the dark editor background.</summary>
internal static class CppHighlighting
{
    private static readonly Lazy<IHighlightingDefinition?> Dark = new(Load);

    public static IHighlightingDefinition? Definition => Dark.Value;

    private static IHighlightingDefinition? Load()
    {
        Assembly assembly = typeof(HighlightingManager).Assembly;
        string? name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("CPP-Mode.xshd", StringComparison.OrdinalIgnoreCase));
        if (name is null || assembly.GetManifestResourceStream(name) is not { } stream)
        {
            return HighlightingManager.Instance.GetDefinition("C++");
        }

        using (stream)
        using (var reader = XmlReader.Create(stream))
        {
            XshdSyntaxDefinition xshd = HighlightingLoader.LoadXshd(reader);
            foreach (XshdColor color in xshd.Elements.OfType<XshdColor>())
            {
                color.Foreground = new SimpleHighlightingBrush(ColorFor(color.Name ?? ""));
                color.FontWeight = null;
                color.FontStyle = null;
            }

            return HighlightingLoader.Load(xshd, HighlightingManager.Instance);
        }
    }

    private static Color ColorFor(string name) => name switch
    {
        _ when name.Contains("Comment", StringComparison.OrdinalIgnoreCase) => Rgb(0x7F8899),
        _ when name.Contains("String", StringComparison.OrdinalIgnoreCase) || name.Contains("Char", StringComparison.OrdinalIgnoreCase) => Rgb(0xE5A86B),
        _ when name.Contains("Preprocessor", StringComparison.OrdinalIgnoreCase) => Rgb(0xC792EA),
        _ when name.Contains("Digit", StringComparison.OrdinalIgnoreCase) || name.Contains("Number", StringComparison.OrdinalIgnoreCase) => Rgb(0xB5CEA8),
        _ when name.Contains("Method", StringComparison.OrdinalIgnoreCase) => Rgb(0xDCDCAA),
        _ when name.Contains("Type", StringComparison.OrdinalIgnoreCase) => Rgb(0x4EC9B0),
        _ when name.Contains("Punctuation", StringComparison.OrdinalIgnoreCase) || name.Contains("Operator", StringComparison.OrdinalIgnoreCase) => Rgb(0xC5CAD3),
        _ => Rgb(0x6CA6E8),
    };

    private static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}

/// <summary>Where a compiler diagnostic sits in the document: from its column to the end of the token.</summary>
internal static class DiagnosticSpans
{
    public static (int Offset, int Length)? Span(TextDocument document, BuildDiagnostic diagnostic)
    {
        if (diagnostic.Line < 1 || diagnostic.Line > document.LineCount)
        {
            return null;
        }

        DocumentLine line = document.GetLineByNumber(diagnostic.Line);
        string text = document.GetText(line);
        int start = diagnostic.Column >= 1 ? Math.Min(diagnostic.Column - 1, text.Length) : text.Length - text.TrimStart().Length;
        int end = start;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
        {
            end++;
        }

        if (end == start)
        {
            end = diagnostic.Column >= 1 ? Math.Min(start + 1, text.Length) : text.TrimEnd().Length;
        }

        if (end <= start)
        {
            // An empty line or a position past its end: mark the last character before it.
            start = Math.Max(0, Math.Min(start, text.Length) - 1);
            end = Math.Min(text.Length, start + 1);
        }

        return (line.Offset + start, Math.Max(0, end - start));
    }
}

/// <summary>Red (error) and amber (warning) squiggles under diagnostics (docs/UI.md §2.5).</summary>
internal sealed class SquiggleRenderer(Func<IReadOnlyList<BuildDiagnostic>> diagnostics) : IBackgroundRenderer
{
    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (textView.Document is not { } document || !textView.VisualLinesValid)
        {
            return;
        }

        foreach (BuildDiagnostic diagnostic in diagnostics())
        {
            if (DiagnosticSpans.Span(document, diagnostic) is not { Length: > 0 } span)
            {
                continue;
            }

            var pen = new Pen(diagnostic.Severity == DiagnosticSeverity.Warning ? CodeEditorBrushes.Warning : CodeEditorBrushes.Error, 1.2);
            pen.Freeze();
            foreach (Rect rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, new TextSegment { StartOffset = span.Offset, Length = span.Length }))
            {
                var geometry = new StreamGeometry();
                using (StreamGeometryContext context = geometry.Open())
                {
                    double y = rect.Bottom - 1.5;
                    context.BeginFigure(new Point(rect.Left, y), false, false);
                    bool up = true;
                    for (double x = rect.Left + 2; x <= rect.Right + 2; x += 2)
                    {
                        context.LineTo(new Point(x, up ? y - 2 : y), true, true);
                        up = !up;
                    }
                }

                geometry.Freeze();
                drawingContext.DrawGeometry(null, pen, geometry);
            }
        }
    }
}

/// <summary>A gutter column with ✖ / ⚠ next to lines that have diagnostics.</summary>
internal sealed class DiagnosticMargin(Func<IReadOnlyList<BuildDiagnostic>> diagnostics) : AbstractMargin
{
    private const double Size = 14;

    public void Refresh() => InvalidateVisual();

    protected override Size MeasureOverride(Size availableSize) => new(Size + 4, 0);

    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        if (oldTextView is not null)
        {
            oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
        }

        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null)
        {
            newTextView.VisualLinesChanged += OnVisualLinesChanged;
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (TextView is not { VisualLinesValid: true } view)
        {
            return;
        }

        ILookup<int, BuildDiagnostic> byLine = diagnostics().ToLookup(d => d.Line);
        foreach (VisualLine line in view.VisualLines)
        {
            int number = line.FirstDocumentLine.LineNumber;
            if (!byLine.Contains(number))
            {
                continue;
            }

            bool error = byLine[number].Any(d => d.Severity != DiagnosticSeverity.Warning);
            double top = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.LineTop) - view.VerticalOffset;
            double middle = top + (line.Height / 2);
            var center = new Point(2 + (Size / 2), middle);
            if (error)
            {
                drawingContext.DrawEllipse(CodeEditorBrushes.Error, null, center, 5.5, 5.5);
                var cross = new Pen(CodeEditorBrushes.OnMarker, 1.5);
                cross.Freeze();
                drawingContext.DrawLine(cross, new Point(center.X - 2.5, center.Y - 2.5), new Point(center.X + 2.5, center.Y + 2.5));
                drawingContext.DrawLine(cross, new Point(center.X + 2.5, center.Y - 2.5), new Point(center.X - 2.5, center.Y + 2.5));
            }
            else
            {
                var triangle = new StreamGeometry();
                using (StreamGeometryContext context = triangle.Open())
                {
                    context.BeginFigure(new Point(center.X, center.Y - 6), true, true);
                    context.LineTo(new Point(center.X + 6, center.Y + 5), true, false);
                    context.LineTo(new Point(center.X - 6, center.Y + 5), true, false);
                }

                triangle.Freeze();
                drawingContext.DrawGeometry(CodeEditorBrushes.Warning, null, triangle);
            }
        }
    }

    private void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();
}

/// <summary>Highlights the bracket at the caret and its partner.</summary>
internal sealed class BracketRenderer : IBackgroundRenderer
{
    private const int ScanLimit = 20_000;
    private (int A, int B)? _pair;

    public KnownLayer Layer => KnownLayer.Selection;

    /// <summary>Recomputes the pair for the caret; true when it changed.</summary>
    public bool Update(TextDocument document, int caret)
    {
        (int, int)? pair = Find(document, caret) ?? Find(document, caret - 1);
        bool changed = pair != _pair;
        _pair = pair;
        return changed;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_pair is not { } pair || !textView.VisualLinesValid)
        {
            return;
        }

        foreach (int offset in new[] { pair.A, pair.B })
        {
            foreach (Rect rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, new TextSegment { StartOffset = offset, Length = 1 }))
            {
                drawingContext.DrawRectangle(CodeEditorBrushes.Bracket, CodeEditorBrushes.BracketPen, rect);
            }
        }
    }

    private static (int, int)? Find(TextDocument document, int offset)
    {
        if (offset < 0 || offset >= document.TextLength)
        {
            return null;
        }

        char c = document.GetCharAt(offset);
        (char open, char close, int step) = c switch
        {
            '(' => ('(', ')', 1),
            '[' => ('[', ']', 1),
            '{' => ('{', '}', 1),
            ')' => (')', '(', -1),
            ']' => (']', '[', -1),
            '}' => ('}', '{', -1),
            _ => ('\0', '\0', 0),
        };
        if (step == 0)
        {
            return null;
        }

        int depth = 0;
        for (int i = offset, n = 0; i >= 0 && i < document.TextLength && n < ScanLimit; i += step, n++)
        {
            char x = document.GetCharAt(i);
            if (x == open)
            {
                depth++;
            }
            else if (x == close && --depth == 0)
            {
                return (offset, i);
            }
        }

        return null;
    }
}

internal static class CodeEditorBrushes
{
    public static readonly Brush Error = Frozen(Color.FromRgb(0xF0, 0x60, 0x5A));
    public static readonly Brush Warning = Frozen(Color.FromRgb(0xF0, 0xB4, 0x4C));
    public static readonly Brush OnMarker = Frozen(Color.FromRgb(0x14, 0x16, 0x1B));
    public static readonly Brush Bracket = Frozen(Color.FromArgb(0x40, 0x2B, 0xC4, 0xA9));
    public static readonly Pen BracketPen = FrozenPen(Color.FromArgb(0xA0, 0x2B, 0xC4, 0xA9));
    public static readonly Brush CurrentLine = Frozen(Color.FromRgb(0x1A, 0x1D, 0x24));
    public static readonly Brush Selection = Frozen(Color.FromArgb(0x60, 0x2B, 0xC4, 0xA9));

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color)
    {
        var pen = new Pen(Frozen(color), 1);
        pen.Freeze();
        return pen;
    }
}
