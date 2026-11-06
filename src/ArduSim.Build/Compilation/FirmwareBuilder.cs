using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ArduSim.Build.Libraries;
using ArduSim.Build.Processes;
using ArduSim.Build.Sketches;
using ArduSim.Build.Toolchains;

namespace ArduSim.Build.Compilation;

/// <summary>Where the C++ Arduino shim lives (docs/ARCHITECTURE.md, "Firmware shim").</summary>
public sealed record ShimLayout(string Root)
{
    public static ShimLayout Bundled { get; } = new(Path.Combine(ArduSimPaths.BundledContentRoot, "firmware", "shim"));

    public string IncludeDirectory => Path.Combine(Root, "include");

    public string SourceDirectory => Path.Combine(Root, "src");

    public string LibrariesDirectory => Path.Combine(Root, "libraries");

    public string VariantDirectory(string variant) => Path.Combine(Root, "variants", variant);

    /// <summary>A board family's own part of the shim (<c>cores/avr</c>, <c>cores/esp32</c>): headers, sources, libraries.</summary>
    public string CoreDirectory(string core) => Path.Combine(Root, "cores", core);

    /// <summary><c>libraries/compat</c> next to <c>firmware</c>: per-library replacement files (libraries/compat/README.md).</summary>
    public string CompatDirectory => Path.GetFullPath(Path.Combine(Root, "..", "..", "libraries", "compat"));
}

/// <param name="Variant">Folder under <c>firmware/shim/variants</c> with the board's <c>pins_arduino.h</c>.</param>
/// <param name="Defines">Board macros (<c>NAME</c> or <c>NAME=value</c>).</param>
/// <param name="LibraryRoots">Library folders after the shim's own: project paths, Documents\Arduino\libraries, bundled.</param>
/// <param name="Core">Folder under <c>firmware/shim/cores</c> for the board family.</param>
/// <param name="CoreBase">A second folder under <c>cores</c> with code the family shares (searched after <paramref name="Core"/>), or null.</param>
/// <param name="ExtraIncludes">Header folders for the sketch's own sources (a PlatformIO <c>include</c>), or null.</param>
public sealed record FirmwareBuildRequest(
    string SketchDirectory,
    string OutputDirectory,
    string Variant,
    IReadOnlyList<string> Defines,
    IReadOnlyList<string> LibraryRoots,
    string Core = "avr",
    string? CoreBase = null,
    IReadOnlyList<string>? ExtraIncludes = null);

public sealed record BuildStatistics(int Units, int CacheHits, bool Linked, TimeSpan Elapsed);

/// <summary>How far a build is: units compiled or found in the cache, of all of them.</summary>
public sealed record BuildProgress(int Finished, int Total);

public sealed record BuildResult(
    bool Succeeded,
    string? FirmwarePath,
    string? SymbolsPath,
    IReadOnlyList<BuildDiagnostic> Diagnostics,
    string Output,
    IReadOnlyList<string> Libraries,
    BuildStatistics Statistics);

/// <param name="SystemIncludes">Searched after <paramref name="Includes"/>; warnings in their headers are not shown.</param>
/// <param name="Quiet">Compile without warnings (library sources).</param>
internal sealed record CompileUnit(string Source, IReadOnlyList<string> SystemIncludes, IReadOnlyList<string> Includes, bool Quiet);

/// <param name="Fingerprint">Hash over every replacement file's content.</param>
internal sealed record CompatOverlay(string Path, string Fingerprint);

/// <summary>Preprocesses the sketch, resolves libraries, compiles every unit (cached, in parallel) and links <c>firmware.dll</c>.</summary>
public sealed class FirmwareBuilder(Toolchain toolchain, ObjectCache cache, ShimLayout shim)
{
    private static readonly string[] CommonFlags = ["-c", "-fno-color-diagnostics", "-fdiagnostics-absolute-paths", "-Wall", "-O1", "-g", "-gcodeview"];
    private static readonly string[] CxxFlags = ["-std=gnu++17", "-fno-exceptions", "-fno-rtti"];
    private static readonly string[] CFlags = ["-std=gnu11"];

    public const string FirmwareFileName = "firmware.dll";

    public Task<BuildResult> BuildAsync(FirmwareBuildRequest request, CancellationToken cancellationToken) => BuildAsync(request, null, cancellationToken);

