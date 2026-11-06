using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ArduSim.Build.Libraries;

/// <param name="Version">From <c>library.properties</c>; empty when it has none.</param>
public sealed record LibraryInfo(string Name, string Version, string Directory);

/// <summary>Library folders on disk: what counts as one, and what it says about itself.</summary>
public static partial class LibraryFolders
{
    private const int SearchDepth = 2;
    private static readonly string[] Headers = ["*.h", "*.hpp", "*.hh"];

    /// <summary>
    /// A folder with <c>library.properties</c>, or with headers at its root or in <c>src</c>. Headers beside
    /// an <c>.ino</c> are a sketch, not a library.
    /// </summary>
    public static bool IsLibrary(string directory)
    {
        if (File.Exists(Path.Combine(directory, "library.properties")))
        {
            return true;
        }

        if (Directory.EnumerateFiles(directory, "*.ino").Any())
        {
            return false;
        }

        string source = Path.Combine(directory, "src");
        return HasHeaders(directory) || (Directory.Exists(source) && HasHeaders(source));
    }

    /// <summary>The folder itself if it is a library, or the libraries one or two levels down.</summary>
    public static IReadOnlyList<string> Find(string directory)
    {
        var found = new List<string>();
        Search(directory, SearchDepth, found);
        return found;
    }

    public static LibraryInfo Read(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        string name = "";
        string version = "";
        string properties = Path.Combine(directory, "library.properties");
        try
        {
            if (File.Exists(properties))
            {
                foreach (string line in File.ReadLines(properties, Encoding.UTF8))
                {
                    int equals = line.IndexOf('=', StringComparison.Ordinal);
                    if (equals > 0)
                    {
                        string key = line[..equals].Trim();
                        if (key == "name")
                        {
                            name = line[(equals + 1)..].Trim();
                        }
                        else if (key == "version")
                        {
                            version = line[(equals + 1)..].Trim();
                        }
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // An unreadable file says nothing: the folder's name will do.
            System.Diagnostics.Trace.TraceWarning("Could not read {0}: {1}", properties, e.Message);
        }

        return new LibraryInfo(name.Length > 0 ? name : WithoutArchiveSuffix(Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar))), version, directory);
    }

    /// <summary>As the Arduino IDE names a library's folder: <c>Adafruit NeoPixel</c> becomes <c>Adafruit_NeoPixel</c>.</summary>
    public static string FolderNameFor(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        char[] invalid = Path.GetInvalidFileNameChars();
        var folder = new StringBuilder(name.Length);
        foreach (char c in name.Trim())
        {
            folder.Append(c == ' ' || invalid.Contains(c) ? '_' : c);
        }

        return folder.ToString().TrimEnd('.', '_');
    }

    /// <summary>A name with case, spaces and punctuation gone, so that <c>DHT_sensor_library</c> and <c>DHT sensor library</c> are the same.</summary>
    public static string Normalize(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var normal = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                normal.Append(char.ToLower(c, CultureInfo.InvariantCulture));
            }
        }

        return normal.ToString();
    }

    private static void Search(string directory, int depth, List<string> found)
    {
        if (IsLibrary(directory))
        {
            found.Add(directory);
            return;
        }

        if (depth > 0)
        {
            foreach (string child in Directory.EnumerateDirectories(directory).Order(StringComparer.OrdinalIgnoreCase))
            {
                if (!Path.GetFileName(child).StartsWith('.'))
                {
                    Search(child, depth - 1, found);
                }
            }
        }
    }

    private static bool HasHeaders(string directory) => Headers.Any(pattern => Directory.EnumerateFiles(directory, pattern).Any());

    // GitHub's archives: Foo-master, Foo-main, Foo-1.2.3.
    private static string WithoutArchiveSuffix(string name) => ArchiveSuffix().Replace(name, "");

    [GeneratedRegex(@"-(master|main|v?\d+(\.\d+)*)$", RegexOptions.IgnoreCase)]
    private static partial Regex ArchiveSuffix();
}
