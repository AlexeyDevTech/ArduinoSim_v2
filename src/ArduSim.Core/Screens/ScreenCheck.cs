using System.Globalization;

namespace ArduSim.Core.Screens;

public enum ScreenProblemKind
{
    /// <summary>An expression that cannot be read. Detail: the expression.</summary>
    BadExpression,

    /// <summary>A name in an expression that nothing has. Detail: the name.</summary>
    UnknownName,

    /// <summary>An action or trigger names a variable there is not, or one of the wrong kind. Detail: the name.</summary>
    WrongVariable,

    /// <summary>A screen wants more programmable characters at once than a character display has. Detail: how many.</summary>
    TooManySymbols,

    /// <summary>A letter with no shape here, which shows as a question mark. Detail: the letter.</summary>
    NoShape,

    /// <summary>A symbol that is not there. Detail: the name.</summary>
    UnknownSymbol,

    /// <summary>A screen has an input and the schematic no keypad to type on.</summary>
    NoKeypad,

    /// <summary>A screen has two inputs; digits go to the first.</summary>
    TwoInputs,

    /// <summary>An action takes what was typed on a screen with no input.</summary>
    NoInput,

    /// <summary>An action drives a part that is not wired. Detail: the part.</summary>
    NoPart,
}

/// <summary>Something a generated project says that will not work as it reads.</summary>
/// <param name="Screen">The screen it is on; empty for what works from any screen.</param>
public sealed record ScreenProblem(string Screen, ScreenProblemKind Kind, string Detail = "");

/// <summary>Finds out here what would otherwise be seen only on the display or in a compiler's message (ADR-019).</summary>
public static class ScreenCheck
{
    public const int ProgrammableCharacters = 8;

    public static IReadOnlyList<ScreenProblem> Of(ScreenDocument document, ScreenWiring? wiring)
    {
        ArgumentNullException.ThrowIfNull(document);
        var problems = new List<ScreenProblem>();
        foreach (ScreenNode screen in document.Screens)
        {
            CheckScreen(document, wiring, screen, problems);
        }

        foreach (ScreenTransition transition in document.FromAnywhere)
        {
            CheckTransition(document, wiring, "", null, transition, problems);
        }

        return [.. problems.Distinct()];
    }

    private static void CheckScreen(ScreenDocument document, ScreenWiring? wiring, ScreenNode screen, List<ScreenProblem> problems)
    {
        void Expression(string text) => CheckExpression(document, screen.Id, text, problems);
        int inputs = screen.Elements.Count(e => e.Kind == ElementKind.Input);
        foreach (ScreenElement element in screen.Elements)
        {
            Expression(element.When);
            switch (element.Kind)
            {
                case ElementKind.Value or ElementKind.BigNumber when !ScreenExpression.IsHookName(document, element.Text):
                    Expression(element.Text);
                    break;
                case ElementKind.Bar:
                    Expression(element.Text);
                    Expression(element.Maximum);
                    break;
                case ElementKind.Symbol when ScreenFont.Find(document, element.Text) is null:
                    problems.Add(new ScreenProblem(screen.Id, ScreenProblemKind.UnknownSymbol, element.Text));
                    break;
                case ElementKind.Input when !CanHold(document, element.Text):
                    problems.Add(new ScreenProblem(screen.Id, ScreenProblemKind.WrongVariable, element.Text));
                    break;
                default:
                    break;
            }
        }

        if (inputs > 1)
        {
            problems.Add(new ScreenProblem(screen.Id, ScreenProblemKind.TwoInputs));
        }

        if (inputs > 0 && wiring is not null && wiring.Inputs.All(i => i.Kind != ScreenInputKind.Keypad))
        {
            problems.Add(new ScreenProblem(screen.Id, ScreenProblemKind.NoKeypad));
        }

        CheckActions(document, wiring, screen.Id, screen, screen.OnEnter, problems);
        foreach (ScreenMenuItem item in screen.Menu?.Items ?? [])
        {
            if (!ScreenExpression.IsHookName(document, item.Value))
            {
                Expression(item.Value);
            }

            Expression(item.Mark);
            CheckActions(document, wiring, screen.Id, screen, item.Actions, problems);
        }

        foreach (ScreenTransition transition in screen.Transitions)
        {
            CheckTransition(document, wiring, screen.Id, screen, transition, problems);
        }

        CheckFont(document, screen, problems);
    }

