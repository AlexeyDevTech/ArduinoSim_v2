namespace ArduSim.Build.Sketches;

internal enum CppTokenKind
{
    Identifier,
    Number,
    Literal,
    Punctuation,
    Directive,
}

/// <param name="Start">Offset of the first character.</param>
/// <param name="End">Offset one past the last character.</param>
/// <param name="Line">1-based line of the first character.</param>
internal readonly record struct CppToken(CppTokenKind Kind, string Text, int Start, int End, int Line);

/// <summary>
/// Just enough of a C++ lexer to find top-level function definitions: skips comments, keeps string,
/// character and raw-string literals whole, and returns each preprocessor directive as one token.
/// </summary>
internal static class CppTokenizer
{
    private static readonly HashSet<string> RawStringPrefixes = new(StringComparer.Ordinal) { "R", "LR", "uR", "UR", "u8R" };
    private static readonly HashSet<string> LiteralPrefixes = new(StringComparer.Ordinal) { "L", "u", "U", "u8" };

    public static List<CppToken> Tokenize(string text)
    {
        var tokens = new List<CppToken>();
        int i = 0;
        int line = 1;
        bool lineStart = true;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\n')
            {
                line++;
                i++;
                lineStart = true;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '/' && At(text, i + 1) == '/')
            {
                i = SkipLineComment(text, i, ref line);
                continue;
            }

            if (c == '/' && At(text, i + 1) == '*')
            {
                i = SkipBlockComment(text, i, ref line);
                continue;
            }

            int start = i;
            int startLine = line;
            if (c == '#' && lineStart)
            {
                i = SkipDirective(text, i, ref line);
                tokens.Add(new CppToken(CppTokenKind.Directive, text[start..i], start, i, startLine));
                continue;
            }

            lineStart = false;
            if (IsIdentifierStart(c))
            {
                while (i < text.Length && IsIdentifierPart(text[i]))
                {
                    i++;
                }

                string word = text[start..i];
                if (At(text, i) == '"' && RawStringPrefixes.Contains(word))
                {
                    i = SkipRawString(text, i, ref line);
                    tokens.Add(new CppToken(CppTokenKind.Literal, text[start..i], start, i, startLine));
                }
                else if ((At(text, i) == '"' || At(text, i) == '\'') && LiteralPrefixes.Contains(word))
                {
                    i = SkipQuoted(text, i, ref line);
                    tokens.Add(new CppToken(CppTokenKind.Literal, text[start..i], start, i, startLine));
                }
                else
                {
                    tokens.Add(new CppToken(CppTokenKind.Identifier, word, start, i, startLine));
                }

                continue;
            }

            if (char.IsAsciiDigit(c) || (c == '.' && char.IsAsciiDigit(At(text, i + 1))))
            {
                i = SkipNumber(text, i);
                tokens.Add(new CppToken(CppTokenKind.Number, text[start..i], start, i, startLine));
                continue;
            }

            if (c is '"' or '\'')
            {
                i = SkipQuoted(text, i, ref line);
                tokens.Add(new CppToken(CppTokenKind.Literal, text[start..i], start, i, startLine));
                continue;
            }

            i += c == ':' && At(text, i + 1) == ':' ? 2 : 1;
            tokens.Add(new CppToken(CppTokenKind.Punctuation, text[start..i], start, i, startLine));
        }

        return tokens;
    }

    private static char At(string text, int index) => index < text.Length ? text[index] : '\0';

    private static bool IsIdentifierStart(char c) => char.IsAsciiLetter(c) || c == '_' || c == '$' || c > 127;

    private static bool IsIdentifierPart(char c) => IsIdentifierStart(c) || char.IsAsciiDigit(c);

    private static int SkipLineComment(string text, int i, ref int line)
    {
        while (i < text.Length && text[i] != '\n')
        {
            if (text[i] == '\\' && At(text, i + 1) == '\n')
            {
                line++;
                i++;
            }

            i++;
        }

        return i;
    }

    private static int SkipBlockComment(string text, int i, ref int line)
    {
        i += 2;
        while (i < text.Length && !(text[i] == '*' && At(text, i + 1) == '/'))
        {
            if (text[i] == '\n')
            {
                line++;
            }

            i++;
        }

        return Math.Min(i + 2, text.Length);
    }

    private static int SkipDirective(string text, int i, ref int line)
    {
        while (i < text.Length && text[i] != '\n')
        {
            if (text[i] == '\\' && At(text, i + 1) == '\n')
            {
                line++;
                i += 2;
                continue;
            }

            if (text[i] == '/' && At(text, i + 1) == '*')
            {
                i = SkipBlockComment(text, i, ref line);
                continue;
            }

            if (text[i] == '/' && At(text, i + 1) == '/')
            {
                return SkipLineComment(text, i, ref line);
            }

            i++;
        }

        return i;
    }

    private static int SkipQuoted(string text, int i, ref int line)
    {
        char quote = text[i++];
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\\')
            {
                if (At(text, i + 1) == '\n')
                {
                    line++;
                }

                i += 2;
                continue;
            }

            if (c == quote)
            {
                return i + 1;
            }

            if (c == '\n')
            {
                return i; // unterminated literal: stop at the line end, the compiler will report it
            }

            i++;
        }

        return Math.Min(i, text.Length);
    }

    private static int SkipRawString(string text, int i, ref int line)
    {
        int open = text.IndexOf('(', i);
        if (open < 0)
        {
            return SkipQuoted(text, i, ref line);
        }

        string terminator = ")" + text[(i + 1)..open] + "\"";
        int close = text.IndexOf(terminator, open, StringComparison.Ordinal);
        int end = close < 0 ? text.Length : close + terminator.Length;
        for (int k = i; k < end; k++)
        {
            if (text[k] == '\n')
            {
                line++;
            }
        }

        return end;
    }

    private static int SkipNumber(string text, int i)
    {
        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsAsciiLetterOrDigit(c) || c == '.' || c == '_')
            {
                bool exponent = (c is 'e' or 'E' or 'p' or 'P') && (At(text, i + 1) is '+' or '-');
                i += exponent ? 2 : 1;
            }
            else if (c == '\'' && char.IsAsciiLetterOrDigit(At(text, i + 1)))
            {
                i++;
            }
            else
            {
                break;
            }
        }

        return i;
    }
}
