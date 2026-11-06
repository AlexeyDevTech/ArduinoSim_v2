using System.Globalization;
using System.Text;

namespace ArduSim.Core.Screens;

internal enum TokenKind
{
    Identifier,
    Number,
    String,
    Char,
    Punct,
}

/// <param name="Offset">Characters from the start of its file.</param>
internal readonly record struct CodeToken(TokenKind Kind, string Text, int Offset, int File)
{
    public bool Is(string text) => Kind != TokenKind.String && Kind != TokenKind.Char && Text == text;
}

/// <summary>A parameter of a function; its type's text matters only for spotting a display.</summary>
internal sealed record CodeParameter(string Type, string Name);

/// <param name="BodyStart">The token after the opening brace.</param>
/// <param name="BodyEnd">The closing brace's token.</param>
internal sealed record CodeFunction(string Name, string Class, IReadOnlyList<CodeParameter> Parameters, IReadOnlyList<CodeToken> Tokens, int BodyStart, int BodyEnd, int NameIndex)
{
    public string FullName => Class.Length > 0 ? Class + "::" + Name : Name;
}

internal sealed record CodeEnum(string Name, IReadOnlyList<CodeToken> Constants);

internal sealed record CodeStringArray(string Name, IReadOnlyList<CodeToken> Items);

/// <summary>A display object: <c>LiquidCrystal_I2C lcd(0x27, 20, 4);</c>.</summary>
internal sealed record CodeDisplay(string Name, string Type, IReadOnlyList<string> Arguments);

/// <summary>A file of a sketch as tokens.</summary>
internal sealed class CodeFile
{
    private readonly List<int> _lineStarts = [0];

    public CodeFile(string path, string text, int index)
    {
        Path = path;
        Text = text;
        Index = index;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                _lineStarts.Add(i + 1);
            }
        }
    }

    public string Path { get; }

    public string Text { get; }

    public int Index { get; }

    public List<CodeToken> Tokens { get; } = [];

    public int LineOf(int offset)
    {
        int found = _lineStarts.BinarySearch(offset);
        return (found >= 0 ? found : ~found - 1) + 1;
    }
}

/// <summary>
/// A tolerant reading of Arduino C++ (ADR-019): tokens, enums, functions with their class and
/// parameters, arrays of strings, display objects and simple <c>#define</c> values. It does not need
/// the code to compile or the libraries to be present, and it ignores what it does not understand.
/// </summary>
internal sealed class SketchScanner
{
    private static readonly string[] TwoCharPunct = ["::", "->", "==", "!=", "<=", ">=", "&&", "||", "++", "--", "+=", "-=", "<<", ">>"];
    private static readonly string[] Keywords = ["if", "for", "while", "switch", "return", "sizeof", "catch", "else", "do", "case", "new", "delete"];
    private static readonly string[] AfterParameters = ["const", "override", "final", "noexcept"];

    public List<CodeFile> Files { get; } = [];

    public Dictionary<string, string> Macros { get; } = new(StringComparer.Ordinal);

    public List<CodeFunction> Functions { get; } = [];

    public List<CodeEnum> Enums { get; } = [];

    public Dictionary<string, CodeStringArray> StringArrays { get; } = new(StringComparer.Ordinal);

    public List<CodeDisplay> Displays { get; } = [];

    /// <summary>The class, if any, a display type name belongs to.</summary>
    public static bool IsDisplayType(string type) =>
        type is "LiquidCrystal" or "LiquidCrystal_I2C" or "Adafruit_SSD1306" or "Adafruit_SH1106G" or "Adafruit_SH1107" or "Adafruit_SH110X" or "GyverOLED" or "U8G2"
        || type.StartsWith("U8G2_", StringComparison.Ordinal);

    public void Add(string path, string text)
    {
        var file = new CodeFile(path, text, Files.Count);
        Files.Add(file);
        Tokenize(file);
        Structure(file);
    }

    public static string Decode(CodeToken literal)
    {
        string body = literal.Text.Length >= 2 ? literal.Text[1..^1] : "";
        var text = new StringBuilder(body.Length);
        for (int i = 0; i < body.Length; i++)
        {
            if (body[i] != '\\' || i + 1 >= body.Length)
            {
                text.Append(body[i]);
                continue;
            }

            char next = body[++i];
            switch (next)
            {
                case 'n':
                    text.Append('\n');
                    break;
                case 't':
                    text.Append('\t');
                    break;
                case 'r':
                    text.Append('\r');
                    break;
                case '0':
                    text.Append('\0');
                    break;
                case 'x' when i + 2 < body.Length && int.TryParse(body.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code):
                    text.Append((char)code);
                    i += 2;
                    break;
                default:
                    text.Append(next);
                    break;
            }
        }

        return text.ToString();
    }