    private static void CheckTransition(ScreenDocument document, ScreenWiring? wiring, string where, ScreenNode? screen, ScreenTransition transition, List<ScreenProblem> problems)
    {
        CheckExpression(document, where, transition.Trigger.When, problems);
        if (transition.Trigger.Kind == TriggerKind.Condition)
        {
            CheckExpression(document, where, transition.Trigger.Value, problems);
        }
        else if (transition.Trigger.Kind == TriggerKind.Finished && !document.Variables.Any(v => v.Name == transition.Trigger.Value && v.Kind == VariableKind.Countdown))
        {
            problems.Add(new ScreenProblem(where, ScreenProblemKind.WrongVariable, transition.Trigger.Value));
        }

        CheckActions(document, wiring, where, screen, transition.Actions, problems);
    }

    private static void CheckActions(ScreenDocument document, ScreenWiring? wiring, string where, ScreenNode? screen, IReadOnlyList<ScreenAction> actions, List<ScreenProblem> problems)
    {
        foreach (ScreenAction action in actions)
        {
            ScreenVariable? variable = document.Variables.FirstOrDefault(v => v.Name == action.Target);
            bool fits = action.Kind switch
            {
                ActionKind.Set or ActionKind.Add => CanHold(document, action.Target),
                ActionKind.Toggle => variable?.Kind is VariableKind.Flag or VariableKind.Choice,
                ActionKind.Start or ActionKind.Stop or ActionKind.StartStop or ActionKind.Reset => variable?.Kind is VariableKind.Stopwatch or VariableKind.Countdown,
                _ => true,
            };
            if (!fits)
            {
                problems.Add(new ScreenProblem(where, ScreenProblemKind.WrongVariable, action.Target));
            }

            switch (action.Kind)
            {
                case ActionKind.Set or ActionKind.Add or ActionKind.Output:
                    CheckExpression(document, where, action.Value, problems);
                    break;
                case ActionKind.InputCommit when screen is null || screen.Elements.All(e => e.Kind != ElementKind.Input):
                    problems.Add(new ScreenProblem(where, ScreenProblemKind.NoInput));
                    break;
                default:
                    break;
            }

            if (action.Kind is ActionKind.Beep or ActionKind.Output && wiring is not null && wiring.Outputs.All(p => p.Part != action.Target))
            {
                problems.Add(new ScreenProblem(where, ScreenProblemKind.NoPart, action.Target));
            }
        }
    }

    private static bool CanHold(ScreenDocument document, string name) =>
        ScreenExpression.Settable.Contains(name) || document.Variables.Any(v => v.Name == name);

    private static void CheckExpression(ScreenDocument document, string where, string text, List<ScreenProblem> problems)
    {
        ExpressionCheck check = ScreenExpression.Check(document, text);
        if (check.Fault == ExpressionFault.Syntax)
        {
            problems.Add(new ScreenProblem(where, ScreenProblemKind.BadExpression, text));
        }
        else if (check.Fault == ExpressionFault.UnknownName)
        {
            problems.Add(new ScreenProblem(where, ScreenProblemKind.UnknownName, check.Name));
        }
    }

