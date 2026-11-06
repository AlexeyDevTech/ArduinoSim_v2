using System.IO;
using System.Net.Http;
using ArduSim.Build;
using ArduSim.Build.Compilation;
using ArduSim.Build.Toolchains;
using ArduSim.Core.Boards;
using ArduSim.Core.Projects;

namespace ArduSim.App.Services;

/// <summary>Toolchain install and firmware builds for the app (brief §3.4).</summary>
public sealed class FirmwareBuildService : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(30) };
    private readonly ToolchainManager _toolchains;

    public FirmwareBuildService()
    {
        _toolchains = new ToolchainManager(ArduSimPaths.Default.Toolchains, _http);
    }

    public ToolchainManifest Manifest => _toolchains.Manifest;

    public static string ToolchainDirectory => ArduSimPaths.Default.Toolchains;

    public Toolchain? FindToolchain() => _toolchains.FindInstalled();

    public Task<Toolchain> InstallToolchainAsync(IProgress<ToolchainProgress> progress, CancellationToken cancellationToken) =>
        _toolchains.EnsureInstalledAsync(progress, cancellationToken);

    /// <summary>What the library dialog works with, for one project.</summary>
    public LibraryService LibrariesFor(string projectDirectory, ProjectFile project, BoardProfile board, Action<string> recycle) =>
        new(_http, ArduSimPaths.Default.LibraryIndex, projectDirectory, project, board, recycle);

    public static Task<BuildResult> BuildAsync(Toolchain toolchain, string projectDirectory, ProjectFile project, BoardProfile board, IProgress<BuildProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(board);
        ArduSimPaths paths = ArduSimPaths.Default;
        var builder = new FirmwareBuilder(toolchain, new ObjectCache(paths.ObjectCache, toolchain.Root), ShimLayout.Bundled);
        var request = new FirmwareBuildRequest(
            project.SketchDirectory(projectDirectory),
            paths.BuildDirectoryFor(projectDirectory),
            board.Variant,
            [.. board.Defines, .. project.Settings.Defines ?? []],
            ArduSimPaths.LibraryRoots(project.LibraryDirectories(projectDirectory)),
            board.Core,
            board.CoreBase,
            [.. project.IncludeDirectories(projectDirectory)]);
        return Task.Run(() => builder.BuildAsync(request, progress, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// What clangd needs to read this project's code as the build does (ADR-016): the shim, the board's
    /// variant and macros, every library's headers, and Arduino.h included first, as in a sketch.
    /// </summary>
    public static IReadOnlyList<string> EditorFlags(ProjectFile project, string projectDirectory, BoardProfile board)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(board);
        ShimLayout shim = ShimLayout.Bundled;
        string[] cores = [shim.CoreDirectory(board.Core), .. board.CoreBase is { } shared ? [shim.CoreDirectory(shared)] : Array.Empty<string>()];
        var flags = new List<string> { "-xc++", "-std=gnu++17", "--target=x86_64-w64-mingw32", "-include", "Arduino.h" };
        flags.AddRange(board.Defines.Select(d => "-D" + d));
        flags.AddRange((project.Settings.Defines ?? []).Select(d => "-D" + d));
        flags.Add("-I" + shim.IncludeDirectory);
        flags.AddRange(cores.Select(c => Path.Combine(c, "include")).Where(Directory.Exists).Select(i => "-I" + i));
        flags.Add("-I" + shim.VariantDirectory(board.Variant));
        flags.Add("-I" + project.SketchDirectory(projectDirectory));
        flags.AddRange(project.IncludeDirectories(projectDirectory).Select(i => "-I" + i));
        IEnumerable<string> roots = [.. cores.Select(c => Path.Combine(c, "libraries")), shim.LibrariesDirectory, .. ArduSimPaths.LibraryRoots(project.LibraryDirectories(projectDirectory))];
        foreach (string library in roots.Where(Directory.Exists).SelectMany(Directory.EnumerateDirectories))
        {
            string source = Path.Combine(library, "src");
            flags.Add("-I" + (Directory.Exists(source) ? source : library));
        }

        return flags;
    }

    public void Dispose() => _http.Dispose();
}
