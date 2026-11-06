using System.Globalization;
using System.Text;

namespace ArduSim.Core.Screens;

/// <summary>One replacement in a source file, of exactly what a scan read there.</summary>
public sealed record SourceEdit(SourceSpan Span, string NewText)
{
    /// <summary>Whether the file still has what the scan saw; an edit is refused if not.</summary>
    public bool Fits(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Span.Offset >= 0 && Span.Offset + Span.Length <= text.Length && string.CompareOrdinal(text, Span.Offset, Span.Expected, 0, Span.Length) == 0;
    }

    /// <summary>The edit that undoes this one, once it has been applied.</summary>
    public SourceEdit Inverse() => new(new SourceSpan(Span.File, Span.Offset, NewText.Length, NewText), Span.Expected);
}

/// <summary>Edits to code that was read (ADR-019): what was recognised exactly, or new code in the sketch's own style.</summary>
public static class ScreenCodeEdits
{
    /// <summary>The text after a set of edits made at once: their offsets all refer to the text before.</summary>
    public static string Apply(string text, IReadOnlyList<SourceEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(edits);
        foreach (SourceEdit edit in edits.OrderByDescending(e => e.Span.Offset))
        {
            text = string.Concat(text.AsSpan(0, edit.Span.Offset), edit.NewText, text.AsSpan(edit.Span.Offset + edit.Span.Length));
        }

        return text;
    }

    /// <summary>The set that undoes a set made at once; it too is made at once.</summary>
    public static IReadOnlyList<SourceEdit> Inverse(IReadOnlyList<SourceEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        var inverse = new List<SourceEdit>(edits.Count);
        foreach (SourceEdit edit in edits)
        {
            int shift = edits.Where(e => e.Span.File == edit.Span.File && e.Span.Offset < edit.Span.Offset).Sum(e => e.NewText.Length - e.Span.Length);
            inverse.Add(new SourceEdit(new SourceSpan(edit.Span.File, edit.Span.Offset + shift, edit.NewText.Length, edit.NewText), edit.Span.Expected));
        }

        return inverse;
    }

    private static SourceEdit Put(SourceInsert place, string text) => new(new SourceSpan(place.File, place.Offset, 0, ""), text);

    private static string Lines(SourceInsert place, params string[] statements) =>
        place.Inline ? string.Join(' ', statements) + " " : string.Concat(statements.Select(s => place.Indent + s + "\n"));

    /// <summary>What draws an element, in the display library's own calls.</summary>
    private static string[] Drawing(ScreenDisplay display, string receiver, ElementKind kind, int x, int y, int width, int height, string text)
    {
        string N(int value) => value.ToString(CultureInfo.InvariantCulture);
        string r = receiver;
        string white = display.Library == DisplayLibrary.AdafruitSh110x ? "SH110X_WHITE" : "SSD1306_WHITE";
        switch (kind)
        {
            case ElementKind.Text or ElementKind.Value:
                string move = display.Library == DisplayLibrary.GyverOled ? "setCursorXY" : "setCursor";
                string what = kind == ElementKind.Text ? "F(\"" + Escape(text, '"') + "\")" : text;
                return [$"{r}{move}({N(x)}, {N(y)});" + $" {r}print({what});"];
            case ElementKind.Line:
                return [display.Library switch
                {
                    DisplayLibrary.U8g2 => $"{r}drawLine({N(x)}, {N(y)}, {N(width)}, {N(height)});",
                    DisplayLibrary.GyverOled => $"{r}line({N(x)}, {N(y)}, {N(width)}, {N(height)});",
                    _ => $"{r}drawLine({N(x)}, {N(y)}, {N(width)}, {N(height)}, {white});",
                }];
            case ElementKind.Frame:
                return [display.Library switch
                {
                    DisplayLibrary.U8g2 => $"{r}drawFrame({N(x)}, {N(y)}, {N(width)}, {N(height)});",
                    DisplayLibrary.GyverOled => $"{r}rect({N(x)}, {N(y)}, {N(x + width - 1)}, {N(y + height - 1)}, OLED_STROKE);",
                    _ => $"{r}drawRect({N(x)}, {N(y)}, {N(width)}, {N(height)}, {white});",
                }];
            default:
                return [display.Library switch
                {
                    DisplayLibrary.U8g2 => $"{r}drawBox({N(x)}, {N(y)}, {N(width)}, {N(height)});",
                    DisplayLibrary.GyverOled => $"{r}rect({N(x)}, {N(y)}, {N(x + width - 1)}, {N(y + height - 1)}, OLED_FILL);",
                    _ => $"{r}fillRect({N(x)}, {N(y)}, {N(width)}, {N(height)}, {white});",
                }];
        }
    }

