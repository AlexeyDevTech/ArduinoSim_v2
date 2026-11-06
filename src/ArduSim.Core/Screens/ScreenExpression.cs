using System.Globalization;
using System.Text;

namespace ArduSim.Core.Screens;

/// <summary>What is wrong with an expression, if anything.</summary>
public enum ExpressionFault
{
    None,

    /// <summary>A character or sequence that has no place in one.</summary>
    Syntax,

    /// <summary>A name that is neither a variable nor one of <see cref="ScreenExpression.BuiltIn"/>.</summary>
    UnknownName,
}

/// <summary>The result of checking an expression.</summary>
/// <param name="Name">The unknown name, or what could not be read.</param>
public readonly record struct ExpressionCheck(ExpressionFault Fault, string Name = "")
{
    public bool IsFine => Fault == ExpressionFault.None;
}

/// <summary>
/// The short expressions of a generated project: whole numbers, its variables and what is built in, with
/// arithmetic, comparison and logic. They become C++ as written, with the names replaced, and they are checked
/// here so that a mistake shows in the panel where it was typed and not as a compiler's message about a generated file.
/// </summary>
public static class ScreenExpression
{
    private static readonly string[] Operators = ["&&", "||", "==", "!=", "<=", ">=", "<", ">", "+", "-", "*", "/", "%"];

    /// <summary>Names every project has: the clock's fields, the date as yyyymmdd, the time of day and the time running in seconds, the menu's place.</summary>
    public static IReadOnlyList<string> BuiltIn { get; } =
        ["hour", "minute", "second", "day", "month", "year", "weekday", "clock", "date", "uptime", "menuItem", "menuCount", "true", "false"];

    /// <summary>The built-in names that can be assigned to.</summary>
    public static IReadOnlyList<string> Settable { get; } = ["clock", "date"];

    private static IEnumerable<string> Tokens(string text)
    {
        int at = 0;
        while (at < text.Length)
        {
            char c = text[at];
            if (char.IsWhiteSpace(c))
            {
                at++;
            }
            else if (char.IsAsciiLetter(c) || c == '_')
            {
                int start = at;
                while (at < text.Length && (char.IsAsciiLetterOrDigit(text[at]) || text[at] == '_'))
                {
                    at++;
                }

                yield return text[start..at];
            }
            else if (char.IsAsciiDigit(c))
            {
                int start = at;
                while (at < text.Length && char.IsAsciiDigit(text[at]))
                {
                    at++;
                }

                yield return text[start..at];
            }
            else if (c is '(' or ')' or '!' && !(c == '!' && at + 1 < text.Length && text[at + 1] == '='))
            {
                at++;
                yield return c.ToString();
            }
            else if (Operators.FirstOrDefault(o => string.CompareOrdinal(text, at, o, 0, o.Length) == 0) is { } op)
            {
                at += op.Length;
                yield return op;
            }
            else
            {
                // Nothing here can be understood: the caller sees it as it stands.
                yield return text[at..];
                yield break;
            }
        }
    }

    private static bool IsName(string token) => char.IsAsciiLetter(token[0]) || token[0] == '_';

    private static bool IsNumber(string token) => char.IsAsciiDigit(token[0]);

    /// <summary>The names an expression uses.</summary>
    public static IEnumerable<string> NamesIn(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return Tokens(expression).Where(IsName);
    }

    /// <summary>Whether a name is a variable or part of one: a stopwatch's <c>name_ms</c> and <c>name_on</c>.</summary>
    public static bool IsKnown(ScreenDocument document, string name)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(name);
        return BuiltIn.Contains(name) || document.Variables.Any(v => v.Name == name) || TimerOf(document, name) is not null;
    }

    private static ScreenVariable? TimerOf(ScreenDocument document, string name)
    {
        string stem = name.EndsWith("_ms", StringComparison.Ordinal) || name.EndsWith("_on", StringComparison.Ordinal) ? name[..^3] : "";
        return document.Variables.FirstOrDefault(v => v.Name == stem && v.Kind is VariableKind.Stopwatch or VariableKind.Countdown);
    }

    /// <summary>A single name that is nothing the project knows: what a value is called in <c>ui_hooks.cpp</c>.</summary>
    public static bool IsHookName(ScreenDocument document, string text)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(text);
        return text.Length > 0 && (char.IsAsciiLetter(text[0]) || text[0] == '_') && text.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') && !IsKnown(document, text);
    }

    /// <summary>Checks an expression; an empty one is fine, and means nothing is asked.</summary>
    public static ExpressionCheck Check(ScreenDocument document, string expression)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(expression);
        if (expression.Trim().Length == 0)
        {
            return default;
        }

        string[] tokens = [.. Tokens(expression)];
        int at = 0;
        if (!Sum(tokens, ref at) || at != tokens.Length)
        {
            return new ExpressionCheck(ExpressionFault.Syntax, at < tokens.Length ? tokens[at] : "");
        }

        return tokens.Where(IsName).FirstOrDefault(n => !IsKnown(document, n)) is { } unknown
            ? new ExpressionCheck(ExpressionFault.UnknownName, unknown)
            : default;
    }

    // operand (operator operand)*, and that is all the shape they need: C++ decides what goes first.
    private static bool Sum(string[] tokens, ref int at)
    {
        if (!Operand(tokens, ref at))
        {
            return false;
        }

        while (at < tokens.Length && Operators.Contains(tokens[at]))
        {
            at++;
            if (!Operand(tokens, ref at))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Operand(string[] tokens, ref int at)
    {
        while (at < tokens.Length && tokens[at] is "!" or "-")
        {
            at++;
        }

        if (at >= tokens.Length)
        {
            return false;
        }

        if (tokens[at] == "(")
        {
            at++;
            if (!Sum(tokens, ref at) || at >= tokens.Length || tokens[at] != ")")
            {
                return false;
            }

            at++;
            return true;
        }

        if (IsName(tokens[at]) || IsNumber(tokens[at]))
        {
            at++;
            return true;
        }

        return false;
    }

    /// <summary>The expression with a variable's new name, written as it was otherwise; a stopwatch's parts go along.</summary>
    public static string Rename(string expression, string name, string newName)
    {
        ArgumentNullException.ThrowIfNull(expression);
        if (!NamesIn(expression).Any(n => n == name || n == name + "_ms" || n == name + "_on"))
        {
            return expression;
        }

        var text = new StringBuilder();
        int at = 0;
        while (at < expression.Length)
        {
            if (char.IsAsciiLetter(expression[at]) || expression[at] == '_')
            {
                int start = at;
                while (at < expression.Length && (char.IsAsciiLetterOrDigit(expression[at]) || expression[at] == '_'))
                {
                    at++;
                }

                string word = expression[start..at];
                text.Append(word == name ? newName : word == name + "_ms" ? newName + "_ms" : word == name + "_on" ? newName + "_on" : word);
            }
            else
            {
                text.Append(expression[at++]);
            }
        }

        return text.ToString();
    }

    /// <summary>The expression with its names as <paramref name="resolve"/> writes them.</summary>
    public static string ToCode(string expression, Func<string, string> resolve)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(resolve);
        var code = new StringBuilder();
        foreach (string token in Tokens(expression))
        {
            if (code.Length > 0 && !(token is ")" || code[^1] is '(' or '!'))
            {
                code.Append(' ');
            }

            code.Append(IsName(token) ? resolve(token)
                : IsNumber(token) && long.Parse(token, CultureInfo.InvariantCulture) > 32767 ? token + "L"
                : token);
        }

        return code.ToString();
    }
}
