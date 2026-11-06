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

    private sealed record SwitchFact(string Variable, IReadOnlyList<(IReadOnlyList<CodeToken> Labels, bool IsDefault, int Start, int End)> Cases);

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

        protected override void SwitchSeen(string variable, IReadOnlyList<(IReadOnlyList<CodeToken> Labels, bool IsDefault, int Start, int End)> cases) =>
            Facts.Switches.Add(new SwitchFact(variable, cases));

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
        return Finish(screens, anywhere, start);
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
                    if (t[i + 1].Is("(") && constants.Contains(t[i + 2].Text))
                    {
                        return t[i + 2].Text;
                    }

                    if (t[i + 1].Is("=") && constants.Contains(t[i + 2].Text))
                    {
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
                    if (chains.Count > 1 || sources.Length > 1)
                    {
                        transition = transition with { TargetSpan = null };
                    }

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

    // ---- what each screen shows ---------------------------------------------------------------------

    private Dictionary<string, ScreenNode> DrawScreens(IReadOnlyList<CodeToken> constants, HashSet<string> variables)
    {
        // The switch whose cases draw is the screen's drawing; another over the same variable is its input.
        (CodeFunction Function, SwitchFact Switch)? draw = null;
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
                }
            }
        }

        // Named after the screen: drawHome() for HOME, without a switch to say so.
        if (!found && NamedAfter(id) is { } named)
        {
            painter.Paint(named, named.BodyStart, named.BodyEnd);
            found = true;
            location = LocationOf(named);
        }

        // An application that draws itself: ALARM_APP, AlarmApp::render.
        if (painter.CallsOutside && RendererFor(id) is { } renderer)
        {
            painter.Paint(renderer, renderer.BodyStart, renderer.BodyEnd);
            painter.CallsOutside = false;
            location = LocationOf(renderer);
        }

        return new ScreenNode
        {
            Id = id,
            Title = id,
            Elements = painter.Elements,
            Menu = painter.Menu,
            DrawnElsewhere = painter.CallsOutside || painter.Elements.Count == 0 && painter.Menu is null,
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

            _depth++;
            new PaintWalker(this, function, bindings).Walk(start, end);
            _depth--;
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
                        Display(name, arguments);
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

            private void Display(string name, List<(int Start, int End)> a)
            {
                switch (name)
                {
                    case "setCursor" or "setCursorXY" when a.Count >= 2:
                        SetCursor(a[0], a[1]);
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
                    Location = LocationAt(a[0].Start),
                }, Loops > 0);
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
                        painter.AddText(text, Analyzer.Span(t[start]), _conditional > 0, inLoop, alternative || (_conditional > 0 && text.Length == 1));
                    }

                    return;
                }

                if (end - start == 1 && t[start].Kind == TokenKind.Number)
                {
                    painter.AddText(t[start].Text, null, _conditional > 0, inLoop, alternative);
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
                painter.AddValue(expression, literals, _conditional > 0, inLoop, alternative);
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

        private void AddText(string text, SourceSpan? span, bool conditional, bool fromLoop, bool alternative)
        {
            if (Menu is null && !fromLoop)
            {
                _titleY = Math.Max(_titleY, _y);
            }

            Add(new ScreenElement { Kind = ElementKind.Text, X = _x, Y = _y, Text = text, Size = _size, TextSpan = span, XSpan = _xSpan, YSpan = _ySpan, IsConditional = conditional, IsAlternative = alternative, IsPlaced = _placed }, fromLoop);
            _x += Advance(text.Length);
            _xSpan = _ySpan = null;
        }

        private void AddValue(string expression, string[] literals, bool conditional, bool fromLoop, bool alternative)
        {
            int width = literals.Length > 0 ? literals.Max(l => l.Length) : Math.Clamp(expression.Length, 2, 4);
            Add(new ScreenElement { Kind = ElementKind.Value, X = _x, Y = _y, Text = expression, Width = width, Size = _size, Sample = literals.FirstOrDefault(), XSpan = _xSpan, YSpan = _ySpan, IsConditional = conditional, IsAlternative = alternative, IsPlaced = _placed }, fromLoop);
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
