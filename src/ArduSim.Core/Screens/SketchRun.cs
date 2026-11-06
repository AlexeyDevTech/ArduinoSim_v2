using System.Globalization;
using System.Text;

namespace ArduSim.Core.Screens;

/// <summary>A string as the sketch has it: bytes, and a place in them that can move along.</summary>
/// <param name="Origin">The literal it comes from, if it is one literal from its start.</param>
internal sealed record TextPointer(byte[] Bytes, int Offset, CodeToken? Origin = null)
{
    public static TextPointer Of(string text, CodeToken? origin = null) => new(Encoding.UTF8.GetBytes(text + "\0"), 0, origin);

    private int End
    {
        get
        {
            int end = Array.IndexOf(Bytes, (byte)0, Math.Min(Offset, Bytes.Length));
            return end < 0 ? Bytes.Length : end;
        }
    }

    public long Length => End - Offset;

    public string Text => Offset >= Bytes.Length ? "" : Encoding.UTF8.GetString(Bytes, Offset, End - Offset);

    public long? ByteAt(long index) => Offset + index >= 0 && Offset + index < Bytes.Length ? Bytes[Offset + index] : null;
}

/// <summary>An array of structs in the sketch's text, row by row.</summary>
/// <param name="Rows">Each row's values: the tokens of each, in the order of <paramref name="Fields"/>. Rows inside an <c>#if</c> that is off are not here.</param>
internal sealed record CodeTable(string Name, IReadOnlyList<CodeToken> Tokens, IReadOnlyList<string> Fields, IReadOnlyList<IReadOnlyList<(int Start, int End)>> Rows);

internal readonly record struct TableRow(CodeTable Table, int Index);

/// <summary>
/// Runs a small function of the sketch on what is known, without the sketch and with no effects outside itself
/// (ADR-019): what it returns on the screen being read, for such things as a menu's title and items kept in helpers
/// that switch on the screen. A value is a whole number, a <see cref="TextPointer"/>, a <see cref="TableRow"/> or an
/// array; null means it cannot be told. Only a function's own locals change: what it writes elsewhere is dropped,
/// and what it reads from elsewhere is unknown, apart from constants, the screen, and the place of a list, which
/// counts as its first item. It stops at anything it cannot follow, and after a number of steps.
/// </summary>
internal sealed class SketchRun
{
    private const int StepLimit = 20_000;
    private const int DepthLimit = 16;

    private static readonly string[][] Levels = [["||"], ["&&"], ["|"], ["^"], ["&"], ["==", "!="], ["<", ">", "<=", ">="], ["<<", ">>"], ["+", "-"], ["*", "/", "%"]];
    private static readonly string[] TypeWords = ["const", "unsigned", "signed", "int", "long", "short", "char", "byte", "bool", "void", "size_t"];

    private readonly SketchScanner _scan;
    private readonly IReadOnlyDictionary<string, (IReadOnlyList<CodeToken> Tokens, int Start, int End)> _constants;
    private readonly IReadOnlyDictionary<string, List<CodeFunction>> _functions;
    private readonly IReadOnlySet<string> _listPlaces;
    private Dictionary<string, long>? _enums;
    private Dictionary<string, CodeTable>? _tables;
    private int _steps;
    private int _depth;

    public SketchRun(
        SketchScanner scan,
        IReadOnlyDictionary<string, (IReadOnlyList<CodeToken> Tokens, int Start, int End)> constants,
        IReadOnlyDictionary<string, List<CodeFunction>> functions,
        IReadOnlySet<string> listPlaces)
    {
        _scan = scan;
        _constants = constants;
        _functions = functions;
        _listPlaces = listPlaces;
    }

    /// <summary>The constant of the screen being read.</summary>
    public string? Screen { get; set; }

    /// <summary>The variable that holds the screen.</summary>
    public string? ScreenVariable { get; set; }

    /// <summary>The last function to return did so from a case of its own for this screen alone, so that what it returned is this screen's.</summary>
    public bool ReturnedFromOwnCase { get; private set; }

    /// <summary>Whether a name is a function of the sketch.</summary>
    public bool IsFunction(string name) => _functions.ContainsKey(name);

    /// <summary>What an expression comes to; null if that cannot be told.</summary>
    /// <param name="known">Numbers for names in it: a loop's variable, a parameter.</param>
    public object? Evaluate(IReadOnlyList<CodeToken> t, int start, int end, IReadOnlyDictionary<string, object?>? known = null)
    {
        _steps = 0;
        ReturnedFromOwnCase = false;
        var frame = new Frame(t);
        foreach ((string name, object? value) in known ?? new Dictionary<string, object?>())
        {
            frame.Locals[name] = value;
        }

        return new Expression(this, frame, start, end).Whole();
    }

    /// <summary>What a function of the sketch returns for these numbers, if it is a number.</summary>
    public long? Number(string function, IReadOnlyList<long> arguments)
    {
        _steps = 0;
        return Call(function, [.. arguments.Select(a => (object?)a)]) as long?;
    }

