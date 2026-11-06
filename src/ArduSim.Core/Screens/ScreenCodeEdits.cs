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

/// <summary>Edits to code that was read (ADR-019): always one token, always what it was read as.</summary>
public static class ScreenCodeEdits
{
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
