using System.Text.RegularExpressions;
using ArduSim.App.Localization;
using ArduSim.Build.Compilation;

namespace ArduSim.App.ViewModels;

/// <summary>An edit that repairs a compiler message: at (line, column), <paramref name="Remove"/> characters give way to <paramref name="Insert"/>.</summary>
public sealed record DiagnosticFix(string Label, int Remove, string Insert)
{
    /// <summary>Not an edit: the library dialog opens, searching for this.</summary>
    public string? FindLibrary { get; init; }
}

/// <summary>
/// Plain Russian beside the common clang messages, and the fix where clang names one. A message
/// with no rule stays as it is: nothing is invented.
/// </summary>
public static partial class DiagnosticHints
{
    public static string? Explain(BuildDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        string message = diagnostic.Message;
        if (Semicolon().IsMatch(message))
        {
            return Strings.Get("Hint_Semicolon");
        }

        if (Undeclared().Match(message) is { Success: true } undeclared)
        {
            return undeclared.Groups[2].Success
                ? Strings.Format("Hint_UndeclaredSuggest", undeclared.Groups[1].Value, undeclared.Groups[2].Value)
                : Strings.Format("Hint_Undeclared", undeclared.Groups[1].Value);
        }

        return message switch
        {
            _ when UnknownType().Match(message) is { Success: true } m => Strings.Format("Hint_UnknownType", m.Groups[1].Value),
            _ when FileNotFound().Match(message) is { Success: true } m => Strings.Format("Hint_FileNotFound", m.Groups[1].Value),
            _ when CloseBrace().IsMatch(message) => Strings.Get("Hint_MissingBrace"),
            _ when CloseParen().IsMatch(message) => Strings.Get("Hint_MissingParen"),
            _ when ExpectedExpression().IsMatch(message) => Strings.Get("Hint_ExpectedExpression"),
            _ when NoMatching().Match(message) is { Success: true } m => Strings.Format("Hint_NoMatching", m.Groups[1].Value),
            _ when TooFew().IsMatch(message) => Strings.Get("Hint_TooFewArgs"),
            _ when TooMany().IsMatch(message) => Strings.Get("Hint_TooManyArgs"),
            _ when NoSetupLoop().Match(message) is { Success: true } m => Strings.Format("Hint_NoSetupLoop", m.Groups[1].Value),
            _ when Redefinition().Match(message) is { Success: true } m => Strings.Format("Hint_Redefinition", m.Groups[1].Value),
            _ => null,
        };
    }

    /// <summary>The fix clang's own message names, when it has a place in a file.</summary>
    public static DiagnosticFix? FixFor(BuildDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        if (diagnostic.File is null || diagnostic.Line < 1 || diagnostic.Column < 1)
        {
            return null;
        }

        if (FileNotFound().Match(diagnostic.Message) is { Success: true } missing)
        {
            return new DiagnosticFix(Strings.Get("Fix_FindLibrary"), 0, "") { FindLibrary = missing.Groups[1].Value };
        }

        if (Semicolon().IsMatch(diagnostic.Message))
        {
            return new DiagnosticFix(Strings.Get("Fix_InsertSemicolon"), 0, ";");
        }

        return Undeclared().Match(diagnostic.Message) is { Success: true } m && m.Groups[2].Success
            ? new DiagnosticFix(Strings.Format("Fix_ReplaceWith", m.Groups[2].Value), m.Groups[1].Length, m.Groups[2].Value)
            : null;
    }

    [GeneratedRegex("^expected ';'")]
    private static partial Regex Semicolon();

    [GeneratedRegex(@"use of undeclared identifier '(.+?)'(?:; did you mean '(.+?)'\?)?")]
    private static partial Regex Undeclared();

    [GeneratedRegex("unknown type name '(.+?)'")]
    private static partial Regex UnknownType();

    [GeneratedRegex("'(.+?)' file not found")]
    private static partial Regex FileNotFound();

    [GeneratedRegex("^expected '}'")]
    private static partial Regex CloseBrace();

    [GeneratedRegex(@"^expected '\)'")]
    private static partial Regex CloseParen();

    [GeneratedRegex("^expected expression")]
    private static partial Regex ExpectedExpression();

    [GeneratedRegex("no matching function for call to '(.+?)'")]
    private static partial Regex NoMatching();

    [GeneratedRegex("^too few arguments")]
    private static partial Regex TooFew();

    [GeneratedRegex("^too many arguments")]
    private static partial Regex TooMany();

    [GeneratedRegex(@"undefined symbol: (setup|loop)(?:\(\))?$")]
    private static partial Regex NoSetupLoop();

    [GeneratedRegex("redefinition of '(.+?)'")]
    private static partial Regex Redefinition();
}