    // ---- names ---------------------------------------------------------------------------------------

    private Dictionary<string, long> Enums
    {
        get
        {
            if (_enums is null)
            {
                _enums = new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (CodeEnum code in _scan.Enums)
                {
                    long next = 0;
                    foreach (CodeToken constant in code.Constants)
                    {
                        if (_constants.TryGetValue(constant.Text, out (IReadOnlyList<CodeToken> Tokens, int Start, int End) given) && Sub(given.Tokens, given.Start, given.End) is long value)
                        {
                            next = value;
                        }

                        _enums[constant.Text] = next++;
                    }
                }
            }

            return _enums;
        }
    }

    private object? Sub(IReadOnlyList<CodeToken> t, int start, int end)
    {
        if (_depth >= DepthLimit)
        {
            return null;
        }

        _depth++;
        object? value = new Expression(this, new Frame(t), start, end).Whole();
        _depth--;
        return value;
    }

    private object? Named(string name, Frame frame)
    {
        if (frame.Locals.TryGetValue(name, out object? local))
        {
            return local;
        }

        switch (name)
        {
            case "true":
                return 1L;
            case "false" or "nullptr" or "NULL":
                return 0L;
            default:
                break;
        }

        if (name == ScreenVariable && Screen is { } screen)
        {
            return Enums.TryGetValue(screen, out long value) ? value : null;
        }

        if (_listPlaces.Contains(name))
        {
            return 0L;
        }

        if (Enums.TryGetValue(name, out long constant))
        {
            return constant;
        }

        // A table's rows can have strings in them, which the scanner lists as an array too: the table comes first.
        if (Tables.TryGetValue(name, out CodeTable? table))
        {
            return table;
        }

        // Written once and read from anywhere: nothing in these is this screen's alone.
        if (_scan.CharArrays.TryGetValue(name, out IReadOnlyList<CodeToken>? literals))
        {
            return TextPointer.Of(string.Concat(literals.Where(IsCompiled).Select(SketchScanner.Decode)));
        }

        if (_scan.StringArrays.TryGetValue(name, out CodeStringArray? array))
        {
            return array with { Items = [.. array.Items.Where(IsCompiled)] };
        }

        if (_constants.TryGetValue(name, out (IReadOnlyList<CodeToken> Tokens, int Start, int End) known))
        {
            return Unowned(Sub(known.Tokens, known.Start, known.End));
        }

        if (_scan.Macros.TryGetValue(name, out string? body) && body.Length > 0)
        {
            List<CodeToken> tokens = SketchScanner.TokensOf(body);
            return Unowned(Sub(tokens, 0, tokens.Count));
        }

        return null;
    }

    private static object? Unowned(object? value) => value is TextPointer text ? text with { Origin = null } : value;

    private bool IsCompiled(CodeToken token) => IsCompiled(_scan.Files[token.File], token.Offset);

    // ---- tables of structs -----------------------------------------------------------------------------

    private Dictionary<string, CodeTable> Tables
    {
        get
        {
            if (_tables is null)
            {
                // An #if around a row can ask for a name, and that asks here again: there are no tables by then.
                _tables = new Dictionary<string, CodeTable>(StringComparer.Ordinal);
                int depth = _depth;
                _depth = 0;
                ReadTables(_tables);
                _depth = depth;
            }

            return _tables;
        }
    }

    private void ReadTables(Dictionary<string, CodeTable> tables)
    {
        Dictionary<string, List<string>> structs = ReadStructs();
        foreach (CodeFile file in _scan.Files)
        {
            List<CodeToken> t = file.Tokens;
            for (int i = 1; i + 4 < t.Count; i++)
            {
                if (t[i].Kind != TokenKind.Identifier || !t[i + 1].Is("[") || !structs.TryGetValue(t[i - 1].Text, out List<string>? fields))
                {
                    continue;
                }

                int k = SketchScanner.Matching(t, i + 1, "[", "]") + 1;
                while (k < t.Count && t[k].Kind == TokenKind.Identifier)
                {
                    k++;
                }

                if (k + 2 >= t.Count || !t[k].Is("=") || !t[k + 1].Is("{") || !t[k + 2].Is("{"))
                {
                    continue;
                }

                int close = SketchScanner.Matching(t, k + 1, "{", "}");
                var rows = new List<IReadOnlyList<(int, int)>>();
                for (int r = k + 2; r < close; r++)
                {
                    if (t[r].Is("{"))
                    {
                        int rowEnd = SketchScanner.Matching(t, r, "{", "}");
                        if (IsCompiled(file, t[r].Offset))
                        {
                            rows.Add(Ranges(t, r + 1, rowEnd));
                        }

                        r = rowEnd;
                    }
                }

                tables[t[i].Text] = new CodeTable(t[i].Text, t, fields, rows);
            }
        }
    }

