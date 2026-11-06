using ArduSim.Build.Compilation;

namespace ArduSim.Build.Libraries;

public enum LibraryOrigin
{
    /// <summary>In the project's folder.</summary>
    Project,

    /// <summary>In Documents\Arduino\libraries.</summary>
    Documents,

    /// <summary>Comes with ArduSim: bundled, or the simulator's own Wire, SPI and the like.</summary>
    BuiltIn,

    /// <summary>In another folder the project file names.</summary>
    Folder,
}

public sealed record KnownLibrary(LibraryInfo Info, LibraryOrigin Origin);

/// <summary>Every library a project can see, in the build's search order (ADR-018).</summary>
public sealed class LibraryInventory
{
    private readonly string _project;
    private readonly string[] _builtIn;
    private readonly Lazy<(List<KnownLibrary> All, Dictionary<string, KnownLibrary> ByName)> _scan;

    /// <param name="projectLibraries">The project's library folders.</param>
    /// <param name="cores">The board's core folders, from <see cref="ShimLayout.CoreDirectory"/>.</param>
    public LibraryInventory(string projectDirectory, IEnumerable<string> projectLibraries, ShimLayout shim, IEnumerable<string> cores)
    {
        ArgumentNullException.ThrowIfNull(shim);
        _project = Inside(projectDirectory);
        string[] coreLibraries = [.. cores.Select(c => Path.Combine(c, "libraries"))];
        _builtIn = [.. coreLibraries.Select(Inside), Inside(shim.LibrariesDirectory), Inside(ArduSimPaths.BundledLibraries)];
        string[] roots = [.. coreLibraries.Append(shim.LibrariesDirectory).Concat(ArduSimPaths.LibraryRoots(projectLibraries))];
        _scan = new(() => Scan(roots));
    }

    public IReadOnlyList<KnownLibrary> Libraries => _scan.Value.All;

    /// <summary>What a name would resolve to; the first in the search order wins.</summary>
    public KnownLibrary? Find(string name) => _scan.Value.ByName.GetValueOrDefault(LibraryFolders.Normalize(name));

    // Reads every library's file, so only when someone asks by name: where a folder comes from needs none of it.
    private (List<KnownLibrary> All, Dictionary<string, KnownLibrary> ByName) Scan(string[] roots)
    {
        List<KnownLibrary> all = [];
        Dictionary<string, KnownLibrary> byName = [];
        foreach (string root in roots.Where(Directory.Exists))
        {
            foreach (string directory in Directory.EnumerateDirectories(root))
            {
                if (!Path.GetFileName(directory).StartsWith('.'))
                {
                    var known = new KnownLibrary(LibraryFolders.Read(directory), OriginOf(directory));
                    all.Add(known);
                    byName.TryAdd(LibraryFolders.Normalize(known.Info.Name), known);
                    byName.TryAdd(LibraryFolders.Normalize(Path.GetFileName(directory)), known);
                }
            }
        }

        return (all, byName);
    }

    public LibraryOrigin OriginOf(string directory)
    {
        string path = Inside(directory);
        if (path.StartsWith(_project, StringComparison.OrdinalIgnoreCase))
        {
            return LibraryOrigin.Project;
        }

        if (_builtIn.Any(b => path.StartsWith(b, StringComparison.OrdinalIgnoreCase)))
        {
            return LibraryOrigin.BuiltIn;
        }

        return path.StartsWith(Inside(ArduSimPaths.DocumentsLibraries), StringComparison.OrdinalIgnoreCase) ? LibraryOrigin.Documents : LibraryOrigin.Folder;
    }

    private static string Inside(string directory) => Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
}
