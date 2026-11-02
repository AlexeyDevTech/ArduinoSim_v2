using System.Globalization;
using System.Text;

namespace ArduSim.Build.Sketches;

/// <summary>A prototype the preprocessor generated, with the location of the definition it declares.</summary>
public sealed record GeneratedPrototype(string Declaration, string File, int Line);

public sealed record PreprocessedSketch(string Code, IReadOnlyList<GeneratedPrototype> Prototypes);

/// <summary>
/// Turns the .ino files of a sketch into one C++ translation unit, following arduino-cli:
/// concatenate (main file first), add <c>#include &lt;Arduino.h&gt;</c>, declare every top-level
/// function before the first function definition, and keep <c>#line</c> directives so diagnostics
/// point at the original files.
/// </summary>
/// <remarks>
/// Not generated, so the sketch must order or declare them itself: functions inside
/// <c>#if</c> blocks, templates, operators, member functions, and names the sketch already declares.
/// Default arguments move into the generated prototype and are blanked out of the definition.
/// </remarks>
public static class SketchPreprocessor
{
    private const string Header = "#include <Arduino.h>\n";

    private static readonly HashSet<string> NotFunctionNames = new(StringComparer.Ordinal)
    {
        "if", "for", "while", "switch", "return", "sizeof", "alignof", "alignas", "decltype", "catch",
        "defined", "static_assert", "noexcept", "new", "delete", "throw", "typeid", "__attribute__", "__declspec",
    };

    private static readonly HashSet<string> NotInReturnType = new(StringComparer.Ordinal)
    {
        "=", "namespace", "typedef", "using", "template", "operator", "return", "friend", "else", "do",
    };

    public static PreprocessedSketch Preprocess(Sketch sketch)
    {
        var files = sketch.InoFiles.Select(f => (f, File.ReadAllText(f))).ToList();
        return Preprocess(files);
    }

    internal static PreprocessedSketch Preprocess(IReadOnlyList<(string Path, string Text)> files)
    {
        var merged = new StringBuilder();
        var segments = new List<(int FirstLine, string Path)>();
        int line = 1;
        foreach ((string path, string raw) in files)
        {
            string text = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            if (!text.EndsWith('\n'))
            {
                text += "\n";
            }

            merged.Append(LineDirective(1, path));
            line++;
            segments.Add((line, path));
            merged.Append(text);
            line += text.Count(c => c == '\n');
        }

        string code = merged.ToString();
        List<FunctionDefinition> definitions = FindFunctions(CppTokenizer.Tokenize(code), out HashSet<string> declared);
        List<FunctionDefinition> generated = definitions.Where(d => !declared.Contains(d.Name)).ToList();
        if (generated.Count == 0)
        {
            return new PreprocessedSketch(Header + code, []);
        }

        char[] chars = code.ToCharArray();
        foreach ((int start, int end) in generated.SelectMany(d => d.DefaultArguments))
        {
            for (int k = start; k < end; k++)
            {
                if (chars[k] != '\n')
                {
                    chars[k] = ' ';
                }
            }
        }

        string blanked = new(chars);
        var prototypes = generated
            .Select(d => (Location: Map(segments, d.Line), d.Declaration))
            .Select(p => new GeneratedPrototype(p.Declaration, p.Location.Path, p.Location.Line))
            .ToList();

        FunctionDefinition first = definitions[0];
        (string firstPath, int firstLine) = Map(segments, first.Line);
        int column = first.Start - (blanked.LastIndexOf('\n', Math.Max(first.Start - 1, 0)) + 1);
        var block = new StringBuilder("\n");
        foreach (GeneratedPrototype prototype in prototypes)
        {
            block.Append(LineDirective(prototype.Line, prototype.File)).Append(prototype.Declaration).Append(";\n");
        }

        block.Append(LineDirective(firstLine, firstPath)).Append(' ', column);
        string result = Header + blanked[..first.Start] + block + blanked[first.Start..];
        return new PreprocessedSketch(result, prototypes);
    }

    internal static string LineDirective(int line, string path) =>
        string.Create(CultureInfo.InvariantCulture, $"#line {line} \"{path.Replace('\\', '/')}\"\n");

    private static (string Path, int Line) Map(List<(int FirstLine, string Path)> segments, int mergedLine)
    {
        (int firstLine, string path) = segments.Last(s => s.FirstLine <= mergedLine);
        return (path, mergedLine - firstLine + 1);
    }

    private sealed record FunctionDefinition(string Name, string Declaration, int Start, int Line, IReadOnlyList<(int Start, int End)> DefaultArguments);