    private Dictionary<string, List<string>> ReadStructs()
    {
        var structs = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (CodeFile file in _scan.Files)
        {
            List<CodeToken> t = file.Tokens;
            for (int i = 0; i + 2 < t.Count; i++)
            {
                if (!t[i].Is("struct") || t[i + 1].Kind != TokenKind.Identifier || !t[i + 2].Is("{"))
                {
                    continue;
                }

                int close = SketchScanner.Matching(t, i + 2, "{", "}");
                var fields = new List<string>();
                int from = i + 3;
                for (int k = from; k < close; k++)
                {
                    if (t[k].Is(";"))
                    {
                        int last = from;
                        while (last < k && !t[last].Is("[") && !t[last].Is("="))
                        {
                            last++;
                        }

                        if (last > from && t[last - 1].Kind == TokenKind.Identifier)
                        {
                            fields.Add(t[last - 1].Text);
                        }

                        from = k + 1;
                    }
                }

                structs[t[i + 1].Text] = fields;
            }
        }

        return structs;
    }

    private static List<(int Start, int End)> Ranges(List<CodeToken> t, int start, int end)
    {
        var ranges = new List<(int, int)>();
        int nested = 0;
        int from = start;
        for (int k = start; k < end; k++)
        {
            if (t[k].Is("(") || t[k].Is("[") || t[k].Is("{"))
            {
                nested++;
            }
            else if (t[k].Is(")") || t[k].Is("]") || t[k].Is("}"))
            {
                nested--;
            }
            else if (nested == 0 && t[k].Is(","))
            {
                ranges.Add((from, k));
                from = k + 1;
            }
        }

        if (end > from)
        {
            ranges.Add((from, end));
        }

        return ranges;
    }

    // Whether what stands here is built, as far as #if can be told from the sketch's own #defines. What depends on
    // something else, such as a flag from the build, counts as built, and so does its #else: better shown than lost.
    // #ifndef counts as on, since it is mostly a guard or a default that follows at once.
    private bool IsCompiled(CodeFile file, int offset)
    {
        var open = new Stack<(bool Active, bool Taken)>();
        foreach ((int at, string line) in file.Conditionals)
        {
            if (at >= offset)
            {
                break;
            }

            string word = line.Split(' ', '\t', '(')[0];
            string rest = line[word.Length..].Trim();
            switch (word)
            {
                case "if":
                    bool? holds = Holds(rest);
                    open.Push((holds != false, holds == true));
                    break;
                case "ifdef":
                    open.Push((true, _scan.Macros.ContainsKey(rest)));
                    break;
                case "ifndef":
                    open.Push((true, false));
                    break;
                case "elif" when open.Count > 0:
                    (bool _, bool taken) = open.Pop();
                    bool? next = taken ? false : Holds(rest);
                    open.Push((next != false, taken || next == true));
                    break;
                case "else" when open.Count > 0:
                    (bool _, bool earlier) = open.Pop();
                    open.Push((!earlier, true));
                    break;
                case "endif" when open.Count > 0:
                    open.Pop();
                    break;
                default:
                    break;
            }
        }

        return open.All(o => o.Active);
    }

    private bool? Holds(string condition)
    {
        if (condition.Contains("defined", StringComparison.Ordinal))
        {
            return null;
        }

        List<CodeToken> tokens = SketchScanner.TokensOf(condition);
        return Truth(Sub(tokens, 0, tokens.Count));
    }

    // ---- calls ---------------------------------------------------------------------------------------

    private object? Call(string name, IReadOnlyList<object?> arguments)
    {
        if (!_functions.TryGetValue(name, out List<CodeFunction>? overloads) || _depth >= DepthLimit)
        {
            return null;
        }

        CodeFunction? function = overloads.FirstOrDefault(f => f.Parameters.Count == arguments.Count) ?? overloads.FirstOrDefault(f => f.Parameters.Count > arguments.Count);
        if (function is null)
        {
            return null;
        }

        var frame = new Frame(function.Tokens);
        for (int i = 0; i < function.Parameters.Count; i++)
        {
            frame.Locals[function.Parameters[i].Name] = i < arguments.Count ? arguments[i] : DefaultArgument(function, i);
        }

        _depth++;
        int at = function.BodyStart;
        Flow flow = Block(frame, ref at, function.BodyEnd);
        _depth--;
        ReturnedFromOwnCase = frame.OwnCase;
        if (flow != Flow.Return)
        {
            return null;
        }

        // A literal that other screens come to as well is not this screen's to change.
        return frame.OwnCase ? frame.Result : Unowned(frame.Result);
    }

