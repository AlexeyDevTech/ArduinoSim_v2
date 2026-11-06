using System.Globalization;
using System.Text.RegularExpressions;

namespace ArduSim.Core.Screens;

/// <summary>
/// Forms a project's screens and transitions from its own code (ADR-019). A heuristic, and safe to be
/// wrong: it changes nothing, every result links to its line, and only a single token it read
/// exactly can be edited through it.
/// </summary>
public sealed partial class ScreenCodeAnalyzer
{
    private const int CallDepth = 5;
    private const int MaxChains = 24;

    private readonly SketchScanner _scan = new();
    private readonly Dictionary<CodeFunction, FunctionFacts> _facts = [];
    private readonly Dictionary<string, List<CodeFunction>> _byName = new(StringComparer.Ordinal);
    private readonly ScreenDisplay? _sceneDisplay;
    private ScreenDisplay _display = new(DisplayFamily.CharLcd, 16, 2, DisplayLibrary.LiquidCrystalI2c, "lcd");
    private HashSet<string> _displayNames = new(StringComparer.Ordinal);
    private HashSet<string> _surfaceTypes = new(StringComparer.Ordinal);
    private (CodeFunction Function, SwitchFact Switch)? _drawSwitch;
    private SourceSpan? _initial;

    private ScreenCodeAnalyzer(ScreenDisplay? sceneDisplay)
    {
        _sceneDisplay = sceneDisplay;
    }

    /// <summary>The screens the sources describe, or null when they draw on no display this knows.</summary>
    /// <param name="files">Each source's path and current text: an open editor's text, not the file's.</param>
    /// <param name="sceneDisplay">The display part on the schematic: its size is what the hardware has.</param>
    public static ScreenDocument? Analyze(IEnumerable<(string Path, string Text)> files, ScreenDisplay? sceneDisplay = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        var analyzer = new ScreenCodeAnalyzer(sceneDisplay);
        foreach ((string path, string text) in files)
        {
            analyzer._scan.Add(path, text);
        }

        return analyzer.Run();
    }

    /// <summary>
    /// The display object a sketch declares at file level, which generated code can use by name; null when
    /// it has none, or only a class member or a <c>static</c> that other files cannot reach.
    /// </summary>
    public static ScreenDisplay? DisplayIn(IEnumerable<(string Path, string Text)> files, ScreenDisplay? sceneDisplay = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        var analyzer = new ScreenCodeAnalyzer(sceneDisplay);
        foreach ((string path, string text) in files)
        {
            analyzer._scan.Add(path, text);
        }

        foreach (CodeDisplay code in analyzer._scan.Displays)
        {
            if (analyzer.FileLevelType(code) is { } type)
            {
                return analyzer.ReadDisplay(code) with { Existing = true, TypeName = type, TextFromBaseline = false };
            }
        }

        return null;
    }

    // The type's text where the object is declared outside every brace.
    private string? FileLevelType(CodeDisplay code)
    {
        foreach (CodeFile file in _scan.Files)
        {
            List<CodeToken> t = file.Tokens;
            int depth = 0;
            for (int i = 0; i + 1 < t.Count; i++)
            {
                depth += t[i].Is("{") ? 1 : t[i].Is("}") ? -1 : 0;
                if (depth != 0 || !t[i].Is(code.Name) || !(t[i + 1].Is("(") || t[i + 1].Is(";") || t[i + 1].Is("=")))
                {
                    continue;
                }

                for (int start = i - 1; start >= 0 && i - start < 32; start--)
                {
                    if (t[start].Is(code.Type))
                    {
                        return start > 0 && t[start - 1].Is("static") ? null : string.Concat(t.Skip(start).Take(i - start).Select(x => x.Text));
                    }
                }
            }
        }

        return null;
    }

    private ScreenDocument? Run()
    {
        if (_scan.Displays.Count == 0)
        {
            return null;
        }

        _display = ReadDisplay(_scan.Displays[0]);
        _displayNames = new HashSet<string>(_scan.Displays.Select(d => d.Name), StringComparer.Ordinal);
        FindSurfaces();
        foreach (CodeFunction function in _scan.Functions)
        {
            if (!_byName.TryGetValue(function.Name, out List<CodeFunction>? list))
            {
                _byName[function.Name] = list = [];
            }

            list.Add(function);
            var collector = new FactCollector(function);
            collector.Walk(function.BodyStart, function.BodyEnd);
            _facts[function] = collector.Facts;
        }

        ComputeDrawing();
        return FromState() ?? FromFunctions();
    }

    // A class of the sketch's own that draws for it, such as a buffer in front of the panel: what has
    // setCursor(x, y) is drawn on as a display is, and its objects count as the display.
    private void FindSurfaces()
    {
        _surfaceTypes = new HashSet<string>(_scan.Functions.Where(f => f.Name == "setCursor" && f.Class.Length > 0 && f.Parameters.Count == 2).Select(f => f.Class), StringComparer.Ordinal);
        foreach (CodeFile file in _scan.Files)
        {
            List<CodeToken> t = file.Tokens;
            for (int i = 0; i + 2 < t.Count; i++)
            {
                if (t[i].Kind == TokenKind.Identifier && _surfaceTypes.Contains(t[i].Text) && t[i + 1].Kind == TokenKind.Identifier && (t[i + 2].Is(";") || t[i + 2].Is("(") || t[i + 2].Is("=") || t[i + 2].Is("{")))
                {
                    _displayNames.Add(t[i + 1].Text);
                }
            }
        }
    }

    private bool IsSurfaceParameter(CodeFunction function, string name) =>
        function.Parameters.Any(p => p.Name == name && LastWord(p.Type) is { } type && (SketchScanner.IsDisplayType(type) || _surfaceTypes.Contains(type)));

    // ---- the display ---------------------------------------------------------------------------

    private ScreenDisplay ReadDisplay(CodeDisplay code)
    {
        DisplayLibrary library = code.Type switch
        {
            "LiquidCrystal" => DisplayLibrary.LiquidCrystal,
            "LiquidCrystal_I2C" => DisplayLibrary.LiquidCrystalI2c,
            "Adafruit_SSD1306" => DisplayLibrary.AdafruitSsd1306,
            "GyverOLED" => DisplayLibrary.GyverOled,
            _ when code.Type.StartsWith("Adafruit", StringComparison.Ordinal) => DisplayLibrary.AdafruitSh110x,
            _ => DisplayLibrary.U8g2,
        };
        DisplayFamily family = library is DisplayLibrary.LiquidCrystal or DisplayLibrary.LiquidCrystalI2c ? DisplayFamily.CharLcd : DisplayFamily.Oled;
        if (_sceneDisplay is { } scene && scene.Family == family)
        {
            return scene with { Library = library, Variable = code.Name, TextFromBaseline = library == DisplayLibrary.U8g2 };
        }

        (int width, int height) = family == DisplayFamily.CharLcd ? (16, 2) : (128, 64);
        IReadOnlyList<string> built = code.Arguments.Count > 0 ? code.Arguments : BuiltWith(code.Name);
        List<string> begun = BegunWith(code.Name);
        if (library == DisplayLibrary.LiquidCrystalI2c && built.Count >= 3 && Number(built[1]) is { } columns && Number(built[2]) is { } rows)
        {
            (width, height) = (columns, rows);
        }
        else if (library == DisplayLibrary.LiquidCrystal && begun.Count >= 2 && Number(begun[0]) is { } cells && Number(begun[1]) is { } lines)
        {
            (width, height) = (cells, lines);
        }
        else if (library == DisplayLibrary.LiquidCrystalI2c && begun.Count >= 2 && Number(begun[0]) is { } cells2 && Number(begun[1]) is { } lines2)
        {
            (width, height) = (cells2, lines2);
        }
        else if (library == DisplayLibrary.AdafruitSsd1306 && built.Count >= 2 && Number(built[0]) is { } w && Number(built[1]) is { } h)
        {
            (width, height) = (w, h);
        }
        else if (SizeInName().Match(code.Type) is { Success: true } size)
        {
            (width, height) = (int.Parse(size.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(size.Groups[2].Value, CultureInfo.InvariantCulture));
        }

        return new ScreenDisplay(family, width, height, library, code.Name, TextFromBaseline: library == DisplayLibrary.U8g2);
    }

    // lcd(ADDRESS, 20, 4) in a constructor's initializer list.
    private List<string> BuiltWith(string name)
    {
        foreach (CodeFile file in _scan.Files)
        {
            List<CodeToken> t = file.Tokens;
            for (int i = 0; i + 1 < t.Count; i++)
            {
                if (t[i].Is(name) && t[i + 1].Is("(") && (i == 0 || !(t[i - 1].Is(".") || t[i - 1].Is("->"))))
                {
                    int close = SketchScanner.Matching(t, i + 1, "(", ")");
                    return [.. ArgumentRanges(t, i + 2, close).Select(a => string.Concat(t.Skip(a.Item1).Take(a.Item2 - a.Item1).Select(x => x.Text)))];
                }
            }
        }

        return [];
    }

    // lcd.begin(16, 2).
    private List<string> BegunWith(string name)
    {
        foreach (CodeFile file in _scan.Files)
        {
            List<CodeToken> t = file.Tokens;
            for (int i = 0; i + 3 < t.Count; i++)
            {
                if (t[i].Is(name) && t[i + 1].Is(".") && t[i + 2].Is("begin") && t[i + 3].Is("("))
                {
                    int close = SketchScanner.Matching(t, i + 3, "(", ")");
                    return [.. ArgumentRanges(t, i + 4, close).Select(a => string.Concat(t.Skip(a.Item1).Take(a.Item2 - a.Item1).Select(x => x.Text)))];
                }
            }
        }

        return [];
    }

    private int? Number(string text)
    {
        for (int hops = 0; hops < 4 && _scan.Macros.TryGetValue(text, out string? value); hops++)
        {
            text = value;
        }

        return ParseInteger(text);
    }

    private static int? ParseInteger(string text)
    {
        text = text.TrimEnd('u', 'U', 'l', 'L');
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hex) ? hex : null
            : int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;
    }

