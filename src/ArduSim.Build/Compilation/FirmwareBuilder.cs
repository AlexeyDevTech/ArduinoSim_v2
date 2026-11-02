using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
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
}

/// <param name="Variant">Folder under <c>firmware/shim/variants</c> with the board's <c>pins_arduino.h</c>.</param>
/// <param name="Defines">Board macros (<c>NAME</c> or <c>NAME=value</c>).</param>
/// <param name="LibraryRoots">Library folders after the shim's own: project paths, Documents\Arduino\libraries, bundled.</param>
public sealed record FirmwareBuildRequest(
    string SketchDirectory,
    string OutputDirectory,
    string Variant,
    IReadOnlyList<string> Defines,
    IReadOnlyList<string> LibraryRoots);

public sealed record BuildStatistics(int Units, int CacheHits, bool Linked, TimeSpan Elapsed);

public sealed record BuildResult(
    bool Succeeded,
    string? FirmwarePath,
    string? SymbolsPath,
    IReadOnlyList<BuildDiagnostic> Diagnostics,
    string Output,
    IReadOnlyList<string> Libraries,
    BuildStatistics Statistics);

/// <summary>Preprocesses the sketch, resolves libraries, compiles every unit (cached, in parallel) and links <c>firmware.dll</c>.</summary>
public sealed class FirmwareBuilder(Toolchain toolchain, ObjectCache cache, ShimLayout shim)
{
    private static readonly string[] CommonFlags = ["-c", "-fno-color-diagnostics", "-fdiagnostics-absolute-paths", "-Wall", "-O1", "-g", "-gcodeview"];
    private static readonly string[] CxxFlags = ["-std=gnu++17", "-fno-exceptions", "-fno-rtti"];
    private static readonly string[] CFlags = ["-std=gnu11"];

    public const string FirmwareFileName = "firmware.dll";

    public async Task<BuildResult> BuildAsync(FirmwareBuildRequest request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        Directory.CreateDirectory(request.OutputDirectory);

        Sketch sketch;
        PreprocessedSketch preprocessed;
        try
        {
            sketch = Sketch.Load(request.SketchDirectory);
            preprocessed = SketchPreprocessor.Preprocess(sketch);
        }
        catch (Exception e) when (e is SketchException or IOException)
        {
            return Failed(e.Message, stopwatch);
        }

        string generated = Path.Combine(request.OutputDirectory, "sketch", sketch.Name + ".ino.cpp");
        WriteIfChanged(generated, preprocessed.Code);

        string[] coreIncludes = [shim.IncludeDirectory, shim.VariantDirectory(request.Variant)];
        var resolver = new LibraryResolver([shim.LibrariesDirectory, .. request.LibraryRoots], coreIncludes);
        IEnumerable<string> sketchHeaders = Directory.EnumerateFiles(sketch.Directory, "*.h");
        LibraryResolution resolution = resolver.Resolve([.. sketch.InoFiles, .. sketch.SourceFiles, .. sketchHeaders]);
        string[] libraryIncludes = [.. coreIncludes, .. resolution.Libraries.SelectMany(l => l.IncludeDirectories)];
        string[] sketchIncludes = [.. libraryIncludes, sketch.Directory];

        var units = new List<(string Source, string[] Includes)> { (generated, sketchIncludes) };
        units.AddRange(sketch.SourceFiles.Select(s => (s, sketchIncludes)));
        units.AddRange(Directory.EnumerateFiles(shim.SourceDirectory).Where(IsSource).Order(StringComparer.Ordinal).Select(s => (s, coreIncludes)));
        units.AddRange(resolution.Libraries.SelectMany(l => l.SourceFiles()).Select(s => (s, libraryIncludes)));

        var hashes = new FileHashes();
        var outputs = new ConcurrentDictionary<int, string>();
        var entries = new CacheEntry?[units.Count];
        int hits = 0;
        await Parallel.ForEachAsync(
            Enumerable.Range(0, units.Count),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken },
            async (index, ct) =>
            {
                (string source, string[] includes) = units[index];
                (CacheEntry? entry, bool hit, string output) = await CompileAsync(source, includes, request.Defines, hashes, ct);
                entries[index] = entry;
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

    private async Task<(CacheEntry? Entry, bool Hit, string Output)> CompileAsync(
        string source, string[] includes, IReadOnlyList<string> defines, FileHashes hashes, CancellationToken cancellationToken)
    {
        bool isC = string.Equals(Path.GetExtension(source), ".c", StringComparison.OrdinalIgnoreCase);
        var arguments = new List<string>(CommonFlags);
        arguments.AddRange(isC ? CFlags : CxxFlags);
        arguments.AddRange(defines.Select(d => "-D" + d));
        arguments.AddRange(includes.Select(i => "-I" + i));

        string key = ObjectCache.ComputeKey([toolchain.Id, .. arguments, source, hashes.Get(source)]);
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