    // A default is written where a function is declared, which need not be where it is defined.
    private object? DefaultArgument(CodeFunction function, int index)
    {
        foreach (CodeFile file in _scan.Files)
        {
            List<CodeToken> t = file.Tokens;
            for (int i = 0; i + 1 < t.Count; i++)
            {
                if (t[i].Is(function.Name) && t[i + 1].Is("("))
                {
                    int close = SketchScanner.Matching(t, i + 1, "(", ")");
                    List<(int Start, int End)> ranges = Ranges(t, i + 2, close);
                    if (ranges.Count == function.Parameters.Count)
                    {
                        (int start, int end) = ranges[index];
                        for (int k = start; k < end; k++)
                        {
                            if (t[k].Is("="))
                            {
                                return Sub(t, k + 1, end);
                            }
                        }
                    }
                }
            }
        }

        return null;
    }

    // ---- statements ------------------------------------------------------------------------------------

    private enum Flow
    {
        Next,
        Break,
        Continue,
        Return,
        Stuck,
    }

    private sealed class Frame(IReadOnlyList<CodeToken> tokens)
    {
        public IReadOnlyList<CodeToken> T { get; } = tokens;

        public Dictionary<string, object?> Locals { get; } = new(StringComparer.Ordinal);

        public object? Result { get; set; }

        public bool OwnCase { get; set; }
    }

    private Flow Block(Frame f, ref int k, int end)
    {
        while (k < end)
        {
            Flow flow = Statement(f, ref k, end);
            if (flow != Flow.Next)
            {
                return flow;
            }
        }

        return Flow.Next;
    }

    private Flow Statement(Frame f, ref int k, int end)
    {
        if (++_steps > StepLimit || k >= end)
        {
            return Flow.Stuck;
        }

        IReadOnlyList<CodeToken> t = f.T;
        CodeToken token = t[k];
        if (token.Is("{"))
        {
            int close = SketchScanner.Matching(t, k, "{", "}");
            int inner = k + 1;
            Flow flow = Block(f, ref inner, close);
            k = close + 1;
            return flow;
        }

        if (token.Is(";"))
        {
            k++;
            return Flow.Next;
        }

        if (token.Is("if"))
        {
            return If(f, ref k, end);
        }

        if (token.Is("while"))
        {
            return While(f, ref k, end);
        }

        if (token.Is("for"))
        {
            return For(f, ref k, end);
        }

        if (token.Is("switch"))
        {
            return Switch(f, ref k, end);
        }

        if (token.Is("do"))
        {
            return Flow.Stuck;
        }

        if (token.Is("break"))
        {
            k += 2;
            return Flow.Break;
        }

        if (token.Is("continue"))
        {
            k += 2;
            return Flow.Continue;
        }

        int semicolon = Semicolon(t, k, end);
        if (token.Is("return"))
        {
            f.Result = semicolon > k + 1 ? new Expression(this, f, k + 1, semicolon).Whole() : null;
            k = semicolon + 1;
            return Flow.Return;
        }

        Simple(f, k, semicolon);
        k = semicolon + 1;
        return Flow.Next;
    }

    private static int Semicolon(IReadOnlyList<CodeToken> t, int start, int end)
    {
        int nested = 0;
        for (int k = start; k < end; k++)
        {
            if (t[k].Is("(") || t[k].Is("{") || t[k].Is("["))
            {
                nested++;
            }
            else if (t[k].Is(")") || t[k].Is("}") || t[k].Is("]"))
            {
                nested--;
            }
            else if (nested == 0 && t[k].Is(";"))
            {
                return k;
            }
        }

        return end;
    }

    // One statement's extent, without running it.
    private static int Past(IReadOnlyList<CodeToken> t, int k, int end)
    {
        if (k >= end)
        {
            return end;
        }

        if (t[k].Is("{"))
        {
            return SketchScanner.Matching(t, k, "{", "}") + 1;
        }

        if (t[k].Is("if"))
        {
            int after = Past(t, SketchScanner.Matching(t, k + 1, "(", ")") + 1, end);
            return after < end && t[after].Is("else") ? Past(t, after + 1, end) : after;
        }

        if (t[k].Is("for") || t[k].Is("while") || t[k].Is("switch"))
        {
            return Past(t, SketchScanner.Matching(t, k + 1, "(", ")") + 1, end);
        }

        return Semicolon(t, k, end) + 1;
    }

    private Flow If(Frame f, ref int k, int end)
    {
        IReadOnlyList<CodeToken> t = f.T;
        int close = SketchScanner.Matching(t, k + 1, "(", ")");
        bool? truth = Truth(new Expression(this, f, k + 2, close).Whole());
        int thenStart = close + 1;
        int thenEnd = Past(t, thenStart, end);
        bool hasElse = thenEnd < end && t[thenEnd].Is("else");
        int after = hasElse ? Past(t, thenEnd + 1, end) : thenEnd;
        k = after;
        if (truth is null)
        {
            return Flow.Stuck;
        }

        if (truth.Value)
        {
            int at = thenStart;
            return Statement(f, ref at, thenEnd);
        }

        if (hasElse)
        {
            int at = thenEnd + 1;
            return Statement(f, ref at, after);
        }

        return Flow.Next;
    }

