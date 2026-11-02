namespace ArduSim.Build.Libraries;

/// <param name="Libraries">Resolved libraries in discovery order.</param>
/// <param name="UnresolvedIncludes">Includes no library provides; left to the compiler (system headers, or a missing library it will report).</param>
public sealed record LibraryResolution(IReadOnlyList<ArduinoLibrary> Libraries, IReadOnlyList<string> UnresolvedIncludes);

/// <summary>
/// Arduino-style library detection: scans <c>#include</c> directives transitively and maps each header
/// to the first library that provides it, searching the roots in priority order
/// (core shim libraries → sketch → project paths → Documents\Arduino\libraries → bundled).
/// Within one root, a library whose folder is named after the header wins, then alphabetical order.
/// </summary>
/// <remarks>Detection is textual: an include inside an inactive <c>#if</c> branch still pulls its library in.</remarks>
public sealed class LibraryResolver(IReadOnlyList<string> searchRoots, IReadOnlyList<string> builtinIncludeDirectories)
{
    private readonly Lazy<List<(ArduinoLibrary Library, int Root, HashSet<string> Headers)>> _candidates = new(() => Index(searchRoots));

    public LibraryResolution Resolve(IEnumerable<string> sourceFiles)
    {
        var resolved = new List<ArduinoLibrary>();
        var includes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scanned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(sourceFiles.Select(Path.GetFullPath));
        while (queue.TryDequeue(out string? file))
        {
            if (!scanned.Add(file))
            {
                continue;
            }

            foreach (string raw in IncludeScanner.Scan(File.ReadAllText(file)))
            {
                string include = raw.Replace('\\', '/');
                includes.Add(include);
                if (File.Exists(Path.Combine(Path.GetDirectoryName(file)!, include)) || IsProvided(include, resolved))
                {
                    continue;
                }

                if (Find(include) is { } library)
                {
                    resolved.Add(library);
                    foreach (string member in library.AllFiles())
                    {
                        queue.Enqueue(member);
                    }
                }
            }
        }

        var unresolved = includes
            .Where(i => !IsProvided(i, resolved) && !scanned.Any(f => File.Exists(Path.Combine(Path.GetDirectoryName(f)!, i))))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new LibraryResolution(resolved, unresolved);
    }

    private bool IsProvided(string include, List<ArduinoLibrary> resolved) =>
        builtinIncludeDirectories.Any(d => File.Exists(Path.Combine(d, include))) ||
        resolved.Any(l => l.IncludeDirectories.Any(d => File.Exists(Path.Combine(d, include))));

    private ArduinoLibrary? Find(string include)
    {
        string stem = Path.GetFileNameWithoutExtension(include);
        bool nested = include.Contains('/', StringComparison.Ordinal);
        return _candidates.Value
            .Where(c => nested ? File.Exists(Path.Combine(c.Library.IncludeDirectory, include)) : c.Headers.Contains(include))
            .OrderBy(c => c.Root)
            .ThenBy(c => string.Equals(c.Library.Name, stem, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(c => c.Library.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => c.Library)
            .FirstOrDefault();
    }

    private static List<(ArduinoLibrary, int, HashSet<string>)> Index(IReadOnlyList<string> roots)
    {
        var candidates = new List<(ArduinoLibrary, int, HashSet<string>)>();
        for (int root = 0; root < roots.Count; root++)
        {
            if (!Directory.Exists(roots[root]))
            {
                continue;
            }

            foreach (string directory in Directory.EnumerateDirectories(roots[root]))
            {
                ArduinoLibrary library = ArduinoLibrary.FromDirectory(directory);
                var headers = new HashSet<string>(library.PublicHeaders(), StringComparer.OrdinalIgnoreCase);
                if (headers.Count > 0)
                {
                    candidates.Add((library, root, headers));
                }
            }
        }

        return candidates;
    }
}
