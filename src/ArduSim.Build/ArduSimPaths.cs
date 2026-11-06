namespace ArduSim.Build;

/// <summary>Per-user storage locations (docs/ARCHITECTURE.md, "Storage").</summary>
public sealed class ArduSimPaths(string root)
{
    public static ArduSimPaths Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArduSim"));

    public string Root { get; } = root;

    public string Toolchains => Path.Combine(Root, "toolchain");

    public string ObjectCache => Path.Combine(Root, "cache");

    public string Builds => Path.Combine(Root, "builds");

    public string Logs => Path.Combine(Root, "logs");

    /// <summary>The saved copy of the Arduino library catalog (ADR-018).</summary>
    public string LibraryIndex => Path.Combine(Root, "library-index");

    /// <summary>Folder that ships with the executables: <c>firmware/shim</c> and <c>libraries/</c>.</summary>
    public static string BundledContentRoot => AppContext.BaseDirectory;

    public static string BundledLibraries => Path.Combine(BundledContentRoot, "libraries");

    public static string DocumentsLibraries => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Arduino", "libraries");

    /// <summary>Per-project build output, outside the project folder: <c>builds\&lt;name&gt;-&lt;hash&gt;</c>.</summary>
    public string BuildDirectoryFor(string projectDirectory)
    {
        string full = Path.GetFullPath(projectDirectory);
        byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(full.ToUpperInvariant()));
        return Path.Combine(Builds, Path.GetFileName(full) + "-" + Convert.ToHexStringLower(hash)[..12]);
    }

    /// <summary>Library search roots after the shim's own (ADR / brief §3.4): project paths, Documents\Arduino\libraries, bundled.</summary>
    public static IReadOnlyList<string> LibraryRoots(IEnumerable<string> projectLibraryDirectories)
    {
        var roots = new List<string>(projectLibraryDirectories);
        if (Directory.Exists(DocumentsLibraries))
        {
            roots.Add(DocumentsLibraries);
        }

        roots.Add(BundledLibraries);
        return roots;
    }
}