    private Flow While(Frame f, ref int k, int end)
    {
        IReadOnlyList<CodeToken> t = f.T;
        int close = SketchScanner.Matching(t, k + 1, "(", ")");
        int bodyEnd = Past(t, close + 1, end);
        int conditionStart = k + 2;
        k = bodyEnd;
        return Loop(f, conditionStart, close, close + 1, bodyEnd, -1, -1);
    }

    private Flow For(Frame f, ref int k, int end)
    {
        IReadOnlyList<CodeToken> t = f.T;
        int close = SketchScanner.Matching(t, k + 1, "(", ")");
        int first = Semicolon(t, k + 2, close);
        int second = Semicolon(t, first + 1, close);
        int bodyEnd = Past(t, close + 1, end);
        Simple(f, k + 2, first);
        k = bodyEnd;
        return Loop(f, first + 1, second, close + 1, bodyEnd, second + 1, close);
    }

    private Flow Loop(Frame f, int conditionStart, int conditionEnd, int bodyStart, int bodyEnd, int stepStart, int stepEnd)
    {
        while (true)
        {
            if (++_steps > StepLimit)
            {
                return Flow.Stuck;
            }

            if (conditionEnd > conditionStart)
            {
                bool? truth = Truth(new Expression(this, f, conditionStart, conditionEnd).Whole());
                if (truth is null)
                {
                    return Flow.Stuck;
                }

                if (!truth.Value)
                {
                    return Flow.Next;
                }
            }

            int at = bodyStart;
            Flow flow = Statement(f, ref at, bodyEnd);
            if (flow == Flow.Break)
            {
                return Flow.Next;
            }

            if (flow is Flow.Return or Flow.Stuck)
            {
                return flow;
            }

            if (stepEnd > stepStart)
            {
                new Expression(this, f, stepStart, stepEnd).Whole();
            }
        }
    }

    private Flow Switch(Frame f, ref int k, int end)
    {
        IReadOnlyList<CodeToken> t = f.T;
        int close = SketchScanner.Matching(t, k + 1, "(", ")");
        int bodyEnd = SketchScanner.Matching(t, close + 1, "{", "}");
        object? value = new Expression(this, f, k + 2, close).Whole();
        bool onScreen = close > k + 2 && t[close - 1].Is(ScreenVariable ?? "\0");
        k = bodyEnd + 1;
        if (value is not long number)
        {
            return Flow.Stuck;
        }

        // The labels at this level, in order: where each one's statements start, and how many labels lead there.
        int? chosen = null;
        int? fallback = null;
        bool own = false;
        int nested = 0;
        for (int i = close + 2; i < bodyEnd; i++)
        {
            if (t[i].Is("{") || t[i].Is("("))
            {
                nested++;
            }
            else if (t[i].Is("}") || t[i].Is(")"))
            {
                nested--;
            }
            else if (nested == 0 && t[i].Is("default"))
            {
                fallback = i + 2;
            }
            else if (nested == 0 && t[i].Is("case") && chosen is null)
            {
                int colon = i + 1;
                while (colon < bodyEnd && !t[colon].Is(":"))
                {
                    colon++;
                }

                if (new Expression(this, new Frame(t), i + 1, colon).Whole() is long label && label == number)
                {
                    chosen = colon + 1;
                    bool sharedBefore = t[i - 1].Is(":") && i - 1 > close + 2;
                    bool sharedAfter = colon + 1 < bodyEnd && (t[colon + 1].Is("case") || t[colon + 1].Is("default"));
                    own = !sharedBefore && !sharedAfter;
                }
            }
        }

        int? start = chosen ?? fallback;
        if (start is null)
        {
            return Flow.Next;
        }

        if (onScreen)
        {
            f.OwnCase = chosen is not null && own;
        }

        // On through what follows until something ends it, as a switch does; labels are not statements.
        int at = start.Value;
        while (at < bodyEnd)
        {
            if (t[at].Is("case"))
            {
                while (at < bodyEnd && !t[at].Is(":"))
                {
                    at++;
                }

                at++;
                continue;
            }

            if (t[at].Is("default"))
            {
                at += 2;
                continue;
            }

            Flow flow = Statement(f, ref at, bodyEnd);
            if (flow != Flow.Next && flow != Flow.Break)
            {
                return flow;
            }

            if (flow == Flow.Break)
            {
                break;
            }
        }

        // What comes after the switch is for every screen.
        f.OwnCase &= !onScreen;
        return Flow.Next;
    }

