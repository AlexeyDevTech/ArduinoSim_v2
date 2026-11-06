using System.Globalization;
using System.Text.RegularExpressions;

namespace ArduSim.Core.Screens;

/// <summary>
/// Forms a project's screens and transitions from its own code (ADR-019). A heuristic, and safe to be
/// wrong: it changes nothing, every result links to its line, and only what it read exactly can be
/// edited through it.
/// </summary>
public sealed partial class ScreenCodeAnalyzer
{
    private const int CallDepth = 5;
    private const int MaxChains = 24;

    private readonly SketchScanner _scan = new();
    private readonly Dictionary<CodeFunction, FunctionFacts> _facts = [];
    private readonly Dictionary<string, List<CodeFunction>> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (IReadOnlyList<CodeToken> Tokens, int Start, int End)> _constants = new(StringComparer.Ordinal);
    private readonly HashSet<string> _cellMethods = new(StringComparer.Ordinal);
    private readonly HashSet<string> _listPlaces = new(StringComparer.Ordinal);
    private readonly ScreenDisplay? _sceneDisplay;
    private SketchRun? _runner;
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
        FindConstants();
        FindCellMethods();
        _display = _display with { OwnFont = DecodesText() };
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
        FindListPlaces();
        return FromState() ?? FromFunctions();
    }

    private SketchRun Runner => _runner ??= new SketchRun(_scan, _constants, _byName, _listPlaces);

    // What a list's loop compares its variable with is where the list stands: for (i...) if (i == selected) mark.
    // What it starts from is the top of what shows: for (i = top; ...). In a picture a list stands at its first item,
    // with that item on top, which is what the picture marks too.
    private void FindListPlaces()
    {
        foreach (CodeFunction function in _scan.Functions)
        {
            IReadOnlyList<CodeToken> t = function.Tokens;
            for (int k = function.BodyStart; k + 1 < function.BodyEnd; k++)
            {
                if (!t[k].Is("for") || !t[k + 1].Is("("))
                {
                    continue;
                }

                int close = SketchScanner.Matching(t, k + 1, "(", ")");
                int equals = k + 2;
                while (equals < close && !t[equals].Is("="))
                {
                    equals++;
                }

                if (equals >= close || close + 1 >= function.BodyEnd)
                {
                    continue;
                }

                string variable = t[equals - 1].Text;
                if (equals + 2 < close && t[equals + 2].Is(";") && IsListPlace(function, equals + 1))
                {
                    _listPlaces.Add(t[equals + 1].Text);
                }

                int bodyEnd = t[close + 1].Is("{") ? SketchScanner.Matching(t, close + 1, "{", "}") : close + 1;
                for (int m = close + 1; m + 2 < bodyEnd; m++)
                {
                    if (!t[m].Is("=="))
                    {
                        continue;
                    }

                    int other = t[m - 1].Is(variable) ? m + 1 : t[m + 1].Is(variable) ? m - 1 : -1;
                    if (other >= 0 && IsListPlace(function, other))
                    {
                        _listPlaces.Add(t[other].Text);
                    }
                }
            }
        }
    }

    // A plain name that belongs to the sketch's state: not a call, a member of something, a constant, or the function's own.
    private bool IsListPlace(CodeFunction function, int at)
    {
        IReadOnlyList<CodeToken> t = function.Tokens;
        string name = t[at].Text;
        if (t[at].Kind != TokenKind.Identifier || t[at + 1].Is("(") || t[at + 1].Is(".") || t[at + 1].Is("->") || t[at + 1].Is("[") || t[at - 1].Is(".") || t[at - 1].Is("->")
            || function.Parameters.Any(p => p.Name == name) || _constants.ContainsKey(name) || _scan.Macros.ContainsKey(name) || _scan.Enums.Any(e => e.Constants.Any(c => c.Text == name)))
        {
            return false;
        }

        for (int k = function.BodyStart + 1; k < function.BodyEnd; k++)
        {
            bool declared = t[k].Is(name) && (t[k - 1].Is("*") || t[k - 1].Is("&") || (t[k - 1].Kind == TokenKind.Identifier && !t[k - 1].Is("return") && !t[k - 1].Is("case")));
            if (declared)
            {
                return false;
            }
        }

        return true;
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

    // const uint8_t COL = 5; constexpr int LAST = COLS - 1; enum { COLS = 20 };
    private void FindConstants()
    {
        foreach (CodeFile file in _scan.Files)
        {
            List<CodeToken> t = file.Tokens;
            for (int i = 0; i + 2 < t.Count; i++)
            {
                if ((t[i].Is("const") || t[i].Is("constexpr")) && !InsideFunction(t, i))
                {
                    int k = i + 1;
                    while (k + 1 < t.Count && (t[k].Kind == TokenKind.Identifier || t[k].Is("*")) && !t[k + 1].Is("="))
                    {
                        k++;
                    }

                    if (k + 1 < t.Count && t[k].Kind == TokenKind.Identifier && t[k + 1].Is("="))
                    {
                        _constants[t[k].Text] = (t, k + 2, EndOfValue(t, k + 2));
                    }
                }
                else if (t[i].Is("enum"))
                {
                    int open = i + 1;
                    while (open < t.Count && open < i + 6 && !t[open].Is("{") && !t[open].Is(";"))
                    {
                        open++;
                    }

                    if (open < t.Count && t[open].Is("{"))
                    {
                        int close = SketchScanner.Matching(t, open, "{", "}");
                        for (int m = open + 1; m + 1 < close; m++)
                        {
                            if (t[m].Kind == TokenKind.Identifier && t[m + 1].Is("="))
                            {
                                _constants[t[m].Text] = (t, m + 2, EndOfValue(t, m + 2));
                            }
                        }
                    }
                }
            }
        }
    }

    private bool InsideFunction(List<CodeToken> t, int index) =>
        _scan.Functions.Any(f => ReferenceEquals(f.Tokens, t) && index > f.BodyStart && index < f.BodyEnd);

    // What a display class's write() puts text out through writes one cell, whatever is in it.
    private void FindCellMethods()
    {
        foreach (CodeFunction write in _scan.Functions.Where(f => f.Name == "write" && _surfaceTypes.Contains(f.Class)))
        {
            IReadOnlyList<CodeToken> t = write.Tokens;
            for (int k = write.BodyStart; k + 1 < write.BodyEnd; k++)
            {
                string name = t[k].Text;
                if (t[k].Kind == TokenKind.Identifier && t[k + 1].Is("(") && !t[k - 1].Is(".") && !t[k - 1].Is("->") && _scan.Functions.Any(f => f.Name == name && f.Class == write.Class))
                {
                    _cellMethods.Add(name);
                }
            }
        }
    }

    // A write() that looks at UTF-8's lead bytes shows letters by itself.
    private bool DecodesText() =>
        _scan.Functions.Where(f => f.Name == "write" && _surfaceTypes.Contains(f.Class)).Any(f =>
            Enumerable.Range(f.BodyStart, Math.Max(0, f.BodyEnd - f.BodyStart)).Any(k => f.Tokens[k].Kind == TokenKind.Number && f.Tokens[k].Text.ToUpperInvariant() is "0XC0" or "0X80"));

    // Up to the comma, semicolon or closing brace that ends a value.
    private static int EndOfValue(IReadOnlyList<CodeToken> t, int start)
    {
        int nested = 0;
        int k = start;
        for (; k < t.Count; k++)
        {
            if (t[k].Is("(") || t[k].Is("[") || t[k].Is("{"))
            {
                nested++;
            }
            else if (nested > 0 && (t[k].Is(")") || t[k].Is("]") || t[k].Is("}")))
            {
                nested--;
            }
            else if (nested == 0 && (t[k].Is(",") || t[k].Is(";") || t[k].Is("}") || t[k].Is(")")))
            {
                break;
            }
        }

        return k;
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
        call.Receiver.Length == 0
            ? _surfaceTypes.Contains(function.Class) && (DrawingCalls.Contains(call.Name) || _cellMethods.Contains(call.Name))
            : IsDisplayObject(call.Receiver, function) && DrawingCalls.Contains(call.Name);

    private bool IsDisplayObject(string name, CodeFunction function) => name.Length > 0 && (_displayNames.Contains(name) || IsSurfaceParameter(function, name));

    // A method of a display class of the sketch's own, which drawing goes through like any helper: lcd.printRight(3, text).
    private CodeFunction? SurfaceMethod(string name, int arguments) =>
        _byName.GetValueOrDefault(name)?.FirstOrDefault(f => _surfaceTypes.Contains(f.Class) && f.Parameters.Count == arguments);

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
                if (!facts.Draws && facts.Calls.Any(c => (c.Receiver.Length == 0 && Callee(c, function) is { } callee && _facts[callee].Draws)
                    || (IsDisplayObject(c.Receiver, function) && SurfaceMethod(c.Name, c.Arguments.Count) is { } method && _facts[method].Draws)))
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
        if (function is not null && _facts.TryGetValue(function, out FunctionFacts? facts) && facts.Calls.FirstOrDefault(c => c.Receiver.Length > 0 && IsDrawCall(c, function)) is { } call)
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
        _facts.Sum(f => f.Value.Calls.Count(c => c.Name == function.Name && (Callee(c, f.Key) == function
            || (_surfaceTypes.Contains(function.Class) && c.Arguments.Count == function.Parameters.Count && IsDisplayObject(c.Receiver, f.Key)))));

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

    // ---- local variables that place things -------------------------------------------------------------

    /// <summary>A variable declared in a function's body.</summary>
    /// <param name="InitStart">Where the value of its first assignment starts.</param>
    /// <param name="Assignments">How many times anything gives it a value.</param>
    /// <param name="Declaration">Its statement's first token and semicolon, if the statement is nothing but <c>type name = value;</c>.</param>
    private sealed record LocalVariable(int InitStart, int InitEnd, int Assignments, bool IsLoopVariable, (int Start, int End)? Declaration);

    private static readonly string[] NotTypes = ["return", "case", "else", "new", "delete", "sizeof", "throw"];

    private static LocalVariable? LocalOf(CodeFunction function, string name)
    {
        IReadOnlyList<CodeToken> t = function.Tokens;
        int declared = -1;
        int assignments = 0;
        int initStart = 0;
        int initEnd = 0;
        for (int k = Math.Max(1, function.BodyStart); k < function.BodyEnd && k + 1 < t.Count; k++)
        {
            if (!t[k].Is(name))
            {
                continue;
            }

            CodeToken before = t[k - 1];
            CodeToken after = t[k + 1];
            if (before.Is(".") || before.Is("->") || before.Is("::"))
            {
                continue;
            }

            if (declared < 0 && ((before.Kind == TokenKind.Identifier && !NotTypes.Contains(before.Text)) || before.Is("*")))
            {
                declared = k;
            }

            if (after.Is("="))
            {
                if (assignments++ == 0)
                {
                    initStart = k + 2;
                    initEnd = EndOfValue(t, k + 2);
                }
            }
            else if (after.Is("+=") || after.Is("-=") || after.Is("++") || after.Is("--") || before.Is("++") || before.Is("--") || (before.Is("&") && !(t[k - 2].Kind == TokenKind.Identifier)))
            {
                assignments++;
            }
        }

        if (declared < 0)
        {
            return null;
        }

        int start = declared;
        while (start > function.BodyStart && !(t[start - 1].Is(";") || t[start - 1].Is("{") || t[start - 1].Is("}") || t[start - 1].Is(":") || t[start - 1].Is("(")))
        {
            start--;
        }

        bool inLoopHeader = start >= 2 && t[start - 1].Is("(") && t[start - 2].Is("for");
        bool plain = !t[start - 1].Is("(") && initStart == declared + 2 && initEnd < t.Count && t[initEnd].Is(";")
            && Enumerable.Range(start, declared - start).All(k => t[k].Kind == TokenKind.Identifier || t[k].Is("*") || t[k].Is("&") || t[k].Is("::"));
        return new LocalVariable(initStart, initEnd, assignments, inLoopHeader, plain ? (start, initEnd) : null);
    }

    // The declarations nothing else uses once this expression is gone: its locals, and the locals only they used.
    private List<SourceSpan> DeadWith(CodeFunction function, IReadOnlyList<CodeToken> t, int start, int end)
    {
        var gone = new List<(int Start, int End)> { (start, end - 1) };
        var dead = new List<(int Start, int End)>();
        bool changed = true;
        while (changed)
        {
            changed = false;
            string[] names = [.. gone.SelectMany(g => Enumerable.Range(g.Start, g.End - g.Start + 1)).Where(k => t[k].Kind == TokenKind.Identifier).Select(k => t[k].Text).Distinct(StringComparer.Ordinal)];
            foreach (string name in names)
            {
                if (LocalOf(function, name) is not { Assignments: 1, IsLoopVariable: false, Declaration: { } statement } local || dead.Contains(statement))
                {
                    continue;
                }

                bool used = Enumerable.Range(function.BodyStart, function.BodyEnd - function.BodyStart)
                    .Any(k => t[k].Is(name) && !(t[k - 1].Is(".") || t[k - 1].Is("->") || t[k - 1].Is("::")) && !gone.Any(g => k >= g.Start && k <= g.End) && !(k >= statement.Start && k < local.InitStart));
                var pure = new Painter.Reckoning(this, function, new(StringComparer.Ordinal), null);
                pure.Of(t, local.InitStart, local.InitEnd);
                if (!used && !pure.Changes)
                {
                    dead.Add(statement);
                    gone.Add(statement);
                    changed = true;
                }
            }
        }

        if (dead.Count == 0)
        {
            return [];
        }

        // Statements side by side go as one: a line they had to themselves goes with them.
        CodeFile file = _scan.Files[t[start].File];
        dead.Sort();
        var spans = new List<SourceSpan>();
        (int first, int last) = dead[0];
        foreach ((int from, int to) in dead.Skip(1))
        {
            if (from == last + 1)
            {
                last = to;
            }
            else
            {
                spans.Add(StatementSpan(file, t[first].Offset, t[last].Offset + 1));
                (first, last) = (from, to);
            }
        }

        spans.Add(StatementSpan(file, t[first].Offset, t[last].Offset + 1));
        return spans;
    }

    private SourceSpan SpanOver(IReadOnlyList<CodeToken> t, int start, int end)
    {
        CodeFile file = _scan.Files[t[start].File];
        int from = t[start].Offset;
        int to = t[end - 1].Offset + t[end - 1].Text.Length;
        return new SourceSpan(file.Path, from, to - from, file.Text[from..to]);
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
        Runner.Screen = id;
        Runner.ScreenVariable = draw?.Switch.Variable;
        painter.ScreenVariable = draw?.Switch.Variable;
        painter.Screen = id;
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

        // No case of its own: what is left falls to the default.
        if (!found && draw is { } fallback)
        {
            // Other screens may come here too: what is drawn is theirs as well, so nothing in it is this screen's to change.
            foreach ((IReadOnlyList<CodeToken> _, bool isDefault, int start, int end) in fallback.Switch.Cases.Where(c => c.IsDefault))
            {
                painter.PaintShared(fallback.Function, start, end);
            }
        }

        // An application that draws itself: ALARM_APP, AlarmApp::render.
        if (painter.CallsOutside && RendererFor(id) is { } renderer)
        {
            painter.Paint(renderer, renderer.BodyStart, renderer.BodyEnd);
            painter.CallsOutside = false;
            location = LocationOf(renderer);
            site = EndOf(renderer);
        }

        painter.FitRuns();
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
        CodeFunction[] drawing = [.. _facts.Keys.Where(f => _facts[f].Draws && !_surfaceTypes.Contains(f.Class))];
        return drawing.FirstOrDefault(f => DrawnFunction().Replace(f.Name, "").Equals(id, StringComparison.OrdinalIgnoreCase))
            ?? drawing.FirstOrDefault(f => Letters(f.Name).Contains(wanted, StringComparison.Ordinal) && wanted.Length >= 3);
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
        private readonly List<int> _runOf = [];
        private int _run;
        private int _lastCell = -1;
        private int _outerLoops;
        private int _outerConditional;
        private int _outerElses;
        private (SourceInsert Insert, CodeFunction Caller, string Receiver)? _callSite;
        private SourceSpan? _xSpan;
        private SourceSpan? _ySpan;
        private CodeExpression? _xCode;
        private CodeExpression? _yCode;
        private Func<int, int?>? _xLater;
        private Func<int, int?>? _yLater;
        private bool _placed = true;
        private int _titleY = -1;
        private bool _shared;

        private (int Start, int End)? _cursorTokens;

        public List<ScreenElement> Elements { get; } = [];

        public ScreenMenu? Menu { get; private set; }

        /// <summary>It called something like <c>active()->render()</c>, which cannot be followed by name.</summary>
        public bool CallsOutside { get; set; }

        /// <summary>The constant of the screen being drawn.</summary>
        public string? Screen { get; set; }

        /// <summary>The variable that holds the screen.</summary>
        public string? ScreenVariable { get; set; }

        public void Paint(CodeFunction function, int start, int end) => Enter(function, start, end, new Dictionary<string, Bound>(StringComparer.Ordinal));

        public void PaintShared(CodeFunction function, int start, int end)
        {
            _shared = true;
            Paint(function, start, end);
            _shared = false;
        }

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
        /// <param name="Shared">The call that gives it is in code other screens share.</param>
        internal sealed record Bound(CodeFunction Function, IReadOnlyList<CodeToken> Tokens, int Start, int End, Dictionary<string, Bound> Bindings, bool Shared);

        /// <summary>A coordinate as the code gives it.</summary>
        /// <param name="Value">What it comes to, if that can be told.</param>
        /// <param name="Span">What a number can replace.</param>
        /// <param name="Code">What the code writes, if not a plain number.</param>
        /// <param name="Later">What it comes to once the width of what is printed there is known: <c>(COLS - strlen(t)) / 2</c>.</param>
        private readonly record struct Coordinate(int? Value, SourceSpan? Span, CodeExpression? Code, Func<int, int?>? Later);

        /// <summary>
        /// What an expression comes to, as far as can be told without running it: numbers, macros, constants, the
        /// bound parameters of a helper and locals given a value once, in <c>+ - * / %</c> and parentheses. What the
        /// sketch only knows at run time makes <see cref="Changes"/> true; <c>strlen</c> of what is printed does not,
        /// since it depends on nothing but that.
        /// </summary>
        internal sealed class Reckoning(ScreenCodeAnalyzer owner, CodeFunction? function, Dictionary<string, Bound> scope, int? printed, int depth = 0)
        {
            private static readonly string[] IntegerTypes = ["int", "unsigned", "long", "short", "byte", "uint8_t", "int8_t", "uint16_t", "int16_t", "uint32_t", "int32_t", "size_t", "char"];
            private IReadOnlyList<CodeToken> _t = [];
            private int _at;
            private int _end;

            public bool Changes { get; private set; }

            public bool NeedsPrinted { get; private set; }

            public bool UsesLocals { get; private set; }

            public int? Of(IReadOnlyList<CodeToken> t, int start, int end)
            {
                (_t, _at, _end) = (t, start, end);
                int? value = Conditional();
                if (_at < _end)
                {
                    Changes = true;
                    return null;
                }

                return value;
            }

            private bool Next(string text) => _at < _end && _t[_at].Is(text);

            private int? Conditional()
            {
                int? condition = Comparison();
                if (!Next("?"))
                {
                    return condition;
                }

                _at++;
                int? first = Conditional();
                if (Next(":"))
                {
                    _at++;
                }
                else
                {
                    Changes = true;
                }

                int? second = Conditional();
                return condition is null ? null : condition != 0 ? first : second;
            }

            private int? Comparison()
            {
                int? left = Sum();
                while (Next("<") || Next(">") || Next("<=") || Next(">=") || Next("==") || Next("!="))
                {
                    string operation = _t[_at++].Text;
                    int? right = Sum();
                    bool? result = left is null || right is null ? null : operation switch
                    {
                        "<" => left < right,
                        ">" => left > right,
                        "<=" => left <= right,
                        ">=" => left >= right,
                        "==" => left == right,
                        _ => left != right,
                    };
                    left = result is null ? null : result.Value ? 1 : 0;
                }

                return left;
            }

            private int? Sum()
            {
                int? value = Product();
                while (Next("+") || Next("-"))
                {
                    bool plus = _t[_at++].Is("+");
                    int? right = Product();
                    value = plus ? value + right : value - right;
                }

                return value;
            }

            private int? Product()
            {
                int? value = Unary();
                while (Next("*") || Next("/") || Next("%"))
                {
                    string operation = _t[_at++].Text;
                    int? right = Unary();
                    value = operation == "*" ? value * right : right is null or 0 ? null : operation == "/" ? value / right : value % right;
                }

                return value;
            }

            private int? Unary()
            {
                if (Next("-"))
                {
                    _at++;
                    return -Unary();
                }

                if (Next("+"))
                {
                    _at++;
                }

                return Primary();
            }

            private int? Primary()
            {
                if (_at >= _end)
                {
                    Changes = true;
                    return null;
                }

                CodeToken token = _t[_at];
                if (token.Is("("))
                {
                    return Parenthesized();
                }

                if (token.Kind == TokenKind.Number)
                {
                    _at++;
                    return ParseInteger(token.Text);
                }

                if (token.Kind != TokenKind.Identifier)
                {
                    Changes = true;
                    _at = _end;
                    return null;
                }

                _at++;
                while (Next("::") && _at + 1 < _end)
                {
                    token = _t[_at + 1];
                    _at += 2;
                }

                if (Next("("))
                {
                    return Call(token.Text);
                }

                return Next(".") || Next("->") || Next("[") ? Access() : Named(token.Text);
            }

            private int? Parenthesized()
            {
                int close = Math.Min(SketchScanner.Matching(_t, _at, "(", ")"), _end);

                // (uint8_t)x says the same as x here.
                if (close == _at + 2 && IntegerTypes.Contains(_t[_at + 1].Text))
                {
                    _at = close + 1;
                    return Unary();
                }

                int outer = _end;
                _at++;
                _end = close;
                int? inner = Conditional();
                if (_at < _end)
                {
                    Changes = true;
                    inner = null;
                }

                _end = outer;
                _at = close + 1;
                return inner;
            }

            // a.b, a->b(), a[i]: known at run time, unless it asks how wide the text is.
            private int? Access()
            {
                string last = "";
                while (Next(".") || Next("->") || Next("[") || Next("("))
                {
                    if (Next(".") || Next("->"))
                    {
                        last = _at + 1 < _end ? _t[_at + 1].Text : "";
                        _at += 2;
                    }
                    else
                    {
                        string open = _t[_at].Text;
                        _at = Math.Min(SketchScanner.Matching(_t, _at, open, open == "(" ? ")" : "]") + 1, _end);
                    }
                }

                if (last is "getStrWidth" or "getUTF8Width")
                {
                    NeedsPrinted = true;
                    return printed * 6;
                }

                Changes = true;
                return null;
            }

            private int? Call(string name)
            {
                int close = Math.Min(SketchScanner.Matching(_t, _at, "(", ")"), _end);
                List<(int Start, int End)> arguments = ArgumentRanges(_t, _at + 1, close);
                _at = Math.Min(close + 1, _end);
                switch (name)
                {
                    case "strlen" or "strlen_P" when arguments.Count == 1:
                        if (StringIn(_t, arguments[0].Start, arguments[0].End, scope) is { } bytes)
                        {
                            return System.Text.Encoding.UTF8.GetByteCount(bytes);
                        }

                        NeedsPrinted = true;
                        return printed;
                    case not ("min" or "max" or "abs" or "sizeof") when arguments.Count == 1 && (name.Contains("width", StringComparison.OrdinalIgnoreCase) || name.Contains("length", StringComparison.OrdinalIgnoreCase)):
                        // A sketch's own measure of text: cells, which is what it asks for.
                        if (StringIn(_t, arguments[0].Start, arguments[0].End, scope) is { } cells)
                        {
                            return cells.Length;
                        }

                        NeedsPrinted = true;
                        return printed;
                    case "min" or "max" when arguments.Count == 2:
                        int? a = Child(function, scope, _t, arguments[0].Start, arguments[0].End);
                        int? b = Child(function, scope, _t, arguments[1].Start, arguments[1].End);
                        return a is null || b is null ? null : name == "min" ? Math.Min(a.Value, b.Value) : Math.Max(a.Value, b.Value);
                    case "abs" when arguments.Count == 1:
                        return Child(function, scope, _t, arguments[0].Start, arguments[0].End) is { } value ? Math.Abs(value) : null;
                    case "sizeof":
                        return null;
                    default:
                        // A function of the sketch, run on what is known. The run can change what it comes to, so that this stays as written.
                        Changes = true;
                        var numbers = new List<long>();
                        foreach ((int from, int to) in arguments)
                        {
                            if (Child(function, scope, _t, from, to) is not { } number)
                            {
                                return null;
                            }

                            numbers.Add(number);
                        }

                        return owner.Runner.IsFunction(name) && owner.Runner.Number(name, numbers) is { } result and >= int.MinValue and <= int.MaxValue ? (int)result : null;
                }
            }

            // The text an argument says, through F() and the parameters it came by.
            private static string? StringIn(IReadOnlyList<CodeToken> t, int start, int end, Dictionary<string, Bound> names)
            {
                for (int hops = 0; hops < CallDepth; hops++)
                {
                    while (end - start >= 4 && t[start].Text is "F" or "PSTR" or "FPSTR" && t[start + 1].Is("(") && SketchScanner.Matching(t, start + 1, "(", ")") == end - 1)
                    {
                        start += 2;
                        end--;
                    }

                    if (end - start != 1)
                    {
                        return null;
                    }

                    if (t[start].Kind == TokenKind.String)
                    {
                        return SketchScanner.Decode(t[start]);
                    }

                    if (!names.TryGetValue(t[start].Text, out Bound? bound))
                    {
                        return null;
                    }

                    (t, start, end, names) = (bound.Tokens, bound.Start, bound.End, bound.Bindings);
                }

                return null;
            }

            private int? Named(string name)
            {
                if (depth >= 8)
                {
                    Changes = true;
                    return null;
                }

                if (scope.TryGetValue(name, out Bound? bound))
                {
                    return Child(bound.Function, bound.Bindings, bound.Tokens, bound.Start, bound.End);
                }

                if (function is not null && function.Parameters.Any(p => p.Name == name))
                {
                    Changes = true;
                    return null;
                }

                if (function is not null && LocalOf(function, name) is { } local)
                {
                    UsesLocals = true;
                    if (local.Assignments != 1 || local.IsLoopVariable)
                    {
                        Changes = true;
                        return null;
                    }

                    return Child(function, scope, function.Tokens, local.InitStart, local.InitEnd);
                }

                if (owner._listPlaces.Contains(name))
                {
                    Changes = true;
                    return 0;
                }

                if (owner._scan.Macros.TryGetValue(name, out string? body))
                {
                    List<CodeToken> tokens = SketchScanner.TokensOf(body);
                    return Child(null, [], tokens, 0, tokens.Count);
                }

                if (owner._constants.TryGetValue(name, out (IReadOnlyList<CodeToken> Tokens, int Start, int End) constant))
                {
                    return Child(null, [], constant.Tokens, constant.Start, constant.End);
                }

                Changes = true;
                return null;
            }

            private int? Child(CodeFunction? where, Dictionary<string, Bound> names, IReadOnlyList<CodeToken> t, int start, int end)
            {
                var child = new Reckoning(owner, where, names, printed, depth + 1);
                int? value = child.Of(t, start, end);
                Changes |= child.Changes;
                NeedsPrinted |= child.NeedsPrinted;
                UsesLocals |= child.UsesLocals;
                return value;
            }
        }

        private sealed class PaintWalker(Painter painter, CodeFunction function, Dictionary<string, Bound> bindings) : BodyWalker(function.Tokens)
        {
            private readonly Stack<(int X, int Y)> _branch = new();
            private readonly Stack<(int X, int Y)> _afterThen = new();
            private readonly Stack<(string Name, int From)?> _running = new();
            private readonly Stack<(string Variable, int StartOf, int StartEnd, int ConditionStart, int ConditionEnd)?> _headers = new();
            private readonly Stack<(int X, int Y, int Chosen, bool OnScreen, int EndX, int EndY)> _switches = new();
            private int _conditional;
            private int _elses;
            private int _ranLoops;

            private bool InSurface => Analyzer._surfaceTypes.Contains(function.Class);

            private bool IsConditional => _conditional + painter._outerConditional > 0;

            private bool IsInElse => _elses + painter._outerElses > 0;

            // A display class's own loop over known numbers draws what it draws: for (i = 0; i < 6; ++i).
            protected override int LoopPasses(int start, int close)
            {
                _headers.Push(HeaderOf(start, close));
                (string Name, int From)? run = null;
                int passes = 1;
                if (InSurface && close > start)
                {
                    int first = start;
                    while (first < close && !Tokens[first].Is(";"))
                    {
                        first++;
                    }

                    int second = first + 1;
                    while (second < close && !Tokens[second].Is(";"))
                    {
                        second++;
                    }

                    int equals = start;
                    while (equals < first && !Tokens[equals].Is("="))
                    {
                        equals++;
                    }

                    if (equals > start && equals < first && second < close && first + 2 < second && Tokens[first + 1].Is(Tokens[equals - 1].Text) && (Tokens[first + 2].Is("<") || Tokens[first + 2].Is("<=")))
                    {
                        int? from = new Reckoning(Analyzer, function, bindings, null).Of(Tokens, equals + 1, first);
                        int? to = new Reckoning(Analyzer, function, bindings, null).Of(Tokens, first + 3, second);
                        int count = (to - from + (Tokens[first + 2].Is("<=") ? 1 : 0)) ?? 0;
                        if (from is not null && count is >= 1 and <= 64)
                        {
                            run = (Tokens[equals - 1].Text, from.Value);
                            passes = count;
                            _ranLoops++;
                        }
                    }
                }

                _running.Push(run);
                return passes;
            }

            protected override void LoopPass(int pass)
            {
                if (_running.Peek() is { } run)
                {
                    List<CodeToken> number = SketchScanner.TokensOf((run.From + pass).ToString(CultureInfo.InvariantCulture));
                    bindings[run.Name] = new Bound(function, number, 0, number.Count, [], Shared: true);
                }
            }

            protected override void LoopDone()
            {
                _headers.Pop();
                if (_running.Pop() is { } run)
                {
                    bindings.Remove(run.Name);
                    _ranLoops--;
                }
            }

            // for (type v = ...; condition; ...): the variable, and where its condition lies.
            private (string Variable, int StartOf, int StartEnd, int ConditionStart, int ConditionEnd)? HeaderOf(int start, int close)
            {
                int first = start;
                while (first < close && !Tokens[first].Is(";"))
                {
                    first++;
                }

                int second = first + 1;
                while (second < close && !Tokens[second].Is(";"))
                {
                    second++;
                }

                int equals = start;
                while (equals < first && !Tokens[equals].Is("="))
                {
                    equals++;
                }

                return equals > start && equals < first && second <= close ? (Tokens[equals - 1].Text, equals + 1, first, first + 1, second) : null;
            }

            // A switch on the screen draws this screen's case. Any other shows one thing at a time, so its cases are
            // alternatives of what it shows at first: the default, which is what is left when nothing is chosen.
            protected override void SwitchSeen(string variable, string expression, int bodyEnd, IReadOnlyList<(IReadOnlyList<CodeToken> Labels, bool IsDefault, int Start, int End)> cases)
            {
                base.SwitchSeen(variable, expression, bodyEnd, cases);
                bool onScreen = painter.Screen is not null && variable == painter.ScreenVariable;
                int chosen = -1;
                for (int i = 0; i < cases.Count && onScreen; i++)
                {
                    chosen = cases[i].Labels.Any(l => l.Text == painter.Screen) ? i : chosen;
                }

                for (int i = 0; i < cases.Count && chosen < 0; i++)
                {
                    chosen = cases[i].IsDefault ? i : chosen;
                }

                _switches.Push((painter._x, painter._y, Math.Max(chosen, 0), onScreen, painter._x, painter._y));
            }

            protected override bool EntersCase(string variable, int index, IReadOnlyList<(IReadOnlyList<CodeToken> Labels, bool IsDefault, int Start, int End)> cases)
            {
                (int x, int y, int chosen, bool onScreen, int _, int _) = _switches.Peek();
                if (onScreen)
                {
                    return index == chosen;
                }

                (painter._x, painter._y) = (x, y);
                _conditional++;
                _elses += index == chosen ? 0 : 1;
                return true;
            }

            protected override void CaseDone(int index)
            {
                (int x, int y, int chosen, bool onScreen, int endX, int endY) = _switches.Pop();
                if (!onScreen)
                {
                    _conditional--;
                    _elses -= index == chosen ? 0 : 1;
                    (endX, endY) = index == chosen ? (painter._x, painter._y) : (endX, endY);
                }

                _switches.Push((x, y, chosen, onScreen, endX, endY));
            }

            protected override void SwitchDone()
            {
                (int _, int _, int _, bool onScreen, int endX, int endY) = _switches.Pop();
                if (!onScreen)
                {
                    (painter._x, painter._y) = (endX, endY);
                }
            }

            // Only inside a loop that is being run: elsewhere both branches are what the screen can show.
            protected override bool? Decide(int start, int end)
            {
                if (_ranLoops == 0)
                {
                    return null;
                }

                var reckoning = new Reckoning(Analyzer, function, bindings, null);
                int? value = reckoning.Of(Tokens, start, end);
                return reckoning.Changes || value is null ? null : value != 0;
            }

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
                    if (((receiver.Length > 0 && IsDisplay(receiver)) || (receiver.Length == 0 && InSurface)) && DrawingCalls.Contains(name))
                    {
                        Display(name, arguments, k, close);
                        k = close + 1;
                    }
                    else if (receiver.Length == 0 && InSurface && arguments.Count >= 1 && Analyzer._cellMethods.Contains(name))
                    {
                        Cell(arguments[0]);
                        k = close + 1;
                    }
                    else if (receiver.Length == 0 && Analyzer.Callee(new CallFact(name, "", k, k + 1, close, arguments, []), function) is { } callee && Analyzer._facts[callee].Draws)
                    {
                        EnterHelper(callee, arguments, "", k, close);
                        k = close + 1;
                    }
                    else if (IsDisplay(receiver) && Analyzer.SurfaceMethod(name, arguments.Count) is { } method && Analyzer._facts[method].Draws)
                    {
                        EnterHelper(method, arguments, receiver, k, close);
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

            private bool IsDisplay(string receiver) => Analyzer.IsDisplayObject(receiver, function);

            private void EnterHelper(CodeFunction callee, List<(int Start, int End)> arguments, string receiver, int nameIndex, int close)
            {
                var inner = new Dictionary<string, Bound>(StringComparer.Ordinal);
                for (int i = 0; i < callee.Parameters.Count && i < arguments.Count; i++)
                {
                    inner[callee.Parameters[i].Name] = new Bound(function, Tokens, arguments[i].Start, arguments[i].End, bindings, painter._shared);
                }

                (SourceInsert Insert, CodeFunction Caller, string Receiver)? outerSite = painter._callSite;
                if (!painter._shared)
                {
                    painter._callSite = CallStatement(nameIndex, close, receiver);
                }

                int before = painter.Elements.Count;
                int loops = Loops - _ranLoops;
                painter._outerLoops += loops;
                painter._outerConditional += _conditional;
                painter._outerElses += _elses;
                painter.Enter(callee, callee.BodyStart, callee.BodyEnd, inner);
                painter._outerLoops -= loops;
                painter._outerConditional -= _conditional;
                painter._outerElses -= _elses;
                painter._callSite = outerSite;

                // What a display method drew alone goes when the call to it goes, if the call stands alone.
                if (receiver.Length > 0 && !InLoop && !IsConditional && RemovalOf(OwnStatement(nameIndex, close)) is { Count: > 0 } removal)
                {
                    painter.TakesOutWith(before, removal);
                }
            }

            // A call to a helper that stands alone: before it is where a cursor can go, for what the helper prints first.
            private (SourceInsert Insert, CodeFunction Caller, string Receiver)? CallStatement(int nameIndex, int close, string receiver)
            {
                int first = receiver.Length > 0 && nameIndex >= 2 ? nameIndex - 2 : nameIndex;
                bool alone = close + 1 < Tokens.Count && Tokens[close + 1].Is(";") && first > 0
                    && (Tokens[first - 1].Is(";") || Tokens[first - 1].Is("{") || Tokens[first - 1].Is("}") || Tokens[first - 1].Is(":"));
                if (!alone || InLoop)
                {
                    return null;
                }

                string accessor = receiver.Length == 0 ? "" : receiver + (Tokens[nameIndex - 1].Is("->") ? "->" : ".");
                return (InsertBefore(Analyzer._scan.Files[Tokens[first].File], Tokens[first]), function, accessor);
            }

            // The statement of a call, if it is nothing but the call and stands on its own: a branch's only
            // statement cannot go without its branch.
            private (int Start, int End)? OwnStatement(int nameIndex, int close)
            {
                int start = nameIndex >= 2 && (Tokens[nameIndex - 1].Is(".") || Tokens[nameIndex - 1].Is("->")) ? nameIndex - 2 : nameIndex;
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
                _takesCursor = false;
                switch (name)
                {
                    case "setCursor" or "setCursorXY" when a.Count >= 2:
                        SetCursor(a[0], a[1]);
                        painter._cursorTokens = OwnStatement(nameIndex, close);
                        break;
                    case "print" or "println" or "write" when a.Count >= 1:
                        _takesCursor = true;
                        Print(a[0]);
                        if (name == "println")
                        {
                            painter.NewLine();
                        }

                        break;
                    case "drawStr" or "drawUTF8" when a.Count >= 3:
                        SetCursor(a[0], a[1]);
                        Print(a[2]);
                        break;
                    case "setTextSize" or "setScale" when a.Count >= 1:
                        painter._size = Read(a[0]).Value ?? painter._size;
                        break;
                    case "home":
                        painter.MoveTo(0, 0);
                        break;
                    case "clear" when Analyzer._display.Family == DisplayFamily.CharLcd:
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
                Coordinate x = Read(a[0]);
                Coordinate y = Read(a[1]);
                Coordinate c = Read(a[2]);
                Coordinate d = Read(a[3]);

                // A line and GyverOLED's rect name a second corner; the rest a width and height.
                bool second = corners && kind != ElementKind.Line;
                bool line = kind == ElementKind.Line;
                int left = x.Value ?? 0;
                int top = y.Value ?? 0;
                painter.Add(new ScreenElement
                {
                    Kind = kind,
                    X = left,
                    Y = top,

                    // What depends on the run still shows, at a size to find it by.
                    Width = c.Value is { } across ? (second ? across - left : across) : line ? left + 16 : 16,
                    Height = d.Value is { } down ? (second ? down - top : down) : line ? top : 8,
                    IsPlaced = x.Value is not null && y.Value is not null && c.Value is not null && d.Value is not null,
                    IsConditional = IsConditional,
                    IsAlternative = IsInElse,
                    XSpan = x.Span,
                    YSpan = y.Span,
                    XCode = x.Code,
                    YCode = y.Code,
                    WidthSpan = second ? null : c.Span,
                    HeightSpan = second ? null : d.Span,
                    WidthCode = second ? null : c.Code,
                    HeightCode = second ? null : d.Code,
                    RemoveSpans = RemovalOwn(),
                    Location = LocationAt(a[0].Start),
                }, InLoop);
            }

            private (int NameIndex, int Close) _call;
            private bool _takesCursor;

            private bool InLoop => Loops - _ranLoops + painter._outerLoops > 0;

            // What a display class writes one cell at a time: what shape is in it only the run knows.
            private void Cell((int Start, int End) argument)
            {
                (IReadOnlyList<CodeToken> t, int start, int end, Dictionary<string, Bound> _, CodeFunction _, bool _) = Resolve(argument);
                painter.AddCell(string.Concat(t.Skip(start).Take(end - start).Select(x => x.Text)), IsConditional, InLoop, IsInElse, LocationAt(argument.Start));
            }

            // The one thing an if does, such as a leading zero: it shares its place with what follows.
            private bool IsItsBranchAlone()
            {
                int first = _call.NameIndex >= 2 && (Tokens[_call.NameIndex - 1].Is(".") || Tokens[_call.NameIndex - 1].Is("->")) ? _call.NameIndex - 2 : _call.NameIndex;
                if (first == 0)
                {
                    return false;
                }

                int after = _call.Close + 2;
                return Tokens[first - 1].Is(")") || Tokens[first - 1].Is("else") || (Tokens[first - 1].Is("{") && after < Tokens.Count && Tokens[after].Is("}"));
            }

            // A list reached through a pointer or a cast: what it points at, through locals and parameters.
            private CodeStringArray? ArrayBehind(IReadOnlyList<CodeToken> t, int start, int end, Dictionary<string, Bound> scope, CodeFunction where, int depth = 0)
            {
                if (depth > CallDepth)
                {
                    return null;
                }

                for (int k = start; k < end; k++)
                {
                    if (t[k].Kind != TokenKind.Identifier)
                    {
                        continue;
                    }

                    string name = t[k].Text;
                    if (Analyzer._scan.StringArrays.TryGetValue(name, out CodeStringArray? found))
                    {
                        return found;
                    }

                    if (scope.TryGetValue(name, out Bound? bound))
                    {
                        if (ArrayBehind(bound.Tokens, bound.Start, bound.End, bound.Bindings, bound.Function, depth + 1) is { } viaBound)
                        {
                            return viaBound;
                        }
                    }
                    else if (LocalOf(where, name) is { InitEnd: > 0 } local && local.InitEnd - local.InitStart <= 4
                        && ArrayBehind(where.Tokens, local.InitStart, local.InitEnd, scope, where, depth + 1) is { } viaLocal)
                    {
                        return viaLocal;
                    }
                }

                return null;
            }

            // Where a cursor of its own can go: before a print that stands alone, outside loops and shared code, at a place that is known.
            // Inside a helper others use too, that is before the call to it, as long as the helper has not set a cursor itself.
            private (SourceInsert? Insert, string Receiver) CursorSite()
            {
                if (!_takesCursor || InLoop || !painter._placed)
                {
                    return (null, "");
                }

                string receiver = ReceiverOf(Tokens, _call.NameIndex, 0);
                if (painter._shared)
                {
                    if (painter._callSite is not { } site)
                    {
                        return (null, "");
                    }

                    if (site.Receiver.Length > 0)
                    {
                        return (site.Insert, site.Receiver);
                    }

                    (IReadOnlyList<CodeToken> t, int start, int end, Dictionary<string, Bound> _, CodeFunction _, bool _) = bindings.TryGetValue(receiver, out Bound? bound)
                        ? (bound.Tokens, bound.Start, bound.End, bound.Bindings, bound.Function, bound.Shared)
                        : (Tokens, 0, 0, bindings, function, false);
                    string name = end - start == 1 ? t[start].Text : receiver;
                    if (!Analyzer.IsDisplayObject(name, site.Caller))
                    {
                        return (null, "");
                    }

                    bool pointer = site.Caller.Parameters.Any(p => p.Name == name && p.Type.Contains('*', StringComparison.Ordinal));
                    return (site.Insert, name + (pointer ? "->" : "."));
                }

                if (OwnStatement(_call.NameIndex, _call.Close) is not { } own)
                {
                    return (null, "");
                }

                CodeFile file = Analyzer._scan.Files[Tokens[own.Start].File];
                string accessor = receiver.Length == 0 ? "" : receiver + (Tokens[_call.NameIndex - 1].Is("->") ? "->" : ".");
                return (InsertBefore(file, Tokens[own.Start]), accessor);
            }

            // Conditional drawing, or a loop's, has more around it than its own statement.
            private IReadOnlyList<SourceSpan> RemovalOwn()
            {
                IReadOnlyList<SourceSpan> removal = IsConditional || InLoop ? [] : RemovalOf(OwnStatement(_call.NameIndex, _call.Close));
                painter._cursorTokens = null;
                return removal;
            }

            private void SetCursor((int Start, int End) x, (int Start, int End) y)
            {
                Coordinate column = Read(x);
                Coordinate row = Read(y);
                painter._placed = (column.Value is not null || column.Later is not null) && (row.Value is not null || row.Later is not null);
                painter._x = column.Value ?? painter._x;
                painter._y = row.Value ?? painter._y;
                painter._xSpan = column.Span;
                painter._ySpan = row.Span;
                painter._xCode = column.Code;
                painter._yCode = row.Code;
                painter._xLater = column.Later;
                painter._yLater = row.Later;
                painter._callSite = null;
                painter._run++;
            }

            private SourceLocation LocationAt(int index)
            {
                CodeToken token = Tokens[Math.Min(index, Tokens.Count - 1)];
                CodeFile file = Analyzer._scan.Files[token.File];
                return new SourceLocation(file.Path, file.LineOf(token.Offset));
            }

            // A coordinate where it is written: here, or at the call that bound it. A number is just that. Anything
            // else shows as written; a number can take its place if all it does is place this, and nothing changes it.
            private Coordinate Read((int Start, int End) range)
            {
                (IReadOnlyList<CodeToken> t, int start, int end, Dictionary<string, Bound> scope, CodeFunction where, bool shared) = Resolve(range);
                if (end <= start)
                {
                    return default;
                }

                if (end - start == 1 && t[start].Kind == TokenKind.Number)
                {
                    return new Coordinate(ParseInteger(t[start].Text), shared ? null : Analyzer.Span(t[start]), null, null);
                }

                var reckoning = new Reckoning(Analyzer, where, scope, null);
                int? value = reckoning.Of(t, start, end);
                bool changes = reckoning.Changes || (InLoop && reckoning.UsesLocals);

                // A constant in code other screens share: nothing here can change it, and nothing here depends on it.
                if (shared && value is not null && !changes)
                {
                    return new Coordinate(value, null, null, null);
                }

                SourceSpan whole = Analyzer.SpanOver(t, start, end);
                bool placesOnly = !changes && !shared;
                var code = new CodeExpression(whole.Expected, shared ? null : whole, !placesOnly, placesOnly ? Analyzer.DeadWith(where, t, start, end) : []);
                Func<int, int?>? later = value is null && reckoning.NeedsPrinted && !changes ? printed => new Reckoning(Analyzer, where, scope, printed).Of(t, start, end) : null;
                return new Coordinate(value, placesOnly ? whole : null, code, later);
            }

            private (IReadOnlyList<CodeToken> Tokens, int Start, int End, Dictionary<string, Bound> Bindings, CodeFunction Where, bool Shared) Resolve((int Start, int End) range)
            {
                IReadOnlyList<CodeToken> tokens = Tokens;
                int start = range.Start;
                int end = range.End;
                Dictionary<string, Bound> scope = bindings;
                CodeFunction where = function;
                bool shared = painter._shared;
                for (int hops = 0; hops < CallDepth && end - start == 1 && tokens[start].Kind == TokenKind.Identifier && scope.TryGetValue(tokens[start].Text, out Bound? bound); hops++)
                {
                    (tokens, start, end, scope, where, shared) = (bound.Tokens, bound.Start, bound.End, bound.Bindings, bound.Function, bound.Shared);
                }

                return (tokens, start, end, scope, where, shared);
            }

            private void Print((int Start, int End) range)
            {
                (IReadOnlyList<CodeToken> t, int start, int end, Dictionary<string, Bound> scope, CodeFunction where, bool shared) = Resolve(range);

                // F("..."), PSTR("..."), String("...") say the same as their argument.
                while (end - start >= 4 && t[start].Kind == TokenKind.Identifier && t[start].Text is "F" or "PSTR" or "String" or "FPSTR" && t[start + 1].Is("(") && SketchScanner.Matching(t, start + 1, "(", ")") == end - 1)
                {
                    start += 2;
                    end--;
                }

                string expression = string.Concat(t.Skip(start).Take(end - start).Select(x => x.Text));
                bool inLoop = InLoop;

                // What a menu's loop draws around its items (a cursor, a mark) belongs to the menu.
                if (inLoop && painter.Menu is not null)
                {
                    return;
                }

                bool alternative = IsInElse;
                if (end - start == 1 && t[start].Kind is TokenKind.String or TokenKind.Char)
                {
                    string text = SketchScanner.Decode(t[start]);
                    if (!(inLoop && string.IsNullOrWhiteSpace(text)))
                    {
                        (SourceInsert? Insert, string Receiver) site = CursorSite();
                        painter.AddText(text, shared ? null : Analyzer.Span(t[start]), IsConditional, inLoop, alternative || (_conditional > 0 && text.Length == 1 && IsItsBranchAlone()), RemovalOwn(), site);
                    }

                    return;
                }

                if (end - start == 1 && t[start].Kind == TokenKind.Number)
                {
                    (SourceInsert? Insert, string Receiver) site = CursorSite();
                    painter.AddText(t[start].Text, null, IsConditional, inLoop, alternative, RemovalOwn(), site);
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

                if (inLoop && ArrayBehind(t, start, end, scope, where) is { } behind)
                {
                    painter.AddMenu(behind, Analyzer);
                    return;
                }

                if (PrintedByFunction(t, start, end, scope, where, inLoop, alternative))
                {
                    return;
                }

                // cond ? "A" : "B" shows one of them: the first gives the preview, the longest the width.
                string[] literals = [.. t.Skip(start).Take(end - start).Where(x => x.Kind is TokenKind.String or TokenKind.Char).Select(SketchScanner.Decode)];
                SourceSpan? written = null;
                if (!shared && end > start && literals.Length == 0)
                {
                    CodeFile file = Analyzer._scan.Files[t[start].File];
                    int from = t[start].Offset;
                    int to = t[end - 1].Offset + t[end - 1].Text.Length;
                    written = new SourceSpan(file.Path, from, to - from, file.Text[from..to]);
                }

                // What can be worked out shows as what it comes to.
                if (literals.Length == 0 && new Reckoning(Analyzer, where, scope, null).Of(t, start, end) is { } known)
                {
                    literals = [known.ToString(CultureInfo.InvariantCulture)];
                }

                (SourceInsert? Insert, string Receiver) valueSite = CursorSite();
                painter.AddValue(expression, literals, IsConditional, inLoop, alternative, RemovalOwn(), written, valueSite);
            }

            // Text that a function of the sketch says, or a #define: what it comes to on this screen. In a loop, with the
            // loop's variable in it, it says one item for each number, and those are a list.
            private bool PrintedByFunction(IReadOnlyList<CodeToken> t, int start, int end, Dictionary<string, Bound> scope, CodeFunction where, bool inLoop, bool alternative)
            {
                SketchRun run = Analyzer.Runner;
                bool calls = false;
                bool mentions = false;
                string? variable = _headers.Count > 0 ? _headers.Peek()?.Variable : null;
                var known = new Dictionary<string, object?>(StringComparer.Ordinal);
                for (int k = start; k < end; k++)
                {
                    if (t[k].Kind != TokenKind.Identifier)
                    {
                        continue;
                    }

                    bool isCall = k + 1 < end && t[k + 1].Is("(");
                    calls |= isCall && run.IsFunction(t[k].Text);
                    mentions |= t[k].Is(variable ?? "\0");

                    // What it is given: a parameter or a local of the code that draws, as far as that can be told.
                    if (!isCall && !t[k].Is(variable ?? "\0") && new Reckoning(Analyzer, where, scope, null).Of(t, k, k + 1) is { } number)
                    {
                        known[t[k].Text] = (long)number;
                    }
                }

                bool isMacro = end - start == 1 && t[start].Kind == TokenKind.Identifier && Analyzer._scan.Macros.ContainsKey(t[start].Text);
                if (!calls && !isMacro)
                {
                    return false;
                }

                if (inLoop && mentions)
                {
                    var items = new List<string>();
                    (int first, int limit) = LoopRange();
                    for (int i = first; i < limit; i++)
                    {
                        known[variable!] = (long)i;
                        if (run.Evaluate(t, start, end, known) is not TextPointer item)
                        {
                            break;
                        }

                        items.Add(item.Text);
                    }

                    if (items.Count == 0)
                    {
                        return false;
                    }

                    painter.AddMenu(items);
                    return true;
                }

                if (run.Evaluate(t, start, end, known) is not TextPointer answer)
                {
                    return false;
                }

                // Its literal can be edited where it is, if this screen alone comes to it.
                SourceSpan? span = answer.Origin is { } origin && run.ReturnedFromOwnCase ? Analyzer.Span(origin) : null;
                (SourceInsert? Insert, string Receiver) site = CursorSite();
                painter.AddText(answer.Text, span, IsConditional, inLoop, alternative, RemovalOwn(), site);
                return true;
            }

            // Where this loop's variable goes: the list behind it, from what it starts at to the smallest that its condition
            // says. for (i = top; i < count && i < top + 3; ++i) shows a window of a longer list: the window's own bound is left
            // out, and the list starts from its beginning. With no bound, the run stops at the first item that cannot be told.
            private (int First, int Limit) LoopRange()
            {
                const int Most = 64;
                if (_headers.Peek() is not { } header)
                {
                    return (0, Most);
                }

                var reckon = new Reckoning(Analyzer, function, bindings, null);
                int first = Math.Max(reckon.Of(Tokens, header.StartOf, header.StartEnd) ?? 0, 0);
                int limit = first + Most;
                int from = header.ConditionStart;
                int nested = 0;
                for (int k = header.ConditionStart; k <= header.ConditionEnd; k++)
                {
                    bool ends = k == header.ConditionEnd || (nested == 0 && Tokens[k].Is("&&"));
                    if (!ends)
                    {
                        nested += Tokens[k].Is("(") ? 1 : Tokens[k].Is(")") ? -1 : 0;
                        continue;
                    }

                    if (k - from >= 3 && Tokens[from].Is(header.Variable) && (Tokens[from + 1].Is("<") || Tokens[from + 1].Is("<="))
                        && !Tokens.Skip(from + 2).Take(k - from - 2).Any(x => Analyzer._listPlaces.Contains(x.Text))
                        && new Reckoning(Analyzer, function, bindings, null).Of(Tokens, from + 2, k) is { } bound)
                    {
                        limit = Math.Min(limit, Math.Max(bound + (Tokens[from + 1].Is("<=") ? 1 : 0), 0));
                    }

                    from = k + 1;
                }

                return (first, limit);
            }

            private (IReadOnlyList<CodeToken>, int, int, Dictionary<string, Bound>) ResolveName(string name) =>
                bindings.TryGetValue(name, out Bound? bound) ? (bound.Tokens, bound.Start, bound.End, bound.Bindings) : (Tokens, 0, 0, bindings);
        }

        internal ScreenCodeAnalyzer Owner => owner;

        private void MoveTo(int x, int y)
        {
            _x = x;
            _y = y;
            ForgetCursor();
            _callSite = null;
            _run++;
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
            _runOf.Add(_run);
        }

        /// <summary>Taking out the call that drew them takes these out, if they are one thing.</summary>
        private void TakesOutWith(int first, IReadOnlyList<SourceSpan> removal)
        {
            if (Elements.Skip(first).Count(e => !e.IsAlternative) != 1)
            {
                return;
            }

            for (int i = first; i < Elements.Count; i++)
            {
                Elements[i] = Elements[i] with { RemoveSpans = removal };
            }
        }

        // One cell of a shape only the run knows, as a block; cells side by side make one thing.
        private void AddCell(string expression, bool conditional, bool fromLoop, bool alternative, SourceLocation location)
        {
            const string Block = "\u00FF";
            PlaceFor(1);
            if (_lastCell == Elements.Count - 1 && _lastCell >= 0 && Elements[_lastCell] is { } last && last.Y == _y && last.X + last.Width == _x && last.IsConditional == conditional && last.IsAlternative == alternative)
            {
                Elements[_lastCell] = last with { Width = last.Width + 1, Sample = last.Sample + Block };
            }
            else
            {
                Add(Placed(new ScreenElement { Kind = ElementKind.Value, Text = expression, Width = 1, Sample = Block, IsConditional = conditional, IsAlternative = alternative, Location = location }, (null, "")), fromLoop);
                _lastCell = Elements.Count - 1;
            }

            _x += Advance(1);
            ForgetCursor();
        }

        /// <summary>
        /// A value's width is a guess. Prints in a row that would run off the display by that guess are narrower than
        /// it: what the sketch lines up fits.
        /// </summary>
        public void FitRuns()
        {
            if (display.Family != DisplayFamily.CharLcd)
            {
                return;
            }

            static int Right(ScreenElement e) => e.X + (e.Kind == ElementKind.Value ? e.Width : e.Text.Length);
            foreach (IGrouping<int, int> run in Enumerable.Range(0, Elements.Count).GroupBy(i => _runOf[i]))
            {
                int excess = run.Where(i => Elements[i] is { IsAlternative: false, Kind: ElementKind.Text or ElementKind.Value }).Select(i => Right(Elements[i])).DefaultIfEmpty(0).Max() - display.Width;
                int shift = 0;
                foreach (int i in run)
                {
                    ScreenElement element = Elements[i];
                    int narrower = excess > 0 && element is { Kind: ElementKind.Value, Sample: null, IsAlternative: false } ? Math.Min(excess, element.Width - 1) : 0;
                    excess -= narrower;
                    if (shift != 0 || narrower != 0)
                    {
                        Elements[i] = element with { X = element.X - shift, Width = element.Width - narrower };
                    }

                    shift += narrower;
                }
            }
        }

        private int Advance(int characters) => display.Family == DisplayFamily.CharLcd ? characters : characters * 6 * _size;

        private void ForgetCursor()
        {
            _xSpan = _ySpan = null;
            _xCode = _yCode = null;
            _xLater = _yLater = null;
        }

        // A position that centers what is printed is known once the width is.
        private void PlaceFor(int characters)
        {
            _x = _xLater?.Invoke(characters) ?? _x;
            _y = _yLater?.Invoke(characters) ?? _y;
        }

        // With nothing to replace on an axis, it takes a cursor of its own.
        private ScreenElement Placed(ScreenElement element, (SourceInsert? Insert, string Receiver) site)
        {
            bool needsOwn = (_xSpan is null && _xCode is null) || (_ySpan is null && _yCode is null);
            return element with
            {
                X = _x,
                Y = _y,
                Size = _size,
                XSpan = _xSpan,
                YSpan = _ySpan,
                XCode = _xCode,
                YCode = _yCode,
                IsPlaced = _placed,
                CursorInsert = needsOwn ? site.Insert : null,
                CursorReceiver = needsOwn ? site.Receiver : "",
            };
        }

        private void AddText(string text, SourceSpan? span, bool conditional, bool fromLoop, bool alternative, IReadOnlyList<SourceSpan> removal, (SourceInsert? Insert, string Receiver) site)
        {
            PlaceFor(text.Length);
            if (Menu is null && !fromLoop)
            {
                _titleY = Math.Max(_titleY, _y);
            }

            Add(Placed(new ScreenElement { Kind = ElementKind.Text, Text = text, TextSpan = span, IsConditional = conditional, IsAlternative = alternative, RemoveSpans = removal, Location = span is null ? null : new SourceLocation(span.File, owner._scan.Files.First(f => f.Path == span.File).LineOf(span.Offset)) }, site), fromLoop);
            _x += Advance(text.Length);
            ForgetCursor();
            _callSite = alternative ? _callSite : null;
        }

        private void AddValue(string expression, string[] literals, bool conditional, bool fromLoop, bool alternative, IReadOnlyList<SourceSpan> removal, SourceSpan? written, (SourceInsert? Insert, string Receiver) site)
        {
            // After a leading zero that shares its place, it is two digits.
            int width = literals.Length > 0 ? literals.Max(l => l.Length)
                : Elements.LastOrDefault() is { Kind: ElementKind.Text, Text: "0", IsAlternative: true } zero && zero.X == _x && zero.Y == _y ? 2
                : Math.Clamp(expression.Length, 2, 4);
            PlaceFor(width);
            Add(Placed(new ScreenElement { Kind = ElementKind.Value, Text = expression, Width = width, Sample = literals.FirstOrDefault(), IsConditional = conditional, IsAlternative = alternative, RemoveSpans = removal, ExpressionSpan = written, Location = written is null ? null : new SourceLocation(written.File, owner._scan.Files.First(f => f.Path == written.File).LineOf(written.Offset)) }, site), fromLoop);
            _x += Advance(width);
            ForgetCursor();
            _callSite = alternative ? _callSite : null;
        }

        private void AddMenu(CodeStringArray items, ScreenCodeAnalyzer analyzer) =>
            SetMenu(items.Packed is { } packed
                ? [.. packed.Select(text => new ScreenMenuItem { Text = text })]
                : [.. items.Items.Select(i => new ScreenMenuItem { Text = SketchScanner.Decode(i), TextSpan = analyzer.Span(i) })]);

        private void AddMenu(List<string> items) => SetMenu([.. items.Select(text => new ScreenMenuItem { Text = text })]);

        private void SetMenu(IReadOnlyList<ScreenMenuItem> items)
        {
            // What else the loop draws (the cursor, a mark) is the menu's own.
            foreach (int index in Enumerable.Reverse(_fromLoops))
            {
                Elements.RemoveAt(index);
                _runOf.RemoveAt(index);
            }

            _lastCell = -1;

            _fromLoops.Clear();
            int top = _titleY + 1;
            int rows = display.Family == DisplayFamily.CharLcd ? Math.Max(1, display.Height - top) : Math.Max(1, (display.Height - top) / 8);
            Menu = new ScreenMenu
            {
                Items = items,
                Top = top,
                Rows = rows,
            };
        }
    }

    // ---- no screen variable: functions that draw --------------------------------------------------

    private ScreenDocument? FromFunctions()
    {
        // Helpers draw too, so a screen is a drawing function that nothing but non-drawing code calls with arguments to fill.
        CodeFunction[] drawing = [.. _facts.Keys.Where(f => _facts[f].Draws && f.Parameters.Count == 0 && f.Name is not ("setup" or "loop" or "update" or "begin") && !_surfaceTypes.Contains(f.Class))];
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
            painter.FitRuns();
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