    // ---- facts about each function --------------------------------------------------------------

    private sealed record CallFact(string Name, string Receiver, int NameIndex, int Open, int Close, IReadOnlyList<(int Start, int End)> Arguments, IReadOnlyList<Condition> Conditions);

    private sealed record AssignFact(string Variable, int Start, int End, IReadOnlyList<Condition> Conditions);

    private sealed record SwitchFact(string Variable, string Expression, int BodyEnd, IReadOnlyList<(IReadOnlyList<CodeToken> Labels, bool IsDefault, int Start, int End)> Cases);

    private sealed class FunctionFacts
    {
        public List<CallFact> Calls { get; } = [];

        public List<AssignFact> Assigns { get; } = [];

        public List<SwitchFact> Switches { get; } = [];

        /// <summary>Whether it draws, itself or through what it calls.</summary>
        public bool Draws { get; set; }

        /// <summary>Which of its parameters reaches the screen variable, or -1.</summary>
        public int GotoParameter { get; set; } = -1;
    }

    private sealed class FactCollector(CodeFunction function) : BodyWalker(function.Tokens)
    {
        public FunctionFacts Facts { get; } = new();

        protected override void SwitchSeen(string variable, string expression, int bodyEnd, IReadOnlyList<(IReadOnlyList<CodeToken> Labels, bool IsDefault, int Start, int End)> cases) =>
            Facts.Switches.Add(new SwitchFact(variable, expression, bodyEnd, cases));

        protected override void Simple(int from, int to)
        {
            IReadOnlyList<Condition> conditions = [.. Conditions];
            int nested = 0;
            for (int k = from; k < to; k++)
            {
                nested += Tokens[k].Is("(") ? 1 : Tokens[k].Is(")") ? -1 : 0;
                if (nested == 0 && Tokens[k].Is("=") && k > from)
                {
                    Facts.Assigns.Add(new AssignFact(LastName(from, k), k + 1, to, conditions));
                    break;
                }
            }

            foreach (CallFact call in CallsIn(Tokens, from, to, conditions))
            {
                Facts.Calls.Add(call);
            }
        }
    }

    private static List<CallFact> CallsIn(IReadOnlyList<CodeToken> t, int from, int to, IReadOnlyList<Condition> conditions)
    {
        var calls = new List<CallFact>();
        for (int k = from; k < to - 1; k++)
        {
            if (t[k].Kind == TokenKind.Identifier && t[k + 1].Is("("))
            {
                int close = Math.Min(SketchScanner.Matching(t, k + 1, "(", ")"), to);
                calls.Add(new CallFact(t[k].Text, ReceiverOf(t, k, from), k, k + 1, close, ArgumentRanges(t, k + 2, close), conditions));
            }
        }

        return calls;
    }

    private static string ReceiverOf(IReadOnlyList<CodeToken> t, int name, int from)
    {
        if (name - 2 >= from && (t[name - 1].Is(".") || t[name - 1].Is("->")))
        {
            // a.b.c(): c's receiver is b. x()->c(): the receiver is a call.
            return t[name - 2].Kind == TokenKind.Identifier ? t[name - 2].Text : "?";
        }

        return "";
    }

    private static List<(int, int)> ArgumentRanges(IReadOnlyList<CodeToken> t, int start, int end)
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

    private bool IsDrawCall(CallFact call, CodeFunction function) =>
        call.Receiver.Length > 0 && (_displayNames.Contains(call.Receiver) || IsSurfaceParameter(function, call.Receiver)) && DrawingCalls.Contains(call.Name);

