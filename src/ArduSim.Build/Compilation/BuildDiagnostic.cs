using System.Globalization;
using System.Text.RegularExpressions;

namespace ArduSim.Build.Compilation;

public enum DiagnosticSeverity
{
    Note,
    Warning,
    Error,
}

/// <summary>A compiler or linker message; <see cref="File"/> is null for messages without a location.</summary>
public sealed record BuildDiagnostic(DiagnosticSeverity Severity, string Message, string? File, int Line, int Column);

/// <summary>Parses clang and lld output into <see cref="BuildDiagnostic"/>s.</summary>
public static partial class DiagnosticParser
{
    public static IReadOnlyList<BuildDiagnostic> Parse(string output)
    {
        var diagnostics = new List<BuildDiagnostic>();
        foreach (string line in output.Split('\n'))
        {
            string text = line.TrimEnd('\r');
            Match located = Located().Match(text);
            if (located.Success)
            {
                diagnostics.Add(new BuildDiagnostic(
                    Severity(located.Groups["severity"].Value),
                    located.Groups["message"].Value,
                    NormalizePath(located.Groups["file"].Value),
                    int.Parse(located.Groups["line"].Value, CultureInfo.InvariantCulture),
                    located.Groups["column"].Success ? int.Parse(located.Groups["column"].Value, CultureInfo.InvariantCulture) : 0));
                continue;
            }

            Match tool = Tool().Match(text);
            if (tool.Success)
            {
                diagnostics.Add(new BuildDiagnostic(Severity(tool.Groups["severity"].Value), tool.Groups["message"].Value, null, 0, 0));
            }
        }

        return diagnostics;
    }

    private static DiagnosticSeverity Severity(string text) => text switch
    {
        "warning" => DiagnosticSeverity.Warning,
        "note" => DiagnosticSeverity.Note,
        _ => DiagnosticSeverity.Error,
    };

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            return path;
        }
    }

    [GeneratedRegex(@"^(?<file>(?:[A-Za-z]:)?[^:]+):(?<line>\d+):(?:(?<column>\d+):)? (?<severity>fatal error|error|warning|note): (?<message>.*)$")]
    private static partial Regex Located();

    [GeneratedRegex(@"^(?:\S*[\\/])?(?:ld\.lld|lld|clang\+\+|clang|x86_64-w64-mingw32-clang\+\+|x86_64-w64-mingw32-clang)(?:\.exe)?: (?<severity>error|warning): (?<message>.*)$")]
    private static partial Regex Tool();
}