    // A declaration, or an expression for what it does to locals.
    private void Simple(Frame f, int start, int end)
    {
        IReadOnlyList<CodeToken> t = f.T;
        int k = start;
        int names = 0;
        while (k < end && (t[k].Kind == TokenKind.Identifier || t[k].Is("*") || t[k].Is("&") || t[k].Is("::")))
        {
            names += t[k].Kind == TokenKind.Identifier ? 1 : 0;
            k++;
        }

        if (names < 2 || t[k - 1].Kind != TokenKind.Identifier)
        {
            new Expression(this, f, start, end).Whole();
            return;
        }

        while (k <= end)
        {
            string name = t[k - 1].Text;
            int next = k;
            int nested = 0;
            while (next < end && (nested > 0 || !t[next].Is(",")))
            {
                nested += t[next].Is("(") || t[next].Is("[") || t[next].Is("{") ? 1 : t[next].Is(")") || t[next].Is("]") || t[next].Is("}") ? -1 : 0;
                next++;
            }

            f.Locals[name] = k < end && t[k].Is("=") ? new Expression(this, f, k + 1, next).Whole() : null;
            if (next >= end)
            {
                return;
            }

            k = next + 2;
        }
    }

    private static bool? Truth(object? value) => value switch
    {
        long number => number != 0,
        TextPointer or TableRow or CodeTable or CodeStringArray => true,
        _ => null,
    };

    // ---- expressions -----------------------------------------------------------------------------------

    private sealed class Expression(SketchRun run, Frame frame, int start, int end)
    {
        private readonly IReadOnlyList<CodeToken> _t = frame.T;
        private int _at = start;
        private int _end = end;

        // Parsed for its extent only: the side of && or ?: that does not happen.
        private int _skip;

        private bool Next(string text) => _at < _end && _t[_at].Is(text);

        public object? Whole()
        {
            object? value = Assign();
            return _at >= _end ? value : null;
        }

        private object? Skipped(Func<object?> part)
        {
            _skip++;
            part();
            _skip--;
            return null;
        }

        private object? Assign()
        {
            if (_at + 1 < _end && _t[_at].Kind == TokenKind.Identifier)
            {
                string name = _t[_at].Text;
                string? operation = _t[_at + 1].Is("=") ? "="
                    : _t[_at + 1].Is("+=") ? "+"
                    : _t[_at + 1].Is("-=") ? "-"
                    : _at + 2 < _end && _t[_at + 2].Is("=") && (_t[_at + 1].Is("*") || _t[_at + 1].Is("/") || _t[_at + 1].Is("%") || _t[_at + 1].Is("&") || _t[_at + 1].Is("|") || _t[_at + 1].Is("^")) ? _t[_at + 1].Text
                    : null;
                if (operation is not null)
                {
                    _at += operation == "=" || _t[_at + 1].Is("+=") || _t[_at + 1].Is("-=") ? 2 : 3;
                    object? right = Assign();
                    object? value = operation == "=" ? right : Apply(operation, Read(name), right);
                    if (_skip == 0)
                    {
                        Write(name, value);
                    }

                    return value;
                }
            }

            return Ternary();
        }

        private object? Read(string name) => run.Named(name, frame);

        // What is written anywhere but a local is dropped: nothing here reads it back as known.
        private void Write(string name, object? value)
        {
            if (frame.Locals.ContainsKey(name))
            {
                frame.Locals[name] = value;
            }
        }

        private object? Ternary()
        {
            object? condition = Binary(0);
            if (!Next("?"))
            {
                return condition;
            }

            _at++;
            bool? truth = Truth(condition);
            object? first = truth == true ? Assign() : Skipped(Assign);
            if (Next(":"))
            {
                _at++;
            }

            object? second = truth == false ? Assign() : Skipped(Assign);
            return truth == true ? first : truth == false ? second : null;
        }

        private object? Binary(int level)
        {
            if (level == Levels.Length)
            {
                return Unary();
            }

            object? left = Binary(level + 1);
            while (_at < _end && _t[_at].Kind == TokenKind.Punct && Levels[level].Contains(_t[_at].Text))
            {
                string operation = _t[_at++].Text;
                bool? truth = Truth(left);
                if ((operation == "&&" && truth == false) || (operation == "||" && truth == true))
                {
                    Skipped(() => Binary(level + 1));
                    left = operation == "&&" ? 0L : 1L;
                }
                else if (operation is "&&" or "||")
                {
                    bool? right = Truth(Binary(level + 1));
                    left = truth is null || right is null ? null : right.Value ? 1L : 0L;
                }
                else
                {
                    left = Apply(operation, left, Binary(level + 1));
                }
            }

            return left;
        }

        private static object? Apply(string operation, object? left, object? right)
        {
            if (left is TextPointer text && right is long move && operation is "+" or "-")
            {
                long place = text.Offset + (operation == "+" ? move : -move);
                return place >= 0 && place <= text.Bytes.Length ? text with { Offset = (int)place, Origin = null } : null;
            }

            if (left is not long a || right is not long b)
            {
                return null;
            }

