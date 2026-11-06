using System.IO;

namespace ArduSim.App.Services;

/// <summary>The color family of a badge in the explorer and on a tab (tokens <c>Brush.Badge.*</c>).</summary>
public enum BadgeTone
{
    Sketch,
    Cpp,
    C,
    Header,
    Other,
    Function,
    Type,
    Variable,
}

/// <summary>What a file is by its extension, and which file belongs with it: a header and what implements it.</summary>
public static class FileKinds
{
    private static readonly string[] HeaderExtensions = [".h", ".hpp", ".hh", ".hxx"];
    private static readonly string[] SourceExtensions = [".cpp", ".cc", ".cxx", ".c", ".ino", ".pde"];

    public static bool IsHeader(string path) => HeaderExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static bool IsSource(string path) => SourceExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>The badge a file shows: <c>ino</c>, <c>c++</c>, <c>c</c>, <c>h</c>, or its own extension.</summary>
    public static (string Text, BadgeTone Tone) BadgeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".ino" or ".pde" => ("ino", BadgeTone.Sketch),
        ".cpp" or ".cc" or ".cxx" => ("c++", BadgeTone.Cpp),
        ".c" => ("c", BadgeTone.C),
        ".h" or ".hpp" or ".hh" or ".hxx" => ("h", BadgeTone.Header),
        var other => (other.TrimStart('.'), BadgeTone.Other),
    };

    /// <summary>The badge of a symbol, by its LSP kind.</summary>
    public static (string Text, BadgeTone Tone) BadgeOfSymbol(int kind) => kind switch
    {
        5 or 11 or 23 => ("C", BadgeTone.Type),
        10 => ("E", BadgeTone.Type),
        3 or 2 => ("N", BadgeTone.Type),
        6 or 9 or 12 => ("ƒ", BadgeTone.Function),
        22 => ("e", BadgeTone.Variable),
        14 => ("#", BadgeTone.Variable),
        _ => ("v", BadgeTone.Variable),
    };

    /// <summary>
    /// The other half of a file: the source with a header's name, or the header with a source's. In the same
    /// folder first, then anywhere among the given files. Null when it has none.
    /// </summary>
    public static string? Partner(string path, IReadOnlyCollection<string> files)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(files);
        string name = Path.GetFileNameWithoutExtension(path);
        bool header = IsHeader(path);
        var others = files.Where(f => (header ? IsSource(f) : IsHeader(f)) && string.Equals(Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase)).ToList();
        string? folder = Path.GetDirectoryName(path);
        return others.OrderByDescending(f => string.Equals(Path.GetDirectoryName(f), folder, StringComparison.OrdinalIgnoreCase))

            // An .ino comes after a .cpp of the same name: the .cpp is what a header usually goes with.
            .ThenBy(f => Path.GetExtension(f).Equals(".ino", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .FirstOrDefault();
    }
}
