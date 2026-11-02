using System.Text.RegularExpressions;

namespace ArduSim.Build.Libraries;

/// <summary>Finds <c>#include</c> directives in C/C++ text, ignoring comments.</summary>
public static partial class IncludeScanner
{
    public static IReadOnlyList<string> Scan(string text)
    {
        // String literals are matched so that "//" or "/*" inside them is not taken for a comment.
        string code = CommentsAndStrings().Replace(text, m =>
            m.Value[0] == '"' ? m.Value : new string('\n', m.Value.Count(c => c == '\n')));
        return Include().Matches(code).Select(m => m.Groups["name"].Value.Trim()).ToList();
    }

    [GeneratedRegex(@"/\*.*?\*/|//[^\n]*|""(?:\\.|[^""\\\n])*""", RegexOptions.Singleline)]
    private static partial Regex CommentsAndStrings();

    [GeneratedRegex(@"^[ \t]*#[ \t]*include[ \t]*[<""](?<name>[^>""\n]+)[>""]", RegexOptions.Multiline)]
    private static partial Regex Include();
}
