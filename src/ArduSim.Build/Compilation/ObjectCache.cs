using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace ArduSim.Build.Compilation;

/// <summary>A cached object file and a fingerprint of the exact inputs that produced it.</summary>
public sealed record CacheEntry(string ObjectPath, string Fingerprint);

/// <summary>
/// Per-translation-unit object cache in <c>%LOCALAPPDATA%\ArduSim\cache</c>. The key covers the toolchain,
/// the flags, the source path and the source content; each entry also records the content hash of every
/// header the compiler read (from its dependency file), so an edited header invalidates exactly the units
/// that include it. Headers inside the toolchain are skipped: the toolchain id is part of the key.
/// </summary>
public sealed class ObjectCache(string directory, string toolchainRoot)
{
    private const string FormatVersion = "ardusim-object-cache-1";

    private readonly string _toolchainRoot = Path.GetFullPath(toolchainRoot);

    public static string ComputeKey(IEnumerable<string> parts)
    {
        var text = new StringBuilder(FormatVersion);
        foreach (string part in parts)
        {
            text.Append('\0').Append(part);
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    public string EntryDirectory(string key) => Path.Combine(directory, "obj", key[..2], key);

    public CacheEntry? TryGet(string key, FileHashes hashes)
    {
        string entry = EntryDirectory(key);
        string obj = Path.Combine(entry, "unit.o");
        string deps = Path.Combine(entry, "deps.txt");
        if (!File.Exists(obj) || !File.Exists(deps))
        {
            return null;
        }

        string recorded = File.ReadAllText(deps);
        foreach (string line in recorded.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int tab = line.IndexOf('\t', StringComparison.Ordinal);
            if (tab < 0 || !string.Equals(hashes.Get(line[(tab + 1)..]), line[..tab], StringComparison.Ordinal))
            {
                return null;
            }
        }

        return new CacheEntry(obj, Fingerprint(key, recorded));
    }

    /// <summary>Moves a freshly compiled object and its dependency list into the cache.</summary>
    public CacheEntry Commit(string key, string compiledObject, string dependencyFile, FileHashes hashes)
    {
        string entry = EntryDirectory(key);
        var recorded = new StringBuilder();
        foreach (string dependency in DependencyFile.Parse(File.ReadAllText(dependencyFile)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!dependency.StartsWith(_toolchainRoot, StringComparison.OrdinalIgnoreCase))
            {
                recorded.Append(hashes.Get(dependency)).Append('\t').Append(dependency).Append('\n');
            }
        }

        string obj = Path.Combine(entry, "unit.o");
        string deps = Path.Combine(entry, "deps.txt");
        string depsTemp = compiledObject + ".deps";
        File.WriteAllText(depsTemp, recorded.ToString());
        // Another build (another Runner, a parallel test) may commit the same unit at the same moment
        // or be linking it; its files come from identical inputs, so keeping them is correct.
        Replace(compiledObject, obj);
        Replace(depsTemp, deps);
        File.Delete(dependencyFile);
        return new CacheEntry(obj, Fingerprint(key, recorded.ToString()));
    }

    private static void Replace(string source, string target)
    {
        try
        {
            File.Move(source, target, overwrite: true);
        }
        catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && File.Exists(target))
        {
            File.Delete(source);
        }
    }

    private static string Fingerprint(string key, string recordedDependencies) => ComputeKey([key, recordedDependencies]);
}

/// <summary>Content hashes memoized for the duration of one build.</summary>
public sealed class FileHashes
{
    private readonly ConcurrentDictionary<string, string> _hashes = new(StringComparer.OrdinalIgnoreCase);

    public string Get(string path) => _hashes.GetOrAdd(Path.GetFullPath(path), static p =>
        File.Exists(p) ? Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p))) : "missing");
}