    private static void CheckFont(ScreenDocument document, ScreenNode screen, List<ScreenProblem> problems)
    {
        foreach (char letter in screen.Elements.Where(e => e.Kind == ElementKind.Text).SelectMany(e => e.Text).Concat(screen.Menu?.Items.SelectMany(i => i.Text) ?? []))
        {
            if (letter >= 0x80 && ScreenFont.LookAlike(letter) is null && ScreenFont.Shape(letter) is null)
            {
                problems.Add(new ScreenProblem(screen.Id, ScreenProblemKind.NoShape, letter.ToString()));
            }
        }

        if (document.Display.Family == DisplayFamily.CharLcd)
        {
            int wanted = SymbolsAtOnce(document, screen);
            if (wanted > ProgrammableCharacters)
            {
                problems.Add(new ScreenProblem(screen.Id, ScreenProblemKind.TooManySymbols, wanted.ToString(CultureInfo.InvariantCulture)));
            }
        }
    }

    /// <summary>
    /// How many programmable characters a screen can want at once: what it always shows, the longest of the words a
    /// value can be, and the worst place a menu's list can scroll to. What code of the project's own prints is not counted.
    /// </summary>
    public static int SymbolsAtOnce(ScreenDocument document, ScreenNode screen)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(screen);
        var always = new HashSet<string>(StringComparer.Ordinal);
        foreach (ScreenElement element in screen.Elements)
        {
            switch (element.Kind)
            {
                case ElementKind.Text:
                    AddText(document, element.Text, always);
                    break;
                case ElementKind.Symbol:
                    always.Add(element.Text);
                    break;
                case ElementKind.BigNumber:
                    always.Add(ScreenFont.BigTop);
                    always.Add(ScreenFont.BigBottom);
                    always.Add(ScreenFont.BigBoth);
                    break;
                case ElementKind.Value:
                    AddWorstWord(document, element.Text, element.Format, element.Options, always);
                    break;
                default:
                    break;
            }
        }

        if (screen.Menu is not { } menu || menu.Items.Count == 0)
        {
            return always.Count;
        }

        AddText(document, menu.Marker, always);
        int worst = always.Count;
        int rows = Math.Clamp(menu.Rows, 1, menu.Items.Count);
        for (int top = 0; top + rows <= menu.Items.Count; top++)
        {
            var window = new HashSet<string>(always, StringComparer.Ordinal);
            for (int i = top; i < top + rows; i++)
            {
                AddText(document, menu.Items[i].Text, window);
                AddWorstWord(document, menu.Items[i].Value, menu.Items[i].Format, [], window);
            }

            worst = Math.Max(worst, window.Count);
        }

        return worst;
    }

    private static void AddWorstWord(ScreenDocument document, string source, ValueFormat format, IReadOnlyList<string> words, HashSet<string> into)
    {
        ScreenVariable? variable = document.Variables.FirstOrDefault(v => v.Name == source);
        IReadOnlyList<string> list = format == ValueFormat.List ? words
            : format == ValueFormat.Auto && variable?.Kind is VariableKind.Flag or VariableKind.Choice ? variable.Options
            : [];
        HashSet<string>? worst = null;
        foreach (string word in list)
        {
            var these = new HashSet<string>(StringComparer.Ordinal);
            AddLetters(word, these);
            these.ExceptWith(into);
            if (worst is null || these.Count > worst.Count)
            {
                worst = these;
            }
        }

        if (worst is not null)
        {
            into.UnionWith(worst);
        }
    }

    private static void AddText(ScreenDocument document, string text, HashSet<string> into)
    {
        foreach (ScreenFont.TextPart part in ScreenFont.Split(document, text))
        {
            if (part.Letters is { } letters)
            {
                AddLetters(letters, into);
            }
            else
            {
                into.Add(part.Symbol!.Name);
            }
        }
    }

    private static void AddLetters(string text, HashSet<string> into)
    {
        foreach (char c in text)
        {
            if (c >= 0x80 && ScreenFont.LookAlike(c) is null && ScreenFont.Shape(c) is not null)
            {
                into.Add(ScreenFont.Capital(c).ToString());
            }
        }
    }
}