    private static string LastWord(string type) => type.Length == 0 ? "" : type.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1];

    private static readonly string[] DrawingCalls =
    [
        "print", "println", "write", "setCursor", "clear", "home", "clearDisplay", "display", "drawLine", "drawRect", "fillRect", "drawFastHLine", "drawFastVLine", "drawStr", "drawFrame", "drawBox",
        "clearBuffer", "sendBuffer", "setCursorXY", "setScale", "setTextSize", "rect", "line", "update", "drawUTF8", "drawHLine", "drawVLine",
    ];

    private void ComputeDrawing()
    {
        foreach ((CodeFunction function, FunctionFacts facts) in _facts)
        {
            facts.Draws = facts.Calls.Any(c => IsDrawCall(c, function) || (c.Receiver.Length > 0 && c.Name is "render" or "draw" or "show" or "paint"));
        }

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach ((CodeFunction function, FunctionFacts facts) in _facts)
            {
                if (!facts.Draws && facts.Calls.Any(c => c.Receiver.Length == 0 && Callee(c, function) is { } callee && _facts[callee].Draws))
                {
                    facts.Draws = true;
                    changed = true;
                }
            }
        }
    }

    private CodeFunction? Callee(CallFact call, CodeFunction from) =>
        call.Receiver.Length > 0 ? null : _byName.GetValueOrDefault(call.Name) is { } list
            ? list.FirstOrDefault(f => f.Class == from.Class) ?? list.FirstOrDefault(f => f.Class.Length == 0) ?? (list.Count == 1 ? list[0] : null)
            : null;

    // ---- a screen variable ------------------------------------------------------------------------

    private ScreenDocument? FromState()
    {
        (IReadOnlyList<CodeToken> constants, HashSet<string> variables)? state = FindEnumState() ?? FindIntegerState();
        if (state is not { } found)
        {
            return null;
        }

        var names = new HashSet<string>(found.constants.Select(c => c.Text), StringComparer.Ordinal);
        ComputeGotoFunctions(names, found.variables);
        Dictionary<string, ScreenNode> drawn = DrawScreens(found.constants, found.variables);
        (Dictionary<string, List<ScreenTransition>> bySource, List<ScreenTransition> anywhere, string? start) = FindTransitions(names, found.variables);
        start ??= FindInitial(names, found.variables) ?? found.constants[0].Text;
        ScreenNode[] screens =
        [
            .. found.constants.DistinctBy(c => c.Text).Select(c => drawn[c.Text] with { Transitions = bySource.GetValueOrDefault(c.Text) ?? [] }),
        ];
        ScreenCodeShape shape = ShapeOf(found.constants, found.variables, bySource, anywhere);
        screens = [.. screens.Select(s => s with { UseSpans = UsesOf(s.Id), RemoveSpans = RemovalOf(s.Id, found.variables, bySource, anywhere) })];
        return Finish(screens, anywhere, start) with { Shape = shape };
    }

    private ScreenDocument Finish(ScreenNode[] screens, List<ScreenTransition> anywhere, string start)
    {
        var document = new ScreenDocument
        {
            Source = ScreenSource.Code,
            Display = _display,
            Start = start,
            Screens = MenuItemsLinked(screens),
            FromAnywhere = anywhere,
        };
        return ScreenMapLayout.Arrange(document);
    }

    // A menu's item knows where it leads, once the transitions are known.
    private static ScreenNode[] MenuItemsLinked(ScreenNode[] screens) =>
    [
        .. screens.Select(s => s.Menu is not { } menu ? s : s with
        {
            Menu = menu with
            {
                Items = [.. menu.Items.Select((item, i) => s.Transitions.FirstOrDefault(t => t.Trigger.MenuItem == i) is { } link ? item with { Target = link.Target } : item)],
            },
        }),
    ];

    private (IReadOnlyList<CodeToken>, HashSet<string>)? FindEnumState()
    {
        (CodeEnum Enum, int Score, HashSet<string> Variables)? best = null;
        foreach (CodeEnum candidate in _scan.Enums.Where(e => e.Constants.Count >= 2))
        {
            var names = new HashSet<string>(candidate.Constants.Select(c => c.Text), StringComparer.Ordinal);
            var variables = new HashSet<string>(StringComparer.Ordinal);
            int score = 0;
            foreach (FunctionFacts facts in _facts.Values)
            {
                foreach (SwitchFact s in facts.Switches)
                {
                    int hits = s.Cases.SelectMany(c => c.Labels).Count(l => names.Contains(l.Text));
                    if (hits > 0 && s.Variable.Length > 0)
                    {
                        score += hits;
                        variables.Add(s.Variable);
                    }
                }
            }

            string together = candidate.Name + " " + string.Join(' ', candidate.Constants.Select(c => c.Text));
            score += ScreenLike().IsMatch(candidate.Name) ? 8 : StateLike().IsMatch(candidate.Name) ? 3 : 0;
            score -= NotScreenLike().IsMatch(candidate.Name) ? 10 : 0;
            score += ScreenLike().IsMatch(together) ? 2 : 0;
            if (variables.Count > 0 && score >= 3 && (best is null || score > best.Value.Score))
            {
                best = (candidate, score, variables);
            }
        }

        return best is { } found ? (found.Enum.Constants, found.Variables) : null;
    }

    private (IReadOnlyList<CodeToken>, HashSet<string>)? FindIntegerState()
    {
        foreach (FunctionFacts facts in _facts.Values)
        {
            foreach (SwitchFact s in facts.Switches)
            {
                CodeToken[] labels = [.. s.Cases.SelectMany(c => c.Labels)];
                if (labels.Length >= 2 && ScreenLike().IsMatch(s.Variable) && labels.All(l => l.Kind is TokenKind.Number or TokenKind.Identifier))
                {
                    return (labels, new HashSet<string>([s.Variable], StringComparer.Ordinal));
                }
            }
        }

        return null;
    }

    // A function that puts its parameter into the screen variable is itself a way to go to a screen.
    private void ComputeGotoFunctions(HashSet<string> constants, HashSet<string> variables)
    {
        foreach ((CodeFunction function, FunctionFacts facts) in _facts)
        {
            foreach (AssignFact assign in facts.Assigns.Where(a => variables.Contains(a.Variable) && a.End - a.Start == 1))
            {
                int parameter = IndexOfParameter(function, function.Tokens[assign.Start].Text);
                if (parameter >= 0)
                {
                    facts.GotoParameter = parameter;
                }
            }

            // screen.show(next): a method of the screen variable.
            foreach (CallFact call in facts.Calls.Where(c => variables.Contains(c.Receiver) && c.Arguments.Count == 1 && c.Arguments[0].End - c.Arguments[0].Start == 1))
            {
                int parameter = IndexOfParameter(function, function.Tokens[call.Arguments[0].Start].Text);
                if (parameter >= 0)
                {
                    facts.GotoParameter = parameter;
                }
            }
        }

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach ((CodeFunction function, FunctionFacts facts) in _facts)
            {
                if (facts.GotoParameter >= 0)
                {
                    continue;
                }

                foreach (CallFact call in facts.Calls)
                {
                    if (Callee(call, function) is { } callee && _facts[callee].GotoParameter is >= 0 and var index && index < call.Arguments.Count)
                    {
                        (int start, int end) = call.Arguments[index];
                        int parameter = end - start == 1 ? IndexOfParameter(function, function.Tokens[start].Text) : -1;
                        if (parameter >= 0)
                        {
                            facts.GotoParameter = parameter;
                            changed = true;
                            break;
                        }
                    }
                }
            }
        }
    }

    private static int IndexOfParameter(CodeFunction function, string name)
    {
        for (int i = 0; i < function.Parameters.Count; i++)
        {
            if (function.Parameters[i].Name == name)
            {
                return i;
            }
        }

        return -1;
    }

    private string? FindInitial(HashSet<string> constants, HashSet<string> variables)
    {
        foreach (CodeFile file in _scan.Files)
        {
            List<CodeToken> t = file.Tokens;
            for (int i = 0; i + 2 < t.Count; i++)
            {
                if (t[i].Kind == TokenKind.Identifier && variables.Contains(t[i].Text))
                {
                    if ((t[i + 1].Is("(") || t[i + 1].Is("=")) && constants.Contains(t[i + 2].Text))
                    {
                        _initial = Span(t[i + 2]);
                        return t[i + 2].Text;
                    }
                }
            }
        }

        return null;
    }

    // ---- transitions -------------------------------------------------------------------------------

    private (Dictionary<string, List<ScreenTransition>>, List<ScreenTransition>, string?) FindTransitions(HashSet<string> constants, HashSet<string> variables)
    {
        var bySource = new Dictionary<string, List<ScreenTransition>>(StringComparer.Ordinal);
        var anywhere = new List<ScreenTransition>();
        string? start = null;
        foreach ((CodeFunction function, FunctionFacts facts) in _facts)
        {
            foreach ((CodeToken target, IReadOnlyList<Condition> conditions) in Sites(function, facts, constants, variables))
            {
                List<(IReadOnlyList<Condition> Chain, CodeFunction Root)> chains = Chains(function, 0, []);
                foreach ((IReadOnlyList<Condition> chain, CodeFunction root) in chains)
                {
                    List<Condition> all = [.. conditions, .. chain];
                    string[] sources = Sources(all, constants, variables);
                    ScreenTransition transition = Transition(target, all, variables);

                    // The token is this one transition's only if nothing else goes through it.
                    transition = chains.Count > 1 || sources.Length > 1
                        ? transition with { TargetSpan = null }
                        : WithItsStatement(transition, target, all);

                    if (sources.Length == 0)
                    {
                        if (IsStartup(root) || all.Any(c => StartupLike().IsMatch(c.Text)))
                        {
                            start ??= target.Text;
                        }
                        else if (!anywhere.Contains(transition))
                        {
                            anywhere.Add(transition);
                        }

                        continue;
                    }

                    foreach (string source in sources)
                    {
                        if (!bySource.TryGetValue(source, out List<ScreenTransition>? list))
                        {
                            bySource[source] = list = [];
                        }

                        if (!list.Contains(transition))
                        {
                            list.Add(transition);
                        }
                    }
                }
            }
        }

        return (bySource, anywhere, start);
    }

    private static bool IsStartup(CodeFunction root) =>
        root.Name is "setup" or "begin" or "init" || root.Name == root.Class;

    private List<(CodeToken Target, IReadOnlyList<Condition> Conditions)> Sites(CodeFunction function, FunctionFacts facts, HashSet<string> constants, HashSet<string> variables)
    {
        var sites = new List<(CodeToken, IReadOnlyList<Condition>)>();
        foreach (AssignFact assign in facts.Assigns)
        {
            if (variables.Contains(assign.Variable) && assign.End - assign.Start == 1 && constants.Contains(function.Tokens[assign.Start].Text))
            {
                sites.Add((function.Tokens[assign.Start], assign.Conditions));
            }
        }

        foreach (CallFact call in facts.Calls)
        {
            int index = variables.Contains(call.Receiver) ? 0 : Callee(call, function) is { } callee ? _facts[callee].GotoParameter : -1;
            if (index >= 0 && index < call.Arguments.Count)
            {
                (int start, int end) = call.Arguments[index];
                if (end - start >= 1 && constants.Contains(function.Tokens[end - 1].Text))
                {
                    sites.Add((function.Tokens[end - 1], call.Conditions));
                }
            }
        }

        return sites;
    }

    // Every way execution reaches a function, each with the conditions on the way. A function nobody calls is a root.
    private List<(IReadOnlyList<Condition> Chain, CodeFunction Root)> Chains(CodeFunction function, int depth, HashSet<CodeFunction> seen)
    {
        var callers = new List<(CodeFunction Caller, IReadOnlyList<Condition> Conditions)>();
        if (depth < CallDepth && seen.Add(function))
        {
            foreach ((CodeFunction caller, FunctionFacts facts) in _facts)
            {
                foreach (CallFact call in facts.Calls)
                {
                    if (call.Name == function.Name && Callee(call, caller) == function)
                    {
                        callers.Add((caller, call.Conditions));
                    }
                }
            }
        }

        if (callers.Count == 0)
        {
            return [([], function)];
        }

        var chains = new List<(IReadOnlyList<Condition>, CodeFunction)>();
        foreach ((CodeFunction caller, IReadOnlyList<Condition> conditions) in callers)
        {
            foreach ((IReadOnlyList<Condition> chain, CodeFunction root) in Chains(caller, depth + 1, [.. seen]))
            {
                chains.Add(([.. conditions, .. chain], root));
                if (chains.Count >= MaxChains)
                {
                    return chains;
                }
            }
        }

        return chains;
    }

    // What the conditions on the screen variable leave, or nothing when none of them names a screen:
    // "not A" alone, from an early return, is still any screen.
    private static string[] Sources(List<Condition> conditions, HashSet<string> constants, HashSet<string> variables)
    {
        HashSet<string>? left = null;
        foreach (Condition condition in conditions.Where(c => variables.Contains(c.Variable) && (c.Values.Count > 0 || c.Text == "default")))
        {
            IEnumerable<string> here = condition.IsDefault ? constants.Where(c => !condition.Excluded.Contains(c)) : condition.Values.Select(v => v.Text).Where(constants.Contains);
            left = left is null ? new HashSet<string>(here, StringComparer.Ordinal) : [.. left.Intersect(here)];
        }

        if (left is null)
        {
            return [];
        }

        foreach (Condition ruledOut in conditions.Where(c => variables.Contains(c.Variable) && c.Text.StartsWith('!')))
        {
            left.ExceptWith(ruledOut.Excluded);
        }

        return [.. left];
    }

    private ScreenTransition Transition(CodeToken target, List<Condition> conditions, HashSet<string> variables)
    {
        CodeFile file = _scan.Files[target.File];
        return new ScreenTransition
        {
            Target = target.Text,
            Trigger = TriggerFrom(conditions, variables),
            TargetSpan = Span(target),
            Location = new SourceLocation(file.Path, file.LineOf(target.Offset)),
        };
    }

    private ScreenTrigger TriggerFrom(List<Condition> conditions, HashSet<string> variables)
    {
        CodeToken? key = null;
        int item = -1;
        string when = "";
        foreach (Condition condition in conditions)
        {
            if (variables.Contains(condition.Variable))
            {
                continue;
            }

            if (condition.Variable.Length == 0)
            {
                // if (key) handleKey(key): that there is a key at all says nothing.
                if (when.Length == 0 && condition.Text != "else" && !KeyLike().IsMatch(condition.Text))
                {
                    when = condition.Text;
                }

                continue;
            }

            if (condition.Values.Count == 0)
            {
                continue;
            }

            CodeToken value = condition.Values[0];
            if (key is null && (KeyLike().IsMatch(condition.Variable) || KeyValue(value.Text) is not null || value.Kind == TokenKind.Char))
            {
                key = value;
            }
            else if (item < 0 && value.Kind == TokenKind.Number && ItemLike().IsMatch(condition.Variable))
            {
                item = ParseInteger(value.Text) ?? -1;
            }
            else if (when.Length == 0)
            {
                when = condition.Text;
            }
        }

        return key is { } found
            ? new ScreenTrigger(TriggerKind.Key, Value: KeyValue(found.Text) ?? (found.Kind == TokenKind.Char ? SketchScanner.Decode(found) : found.Text), Name: found.Kind == TokenKind.Identifier ? found.Text : "", MenuItem: item, When: when)
            : new ScreenTrigger(TriggerKind.Code, Value: when, MenuItem: item);
    }

    private string? KeyValue(string text) =>
        _scan.Macros.TryGetValue(text, out string? value) && value.Length >= 3 && value[0] == '\'' ? value[1..^1] : null;

    private SourceSpan Span(CodeToken token) => new(_scan.Files[token.File].Path, token.Offset, token.Text.Length, token.Text);

    // ---- what the constructor needs to add and remove code ---------------------------------------------

    private static int IndexOf(List<CodeToken> tokens, CodeToken token)
    {
        int low = 0;
        int high = tokens.Count - 1;
        while (low <= high)
        {
            int middle = (low + high) / 2;
            if (tokens[middle].Offset == token.Offset)
            {
                return middle;
            }

            if (tokens[middle].Offset < token.Offset)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return -1;
    }

    // The statement a token is in: the index of its first token and of its semicolon.
    private static (int Start, int End)? StatementAround(List<CodeToken> t, int index)
    {
        int nested = 0;
        int start = index;
        while (start > 0)
        {
            CodeToken before = t[start - 1];

            // A close paren ends what comes before a statement only if a statement can start after it: if (...) go(X);
            bool endsCondition = before.Is(")") && t[start].Kind == TokenKind.Identifier;
            if (nested == 0 && (before.Is(";") || before.Is("{") || before.Is("}") || before.Is(":") || before.Is("else") || endsCondition))
            {
                break;
            }

            nested = Math.Max(0, nested + (before.Is(")") ? 1 : before.Is("(") ? -1 : 0));
            start--;
        }

        int end = index;
        nested = 0;
        while (end < t.Count)
        {
            nested += t[end].Is("(") ? 1 : t[end].Is(")") ? -1 : 0;
            if (nested <= 0 && t[end].Is(";"))
            {
                return (start, end);
            }

            if (t[end].Is("{") || t[end].Is("}"))
            {
                return null;
            }

            end++;
        }

        return null;
    }

    private static bool IsIf(List<CodeToken> t, int closeParen, out int ifIndex)
    {
        ifIndex = -1;
        if (closeParen <= 0 || !t[closeParen].Is(")"))
        {
            return false;
        }

        int nested = 0;
        for (int k = closeParen; k >= 0; k--)
        {
            nested += t[k].Is(")") ? 1 : t[k].Is("(") ? -1 : 0;
            if (nested == 0)
            {
                ifIndex = k - 1;
                return ifIndex >= 0 && t[ifIndex].Is("if") && (ifIndex == 0 || !t[ifIndex - 1].Is("else"));
            }
        }

        return false;
    }

    // A transition's statement, and the if it alone makes up: that is what removing it takes away, and
    // what makes the key in that if its own to change.
    private ScreenTransition WithItsStatement(ScreenTransition transition, CodeToken target, List<Condition> conditions)
    {
        CodeFile file = _scan.Files[target.File];
        List<CodeToken> t = file.Tokens;
        int index = IndexOf(t, target);
        if (index < 0 || StatementAround(t, index) is not { } statement)
        {
            return transition;
        }

        (int start, int end) = statement;
        CodeToken before = start > 0 ? t[start - 1] : default;
        int removeStart = start;
        int removeEnd = end;
        int ifIndex = -1;
        if (start > 0 && before.Is(")") && IsIf(t, start - 1, out ifIndex))
        {
            removeStart = ifIndex;
        }
        else if (start > 0 && before.Is("{"))
        {
            int close = SketchScanner.Matching(t, start - 1, "{", "}");
            int after = end + 1;
            if (close >= t.Count || after + 1 >= t.Count)
            {
                return transition;
            }

            bool onlyThis = after == close || ((t[after].Is("return") || t[after].Is("break")) && t[after + 1].Is(";") && after + 2 == close);
            if (onlyThis && start >= 2 && IsIf(t, start - 2, out ifIndex))
            {
                removeStart = ifIndex;
                removeEnd = close;
            }
        }
        else if (before.Is("else") || before.Is(")"))
        {
            return transition;
        }

        // What an else hangs on stays.
        if (removeEnd + 1 < t.Count && t[removeEnd + 1].Is("else"))
        {
            return transition;
        }

        SourceSpan removal = StatementSpan(file, t[removeStart].Offset, t[removeEnd].Offset + t[removeEnd].Text.Length);
        SourceSpan? key = null;
        if (ifIndex >= 0)
        {
            int conditionEnd = SketchScanner.Matching(t, ifIndex + 1, "(", ")");
            foreach (CodeToken value in conditions.Where(c => c.Variable.Length > 0 && KeyLike().IsMatch(c.Variable)).SelectMany(c => c.Values))
            {
                int at = IndexOf(t, value);
                if (at > ifIndex && at < conditionEnd && value.File == target.File)
                {
                    key = Span(value);
                }
            }
        }

        return transition with { RemoveSpans = [removal], KeySpan = key };
    }

    // The characters of a statement, with the line it leaves empty if it had the line to itself.
    private static SourceSpan StatementSpan(CodeFile file, int start, int end)
    {
        string text = file.Text;
        int after = end;
        while (after < text.Length && text[after] is ' ' or '\t')
        {
            after++;
        }

        int lineStart = start;
        while (lineStart > 0 && text[lineStart - 1] is ' ' or '\t')
        {
            lineStart--;
        }

        bool startsLine = lineStart == 0 || text[lineStart - 1] == '\n';
        bool endsLine = after >= text.Length || text[after] is '\r' or '\n';
        if (startsLine && endsLine)
        {
            start = lineStart;
            end = after;
            if (end < text.Length && text[end] == '\r')
            {
                end++;
            }

            if (end < text.Length && text[end] == '\n')
            {
                end++;
            }
        }
        else
        {
            end = after;
        }

        return new SourceSpan(file.Path, start, end - start, text[start..end]);
    }

    // Before a token: on a line of its own when the token starts one, inside the line otherwise.
    private static SourceInsert InsertBefore(CodeFile file, CodeToken token, string extraIndent = "")
    {
        string text = file.Text;
        int lineStart = token.Offset;
        while (lineStart > 0 && text[lineStart - 1] != '\n')
        {
            lineStart--;
        }

        string lead = text[lineStart..token.Offset];
        return string.IsNullOrWhiteSpace(lead)
            ? new SourceInsert(file.Path, lineStart, lead + extraIndent, Inline: false)
            : new SourceInsert(file.Path, token.Offset, "", Inline: true);
    }

    // After a token, as the first thing on the line below it when nothing follows it on its own line.
    private static SourceInsert InsertAfter(CodeFile file, CodeToken token, List<CodeToken> t, int index)
    {
        string text = file.Text;
        int end = token.Offset + token.Text.Length;
        int next = end;
        while (next < text.Length && text[next] is ' ' or '\t' or '\r')
        {
            next++;
        }

        if (next >= text.Length || text[next] != '\n')
        {
            return new SourceInsert(file.Path, end + (end < text.Length && text[end] == ' ' ? 1 : 0), "", Inline: true);
        }

        string indent = "  ";
        if (index + 1 < t.Count)
        {
            int lineStart = t[index + 1].Offset;
            while (lineStart > 0 && text[lineStart - 1] != '\n')
            {
                lineStart--;
            }

            string lead = text[lineStart..t[index + 1].Offset];
            indent = string.IsNullOrWhiteSpace(lead) ? lead : indent;
            if (t[index + 1].Is("}"))
            {
                indent += "  ";
            }
        }

        return new SourceInsert(file.Path, next + 1, indent, Inline: false);
    }

    private static string Accessor(CodeFunction function, CallFact call) =>
        call.Receiver + (call.NameIndex > 0 && function.Tokens[call.NameIndex - 1].Is("->") ? "->" : ".");

    // Whom a function's own drawing goes to, or else the sketch's own display: a class of its own before the panel behind it.
    private string ReceiverIn(CodeFunction? function)
    {
        if (function is not null && _facts.TryGetValue(function, out FunctionFacts? facts) && facts.Calls.FirstOrDefault(c => IsDrawCall(c, function)) is { } call)
        {
            return Accessor(function, call);
        }

        if (function?.Parameters.FirstOrDefault(p => IsSurfaceParameter(function, p.Name)) is { } parameter)
        {
            return parameter.Name + (parameter.Type.Contains('*', StringComparison.Ordinal) ? "->" : ".");
        }

        string? any = _displayNames.FirstOrDefault(n => !_scan.Displays.Any(d => d.Name == n)) ?? _scan.Displays.FirstOrDefault()?.Name;
        return any is null ? "" : any + ".";
    }

    private int CallCount(CodeFunction function) =>
        _facts.Sum(f => f.Value.Calls.Count(c => c.Name == function.Name && Callee(c, f.Key) == function));

    // A new element goes where the screen's own drawing ends: at the end of the function that is its
    // alone (before what sends a buffer to the display), or in its case, before the break.
    private (SourceInsert? Insert, string Receiver) DrawSiteIn(CodeFunction function, int start, int end)
    {
        CodeFile file = _scan.Files[function.Tokens[function.NameIndex].File];
        IReadOnlyList<CodeToken> t = function.Tokens;
        if (start >= t.Count)
        {
            return (null, "");
        }

        List<CallFact> calls = CallsIn(t, start, end, []);
        if (calls.Count == 1 && calls[0].Receiver.Length == 0 && Callee(calls[0], function) is { } callee && _facts[callee].Draws && CallCount(callee) == 1)
        {
            return EndOf(callee);
        }

        for (int k = end - 1; k >= start; k--)
        {
            if (t[k].Is("break"))
            {
                return (InsertBefore(file, t[k]), ReceiverIn(function));
            }
        }

        return end < t.Count ? (InsertBefore(file, t[end]), ReceiverIn(function)) : (null, "");
    }

    private (SourceInsert? Insert, string Receiver) EndOf(CodeFunction function)
    {
        IReadOnlyList<CodeToken> t = function.Tokens;
        if (function.BodyEnd >= t.Count)
        {
            return (null, "");
        }

        CodeToken close = t[function.BodyEnd];
        CodeFile file = _scan.Files[close.File];

        // lcd.update(); oled.display(); at the very end: new drawing goes before it.
        for (int k = function.BodyEnd - 1; k > function.BodyStart + 2 && k >= function.BodyEnd - 8; k--)
        {
            if (t[k].Kind == TokenKind.Identifier && t[k].Text is "display" or "sendBuffer" or "update" && k + 1 < t.Count && t[k + 1].Is("(") && k >= 2 && (t[k - 1].Is(".") || t[k - 1].Is("->")))
            {
                return (InsertBefore(file, t[k - 2]), ReceiverIn(function));
            }
        }

        return (InsertBefore(file, close, "  "), ReceiverIn(function));
    }

    private ScreenCodeShape ShapeOf(IReadOnlyList<CodeToken> constants, HashSet<string> variables, Dictionary<string, List<ScreenTransition>> bySource, List<ScreenTransition> anywhere)
    {
        var keyNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ScreenTrigger trigger in bySource.Values.SelectMany(l => l).Concat(anywhere).Select(t => t.Trigger).Where(t => t is { Kind: TriggerKind.Key, Name.Length: > 0 }))
        {
            keyNames[trigger.Value] = trigger.Name;
        }

        (SourceInsert? constant, string format) = NewConstantIn(constants);
        (CodeFunction? handler, string keyVariable) = FindKeyHandler();
        string expression = _facts.SelectMany(f => f.Value.Switches.Where(s => variables.Contains(s.Variable)).Select(s => (f.Key, s.Expression)))
            .OrderByDescending(p => p.Key == handler).Select(p => p.Expression).FirstOrDefault() ?? variables.First();
        string gotoFormat = FindGoto(constants, variables, handler);
        return new ScreenCodeShape
        {
            NewConstant = constant,
            ConstantFormat = format,
            NewDrawCase = _drawSwitch is { } draw ? NewCaseIn(draw.Function, draw.Switch) : null,
            Receiver = ReceiverIn(_drawSwitch?.Function),
            NewTransition = handler is null || gotoFormat.Length == 0 ? null : TransitionPlaceIn(handler, keyVariable),
            KeyVariable = keyVariable,
            StateExpression = expression,
            GotoFormat = gotoFormat,
            StartSpan = _initial,
            CanReturn = handler is not null && handler.NameIndex > 0 && handler.Tokens[handler.NameIndex - (handler.Tokens[handler.NameIndex - 1].Is("::") ? 3 : 1)].Is("void"),
            KeyNames = keyNames,
        };
    }

    private (SourceInsert?, string) NewConstantIn(IReadOnlyList<CodeToken> constants)
    {
        CodeToken last = constants[^1];
        CodeFile file = _scan.Files[last.File];
        List<CodeToken> t = file.Tokens;
        int index = IndexOf(t, last);
        if (index < 0 || !_scan.Enums.Any(e => e.Constants.Contains(last)))
        {
            return (null, "");
        }

        // A last constant that counts the others stays last.
        if (CountLike().IsMatch(last.Text))
        {
            return (InsertBefore(file, last) with { Inline = true, Offset = last.Offset, Indent = "" }, "{0}, ");
        }

        int close = index;
        while (close < t.Count && !t[close].Is("}"))
        {
            close++;
        }

        if (close >= t.Count)
        {
            return (null, "");
        }

        if (t[close - 1].Is(","))
        {
            return InsertBefore(file, t[close], "  ") is { Inline: false } own ? (own, "{0},\n") : (new SourceInsert(file.Path, t[close].Offset, "", true), "{0}, ");
        }

        CodeToken end = t[close - 1];
        return (new SourceInsert(file.Path, end.Offset + end.Text.Length, "", true), ", {0}");
    }

    private SourceInsert? NewCaseIn(CodeFunction function, SwitchFact draw)
    {
        IReadOnlyList<CodeToken> t = function.Tokens;
        if (draw.BodyEnd >= t.Count)
        {
            return null;
        }

        CodeFile file = _scan.Files[t[draw.BodyEnd].File];
        for (int k = draw.BodyEnd - 1; k > 0 && draw.Cases.Count > 0 && k >= draw.Cases[0].Start; k--)
        {
            if (t[k].Is("default"))
            {
                return InsertBefore(file, t[k]);
            }
        }

        return InsertBefore(file, t[draw.BodyEnd], "  ");
    }

    // The function with the most comparisons of a key: what handles keys, and its variable.
    private (CodeFunction?, string) FindKeyHandler()
    {
        (CodeFunction? Function, string Variable, int Count) best = (null, "", 0);
        foreach ((CodeFunction function, FunctionFacts facts) in _facts)
        {
            foreach (IGrouping<string, Condition> keys in facts.Calls.SelectMany(c => c.Conditions).Concat(facts.Assigns.SelectMany(a => a.Conditions))
                .Where(c => c.Variable.Length > 0 && c.Values.Count > 0 && KeyLike().IsMatch(c.Variable)).GroupBy(c => c.Variable, StringComparer.Ordinal))
            {
                if (keys.Count() > best.Count)
                {
                    best = (function, keys.Key, keys.Count());
                }
            }
        }

        return (best.Function, best.Variable);
    }

    private string FindGoto(IReadOnlyList<CodeToken> constants, HashSet<string> variables, CodeFunction? handler)
    {
        var names = new HashSet<string>(constants.Select(c => c.Text), StringComparer.Ordinal);
        foreach ((CodeFunction function, FunctionFacts facts) in _facts.OrderByDescending(f => f.Key == handler).ThenByDescending(f => f.Key.Class == handler?.Class).ThenBy(f => IsStartup(f.Key) ? 1 : 0))
        {
            foreach ((CodeToken target, IReadOnlyList<Condition> _) in Sites(function, facts, names, variables))
            {
                CodeFile file = _scan.Files[target.File];
                int index = IndexOf(file.Tokens, target);
                if (index >= 0 && StatementAround(file.Tokens, index) is { } statement && !IsStartup(function))
                {
                    int start = file.Tokens[statement.Start].Offset;
                    int end = file.Tokens[statement.End].Offset + 1;
                    return string.Concat(file.Text.AsSpan(start, target.Offset - start), "{0}", file.Text.AsSpan(target.Offset + target.Text.Length, end - target.Offset - target.Text.Length));
                }
            }
        }

        return "";
    }

    // At the start of the handler: where the key is known, ahead of what the handler does with it.
    private SourceInsert? TransitionPlaceIn(CodeFunction handler, string keyVariable)
    {
        IReadOnlyList<CodeToken> t = handler.Tokens;
        if (t is not List<CodeToken> list)
        {
            return null;
        }

        CodeFile file = _scan.Files[t[handler.BodyStart - 1].File];
        if (handler.Parameters.Any(p => p.Name == keyVariable))
        {
            return InsertAfter(file, t[handler.BodyStart - 1], list, handler.BodyStart - 1);
        }

        for (int k = handler.BodyStart; k + 1 < handler.BodyEnd; k++)
        {
            if (t[k].Is(keyVariable) && t[k + 1].Is("=") && k > handler.BodyStart && t[k - 1].Kind == TokenKind.Identifier)
            {
                int semicolon = k;
                while (semicolon < handler.BodyEnd && !t[semicolon].Is(";"))
                {
                    semicolon++;
                }

                return InsertAfter(file, t[semicolon], list, semicolon);
            }
        }

        return null;
    }

    private List<SourceSpan> UsesOf(string constant) =>
        [.. _scan.Files.SelectMany(f => f.Tokens.Where(t => t.Is(constant)).Select(Span))];

    // What takes a screen out: its constant, its cases, what leads to it. If anything else names it, nothing.
    private List<SourceSpan> RemovalOf(string id, HashSet<string> variables, Dictionary<string, List<ScreenTransition>> bySource, List<ScreenTransition> anywhere)
    {
        var removal = new List<SourceSpan>();
        var covered = new List<(string File, int Start, int End)>();
        void Cover(SourceSpan span)
        {
            removal.Add(span);
            covered.Add((span.File, span.Offset, span.Offset + span.Length));
        }

        foreach (CodeEnum e in _scan.Enums.Where(e => e.Constants.Any(c => c.Text == id)))
        {
            CodeToken constant = e.Constants.First(c => c.Text == id);
            CodeFile file = _scan.Files[constant.File];
            int at = IndexOf(file.Tokens, constant);
            if (at < 0)
            {
                return [];
            }

            int after = at + 1;
            while (after < file.Tokens.Count && !file.Tokens[after].Is(",") && !file.Tokens[after].Is("}"))
            {
                after++;
            }

            if (after >= file.Tokens.Count)
            {
                return [];
            }

            bool hasComma = file.Tokens[after].Is(",");
            int end = hasComma ? file.Tokens[after].Offset + 1 : file.Tokens[after - 1].Offset + file.Tokens[after - 1].Text.Length;
            int start = constant.Offset;

            // The last one takes the comma before it, and leaves what follows it.
            if (!hasComma && at > 0 && file.Tokens[at - 1].Is(","))
            {
                start = file.Tokens[at - 1].Offset;
            }

            while (hasComma && end < file.Text.Length && file.Text[end] == ' ')
            {
                end++;
            }

            Cover(new SourceSpan(file.Path, start, end - start, file.Text[start..end]));
        }

        foreach ((CodeFunction function, FunctionFacts facts) in _facts)
        {
            foreach (SwitchFact s in facts.Switches.Where(s => variables.Contains(s.Variable)))
            {
                foreach ((IReadOnlyList<CodeToken> labels, bool _, int start, int end) in s.Cases.Where(c => c.Labels.Any(l => l.Text == id)))
                {
                    CodeToken label = labels.First(l => l.Text == id);
                    CodeFile file = _scan.Files[label.File];
                    int at = IndexOf(file.Tokens, label);
                    if (at < 1 || !file.Tokens[at - 1].Is("case") || labels.Count > 1)
                    {
                        return [];
                    }

                    if (Math.Min(end, s.BodyEnd) >= function.Tokens.Count)
                    {
                        return [];
                    }

                    CodeToken next = function.Tokens[Math.Min(end, s.BodyEnd)];
                    Cover(StatementSpan(file, file.Tokens[at - 1].Offset, TrimmedEnd(file, next.Offset)));
                }
            }
        }

        foreach (ScreenTransition transition in bySource.Values.SelectMany(l => l).Concat(anywhere).Where(t => t.Target == id))
        {
            foreach (SourceSpan span in transition.RemoveSpans)
            {
                Cover(span);
            }
        }

        // Anything else that names it would no longer compile.
        foreach (SourceSpan use in UsesOf(id))
        {
            if (!covered.Any(c => c.File == use.File && c.Start <= use.Offset && use.Offset + use.Length <= c.End))
            {
                return [];
            }
        }

        // Spans inside others go with them.
        return [.. removal.Where(r => !covered.Any(c => c.File == r.File && c.Start <= r.Offset && r.Offset + r.Length <= c.End && (c.Start != r.Offset || c.End != r.Offset + r.Length))).Distinct()];
    }

    private static int TrimmedEnd(CodeFile file, int offset)
    {
        while (offset > 0 && char.IsWhiteSpace(file.Text[offset - 1]))
        {
            offset--;
        }

        return offset;
    }

    // ---- what each screen shows ---------------------------------------------------------------------

    private Dictionary<string, ScreenNode> DrawScreens(IReadOnlyList<CodeToken> constants, HashSet<string> variables)
    {
        // The switch whose cases draw is the screen's drawing; another over the same variable is its input.
        (CodeFunction Function, SwitchFact Switch)? draw = null;
        _drawSwitch = null;
        foreach ((CodeFunction function, FunctionFacts facts) in _facts)
        {
            foreach (SwitchFact s in facts.Switches.Where(s => variables.Contains(s.Variable)))
            {
                int drawing = s.Cases.Count(c => CaseDraws(function, c.Start, c.End));
                if (drawing * 2 >= s.Cases.Count && drawing > 0)
                {
                    draw = (function, s);
                }
            }
        }

        _drawSwitch = draw;
        var screens = new Dictionary<string, ScreenNode>(StringComparer.Ordinal);
        foreach (string id in constants.Select(c => c.Text).Distinct(StringComparer.Ordinal))
        {
            screens[id] = Draw(id, constants.First(c => c.Text == id), draw);
        }

        return screens;
    }

    private bool CaseDraws(CodeFunction function, int start, int end) =>
        CallsIn(function.Tokens, start, end, []).Any(c => IsDrawCall(c, function) || (Callee(c, function) is { } callee && _facts[callee].Draws) || c.Receiver == "?");

    private ScreenNode Draw(string id, CodeToken declaration, (CodeFunction Function, SwitchFact Switch)? draw)
    {
        var painter = new Painter(this, _display);
        CodeFile declaredIn = _scan.Files[declaration.File];
        var location = new SourceLocation(declaredIn.Path, declaredIn.LineOf(declaration.Offset));
        bool found = false;
        (SourceInsert? Insert, string Receiver) site = (null, "");
        if (draw is { } drawing)
        {
            foreach ((IReadOnlyList<CodeToken> labels, bool _, int start, int end) in drawing.Switch.Cases)
            {
                if (labels.Any(l => l.Text == id))
                {
                    painter.Paint(drawing.Function, start, end);
                    CodeFile file = _scan.Files[drawing.Function.Tokens[start].File];
                    location = new SourceLocation(file.Path, file.LineOf(drawing.Function.Tokens[start].Offset));
                    found = true;
                    site = DrawSiteIn(drawing.Function, start, end);
                }
            }
        }

        // Named after the screen: drawHome() for HOME, without a switch to say so.
        if (!found && NamedAfter(id) is { } named)
        {
            painter.Paint(named, named.BodyStart, named.BodyEnd);
            found = true;
            location = LocationOf(named);
            site = EndOf(named);
        }

        // An application that draws itself: ALARM_APP, AlarmApp::render.
        if (painter.CallsOutside && RendererFor(id) is { } renderer)
        {
            painter.Paint(renderer, renderer.BodyStart, renderer.BodyEnd);
            painter.CallsOutside = false;
            location = LocationOf(renderer);
            site = EndOf(renderer);
        }

        return new ScreenNode
        {
            Id = id,
            Title = id,
            Elements = painter.Elements,
            Menu = painter.Menu,
            DrawnElsewhere = painter.CallsOutside || painter.Elements.Count == 0 && painter.Menu is null,
            DrawInsert = site.Insert,
            Receiver = site.Receiver,
            Location = location,
        };
    }

    private SourceLocation LocationOf(CodeFunction function)
    {
        CodeToken name = function.Tokens[function.NameIndex];
        CodeFile file = _scan.Files[name.File];
        return new SourceLocation(file.Path, file.LineOf(name.Offset));
    }

    private CodeFunction? NamedAfter(string id)
    {
        string wanted = Letters(id);
        return _facts.Keys.FirstOrDefault(f => _facts[f].Draws && DrawnFunction().Replace(f.Name, "").Equals(id, StringComparison.OrdinalIgnoreCase))
            ?? _facts.Keys.FirstOrDefault(f => _facts[f].Draws && Letters(f.Name).Contains(wanted, StringComparison.Ordinal) && wanted.Length >= 3);
    }

    private CodeFunction? RendererFor(string id)
    {
        string wanted = Letters(id);
        return _facts.Keys.FirstOrDefault(f => f.Class.Length > 0 && f.Name is "render" or "draw" or "show" or "paint" && _facts[f].Draws &&
            (Letters(f.Class).Contains(wanted, StringComparison.Ordinal) || wanted.Contains(Letters(f.Class), StringComparison.Ordinal)));
    }

    private static string Letters(string name) => string.Concat(name.Where(char.IsLetterOrDigit)).ToUpperInvariant();

    /// <summary>Follows the drawing calls with a cursor, constants bound, helpers entered.</summary>
    private sealed class Painter(ScreenCodeAnalyzer owner, ScreenDisplay display)
    {
        private readonly List<int> _fromLoops = [];
        private int _x;
        private int _y;
        private int _size = 1;
        private int _depth;
        private int _next;
        private SourceSpan? _xSpan;
        private SourceSpan? _ySpan;
        private bool _placed = true;
        private int _titleY = -1;
        private bool _shared;

        private (int Start, int End)? _cursorTokens;

        public List<ScreenElement> Elements { get; } = [];

        public ScreenMenu? Menu { get; private set; }

        /// <summary>It called something like <c>active()->render()</c>, which cannot be followed by name.</summary>
        public bool CallsOutside { get; set; }

        public void Paint(CodeFunction function, int start, int end) => Enter(function, start, end, new Dictionary<string, Bound>(StringComparer.Ordinal));

        private void Enter(CodeFunction function, int start, int end, Dictionary<string, Bound> bindings)
        {
            if (_depth >= CallDepth)
            {
                return;
            }

            // What a helper several screens use draws cannot be removed for one of them.
            bool wasShared = _shared;
            _shared |= _depth > 0 && owner.CallCount(function) > 1;
            _depth++;
            new PaintWalker(this, function, bindings).Walk(start, end);
            _depth--;
            _shared = wasShared;
        }

        /// <summary>A parameter's argument: tokens, in the caller's function, with the caller's bindings.</summary>
        private sealed record Bound(IReadOnlyList<CodeToken> Tokens, int Start, int End, Dictionary<string, Bound> Bindings);

        private sealed class PaintWalker(Painter painter, CodeFunction function, Dictionary<string, Bound> bindings) : BodyWalker(function.Tokens)
        {
            private readonly Stack<(int X, int Y)> _branch = new();
            private readonly Stack<(int X, int Y)> _afterThen = new();
            private int _conditional;
            private int _elses;

            protected override void BranchStart()
            {
                _branch.Push((painter._x, painter._y));
                _conditional++;
            }

            protected override void BranchElse()
            {
                _afterThen.Push((painter._x, painter._y));
                (painter._x, painter._y) = _branch.Peek();
                _elses++;
            }

            protected override void BranchEnd(bool hadElse)
            {
                (int x, int y) = _branch.Pop();
                _conditional--;
                if (hadElse)
                {
                    (int thenX, int _) = _afterThen.Pop();
                    painter._x = Math.Max(painter._x, thenX);
                    _elses--;
                }
                else
                {
                    (painter._x, painter._y) = (x, y);
                }
            }

            protected override void Simple(int from, int to)
            {
                int k = from;
                while (k < to - 1)
                {
                    if (Tokens[k].Kind != TokenKind.Identifier || !Tokens[k + 1].Is("("))
                    {
                        k++;
                        continue;
                    }

                    int close = Math.Min(SketchScanner.Matching(Tokens, k + 1, "(", ")"), to);
                    string receiver = ReceiverOf(Tokens, k, from);
                    string name = Tokens[k].Text;
                    List<(int Start, int End)> arguments = ArgumentRanges(Tokens, k + 2, close);
                    if (receiver.Length > 0 && IsDisplay(receiver) && DrawingCalls.Contains(name))
                    {
                        Display(name, arguments, k, close);
                        k = close + 1;
                    }
                    else if (receiver.Length == 0 && Analyzer.Callee(new CallFact(name, "", k, k + 1, close, arguments, []), function) is { } callee && Analyzer._facts[callee].Draws)
                    {
                        var inner = new Dictionary<string, Bound>(StringComparer.Ordinal);
                        for (int i = 0; i < callee.Parameters.Count && i < arguments.Count; i++)
                        {
                            inner[callee.Parameters[i].Name] = new Bound(Tokens, arguments[i].Start, arguments[i].End, bindings);
                        }

                        painter.Enter(callee, callee.BodyStart, callee.BodyEnd, inner);
                        k = close + 1;
                    }
                    else if (receiver.Length > 0 && name is "render" or "draw" or "show" or "paint")
                    {
                        painter.CallsOutside = true;
                        k = close + 1;
                    }
                    else
                    {
                        k++;
                    }
                }
            }

            private ScreenCodeAnalyzer Analyzer => painter.Owner;

            private bool IsDisplay(string receiver)
            {
                return Analyzer._displayNames.Contains(receiver) || Analyzer.IsSurfaceParameter(function, receiver);
            }

            // The statement of a call, if it is nothing but the call and stands on its own: a branch's only
            // statement cannot go without its branch.
            private (int Start, int End)? OwnStatement(int nameIndex, int close)
            {
                int start = nameIndex >= 2 ? nameIndex - 2 : nameIndex;
                bool alone = close + 1 < Tokens.Count && Tokens[close + 1].Is(";") && start > 0 && (Tokens[start - 1].Is(";") || Tokens[start - 1].Is("{") || Tokens[start - 1].Is("}") || Tokens[start - 1].Is(":"));
                return alone && !painter._shared ? (start, close + 1) : null;
            }

            private IReadOnlyList<SourceSpan> RemovalOf((int Start, int End)? statement)
            {
                if (statement is not { } own)
                {
                    return [];
                }

                ScreenCodeAnalyzer analyzer = Analyzer;
                CodeFile file = analyzer._scan.Files[Tokens[own.Start].File];
                int start = Tokens[own.Start].Offset;
                int end = Tokens[own.End].Offset + 1;

                // Its setCursor goes with it, when nothing else was printed there.
                if (painter._cursorTokens is { } cursor)
                {
                    int cursorEnd = Tokens[cursor.End].Offset + 1;
                    if (cursor.End + 1 == own.Start)
                    {
                        start = Tokens[cursor.Start].Offset;
                    }
                    else
                    {
                        return [StatementSpan(file, Tokens[cursor.Start].Offset, cursorEnd), StatementSpan(file, start, end)];
                    }
                }

                return [StatementSpan(file, start, end)];
            }

            private void Display(string name, List<(int Start, int End)> a, int nameIndex, int close)
            {
                _call = (nameIndex, close);
                switch (name)
                {
                    case "setCursor" or "setCursorXY" when a.Count >= 2:
                        SetCursor(a[0], a[1]);
                        painter._cursorTokens = OwnStatement(nameIndex, close);
                        break;
                    case "print" or "println" or "write" or "drawUTF8" when a.Count >= 1:
                        Print(a[0]);
                        if (name == "println")
                        {
                            painter.NewLine();
                        }

                        break;
                    case "drawStr" when a.Count >= 3:
                        SetCursor(a[0], a[1]);
                        Print(a[2]);
                        break;
                    case "setTextSize" or "setScale" when a.Count >= 1:
                        painter._size = Evaluate(a[0]) ?? painter._size;
                        break;
                    case "home":
                        painter.MoveTo(0, 0);
                        break;
                    case "drawLine" or "line" when a.Count >= 4:
                        Shape(ElementKind.Line, a, corners: true);
                        break;
                    case "drawRect" or "drawFrame" when a.Count >= 4:
                        Shape(ElementKind.Frame, a, corners: false);
                        break;
                    case "fillRect" or "drawBox" when a.Count >= 4:
                        Shape(ElementKind.Box, a, corners: false);
                        break;
                    case "rect" when a.Count >= 4:
                        Shape(ElementKind.Frame, a, corners: true);
                        break;
                    default:
                        break;
                }
            }

            private void Shape(ElementKind kind, List<(int Start, int End)> a, bool corners)
            {
                int? x = Evaluate(a[0]);
                int? y = Evaluate(a[1]);
                int? c = Evaluate(a[2]);
                int? d = Evaluate(a[3]);
                if (x is null || y is null || c is null || d is null)
                {
                    return;
                }

                // A line and GyverOLED's rect name a second corner; the rest a width and height.
                bool second = corners && kind != ElementKind.Line;
                painter.Add(new ScreenElement
                {
                    Kind = kind,
                    X = x.Value,
                    Y = y.Value,
                    Width = second ? c.Value - x.Value : c.Value,
                    Height = second ? d.Value - y.Value : d.Value,
                    IsConditional = _conditional > 0,
                    XSpan = SpanOf(a[0]),
                    YSpan = SpanOf(a[1]),
                    WidthSpan = second ? null : SpanOf(a[2]),
                    HeightSpan = second ? null : SpanOf(a[3]),
                    RemoveSpans = RemovalOwn(),
                    Location = LocationAt(a[0].Start),
                }, Loops > 0);
            }

            private (int NameIndex, int Close) _call;

            // Conditional drawing, or a loop's, has more around it than its own statement.
            private IReadOnlyList<SourceSpan> RemovalOwn()
            {
                IReadOnlyList<SourceSpan> removal = _conditional > 0 || Loops > 0 ? [] : RemovalOf(OwnStatement(_call.NameIndex, _call.Close));
                painter._cursorTokens = null;
                return removal;
            }

            private void SetCursor((int Start, int End) x, (int Start, int End) y)
            {
                int? column = Evaluate(x);
                int? row = Evaluate(y);
                painter._placed = column is not null && row is not null;
                painter._x = column ?? painter._x;
                painter._y = row ?? painter._y;
                painter._xSpan = SpanOf(x);
                painter._ySpan = SpanOf(y);
            }

            private SourceLocation LocationAt(int index)
            {
                CodeToken token = Tokens[Math.Min(index, Tokens.Count - 1)];
                CodeFile file = Analyzer._scan.Files[token.File];
                return new SourceLocation(file.Path, file.LineOf(token.Offset));
            }

            // Exactly one token, here or at the call that bound it, is what an edit may replace.
            private SourceSpan? SpanOf((int Start, int End) range)
            {
                (IReadOnlyList<CodeToken> tokens, int start, int end, Dictionary<string, Bound> _) = Resolve(range);
                return end - start == 1 && tokens[start].Kind == TokenKind.Number ? Analyzer.Span(tokens[start]) : null;
            }

            private (IReadOnlyList<CodeToken> Tokens, int Start, int End, Dictionary<string, Bound> Bindings) Resolve((int Start, int End) range)
            {
                IReadOnlyList<CodeToken> tokens = Tokens;
                int start = range.Start;
                int end = range.End;
                Dictionary<string, Bound> scope = bindings;
                for (int hops = 0; hops < CallDepth && end - start == 1 && tokens[start].Kind == TokenKind.Identifier && scope.TryGetValue(tokens[start].Text, out Bound? bound); hops++)
                {
                    (tokens, start, end, scope) = (bound.Tokens, bound.Start, bound.End, bound.Bindings);
                }

                return (tokens, start, end, scope);
            }

            private int? Evaluate((int Start, int End) range)
            {
                (IReadOnlyList<CodeToken> tokens, int start, int end, Dictionary<string, Bound> scope) = Resolve(range);
                if (end - start == 1)
                {
                    return Analyzer.Number(tokens[start].Text);
                }

                // a + b, a - b, with both sides constant.
                if (end - start == 3 && tokens[start + 1].Text is "+" or "-")
                {
                    int? left = Analyzer.Number(tokens[start].Text);
                    int? right = Analyzer.Number(tokens[start + 2].Text);
                    return left is null || right is null ? null : tokens[start + 1].Text == "+" ? left + right : left - right;
                }

                return null;
            }

            private void Print((int Start, int End) range)
            {
                (IReadOnlyList<CodeToken> t, int start, int end, Dictionary<string, Bound> _) = Resolve(range);

                // F("..."), PSTR("..."), String("...") say the same as their argument.
                while (end - start >= 4 && t[start].Kind == TokenKind.Identifier && t[start].Text is "F" or "PSTR" or "String" or "FPSTR" && t[start + 1].Is("(") && SketchScanner.Matching(t, start + 1, "(", ")") == end - 1)
                {
                    start += 2;
                    end--;
                }

                string expression = string.Concat(t.Skip(start).Take(end - start).Select(x => x.Text));
                bool inLoop = Loops > 0;

                // What a menu's loop draws around its items (a cursor, a mark) belongs to the menu.
                if (inLoop && painter.Menu is not null)
                {
                    return;
                }

                bool alternative = _elses > 0;
                if (end - start == 1 && t[start].Kind is TokenKind.String or TokenKind.Char)
                {
                    string text = SketchScanner.Decode(t[start]);
                    if (!(inLoop && string.IsNullOrWhiteSpace(text)))
                    {
                        painter.AddText(text, Analyzer.Span(t[start]), _conditional > 0, inLoop, alternative || (_conditional > 0 && text.Length == 1), RemovalOwn());
                    }

                    return;
                }

                if (end - start == 1 && t[start].Kind == TokenKind.Number)
                {
                    painter.AddText(t[start].Text, null, _conditional > 0, inLoop, alternative, RemovalOwn());
                    return;
                }

                // items[i] in a loop: the array is what the menu shows.
                if (end - start >= 3 && t[start].Kind == TokenKind.Identifier && t[start + 1].Is("["))
                {
                    (IReadOnlyList<CodeToken> at, int arrayStart, int arrayEnd, Dictionary<string, Bound> _) = ResolveName(t[start].Text);
                    string array = arrayEnd - arrayStart == 1 ? at[arrayStart].Text : t[start].Text;
                    if (inLoop && Analyzer._scan.StringArrays.TryGetValue(array, out CodeStringArray? items))
                    {
                        painter.AddMenu(items, Analyzer);
                        return;
                    }
                }

                // cond ? "A" : "B" shows one of them: the first gives the preview, the longest the width.
                string[] literals = [.. t.Skip(start).Take(end - start).Where(x => x.Kind is TokenKind.String or TokenKind.Char).Select(SketchScanner.Decode)];
                SourceSpan? written = null;
                if (ReferenceEquals(t, Tokens) && end > start && literals.Length == 0)
                {
                    CodeFile file = Analyzer._scan.Files[t[start].File];
                    int from = t[start].Offset;
                    int to = t[end - 1].Offset + t[end - 1].Text.Length;
                    written = new SourceSpan(file.Path, from, to - from, file.Text[from..to]);
                }

                painter.AddValue(expression, literals, _conditional > 0, inLoop, alternative, RemovalOwn(), written);
            }

            private (IReadOnlyList<CodeToken>, int, int, Dictionary<string, Bound>) ResolveName(string name) =>
                bindings.TryGetValue(name, out Bound? bound) ? (bound.Tokens, bound.Start, bound.End, bound.Bindings) : (Tokens, 0, 0, bindings);
        }

        internal ScreenCodeAnalyzer Owner => owner;

        private void MoveTo(int x, int y)
        {
            _x = x;
            _y = y;
            _xSpan = _ySpan = null;
            _placed = true;
        }

        private void NewLine() => MoveTo(0, _y + (display.Family == DisplayFamily.CharLcd ? 1 : 8 * _size));

        private void Add(ScreenElement element, bool fromLoop)
        {
            if (fromLoop)
            {
                _fromLoops.Add(Elements.Count);
            }

            Elements.Add(element with { Id = "e" + (_next++).ToString(CultureInfo.InvariantCulture) });
        }

        private int Advance(int characters) => display.Family == DisplayFamily.CharLcd ? characters : characters * 6 * _size;

        private void AddText(string text, SourceSpan? span, bool conditional, bool fromLoop, bool alternative, IReadOnlyList<SourceSpan> removal)
        {
            if (Menu is null && !fromLoop)
            {
                _titleY = Math.Max(_titleY, _y);
            }

            Add(new ScreenElement { Kind = ElementKind.Text, X = _x, Y = _y, Text = text, Size = _size, TextSpan = span, XSpan = _xSpan, YSpan = _ySpan, IsConditional = conditional, IsAlternative = alternative, IsPlaced = _placed, RemoveSpans = removal, Location = span is null ? null : new SourceLocation(span.File, owner._scan.Files.First(f => f.Path == span.File).LineOf(span.Offset)) }, fromLoop);
            _x += Advance(text.Length);
            _xSpan = _ySpan = null;
        }

        private void AddValue(string expression, string[] literals, bool conditional, bool fromLoop, bool alternative, IReadOnlyList<SourceSpan> removal, SourceSpan? written)
        {
            int width = literals.Length > 0 ? literals.Max(l => l.Length) : Math.Clamp(expression.Length, 2, 4);
            Add(new ScreenElement { Kind = ElementKind.Value, X = _x, Y = _y, Text = expression, Width = width, Size = _size, Sample = literals.FirstOrDefault(), XSpan = _xSpan, YSpan = _ySpan, IsConditional = conditional, IsAlternative = alternative, IsPlaced = _placed, RemoveSpans = removal, ExpressionSpan = written, Location = written is null ? null : new SourceLocation(written.File, owner._scan.Files.First(f => f.Path == written.File).LineOf(written.Offset)) }, fromLoop);
            _x += Advance(width);
            _xSpan = _ySpan = null;
        }

        private void AddMenu(CodeStringArray items, ScreenCodeAnalyzer analyzer)
        {
            // What else the loop draws (the cursor, a mark) is the menu's own.
            foreach (int index in Enumerable.Reverse(_fromLoops))
            {
                Elements.RemoveAt(index);
            }

            _fromLoops.Clear();
            int top = _titleY + 1;
            int rows = display.Family == DisplayFamily.CharLcd ? Math.Max(1, display.Height - top) : Math.Max(1, (display.Height - top) / 8);
            Menu = new ScreenMenu
            {
                Items = [.. items.Items.Select(i => new ScreenMenuItem { Text = SketchScanner.Decode(i), TextSpan = analyzer.Span(i) })],
                Top = top,
                Rows = rows,
            };
        }
    }

    // ---- no screen variable: functions that draw --------------------------------------------------

    private ScreenDocument? FromFunctions()
    {
        // Helpers draw too, so a screen is a drawing function that nothing but non-drawing code calls with arguments to fill.
        CodeFunction[] drawing = [.. _facts.Keys.Where(f => _facts[f].Draws && f.Parameters.Count == 0 && f.Name is not ("setup" or "loop" or "update" or "begin"))];
        if (drawing.Length == 0)
        {
            return null;
        }

        var ids = new HashSet<string>(drawing.Select(f => f.Name), StringComparer.Ordinal);
        var screens = new List<ScreenNode>();
        var anywhere = new List<ScreenTransition>();
        foreach (CodeFunction function in drawing)
        {
            var painter = new Painter(this, _display);
            painter.Paint(function, function.BodyStart, function.BodyEnd);
            screens.Add(new ScreenNode
            {
                Id = function.Name,
                Title = function.Name,
                Elements = painter.Elements,
                Menu = painter.Menu,
                DrawnElsewhere = painter.CallsOutside,
                DrawInsert = EndOf(function).Insert,
                Receiver = ReceiverIn(function),
                Location = LocationOf(function),
            });
        }

        // A call to a drawing function is the transition: from the screen that makes it, if it is in one.
        var bySource = new Dictionary<string, List<ScreenTransition>>(StringComparer.Ordinal);
        string? start = null;
        foreach ((CodeFunction function, FunctionFacts facts) in _facts)
        {
            foreach (CallFact call in facts.Calls.Where(c => c.Receiver.Length == 0 && ids.Contains(c.Name)))
            {
                CodeToken target = function.Tokens[call.NameIndex];
                ScreenTransition transition = Transition(target, [.. call.Conditions], []);
                if (function.Name == "setup" || IsStartup(function))
                {
                    start ??= call.Name;
                }
                else if (ids.Contains(function.Name))
                {
                    if (!bySource.TryGetValue(function.Name, out List<ScreenTransition>? list))
                    {
                        bySource[function.Name] = list = [];
                    }

                    list.Add(transition);
                }
                else if (!anywhere.Contains(transition))
                {
                    anywhere.Add(transition);
                }
            }
        }

        ScreenNode[] linked = [.. screens.Select(s => s with { Transitions = bySource.GetValueOrDefault(s.Id) ?? [] })];
        return Finish(linked, anywhere, start ?? linked[0].Id);
    }

    [GeneratedRegex("COUNT|_MAX$|_LAST$|_NUM$|^NUM_", RegexOptions.IgnoreCase)]
    private static partial Regex CountLike();

    [GeneratedRegex("startup|splash|boot", RegexOptions.IgnoreCase)]
    private static partial Regex StartupLike();

    [GeneratedRegex(@"(\d{2,3})X(\d{2,3})", RegexOptions.IgnoreCase)]
    private static partial Regex SizeInName();

    [GeneratedRegex("screen|page|view|menu|экран", RegexOptions.IgnoreCase)]
    private static partial Regex ScreenLike();

    [GeneratedRegex("state|mode", RegexOptions.IgnoreCase)]
    private static partial Regex StateLike();

    [GeneratedRegex("format|type|kind|pin|key|error|status|color|font", RegexOptions.IgnoreCase)]
    private static partial Regex NotScreenLike();

    [GeneratedRegex("^(key|k|c|ch|button|btn|cmd|command|input|pressed|keyPressed)$", RegexOptions.IgnoreCase)]
    private static partial Regex KeyLike();

    [GeneratedRegex("index|item|sel|cursor|pos|choice", RegexOptions.IgnoreCase)]
    private static partial Regex ItemLike();

    [GeneratedRegex("^(draw|show|render|display|print|screen|page)_?", RegexOptions.IgnoreCase)]
    private static partial Regex DrawnFunction();
}