            return operation switch
            {
                "+" => a + b,
                "-" => a - b,
                "*" => a * b,
                "/" => b is 0 or -1 ? (b == 0 ? null : -a) : a / b,
                "%" => b is 0 or -1 ? (b == 0 ? null : 0L) : a % b,
                "&" => a & b,
                "|" => a | b,
                "^" => a ^ b,
                "<<" => a << (int)b,
                ">>" => a >> (int)b,
                "==" => a == b ? 1L : 0L,
                "!=" => a != b ? 1L : 0L,
                "<" => a < b ? 1L : 0L,
                ">" => a > b ? 1L : 0L,
                "<=" => a <= b ? 1L : 0L,
                ">=" => a >= b ? 1L : 0L,
                _ => null,
            };
        }

        private object? Unary()
        {
            if (_at >= _end)
            {
                return null;
            }

            CodeToken token = _t[_at];
            if (token.Kind == TokenKind.Punct)
            {
                switch (token.Text)
                {
                    case "!":
                        _at++;
                        return Truth(Unary()) is { } truth ? (truth ? 0L : 1L) : null;
                    case "-":
                        _at++;
                        return Unary() is long negated ? -negated : null;
                    case "+":
                        _at++;
                        return Unary();
                    case "~":
                        _at++;
                        return Unary() is long flipped ? ~flipped : null;
                    case "&":
                        _at++;
                        return Unary();
                    case "*":
                        _at++;
                        return Unary() switch
                        {
                            TextPointer text => text.ByteAt(0),
                            TableRow row => row,
                            _ => null,
                        };
                    case "++" or "--" when _at + 1 < _end && _t[_at + 1].Kind == TokenKind.Identifier:
                        string name = _t[_at + 1].Text;
                        _at += 2;
                        object? changed = Apply(token.Text == "++" ? "+" : "-", Read(name), 1L);
                        if (_skip == 0)
                        {
                            Write(name, changed);
                        }

                        return changed;
                    default:
                        break;
                }
            }

            if (IsCast())
            {
                _at = SketchScanner.Matching(_t, _at, "(", ")") + 1;
                return Unary();
            }

            return Postfix();
        }

        // (uint8_t)x, (const char *)x: followed by what can be cast, and with nothing in it but a type.
        private bool IsCast()
        {
            if (!Next("("))
            {
                return false;
            }

            int close = SketchScanner.Matching(_t, _at, "(", ")");
            if (close >= _end || close + 1 >= _end || close == _at + 1)
            {
                return false;
            }

            bool typed = false;
            for (int k = _at + 1; k < close; k++)
            {
                if (_t[k].Kind == TokenKind.Identifier)
                {
                    typed |= TypeWords.Contains(_t[k].Text) || _t[k].Text.EndsWith("_t", StringComparison.Ordinal) || _t[k].Text.StartsWith("__", StringComparison.Ordinal);
                }
                else if (_t[k].Is("*"))
                {
                    typed = true;
                }
                else if (!_t[k].Is("&") && !_t[k].Is("::"))
                {
                    return false;
                }
            }

            CodeToken after = _t[close + 1];
            return typed && (after.Kind is TokenKind.Identifier or TokenKind.Number or TokenKind.String or TokenKind.Char || after.Is("(") || after.Is("*") || after.Is("&") || after.Is("!") || after.Is("-"));
        }

        private object? Postfix()
        {
            object? value = Primary();
            while (_at < _end)
            {
                if (Next("["))
                {
                    object? index = Inside("[", "]");
                    value = index is long number ? IndexOf(value, number) : null;
                }
                else if ((Next(".") || Next("->")) && _at + 1 < _end)
                {
                    value = FieldOf(value, _t[_at + 1].Text);
                    _at += 2;
                }
                else if (Next("("))
                {
                    Inside("(", ")");
                    value = null;
                }
                else
                {
                    break;
                }
            }

            return value;
        }

        private static object? IndexOf(object? value, long index) => value switch
        {
            CodeTable table => index >= 0 && index < table.Rows.Count ? new TableRow(table, (int)index) : null,
            CodeStringArray array => index >= 0 && index < array.Items.Count ? TextPointer.Of(SketchScanner.Decode(array.Items[(int)index]), array.Items[(int)index]) : null,
            TextPointer text => text.ByteAt(index),
            _ => null,
        };

        private object? FieldOf(object? value, string field)
        {
            if (value is not TableRow row)
            {
                return null;
            }

            int column = -1;
            for (int i = 0; i < row.Table.Fields.Count; i++)
            {
                column = row.Table.Fields[i] == field ? i : column;
            }

            IReadOnlyList<(int Start, int End)> cells = row.Table.Rows[row.Index];
            return column >= 0 && column < cells.Count ? run.Sub(row.Table.Tokens, cells[column].Start, cells[column].End) : null;
        }

        // What is between a pair of brackets, the cursor left past them.
        private object? Inside(string open, string close)
        {
            int match = SketchScanner.Matching(_t, _at, open, close);
            int outer = _end;
            _end = Math.Min(match, outer);
            _at++;
            object? value = Assign();
            if (_at < _end)
            {
                value = null;
            }

            _end = outer;
            _at = match + 1;
            return value;
        }

