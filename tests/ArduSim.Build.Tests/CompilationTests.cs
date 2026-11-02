using ArduSim.Build.Compilation;

namespace ArduSim.Build.Tests;

public sealed class DiagnosticParserTests
{
    [Fact]
    public void Parses_located_clang_messages_with_drive_letters()
    {
        const string output = """
            In file included from D:/p/S.ino.cpp:1:
            D:/sk/Blink/Blink.ino:7:3: error: use of undeclared identifier 'digitalWrit'
            D:/sk/Blink/Blink.ino:3:9: warning: unused variable 'x' [-Wunused-variable]
            D:/sk/Blink/Blink.ino:2:6: note: previous definition is here
            C:\lib\Foo.h:10: fatal error: 'Bar.h' file not found
            1 error generated.
            """;

        IReadOnlyList<BuildDiagnostic> diagnostics = DiagnosticParser.Parse(output);

        Assert.Equal(4, diagnostics.Count);
        Assert.Equal(new BuildDiagnostic(DiagnosticSeverity.Error, "use of undeclared identifier 'digitalWrit'", Path.GetFullPath("D:/sk/Blink/Blink.ino"), 7, 3), diagnostics[0]);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostics[1].Severity);
        Assert.Equal(DiagnosticSeverity.Note, diagnostics[2].Severity);
        Assert.Equal((DiagnosticSeverity.Error, 10, 0), (diagnostics[3].Severity, diagnostics[3].Line, diagnostics[3].Column));
    }

    [Fact]
    public void Parses_linker_errors_without_location()
    {
        const string output = """
            ld.lld: error: undefined symbol: loop()
            x86_64-w64-mingw32-clang++: error: linker command failed with exit code 1 (use -v to see invocation)
            """;

        IReadOnlyList<BuildDiagnostic> diagnostics = DiagnosticParser.Parse(output);

        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Null(d.File));
        Assert.Equal("undefined symbol: loop()", diagnostics[0].Message);
    }
}

public sealed class DependencyFileTests
{
    [Fact]
    public void Parses_continuations_and_escaped_spaces()
    {
        const string text = "D:/cache/x.o: D:/sk/S.ino.cpp \\\n  C:/Program\\ Files/inc/a.h \\\n  D:/shim/Arduino.h\n";

        Assert.Equal(["D:/sk/S.ino.cpp", "C:/Program Files/inc/a.h", "D:/shim/Arduino.h"], DependencyFile.Parse(text));
    }
}

public sealed class ObjectCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ardusim-cache-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Entry_is_reused_until_an_included_header_changes()
    {
        Directory.CreateDirectory(_root);
        string header = Path.Combine(_root, "a.h");
        string source = Path.Combine(_root, "a.cpp");
        File.WriteAllText(header, "int a;");
        File.WriteAllText(source, "#include \"a.h\"");
        var cache = new ObjectCache(Path.Combine(_root, "cache"), Path.Combine(_root, "toolchain"));
        string key = ObjectCache.ComputeKey(["flags", source]);

        CacheEntry stored = Commit(cache, key, new FileHashes(), header, source);
        CacheEntry? hit = cache.TryGet(key, new FileHashes());
        File.WriteAllText(header, "int b;");
        CacheEntry? afterEdit = cache.TryGet(key, new FileHashes());

        Assert.Equal(stored, hit);
        Assert.Null(afterEdit);
    }

    [Fact]
    public void Toolchain_headers_are_not_tracked()
    {
        string toolchainHeader = Path.Combine(_root, "toolchain", "include", "stdio.h");
        Directory.CreateDirectory(Path.GetDirectoryName(toolchainHeader)!);
        File.WriteAllText(toolchainHeader, "");
        var cache = new ObjectCache(Path.Combine(_root, "cache"), Path.Combine(_root, "toolchain"));
        string key = ObjectCache.ComputeKey(["k"]);

        Commit(cache, key, new FileHashes(), toolchainHeader);

        Assert.Equal("", File.ReadAllText(Path.Combine(cache.EntryDirectory(key), "deps.txt")));
    }

    [Fact]
    public void Keys_differ_when_any_part_differs()
    {
        Assert.NotEqual(ObjectCache.ComputeKey(["-O1", "a.cpp"]), ObjectCache.ComputeKey(["-O2", "a.cpp"]));
        Assert.Equal(ObjectCache.ComputeKey(["-O1", "a.cpp"]), ObjectCache.ComputeKey(["-O1", "a.cpp"]));
    }

    private static CacheEntry Commit(ObjectCache cache, string key, FileHashes hashes, params string[] dependencies)
    {
        string entry = cache.EntryDirectory(key);
        Directory.CreateDirectory(entry);
        string obj = Path.Combine(entry, "tmp.o");
        string dep = Path.Combine(entry, "tmp.d");
        File.WriteAllText(obj, "object");
        File.WriteAllText(dep, "x.o: " + string.Join(" \\\n ", dependencies.Select(d => d.Replace('\\', '/').Replace(" ", "\\ ", StringComparison.Ordinal))) + "\n");
        return cache.Commit(key, obj, dep, hashes);
    }
}
