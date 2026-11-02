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
}
