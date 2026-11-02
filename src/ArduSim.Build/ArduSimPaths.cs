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

    /// <summary>Folder that ships with the executables: <c>firmware/shim</c> and <c>libraries/</c>.</summary>
    public static string BundledContentRoot => AppContext.BaseDirectory;

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
        string documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Arduino", "libraries");
        if (Directory.Exists(documents))
        {
            roots.Add(documents);
        }

        roots.Add(Path.Combine(BundledContentRoot, "libraries"));
        return roots;
    }
}