    /// <summary>A new element's code, where the screen's own drawing ends; null if no such place was found.</summary>
    public static IReadOnlyList<SourceEdit>? AddElement(ScreenNode screen, ScreenDisplay display, ElementKind kind, int x, int y, int width, int height, string text)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(display);
        return screen.DrawInsert is { } place && screen.Receiver.Length > 0
            ? [Put(place, Lines(place, Drawing(display, screen.Receiver, kind, x, y, width, height, text)))]
            : null;
    }

    /// <summary>Takes what was read as one element, or one transition, out of the code; null if it cannot go alone.</summary>
    public static IReadOnlyList<SourceEdit>? Remove(IReadOnlyList<SourceSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);
        return spans.Count == 0 ? null : [.. spans.Select(s => new SourceEdit(s, ""))];
    }

    /// <summary>A new screen: its constant and a case that draws its name.</summary>
    public static IReadOnlyList<SourceEdit>? AddScreen(ScreenDocument document, string id)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(id);
        if (document.Shape is not { NewConstant: { } constant, NewDrawCase: { } drawCase, Receiver.Length: > 0 } shape)
        {
            return null;
        }

        string constantText = string.Format(CultureInfo.InvariantCulture, shape.ConstantFormat, id);
        string draw = Drawing(document.Display, shape.Receiver, ElementKind.Text, 0, document.Display.TextFromBaseline ? 8 : 0, 0, 0, id)[0];
        string caseText = drawCase.Inline
            ? $"case {id}: {draw} break; "
            : $"{drawCase.Indent}case {id}:\n{drawCase.Indent}  {draw}\n{drawCase.Indent}  break;\n";
        return [Put(constant with { Indent = "" }, constant.Inline ? constantText : constant.Indent + constantText), Put(drawCase, caseText)];
    }

    /// <summary>How a key is written: the sketch's name for it, or the character.</summary>
    public static string KeyText(ScreenCodeShape shape, string key)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(key);
        return shape.KeyNames.TryGetValue(key, out string? name) ? name : "'" + Escape(key, '\'') + "'";
    }

    /// <summary>A new transition, at the start of what handles keys, before anything else it does with a key.</summary>
    /// <param name="document">What was read.</param>
    /// <param name="from">The screen it leaves, or null for any.</param>
    /// <param name="key">The key.</param>
    /// <param name="target">The screen it opens.</param>
    public static IReadOnlyList<SourceEdit>? AddTransition(ScreenDocument document, string? from, string key, string target)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Shape is not { NewTransition: { } place, KeyVariable.Length: > 0, GotoFormat.Length: > 0 } shape)
        {
            return null;
        }

        string go = string.Format(CultureInfo.InvariantCulture, shape.GotoFormat.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal).Replace("{{0}}", "{0}", StringComparison.Ordinal), target);
        string condition = (from is null ? "" : $"{shape.StateExpression} == {from} && ") + $"{shape.KeyVariable} == {KeyText(shape, key)}";
        string statement = shape.CanReturn ? $"if ({condition}) {{ {go} return; }}" : $"if ({condition}) {{ {go} }}";
        return [Put(place, Lines(place, statement))];
    }

    /// <summary>A screen's constant, everywhere it is written.</summary>
    public static IReadOnlyList<SourceEdit> Rename(IReadOnlyList<SourceSpan> uses, string name)
    {
        ArgumentNullException.ThrowIfNull(uses);
        ArgumentNullException.ThrowIfNull(name);
        return [.. uses.Select(s => new SourceEdit(s, name))];
    }

    /// <summary>A text element's new words, in a literal of the same kind where it can be.</summary>
    public static SourceEdit? SetText(SourceSpan? span, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (span is null)
        {
            return null;
        }

        // lcd.print('x') stays a character while it can; "ab" needs a string either way.
        bool wasChar = span.Expected.StartsWith('\'');
        return new SourceEdit(span, wasChar && text.Length == 1 ? "'" + Escape(text, '\'') + "'" : "\"" + Escape(text, '"') + "\"");
    }

    public static SourceEdit? SetNumber(SourceSpan? span, int value) =>
        span is null ? null : new SourceEdit(span, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// What puts an element somewhere else: a number where the code has one, or a name or expression that only
    /// places it, with the declarations nothing else then uses. What has no such place of its own gets a cursor
    /// of its own. Null if nothing can move it there.
    /// </summary>
    public static IReadOnlyList<SourceEdit>? Move(ScreenElement element, ScreenDisplay display, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(display);
        var edits = new List<SourceEdit>();
        void Replace(SourceSpan span, CodeExpression? code, int value)
        {
            edits.Add(new SourceEdit(span, value.ToString(CultureInfo.InvariantCulture)));
            edits.AddRange((code?.Dead ?? []).Select(dead => new SourceEdit(dead, "")));
        }

        bool across = x != element.X;
        bool down = y != element.Y;
        if (across && element.XSpan is { } columnSpan)
        {
            Replace(columnSpan, element.XCode, x);
        }

        if (down && element.YSpan is { } rowSpan)
        {
            Replace(rowSpan, element.YCode, y);
        }

        if (((across && element.XSpan is null) || (down && element.YSpan is null)) && element.CursorInsert is { } place)
        {
            string move = display.Library == DisplayLibrary.GyverOled ? "setCursorXY" : "setCursor";
            edits.Add(Put(place, Lines(place, string.Create(CultureInfo.InvariantCulture, $"{element.CursorReceiver}{move}({x}, {y});"))));
        }

        return edits.Count == 0 ? null : [.. edits.Distinct()];
    }

    /// <summary>A number or an expression typed over what the code wrote. A number leaves the declarations it used with nothing to do.</summary>
    public static IReadOnlyList<SourceEdit>? SetExpression(CodeExpression code, string text)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(text);
        text = text.Trim();
        if (code.Span is not { } span || text.Length == 0 || text == code.Text)
        {
            return null;
        }

        var edits = new List<SourceEdit> { new(span, text) };
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            edits.AddRange(code.Dead.Select(dead => new SourceEdit(dead, "")));
        }

        return edits;
    }

    /// <summary>A transition's new target: the constant it names, replaced by another.</summary>
    public static SourceEdit? SetTarget(ScreenTransition transition, string target)
    {
        ArgumentNullException.ThrowIfNull(transition);
        return transition.TargetSpan is { } span ? new SourceEdit(span, target) : null;
    }

    /// <summary>C++ escapes for a literal's content.</summary>
    public static string Escape(string text, char quote)
    {
        ArgumentNullException.ThrowIfNull(text);
        var escaped = new StringBuilder(text.Length + 2);
        foreach (char c in text)
        {
            switch (c)
            {
                case '\\':
                    escaped.Append("\\\\");
                    break;
                case '\n':
                    escaped.Append("\\n");
                    break;
                case '\t':
                    escaped.Append("\\t");
                    break;
                case '\r':
                    escaped.Append("\\r");
                    break;
                case '\0':
                    escaped.Append("\\0");
                    break;
                default:
                    if (c == quote)
                    {
                        escaped.Append('\\');
                    }

                    escaped.Append(c);
                    break;
            }
        }

        return escaped.ToString();
    }
}
