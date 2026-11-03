using ArduSim.Core.Components.Parts;
using ArduSim.Core.Storage;

namespace ArduSim.Runner;

/// <summary>
/// The project folders behind <see cref="IPartFiles"/> (ADR-012): the SD card's files come from a
/// folder of the project when the run starts and go back when it ends. Only files the card was
/// loaded with can be deleted, so a file put into the folder during the run survives. Folders
/// outside the project are refused.
/// </summary>
internal sealed class ProjectFolderFiles(string projectDirectory) : IPartFiles
{
    private readonly Dictionary<string, HashSet<string>> _loaded = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<CardFile> Load(string folder)
    {
        if (Resolve(folder) is not { } root || !Directory.Exists(root))
        {
            return [];
        }

        var files = new List<CardFile>();
        foreach (string directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            files.Add(new CardFile(Relative(root, directory), null));
        }

        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (new FileInfo(file).Length <= FatImage.Capacity)
            {
                files.Add(new CardFile(Relative(root, file), File.ReadAllBytes(file)));
            }
        }

        _loaded[root] = new HashSet<string>(files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
        return files;
    }

    public void Save(string folder, IReadOnlyList<CardFile> files)
    {
        if (Resolve(folder) is not { } root)
        {
            return;
        }

        Directory.CreateDirectory(root);
        var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CardFile file in files)
        {
            string path = Path.Combine(root, file.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            kept.Add(file.Path);
            if (file.IsFolder)
            {
                Directory.CreateDirectory(path);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(file.Content))
            {
                File.WriteAllBytes(path, file.Content!);
            }
        }

        // What the sketch deleted from the card: files first, then folders that became empty, deepest first.
        IEnumerable<string> gone = (_loaded.GetValueOrDefault(root) ?? []).Where(p => !kept.Contains(p)).OrderByDescending(p => p.Length);
        foreach (string relative in gone)
        {
            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            else if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }

        _loaded[root] = kept;
    }

    private string? Resolve(string folder)
    {
        string project = Path.GetFullPath(projectDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string root = Path.GetFullPath(Path.Combine(project, folder)).TrimEnd(Path.DirectorySeparatorChar);
        return root.StartsWith(project, StringComparison.OrdinalIgnoreCase) && root.Length >= project.Length ? root + Path.DirectorySeparatorChar : null;
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
}