        private object? Primary()
        {
            if (_at >= _end)
            {
                return null;
            }

            CodeToken token = _t[_at];
            switch (token.Kind)
            {
                case TokenKind.Number:
                    _at++;
                    return Integer(token.Text);
                case TokenKind.String:
                    return Strings();
                case TokenKind.Char:
                    _at++;
                    string character = SketchScanner.Decode(token);
                    return character.Length > 0 ? (long)character[0] : null;
                case TokenKind.Identifier:
                    return Named();
                default:
                    return Next("(") ? Inside("(", ")") : Nothing();
            }
        }

        private object? Nothing()
        {
            _at = _end + 1;
            return null;
        }

        // "one" "two" is one string.
        private TextPointer Strings()
        {
            CodeToken first = _t[_at];
            var text = new StringBuilder();
            int count = 0;
            while (_at < _end && _t[_at].Kind == TokenKind.String)
            {
                text.Append(SketchScanner.Decode(_t[_at++]));
                count++;
            }

            return TextPointer.Of(text.ToString(), count == 1 ? first : null);
        }

        private static long? Integer(string text)
        {
            string digits = text.TrimEnd('u', 'U', 'l', 'L');
            if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return long.TryParse(digits.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long hex) ? hex : null;
            }

            if (digits.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            {
                long value = 0;
                foreach (char c in digits.AsSpan(2))
                {
                    value = (value * 2) + (c - '0');
                }

                return value;
            }

            return long.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number) ? number : null;
        }

        private object? Named()
        {
            string name = _t[_at++].Text;
            while (Next("::") && _at + 1 < _end)
            {
                name = _t[_at + 1].Text;
                _at += 2;
            }

            switch (name)
            {
                case "this" when Next("->"):
                    _at++;
                    return Primary();
                case "sizeof":
                    return SizeOf();
                case "reinterpret_cast" or "static_cast" or "const_cast" or "dynamic_cast" when Next("<"):
                    int nested = 0;
                    do
                    {
                        nested += Next("<") ? 1 : Next(">") ? -1 : 0;
                        _at++;
                    }
                    while (nested > 0 && _at < _end);
                    return Next("(") ? Inside("(", ")") : null;
                default:
                    break;
            }

            if (Next("("))
            {
                return Called(name);
            }

            if (Next("++") || Next("--"))
            {
                string operation = _t[_at++].Is("++") ? "+" : "-";
                object? before = Read(name);
                if (_skip == 0)
                {
                    Write(name, Apply(operation, before, 1L));
                }

                return before;
            }

            return Read(name);
        }

        // sizeof(TABLE) / sizeof(TABLE[0]): the only thing a count of elements needs from it.
        private object? SizeOf()
        {
            if (!Next("("))
            {
                return null;
            }

            int close = SketchScanner.Matching(_t, _at, "(", ")");
            string name = _at + 1 < close ? _t[_at + 1].Text : "";
            bool oneElement = _at + 2 < close && _t[_at + 2].Is("[");
            _at = close + 1;
            return oneElement ? 1L : run.Named(name, frame) switch
            {
                CodeTable table => (long)table.Rows.Count,
                CodeStringArray array => (long)array.Items.Count,
                TextPointer text => (long)text.Bytes.Length,
                _ => null,
            };
        }

        private object? Called(string name)
        {
            int close = SketchScanner.Matching(_t, _at, "(", ")");
            int outer = _end;
            _end = Math.Min(close, outer);
            _at++;
            var arguments = new List<object?>();
            while (_at < _end)
            {
                arguments.Add(Assign());
                if (Next(","))
                {
                    _at++;
                }
                else if (_at < _end)
                {
                    arguments.Add(null);
                    _at = _end;
                }
            }

            _end = outer;
            _at = close + 1;
            return _skip > 0 ? null : Function(name, arguments);
        }

        private object? Function(string name, List<object?> a)
        {
            switch (name)
            {
                case "F" or "PSTR" or "FPSTR" or "String" when a.Count == 1:
                    return a[0];
                case "pgm_read_ptr" or "pgm_read_ptr_near" or "pgm_read_word" or "pgm_read_word_near" or "pgm_read_dword" or "pgm_read_dword_near" when a.Count == 1:
                    return a[0];
                case "pgm_read_byte" or "pgm_read_byte_near" when a.Count == 1:
                    return a[0] is TextPointer text ? text.ByteAt(0) : a[0];
                case "strlen" or "strlen_P" when a.Count == 1:
                    return a[0] is TextPointer measured ? measured.Length : null;
                case "min" when a.Count == 2 && a[0] is long x && a[1] is long y:
                    return Math.Min(x, y);
                case "max" when a.Count == 2 && a[0] is long x && a[1] is long y:
                    return Math.Max(x, y);
                case "abs" when a.Count == 1 && a[0] is long z && z != long.MinValue:
                    return Math.Abs(z);
                default:
                    return run.Call(name, a);
            }
        }
    }
}
