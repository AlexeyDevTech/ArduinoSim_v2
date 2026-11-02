namespace ArduSim.Build.Toolchains;

/// <summary>A pinned, checksummed toolchain archive (ADR-002).</summary>
/// <param name="Id">Folder name under the toolchain directory; changes whenever the archive changes.</param>
/// <param name="ArchiveRoot">Top-level folder inside the zip, with a trailing slash.</param>
/// <param name="ExcludedPaths">Paths relative to <paramref name="ArchiveRoot"/> that are not extracted (prefix match).</param>
public sealed record ToolchainManifest(
    string Id,
    Uri Url,
    string Sha256,
    long SizeBytes,
    string ArchiveRoot,
    IReadOnlyList<string> ExcludedPaths)
{
    public static ToolchainManifest LlvmMingw { get; } = new(
        Id: "llvm-mingw-20261020-ucrt-x86_64",
        Url: new Uri("https://github.com/mstorsjo/llvm-mingw/releases/download/20261020/llvm-mingw-20261020-ucrt-x86_64.zip"),
        Sha256: "fd5462b7f5d93b2ff981fcd3f9c7e427e47f3203c64ea91c27bcc444eebd74cd",
        SizeBytes: 190_612_575,
        ArchiveRoot: "llvm-mingw-20261020-ucrt-x86_64/",
        ExcludedPaths:
        [
            "aarch64-w64-mingw32/", "armv7-w64-mingw32/", "i686-w64-mingw32/", "arm64ec-w64-mingw32/",
            "bin/aarch64-", "bin/armv7-", "bin/i686-", "bin/arm64ec-",
            "bin/lldb", "bin/liblldb", "python/", "share/",
        ]);

    public string FileName => Path.GetFileName(Url.AbsolutePath);

    public bool IsExcluded(string relativePath) =>
        ExcludedPaths.Any(prefix => relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
