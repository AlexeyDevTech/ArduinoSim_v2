using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArduSim.Build.Libraries;

/// <param name="Sha256">Lower-case hex.</param>
/// <param name="Architectures">What it says it runs on; <c>*</c> for anything.</param>
public sealed record CatalogRelease(
    string Version,
    Uri Url,
    string FileName,
    long Size,
    string Sha256,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> Includes,
    IReadOnlyList<string> Architectures);

/// <param name="Releases">Newest first.</param>
public sealed record CatalogLibrary(string Name, string Author, string Sentence, string Website, IReadOnlyList<CatalogRelease> Releases)
{
    public CatalogRelease Latest => Releases[0];
}

/// <param name="Items">The library first, then what it needs that is not available yet.</param>
/// <param name="Unknown">Dependencies the catalog does not have.</param>
public sealed record InstallPlan(IReadOnlyList<(CatalogLibrary Library, CatalogRelease Release)> Items, IReadOnlyList<string> Unknown);

/// <summary>The Arduino library index in memory (ADR-018), grouped by library, with its search.</summary>
public sealed class LibraryCatalog
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private readonly Dictionary<string, CatalogLibrary> _exact;
    private readonly Dictionary<string, CatalogLibrary> _byName;
    private readonly (CatalogLibrary Library, string Name, string Text, HashSet<string> Includes)[] _search;

    private LibraryCatalog(List<CatalogLibrary> libraries)
    {
        Libraries = libraries;
        _exact = libraries.ToDictionary(l => l.Name, StringComparer.Ordinal);
        _byName = [];
        foreach (CatalogLibrary library in libraries)
        {
            _byName.TryAdd(LibraryFolders.Normalize(library.Name), library);
        }

        _search = [.. libraries.Select(l => (
            l,
            LibraryFolders.Normalize(l.Name),
            (l.Sentence + " " + l.Author).ToUpperInvariant(),
            new HashSet<string>(l.Latest.Includes, StringComparer.OrdinalIgnoreCase)))];
    }

    public IReadOnlyList<CatalogLibrary> Libraries { get; }

    /// <summary>Reads the index as JSON.</summary>
    public static LibraryCatalog Parse(Stream json)
    {
        Index index = JsonSerializer.Deserialize<Index>(json, Options) ?? new Index();
        List<CatalogLibrary> libraries = [.. (index.Libraries ?? [])
            .Where(e => e.Name is { Length: > 0 } && e.Version is { Length: > 0 } && IsDownloadable(e))
            .GroupBy(e => e.Name!, StringComparer.Ordinal)
            .Select(ToLibrary)
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)];
        return new LibraryCatalog(libraries);
    }

    /// <summary>Reads the index as it is cached: gzipped.</summary>
    public static LibraryCatalog ParseGzip(string path)
    {
        using FileStream file = File.OpenRead(path);
        using var unpacked = new GZipStream(file, CompressionMode.Decompress);
        return Parse(unpacked);
    }

    /// <summary>By the exact name, or else one that differs only in case and punctuation (the catalog has both <c>ssd1306</c> and <c>SSD1306</c>).</summary>
    public CatalogLibrary? Find(string name) => _exact.GetValueOrDefault(name) ?? _byName.GetValueOrDefault(LibraryFolders.Normalize(name));

    /// <summary>The best matches first (ADR-018); everything by name when the query is empty.</summary>
    public IReadOnlyList<CatalogLibrary> Search(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        string[] words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return Libraries;
        }

        var scored = new List<(int Score, CatalogLibrary Library)>();
        foreach ((CatalogLibrary library, string name, string text, HashSet<string> includes) in _search)
        {
            int total = 0;
            foreach (string word in words)
            {
                int score = Score(word, name, text, includes);
                if (score == int.MaxValue)
                {
                    total = int.MaxValue;
                    break;
                }

                total += score;
            }

            if (total != int.MaxValue)
            {
                scored.Add((total, library));
            }
        }

        return [.. scored.OrderBy(s => s.Score).ThenBy(s => s.Library.Name, StringComparer.OrdinalIgnoreCase).Select(s => s.Library)];
    }

    /// <summary>What installing this release takes: it, and what it needs that <paramref name="isAvailable"/> does not know, transitively.</summary>
    public InstallPlan Plan(CatalogLibrary library, CatalogRelease release, Func<string, bool> isAvailable)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(isAvailable);
        var items = new List<(CatalogLibrary, CatalogRelease)> { (library, release) };
        var unknown = new List<string>();
        var seen = new HashSet<string> { LibraryFolders.Normalize(library.Name) };
        var pending = new Queue<string>(release.Dependencies);
        while (pending.TryDequeue(out string? name))
        {
            if (!seen.Add(LibraryFolders.Normalize(name)) || isAvailable(name))
            {
                continue;
            }

            if (Find(name) is { } needed)
            {
                items.Add((needed, needed.Latest));
                foreach (string next in needed.Latest.Dependencies)
                {
                    pending.Enqueue(next);
                }
            }
            else
            {
                unknown.Add(name);
            }
        }

        return new InstallPlan(items, unknown);
    }

    /// <summary>Orders dotted versions numerically; a pre-release (<c>1.0.0-rc1</c>) comes before its release.</summary>
    public static int CompareVersions(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        string[] left = a.Split('-', 2);
        string[] right = b.Split('-', 2);
        string[] leftParts = left[0].Split('.');
        string[] rightParts = right[0].Split('.');
        for (int i = 0; i < Math.Max(leftParts.Length, rightParts.Length); i++)
        {
            string x = i < leftParts.Length ? leftParts[i] : "0";
            string y = i < rightParts.Length ? rightParts[i] : "0";
            int order = int.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out int m) && int.TryParse(y, NumberStyles.None, CultureInfo.InvariantCulture, out int n)
                ? m.CompareTo(n)
                : string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
            if (order != 0)
            {
                return order;
            }
        }

        return (left.Length > 1, right.Length > 1) switch
        {
            (true, false) => -1,
            (false, true) => 1,
            (true, true) => string.Compare(left[1], right[1], StringComparison.OrdinalIgnoreCase),
            _ => 0,
        };
    }

    // Lower is better; MaxValue means the word is not here at all.
    private static int Score(string word, string name, string text, HashSet<string> includes)
    {
        bool isHeader = word.EndsWith(".h", StringComparison.OrdinalIgnoreCase) || word.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase);
        string normal = LibraryFolders.Normalize(isHeader ? Path.GetFileNameWithoutExtension(word) : word);
        if (isHeader)
        {
            // Only 44 % of releases list their headers: a name equal to the header's stem counts as well.
            return includes.Contains(word) ? 0 : name == normal ? 1 : int.MaxValue;
        }

        if (name == normal)
        {
            return 0;
        }

        if (normal.Length > 0 && name.StartsWith(normal, StringComparison.Ordinal))
        {
            return 1;
        }

        if (normal.Length > 0 && name.Contains(normal, StringComparison.Ordinal))
        {
            return 2;
        }

        if (includes.Contains(word + ".h"))
        {
            return 3;
        }

        return text.Contains(word.ToUpperInvariant(), StringComparison.Ordinal) ? 4 : int.MaxValue;
    }

    private static bool IsDownloadable(Entry entry) =>
        Uri.TryCreate(entry.Url, UriKind.Absolute, out Uri? url) && url.Scheme == Uri.UriSchemeHttps;

    private static CatalogLibrary ToLibrary(IGrouping<string, Entry> releases)
    {
        List<Entry> newestFirst = [.. releases.OrderByDescending(e => e.Version!, Comparer<string>.Create(CompareVersions))];
        Entry latest = newestFirst[0];
        return new CatalogLibrary(
            releases.Key,
            WithoutAddresses(latest.Author ?? ""),
            latest.Sentence ?? "",
            latest.Website ?? "",
            [.. newestFirst.Select(e => new CatalogRelease(
                e.Version!,
                new Uri(e.Url!),
                e.ArchiveFileName ?? "",
                e.Size,
                (e.Checksum ?? "").Replace("SHA-256:", "", StringComparison.OrdinalIgnoreCase).ToUpperInvariant(),
                e.Dependencies is { } dependencies ? [.. dependencies.Where(d => d.Name is { Length: > 0 }).Select(d => d.Name!)] : [],
                e.ProvidesIncludes ?? [],
                e.Architectures ?? []))]);
    }

    // "Jane Doe <jane@example.com>, John": what a person reads in a list has no address in it.
    private static string WithoutAddresses(string author)
    {
        int open;
        while ((open = author.IndexOf('<', StringComparison.Ordinal)) >= 0)
        {
            int close = author.IndexOf('>', open);
            author = close < 0 ? author[..open] : author.Remove(open, close - open + 1);
        }

        return string.Join(' ', author.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Replace(" ,", ",", StringComparison.Ordinal);
    }

    private sealed class Index
    {
        public List<Entry>? Libraries { get; set; }
    }

    private sealed class Entry
    {
        public string? Name { get; set; }

        public string? Version { get; set; }

        public string? Author { get; set; }

        public string? Sentence { get; set; }

        public string? Website { get; set; }

        public string? Url { get; set; }

        public string? ArchiveFileName { get; set; }

        public long Size { get; set; }

        public string? Checksum { get; set; }

        public List<string>? Architectures { get; set; }

        public List<string>? ProvidesIncludes { get; set; }

        public List<Dependency>? Dependencies { get; set; }
    }

    private sealed class Dependency
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