/// <summary>Where screens lie on the map when nothing says yet: in columns by how far they are from the start.</summary>
public static class ScreenMapLayout
{
    public const double ColumnStep = 300;
    public const double RowStep = 170;
    private const int MaxRows = 4;

    public static ScreenDocument Arrange(ScreenDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var distance = new Dictionary<string, int>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        if (document.Find(document.Start) is not null)
        {
            distance[document.Start] = 0;
            queue.Enqueue(document.Start);
        }

        while (queue.TryDequeue(out string? id))
        {
            foreach (ScreenTransition transition in document.Find(id)?.Transitions ?? [])
            {
                if (document.Find(transition.Target) is not null && distance.TryAdd(transition.Target, distance[id] + 1))
                {
                    queue.Enqueue(transition.Target);
                }
            }
        }

        // What cannot be reached from the start, one column past the rest. A level taller than a window
        // is wide folds into more columns, so that the whole map fits at a size that can be read.
        int last = distance.Count == 0 ? 0 : distance.Values.Max() + 1;
        var places = new Dictionary<string, (int Column, int Row)>(StringComparer.Ordinal);
        int column = 0;
        foreach (IGrouping<int, ScreenNode> level in document.Screens.GroupBy(s => distance.GetValueOrDefault(s.Id, last)).OrderBy(g => g.Key))
        {
            int row = 0;
            foreach (ScreenNode screen in level)
            {
                places[screen.Id] = (column + (row / MaxRows), row % MaxRows);
                row++;
            }

            column += ((row - 1) / MaxRows) + 1;
        }

        return document with { Screens = [.. document.Screens.Select(s => s with { MapX = places[s.Id].Column * ColumnStep, MapY = places[s.Id].Row * RowStep })] };
    }
}