    public async Task<BuildResult> BuildAsync(FirmwareBuildRequest request, IProgress<BuildProgress>? progress, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        Directory.CreateDirectory(request.OutputDirectory);

        Sketch sketch;
        PreprocessedSketch preprocessed;
        try
        {
            sketch = Sketch.Load(request.SketchDirectory);
            preprocessed = sketch.InoFiles.Count > 0 ? SketchPreprocessor.Preprocess(sketch) : null!;
        }
        catch (Exception e) when (e is SketchException or IOException)
        {
            return Failed(e.Message, stopwatch);
        }

        string generated = Path.Combine(request.OutputDirectory, "sketch", sketch.Name + ".ino.cpp");
        if (sketch.InoFiles.Count > 0)
        {
            WriteIfChanged(generated, preprocessed.Code);
        }

        string[] cores = [shim.CoreDirectory(request.Core), .. request.CoreBase is { } shared ? [shim.CoreDirectory(shared)] : Array.Empty<string>()];
        string[] coreIncludes = [shim.IncludeDirectory, .. cores.Select(c => Path.Combine(c, "include")).Where(Directory.Exists), shim.VariantDirectory(request.Variant)];
        string[] roots = [.. cores.Select(c => Path.Combine(c, "libraries")).Where(Directory.Exists), shim.LibrariesDirectory, .. request.LibraryRoots];
        var resolver = new LibraryResolver(roots, coreIncludes, file => CompatReplacement(roots, file));
        string[] sketchIncludes = [sketch.Directory, .. (request.ExtraIncludes ?? []).Where(Directory.Exists)];
        IEnumerable<string> sketchHeaders = sketchIncludes.SelectMany(d => Directory.EnumerateFiles(d, "*.h"));
        LibraryResolution resolution = resolver.Resolve([.. sketch.InoFiles, .. sketch.SourceFiles, .. sketchHeaders]);
        string[] libraryIncludes = [.. coreIncludes, .. resolution.Libraries.SelectMany(l => l.IncludeDirectories)];

        // As the Arduino IDE (warnings "none" for libraries): the sketch sees the core and library
        // headers as system headers, and library sources compile without warnings, so the build
        // reports the sketch's own warnings, not third-party ones.
        var units = new List<CompileUnit>();
        if (sketch.InoFiles.Count > 0)
        {
            units.Add(new CompileUnit(generated, libraryIncludes, sketchIncludes, Quiet: false));
        }

        units.AddRange(sketch.SourceFiles.Select(s => new CompileUnit(s, libraryIncludes, sketchIncludes, Quiet: false)));
        units.AddRange(new[] { shim.SourceDirectory }.Concat(cores.Select(c => Path.Combine(c, "src"))).Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d).Where(IsSource).Order(StringComparer.Ordinal))
            .Select(s => new CompileUnit(s, [], coreIncludes, Quiet: false)));
        units.AddRange(resolution.Libraries.SelectMany(l => l.SourceFiles().Select(s => new CompileUnit(s, [], libraryIncludes, Quiet: true))));

        var hashes = new FileHashes();
        CompatOverlay? overlay = WriteCompatOverlay(resolution.Libraries, request.OutputDirectory, hashes);
        var outputs = new ConcurrentDictionary<int, string>();
        var entries = new CacheEntry?[units.Count];
        int hits = 0;
        int finished = 0;
        await Parallel.ForEachAsync(
            Enumerable.Range(0, units.Count),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken },
            async (index, ct) =>
            {
                CompileUnit unit = units[index];
                string source = unit.Source;
                (CacheEntry? entry, bool hit, string output) = await CompileAsync(unit, request.Defines, overlay, hashes, ct);
                entries[index] = entry;
                progress?.Report(new BuildProgress(Interlocked.Increment(ref finished), units.Count));
                if (hit)
                {
                    Interlocked.Increment(ref hits);
                }

                if (output.Length > 0)
                {
                    outputs[index] = $"--- {source}\n{output}";
                }
            });

        var log = new StringBuilder();
        foreach (int index in outputs.Keys.Order())
        {
            log.Append(outputs[index]);
        }

        string dll = Path.Combine(request.OutputDirectory, FirmwareFileName);
        string pdb = Path.ChangeExtension(dll, ".pdb");
        bool compiled = entries.All(e => e is not null);
        bool linked = false;
        bool succeeded = compiled;
        if (compiled)
        {
            (succeeded, linked, string linkOutput) = await LinkAsync(entries!, dll, pdb, request.OutputDirectory, cancellationToken);
            log.Append(linkOutput);
        }

        var diagnostics = DiagnosticParser.Parse(log.ToString()).Distinct().ToList();
        diagnostics.AddRange(FreeSramWarnings(sketch));
        if (succeeded is false && compiled && log.ToString().Contains("permission denied", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(new BuildDiagnostic(DiagnosticSeverity.Error, $"{dll} is in use; stop the running simulation and build again.", null, 0, 0));
        }

        var statistics = new BuildStatistics(units.Count, hits, linked, stopwatch.Elapsed);
        File.WriteAllText(Path.Combine(request.OutputDirectory, "build.log"), log.ToString());
        return new BuildResult(
            succeeded,
            succeeded ? dll : null,
            succeeded ? pdb : null,
            diagnostics,
            log.ToString(),
            resolution.Libraries.Select(l => l.Name).ToList(),
            statistics);
    }

    // The idiom links, but there is no AVR memory map behind it: the number must not be taken for the truth.
    private static IEnumerable<BuildDiagnostic> FreeSramWarnings(Sketch sketch)
    {
        IEnumerable<string> own = sketch.InoFiles.Concat(sketch.SourceFiles)
            .Concat(Directory.EnumerateFiles(sketch.Directory, "*.h", SearchOption.TopDirectoryOnly))
            .Concat(Directory.Exists(Path.Combine(sketch.Directory, "src")) ? Directory.EnumerateFiles(Path.Combine(sketch.Directory, "src"), "*.h", SearchOption.AllDirectories) : []);
        foreach (string file in own)
        {
            string[] lines = File.ReadAllLines(file);
            int index = Array.FindIndex(lines, l => l.Contains("__brkval", StringComparison.Ordinal) || l.Contains("__heap_start", StringComparison.Ordinal));
            if (index >= 0)
            {
                yield return new BuildDiagnostic(DiagnosticSeverity.Warning, "__heap_start and __brkval are not modelled: free SRAM worked out from them is not real.", file, index + 1, 1);
            }
        }
    }

    private async Task<(CacheEntry? Entry, bool Hit, string Output)> CompileAsync(
        CompileUnit unit, IReadOnlyList<string> defines, CompatOverlay? overlay, FileHashes hashes, CancellationToken cancellationToken)
    {
        string source = unit.Source;
        bool isC = string.Equals(Path.GetExtension(source), ".c", StringComparison.OrdinalIgnoreCase);
        var arguments = new List<string>(CommonFlags);
        arguments.AddRange(isC ? CFlags : CxxFlags);
        if (unit.Quiet)
        {
            arguments.Add("-w");
        }

        arguments.AddRange(defines.Select(d => "-D" + d));
        arguments.AddRange(unit.Includes.Select(i => "-I" + i));
        arguments.AddRange(unit.SystemIncludes.SelectMany(i => new[] { "-isystem", i }));
        if (overlay is not null)
        {
            arguments.AddRange(["-ivfsoverlay", overlay.Path]);
        }

        // The overlay's fingerprint covers the replacement files: the dependency file may name a
        // replaced source by its vendored path.
        string key = ObjectCache.ComputeKey([toolchain.Id, .. arguments, overlay?.Fingerprint ?? "", source, hashes.Get(source)]);
        if (cache.TryGet(key, hashes) is { } cached)
        {
            return (cached, true, "");
        }

        string entryDirectory = cache.EntryDirectory(key);
        Directory.CreateDirectory(entryDirectory);
        string temp = Path.Combine(entryDirectory, Guid.NewGuid().ToString("N"));
        arguments.AddRange(["-MD", "-MF", temp + ".d", "-o", temp + ".o", source]);
        ProcessResult result = await ProcessRunner.RunAsync(isC ? toolchain.CCompiler : toolchain.CxxCompiler, arguments, entryDirectory, cancellationToken);
        if (result.ExitCode != 0 || !File.Exists(temp + ".o"))
        {
            File.Delete(temp + ".o");
            File.Delete(temp + ".d");
            return (null, false, result.Output.Length > 0 ? result.Output : $"compiler exited with code {result.ExitCode}\n");
        }

        return (cache.Commit(key, temp + ".o", temp + ".d", hashes), false, result.Output);
    }

    private async Task<(bool Succeeded, bool Linked, string Output)> LinkAsync(
        CacheEntry[] entries, string dll, string pdb, string outputDirectory, CancellationToken cancellationToken)
    {
        string[] flags = ["-shared", "-static", "-g", "-gcodeview", "-Wl,--pdb=" + pdb, "-o", dll];
        string signature = ObjectCache.ComputeKey([toolchain.Id, .. flags, .. entries.Select(e => e.ObjectPath + "|" + e.Fingerprint)]);
        string signatureFile = Path.Combine(outputDirectory, "firmware.link");
        if (File.Exists(dll) && File.Exists(signatureFile) && File.ReadAllText(signatureFile) == signature)
        {
            return (true, false, "");
        }

        string responseFile = Path.Combine(outputDirectory, "link.rsp");
        await File.WriteAllLinesAsync(responseFile, entries.Select(e => "\"" + e.ObjectPath.Replace('\\', '/') + "\""), cancellationToken);
        File.Delete(signatureFile);
        ProcessResult result = await ProcessRunner.RunAsync(toolchain.CxxCompiler, [.. flags, "@" + responseFile], outputDirectory, cancellationToken);
        if (result.ExitCode != 0)
        {
            return (false, false, result.Output.Length > 0 ? result.Output : $"linker exited with code {result.ExitCode}\n");
        }

        await File.WriteAllTextAsync(signatureFile, signature, cancellationToken);
        return (true, true, result.Output);
    }

    /// <summary>The compat file that replaces <paramref name="file"/> of a library under one of <paramref name="roots"/>, or the file itself.</summary>
    private string CompatReplacement(IReadOnlyList<string> roots, string file)
    {
        foreach (string root in roots)
        {
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string candidate = Path.Combine(shim.CompatDirectory, file[prefix.Length..]);
                return File.Exists(candidate) ? candidate : file;
            }
        }

        return file;
    }

    /// <summary>
    /// A file at <c>libraries/compat/&lt;library folder&gt;/&lt;relative path&gt;</c> replaces the vendored file
    /// at that path, header or source (libraries/compat/README.md). Clang reads the replacement through
    /// a VFS overlay, so the vendored tree stays untouched and the library's own quoted includes find it.
    /// </summary>
    private CompatOverlay? WriteCompatOverlay(IReadOnlyList<ArduinoLibrary> libraries, string outputDirectory, FileHashes hashes)
    {
        var roots = new List<Dictionary<string, string>>();
        var fingerprint = new List<string>();
        foreach (ArduinoLibrary library in libraries)
        {
            string compat = Path.Combine(shim.CompatDirectory, Path.GetFileName(library.Directory));
            if (!Directory.Exists(compat))
            {
                continue;
            }

            foreach (string replacement in Directory.EnumerateFiles(compat, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                string relative = Path.GetRelativePath(compat, replacement);
                if (relative.Equals("README.md", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                roots.Add(new Dictionary<string, string>
                {
                    ["type"] = "file",
                    ["name"] = Path.Combine(library.Directory, relative).Replace('\\', '/'),
                    ["external-contents"] = replacement.Replace('\\', '/'),
                });
                fingerprint.Add(relative + "|" + hashes.Get(replacement));
            }
        }

        if (roots.Count == 0)
        {
            return null;
        }

        string path = Path.Combine(outputDirectory, "compat-overlay.json");
        WriteIfChanged(path, JsonSerializer.Serialize(new Dictionary<string, object> { ["version"] = 0, ["case-sensitive"] = "false", ["roots"] = roots }));
        return new CompatOverlay(path, ObjectCache.ComputeKey(fingerprint));
    }

    private static bool IsSource(string file) =>
        Path.GetExtension(file) is ".cpp" or ".c" or ".cc";

    private static void WriteIfChanged(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path) || File.ReadAllText(path) != content)
        {
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    private static BuildResult Failed(string message, Stopwatch stopwatch) =>
        new(false, null, null, [new BuildDiagnostic(DiagnosticSeverity.Error, message, null, 0, 0)], message + "\n", [], new BuildStatistics(0, 0, false, stopwatch.Elapsed));
}
