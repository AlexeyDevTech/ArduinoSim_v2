using System.IO;
using System.Net.Http;
using ArduSim.Build.Compilation;
using ArduSim.Build.Libraries;
using ArduSim.Core.Boards;
using ArduSim.Core.Projects;

namespace ArduSim.App.Services;

/// <summary>The catalog, the installer and the inventory for one project (ADR-018).</summary>
public sealed class LibraryService(HttpClient http, string catalogCache, string projectDirectory, ProjectFile project, BoardProfile board, Action<string> recycle)
{
    private readonly LibraryCatalogClient _catalog = new(http, catalogCache);

    public string LibsDirectory { get; } = Path.GetFullPath(Path.Combine(projectDirectory, ProjectFile.LibrariesFolder));

    /// <summary>The family the board's core serves, as the catalog's <c>architectures</c> name it.</summary>
    public string Architecture { get; } = board.Core;

    public LibraryInstaller Installer => new(http, LibsDirectory, recycle);

    public Task<CatalogLoad> LoadCatalogAsync(bool refresh, IProgress<CatalogProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => _catalog.LoadAsync(refresh, progress, cancellationToken), cancellationToken);

    public LibraryInventory Inventory()
    {
        ShimLayout shim = ShimLayout.Bundled;
        string[] cores = [shim.CoreDirectory(board.Core), .. board.CoreBase is { } shared ? [shim.CoreDirectory(shared)] : Array.Empty<string>()];
        return new LibraryInventory(projectDirectory, project.LibraryDirectories(projectDirectory), shim, cores);
    }

    /// <summary>Moves a library of the project to the Recycle Bin.</summary>
    public void Remove(string directory) => recycle(directory);
}