/// <summary>What a value looks like before the run decides: for the picture of a screen.</summary>
public static class ScreenSample
{
    // Some time of some day, with no two digits alike where that helps to see which is which.
    private static readonly Dictionary<string, long> BuiltIn = new(StringComparer.Ordinal)
    {
        ["hour"] = 12,
        ["minute"] = 34,
        ["second"] = 56,
        ["day"] = 31,
        ["month"] = 12,
        ["year"] = 2026,
        ["clock"] = (12 * 3600) + (34 * 60) + 56,
        ["date"] = 20261231,
        ["weekday"] = 3,
        ["uptime"] = 3725,
        ["menuItem"] = 1,
        ["menuCount"] = 1,
    };

    /// <summary>The number it starts as, as far as that can be told without running it.</summary>
    public static long Number(ScreenDocument document, string source)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(source);
        string name = source.Trim();
        if (BuiltIn.TryGetValue(name, out long known))
        {
            return known;
        }

        if (document.Variables.FirstOrDefault(v => v.Name == name) is { } variable)
        {
            return variable.Initial;
        }

        return long.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number) ? number : 0;
    }

    /// <summary>A value as it would read; null when only the code can say: a hook's, an expression's.</summary>
    public static string? Text(ScreenDocument document, string source, ValueFormat format, IReadOnlyList<string> words)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(words);
        string name = source.Trim();
        ScreenVariable? variable = document.Variables.FirstOrDefault(v => v.Name == name);
        bool isOneName = variable is not null || BuiltIn.ContainsKey(name) || long.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        if (!isOneName)
        {
            return format == ValueFormat.Auto || !ScreenExpression.Check(document, name).IsFine ? null : Formatted(0, format);
        }

        long value = Number(document, name);
        IReadOnlyList<string> list = format == ValueFormat.List ? words
            : format == ValueFormat.Auto && variable?.Kind is VariableKind.Flag or VariableKind.Choice ? variable.Options
            : [];
        if (list.Count > 0)
        {
            return value >= 0 && value < list.Count ? list[(int)value] : "";
        }

        ValueFormat shown = format != ValueFormat.Auto ? format : variable?.Kind switch
        {
            VariableKind.Time => ValueFormat.HourMinute,
            VariableKind.Stopwatch or VariableKind.Countdown => ValueFormat.MinuteSecond,
            _ => ValueFormat.Auto,
        };
        return Formatted(value, shown);
    }

    private static string Formatted(long value, ValueFormat format)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        return format switch
        {
            ValueFormat.TwoDigits => value.ToString("00", c),
            ValueFormat.HourMinute => string.Create(c, $"{value / 3600:00}:{value / 60 % 60:00}"),
            ValueFormat.HourMinuteSecond => string.Create(c, $"{value / 3600:00}:{value / 60 % 60:00}:{value % 60:00}"),
            ValueFormat.MinuteSecond => string.Create(c, $"{value / 60:00}:{value % 60:00}"),
            ValueFormat.MinuteSecondCentis => string.Create(c, $"{value / 60000:00}:{value / 1000 % 60:00}.{value / 10 % 100:00}"),
            ValueFormat.DateDayFirst => string.Create(c, $"{value % 100:00}.{value / 100 % 100:00}.{value / 10000:0000}"),
            ValueFormat.DateMonthFirst => string.Create(c, $"{value / 100 % 100:00}.{value % 100:00}.{value / 10000:0000}"),
            ValueFormat.DateYearFirst => string.Create(c, $"{value / 10000:0000}-{value / 100 % 100:00}-{value % 100:00}"),
            _ => value.ToString(c),
        };
    }

    /// <summary>How an input's mask reads before anything is typed.</summary>
    public static string Mask(ScreenElement input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.Format switch
        {
            ValueFormat.HourMinute or ValueFormat.MinuteSecond => "__:__",
            ValueFormat.HourMinuteSecond => "__:__:__",
            ValueFormat.DateDayFirst or ValueFormat.DateMonthFirst => "__.__.____",
            ValueFormat.DateYearFirst => "____-__-__",
            _ => new string('_', Math.Clamp(input.Width, 1, 9)),
        };
    }
}
