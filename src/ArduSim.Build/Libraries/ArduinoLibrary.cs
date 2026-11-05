namespace ArduSim.Build.Libraries;

public enum LibraryLayout
{
    /// <summary>Legacy: headers and sources in the root folder, plus <c>utility/</c>.</summary>
    Flat,

    /// <summary>1.5 format: <c>library.properties</c> and everything under <c>src/</c>, recursively.</summary>
    Recursive,
}

/// <summary>An Arduino library folder.</summary>
public sealed record ArduinoLibrary(string Name, string Directory, LibraryLayout Layout)
{
    private static readonly string[] SourceExtensions = [".c", ".cpp", ".cc"];
    private static readonly string[] HeaderExtensions = [".h", ".hpp", ".hh"];

    public string IncludeDirectory => Layout == LibraryLayout.Recursive ? Path.Combine(Directory, "src") : Directory;

    public IReadOnlyList<string> IncludeDirectories
    {
        get
        {
            string utility = Path.Combine(Directory, "utility");
            return Layout == LibraryLayout.Flat && System.IO.Directory.Exists(utility) ? [Directory, utility] : [IncludeDirectory];
        }
    }

    public static ArduinoLibrary FromDirectory(string directory)
    {
        // Without library.properties a src folder still counts when no header sits in the root: PlatformIO's lib/<name>/src.
        bool recursive = System.IO.Directory.Exists(Path.Combine(directory, "src"))
            && (File.Exists(Path.Combine(directory, "library.properties")) || !System.IO.Directory.EnumerateFiles(directory, "*.h").Any());
        return new ArduinoLibrary(Path.GetFileName(directory), directory, recursive ? LibraryLayout.Recursive : LibraryLayout.Flat);
    }

    /// <summary>Headers directly in the include folder: what <c>#include &lt;X.h&gt;</c> can resolve to.</summary>
    public IEnumerable<string> PublicHeaders() =>
        System.IO.Directory.Exists(IncludeDirectory)
            ? System.IO.Directory.EnumerateFiles(IncludeDirectory).Where(f => HasExtension(f, HeaderExtensions)).Select(f => Path.GetFileName(f))
            : [];

    public IReadOnlyList<string> SourceFiles() => Files(SourceExtensions);

    public IReadOnlyList<string> AllFiles() => Files([.. SourceExtensions, .. HeaderExtensions]);

    private List<string> Files(string[] extensions)
    {
        IEnumerable<string> files = Layout == LibraryLayout.Recursive
            ? EnumerateIfExists(IncludeDirectory, SearchOption.AllDirectories)
            : EnumerateIfExists(Directory, SearchOption.TopDirectoryOnly).Concat(EnumerateIfExists(Path.Combine(Directory, "utility"), SearchOption.TopDirectoryOnly));
        return files.Where(f => HasExtension(f, extensions)).Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> EnumerateIfExists(string directory, SearchOption option) =>
        System.IO.Directory.Exists(directory) ? System.IO.Directory.EnumerateFiles(directory, "*", option) : [];

    private static bool HasExtension(string file, string[] extensions) =>
        extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);
}