    private void Tokenize(CodeFile file)
    {
        string s = file.Text;
        int i = 0;
        bool lineStart = true;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '\n')
            {
                lineStart = true;
                i++;
            }
            else if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n')
                {
                    i++;
                }
            }
            else if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? s.Length : end + 2;
            }
            else if (c == '#' && lineStart)
            {
                i = Directive(s, i);
            }
            else
            {
                lineStart = false;
                i = NextToken(file, s, i);
            }
        }
    }

    // A directive to its line's end, continuations included; a simple #define's value is kept.
    private int Directive(string s, int start)
    {
        int i = start;
        while (i < s.Length && s[i] != '\n')
        {
            i += s[i] == '\\' && i + 1 < s.Length ? 2 : 1;
        }

        string line = s[(start + 1)..i].Trim();
        if (line.StartsWith("define", StringComparison.Ordinal))
        {
            string rest = line["define".Length..].TrimStart();
            int nameEnd = 0;
            while (nameEnd < rest.Length && (char.IsLetterOrDigit(rest[nameEnd]) || rest[nameEnd] == '_'))
            {
                nameEnd++;
            }

            if (nameEnd > 0 && nameEnd < rest.Length && rest[nameEnd] != '(')
            {
                string value = rest[nameEnd..];
                int comment = value.IndexOf("//", StringComparison.Ordinal);
                Macros[rest[..nameEnd]] = (comment >= 0 ? value[..comment] : value).Trim();
            }
        }

        return i;
    }

    private static int NextToken(CodeFile file, string s, int i)
    {
        char c = s[i];
        if (char.IsLetter(c) || c == '_')
        {
            int end = i + 1;
            while (end < s.Length && (char.IsLetterOrDigit(s[end]) || s[end] == '_'))
            {
                end++;
            }

            // A raw string: R"( ... )", read whole so that nothing inside it counts as code.
            if (end < s.Length && s[end] == '"' && s[i..end] == "R")
            {
                int close = s.IndexOf(")\"", end, StringComparison.Ordinal);
                end = close < 0 ? s.Length : close + 2;
                file.Tokens.Add(new CodeToken(TokenKind.Punct, "?", i, file.Index));
                return end;
            }

            file.Tokens.Add(new CodeToken(TokenKind.Identifier, s[i..end], i, file.Index));
            return end;
        }

        if (char.IsDigit(c))
        {
            int end = i + 1;
            while (end < s.Length && (char.IsLetterOrDigit(s[end]) || s[end] == '.'))
            {
                end++;
            }

            file.Tokens.Add(new CodeToken(TokenKind.Number, s[i..end], i, file.Index));
            return end;
        }

        if (c is '"' or '\'')
        {
            int end = i + 1;
            while (end < s.Length && s[end] != c && s[end] != '\n')
            {
                end += s[end] == '\\' ? 2 : 1;
            }

            end = Math.Min(end + 1, s.Length);
            file.Tokens.Add(new CodeToken(c == '"' ? TokenKind.String : TokenKind.Char, s[i..end], i, file.Index));
            return end;
        }

        if (i + 1 < s.Length && TwoCharPunct.Contains(s.Substring(i, 2)))
        {
            file.Tokens.Add(new CodeToken(TokenKind.Punct, s.Substring(i, 2), i, file.Index));
            return i + 2;
        }

        file.Tokens.Add(new CodeToken(TokenKind.Punct, c.ToString(), i, file.Index));
        return i + 1;
    }

    // Declarations outside function bodies: classes are walked into, function bodies skipped over.
    private void Structure(CodeFile file)
    {
        List<CodeToken> t = file.Tokens;
        var classes = new Stack<(string Name, int Depth)>();
        int depth = 0;
        int i = 0;
        while (i < t.Count)
        {
            CodeToken token = t[i];
            if (token.Is("enum"))
            {
                i = ReadEnum(t, i);
            }
            else if ((token.Is("class") || token.Is("struct") || token.Is("namespace")) && OpensScope(t, i, out int open))
            {
                string name = i + 1 < t.Count && t[i + 1].Kind == TokenKind.Identifier ? t[i + 1].Text : "";
                classes.Push((token.Is("namespace") ? "" : name, ++depth));
                i = open + 1;
            }
            else if (token.Is("{"))
            {
                i = Matching(t, i, "{", "}") + 1;
            }
            else if (token.Is("}"))
            {
                if (classes.Count > 0 && classes.Peek().Depth == depth)
                {
                    classes.Pop();
                }

                depth = Math.Max(0, depth - 1);
                i++;
            }
            else if (token.Kind == TokenKind.Identifier && i + 1 < t.Count && t[i + 1].Is("(") && !Keywords.Contains(token.Text))
            {
                i = ReadCallLike(t, i, classes.Count > 0 ? classes.Peek().Name : "");
            }
            else if (token.Kind == TokenKind.Identifier && i + 1 < t.Count && t[i + 1].Is("["))
            {
                i = ReadArray(t, i);
            }
            else
            {
                if (token.Kind == TokenKind.Identifier && IsDisplayType(token.Text))
                {
                    NoteDisplay(t, i);
                }

                i++;
            }
        }
    }

    private static bool OpensScope(List<CodeToken> t, int i, out int open)
    {
        // class Name : public Base {   but not   class Name;   or   struct Name value = ...
        for (int k = i + 1; k < t.Count && k < i + 16; k++)
        {
            if (t[k].Is("{"))
            {
                open = k;
                return true;
            }

            if (t[k].Is(";") || t[k].Is("(") || t[k].Is("="))
            {
                break;
            }
        }

        open = -1;
        return false;
    }

    private int ReadEnum(List<CodeToken> t, int i)
    {
        int k = i + 1;
        if (k < t.Count && (t[k].Is("class") || t[k].Is("struct")))
        {
            k++;
        }

        string name = k < t.Count && t[k].Kind == TokenKind.Identifier ? t[k].Text : "";
        while (k < t.Count && !t[k].Is("{") && !t[k].Is(";"))
        {
            k++;
        }

        if (k >= t.Count || t[k].Is(";"))
        {
            return k + 1;
        }

        int end = Matching(t, k, "{", "}");
        var constants = new List<CodeToken>();
        bool expectName = true;
        int nested = 0;
        for (int m = k + 1; m < end; m++)
        {
            if (t[m].Is("("))
            {
                nested++;
            }
            else if (t[m].Is(")"))
            {
                nested--;
            }
            else if (t[m].Is(",") && nested == 0)
            {
                expectName = true;
            }
            else if (expectName && t[m].Kind == TokenKind.Identifier)
            {
                constants.Add(t[m]);
                expectName = false;
            }
        }

        // After the closing brace the enum may name its type: typedef enum { ... } Screen;
        if (name.Length == 0 && end + 1 < t.Count && t[end + 1].Kind == TokenKind.Identifier)
        {
            name = t[end + 1].Text;
        }

        Enums.Add(new CodeEnum(name, constants));
        return end + 1;
    }

    // name(...) is a function definition if a body follows, else a global's constructor call or a prototype.
    private int ReadCallLike(List<CodeToken> t, int i, string enclosingClass)
    {
        int close = Matching(t, i + 1, "(", ")");
        if (close >= t.Count)
        {
            return i + 1;
        }

        int k = close + 1;
        while (k < t.Count && t[k].Kind == TokenKind.Identifier && AfterParameters.Contains(t[k].Text))
        {
            k++;
        }

        // A constructor's initializer list: `: a(1), b(2) {`.
        if (k < t.Count && t[k].Is(":"))
        {
            k++;
            while (k < t.Count && !t[k].Is("{") && !t[k].Is(";"))
            {
                k = t[k].Is("(") ? Matching(t, k, "(", ")") + 1 : k + 1;
            }
        }

        if (k < t.Count && t[k].Is("{"))
        {
            string owner = i >= 2 && t[i - 1].Is("::") && t[i - 2].Kind == TokenKind.Identifier ? t[i - 2].Text : enclosingClass;
            int end = Matching(t, k, "{", "}");
            Functions.Add(new CodeFunction(t[i].Text, owner, ReadParameters(t, i + 2, close), t, k + 1, end, i));
            return end + 1;
        }

        if (i >= 1 && t[i - 1].Kind == TokenKind.Identifier && IsDisplayType(t[i - 1].Text))
        {
            Displays.Add(new CodeDisplay(t[i].Text, t[i - 1].Text, Arguments(t, i + 2, close)));
        }
        else if (i >= 1 && t[i - 1].Is(">") && TemplateTypeBefore(t, i - 1) is { } templated && IsDisplayType(templated))
        {
            Displays.Add(new CodeDisplay(t[i].Text, templated, Arguments(t, i + 2, close)));
        }

        return close + 1;
    }

    private static string? TemplateTypeBefore(List<CodeToken> t, int close)
    {
        for (int k = close - 1, nested = 1; k >= 1 && close - k < 32; k--)
        {
            nested += t[k].Is(">") ? 1 : t[k].Is("<") ? -1 : 0;
            if (nested == 0)
            {
                return t[k - 1].Kind == TokenKind.Identifier ? t[k - 1].Text : null;
            }
        }

        return null;
    }

    private void NoteDisplay(List<CodeToken> t, int i)
    {
        // Type name;   Type<...> name;   a member, or a global built with its defaults.
        int k = i + 1;
        if (k < t.Count && t[k].Is("<"))
        {
            for (int nested = 1; ++k < t.Count && nested > 0;)
            {
                nested += t[k].Is("<") ? 1 : t[k].Is(">") ? -1 : 0;
            }
        }

        if (k + 1 < t.Count && t[k].Kind == TokenKind.Identifier && (t[k + 1].Is(";") || t[k + 1].Is("=")))
        {
            Displays.Add(new CodeDisplay(t[k].Text, t[i].Text, []));
        }
    }

    private static List<string> Arguments(List<CodeToken> t, int start, int end)
    {
        var arguments = new List<string>();
        var current = new StringBuilder();
        int nested = 0;
        for (int k = start; k < end; k++)
        {
            nested += t[k].Is("(") ? 1 : t[k].Is(")") ? -1 : 0;
            if (t[k].Is(",") && nested == 0)
            {
                arguments.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(t[k].Text);
            }
        }

        if (current.Length > 0)
        {
            arguments.Add(current.ToString());
        }

        return arguments;
    }

    private static List<CodeParameter> ReadParameters(List<CodeToken> t, int start, int end)
    {
        var parameters = new List<CodeParameter>();
        int from = start;
        int nested = 0;
        for (int k = start; k <= end; k++)
        {
            if (k < end && (t[k].Is("<") || t[k].Is("(")))
            {
                nested++;
            }
            else if (k < end && (t[k].Is(">") || t[k].Is(")")))
            {
                nested--;
            }
            else if (k == end || (t[k].Is(",") && nested == 0))
            {
                string name = "";
                var type = new StringBuilder();
                for (int m = from; m < k; m++)
                {
                    if (t[m].Is("="))
                    {
                        break;
                    }

                    if (t[m].Kind == TokenKind.Identifier)
                    {
                        if (name.Length > 0)
                        {
                            type.Append(name).Append(' ');
                        }

                        name = t[m].Text;
                    }
                }

                if (name.Length > 0 && !(type.Length == 0 && name == "void"))
                {
                    parameters.Add(new CodeParameter(type.ToString().Trim(), name));
                }

                from = k + 1;
            }
        }

        return parameters;
    }

    // const char* const NAME[] PROGMEM = { "a", "b" };
    private int ReadArray(List<CodeToken> t, int i)
    {
        int k = i + 1;
        while (k < t.Count && t[k].Is("["))
        {
            k = Matching(t, k, "[", "]") + 1;
        }

        while (k < t.Count && t[k].Kind == TokenKind.Identifier)
        {
            k++;
        }

        if (k + 1 >= t.Count || !t[k].Is("=") || !t[k + 1].Is("{"))
        {
            return i + 1;
        }

        int end = Matching(t, k + 1, "{", "}");
        var items = new List<CodeToken>();
        for (int m = k + 2; m < end; m++)
        {
            if (t[m].Kind == TokenKind.String)
            {
                items.Add(t[m]);
            }
        }

        if (items.Count > 0)
        {
            StringArrays[t[i].Text] = new CodeStringArray(t[i].Text, items);
        }

        return end + 1;
    }

    /// <summary>The index of the token closing the one at <paramref name="open"/>, or the end of the list.</summary>
    public static int Matching(IReadOnlyList<CodeToken> t, int open, string opening, string closing)
    {
        int nested = 0;
        for (int k = open; k < t.Count; k++)
        {
            if (t[k].Is(opening))
            {
                nested++;
            }
            else if (t[k].Is(closing) && --nested == 0)
            {
                return k;
            }
        }

        return t.Count;
    }
}