    private static List<FunctionDefinition> FindFunctions(List<CppToken> tokens, out HashSet<string> declared)
    {
        var definitions = new List<FunctionDefinition>();
        declared = new HashSet<string>(StringComparer.Ordinal);
        int braces = 0;
        int parens = 0;
        int conditionals = 0;
        int headStart = 0;
        bool headInConditional = false;
        for (int i = 0; i < tokens.Count; i++)
        {
            CppToken token = tokens[i];
            if (token.Kind == CppTokenKind.Directive)
            {
                conditionals += ConditionalDelta(token.Text);
                if (braces == 0)
                {
                    headStart = i + 1;
                    headInConditional = conditionals > 0;
                }

                continue;
            }

            if (braces > 0)
            {
                if (token.Text == "{")
                {
                    braces++;
                }
                else if (token.Text == "}" && --braces == 0)
                {
                    headStart = i + 1;
                    headInConditional = conditionals > 0;
                }

                continue;
            }

            switch (token.Text)
            {
                case "(":
                    parens++;
                    break;
                case ")":
                    parens = Math.Max(parens - 1, 0);
                    break;
                case ";" when parens == 0:
                    if (TryParseHead(tokens, headStart, i, out FunctionHead declaration))
                    {
                        declared.Add(tokens[declaration.Name].Text);
                    }

                    headStart = i + 1;
                    headInConditional = conditionals > 0;
                    break;
                case "{" when parens == 0:
                    if (!headInConditional && conditionals == 0 && TryParseHead(tokens, headStart, i, out FunctionHead head))
                    {
                        definitions.Add(new FunctionDefinition(
                            tokens[head.Name].Text,
                            Join(tokens, headStart, head.End),
                            tokens[headStart].Start,
                            tokens[headStart].Line,
                            DefaultArguments(tokens, head.Open, head.Close)));
                    }

                    braces = 1;
                    break;
            }
        }

        return definitions;
    }

    private static int ConditionalDelta(string directive)
    {
        string name = new(directive.TrimStart('#', ' ', '\t').TakeWhile(char.IsAsciiLetter).ToArray());
        return name switch
        {
            "if" or "ifdef" or "ifndef" => 1,
            "endif" => -1,
            _ => 0,
        };
    }

    /// <param name="Name">Index of the function name token.</param>
    /// <param name="Open">Index of the parameter list's opening parenthesis.</param>
    /// <param name="Close">Index of its closing parenthesis.</param>
    /// <param name="End">Index of the last token of the head (inclusive).</param>
    private readonly record struct FunctionHead(int Name, int Open, int Close, int End);

    private static bool TryParseHead(List<CppToken> tokens, int start, int end, out FunctionHead head)
    {
        head = default;
        int last = end - 1;
        while (last > start && tokens[last].Text == "noexcept")
        {
            last--;
        }

        if (last - start < 3 || tokens[last].Text != ")")
        {
            return false;
        }

        int depth = 0;
        int open = -1;
        for (int k = last; k >= start; k--)
        {
            depth += tokens[k].Text switch { ")" => 1, "(" => -1, _ => 0 };
            if (depth == 0)
            {
                open = k;
                break;
            }
        }

        int name = open - 1;
        if (open < 0 || name <= start || tokens[name].Kind != CppTokenKind.Identifier ||
            NotFunctionNames.Contains(tokens[name].Text) || tokens[name - 1].Text == "::")
        {
            return false;
        }

        bool hasType = false;
        for (int k = start; k < name; k++)
        {
            CppToken t = tokens[k];
            if (NotInReturnType.Contains(t.Text))
            {
                return false;
            }

            if (t.Text is "__attribute__" or "__declspec" or "alignas" && k + 1 < name && tokens[k + 1].Text == "(")
            {
                k = SkipBalanced(tokens, k + 1);
                continue;
            }

            if (t.Text is "(" or ")" or "{" or "}" or "[" or "]")
            {
                return false;
            }

            hasType |= t.Kind == CppTokenKind.Identifier;
        }

        head = new FunctionHead(name, open, last, end - 1);
        return hasType;
    }

    private static int SkipBalanced(List<CppToken> tokens, int open)
    {
        int depth = 0;
        for (int k = open; k < tokens.Count; k++)
        {
            depth += tokens[k].Text switch { "(" => 1, ")" => -1, _ => 0 };
            if (depth == 0)
            {
                return k;
            }
        }

        return tokens.Count - 1;
    }

    private static List<(int Start, int End)> DefaultArguments(List<CppToken> tokens, int open, int close)
    {
        var ranges = new List<(int, int)>();
        int depth = 0;
        int defaultStart = -1;
        for (int k = open + 1; k < close; k++)
        {
            string text = tokens[k].Text;
            if (text is "(" or "[" or "{")
            {
                depth++;
            }
            else if (text is ")" or "]" or "}")
            {
                depth--;
            }
            else if (depth == 0 && text == "=" && defaultStart < 0)
            {
                defaultStart = tokens[k].Start;
            }
            else if (depth == 0 && text == "," && defaultStart >= 0)
            {
                ranges.Add((defaultStart, tokens[k].Start));
                defaultStart = -1;
            }
        }

        if (defaultStart >= 0)
        {
            ranges.Add((defaultStart, tokens[close].Start));
        }

        return ranges;
    }

    private static string Join(List<CppToken> tokens, int start, int endInclusive)
    {
        var text = new StringBuilder();
        for (int k = start; k <= endInclusive; k++)
        {
            string t = tokens[k].Text;
            bool tight = text.Length == 0 || t is "(" or ")" or "," || text[^1] == '(';
            if (!tight)
            {
                text.Append(' ');
            }

            text.Append(t);
        }

        return text.ToString();
    }
}
