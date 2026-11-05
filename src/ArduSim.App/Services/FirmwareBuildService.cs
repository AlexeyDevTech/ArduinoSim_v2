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

    public static Task<BuildResult> BuildAsync(Toolchain toolchain, string projectDirectory, ProjectFile project, BoardProfile board, CancellationToken cancellationToken)
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
        return Task.Run(() => builder.BuildAsync(request, cancellationToken), cancellationToken);
    }

    public void Dispose() => _http.Dispose();
}
