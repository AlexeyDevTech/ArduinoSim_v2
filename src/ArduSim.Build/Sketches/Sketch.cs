namespace ArduSim.Build.Sketches;

/// <summary>An Arduino sketch folder: the main .ino (named after the folder), other .ino files and companion sources.</summary>
public sealed record Sketch(string Directory, string Name, IReadOnlyList<string> InoFiles, IReadOnlyList<string> SourceFiles)
{
    private static readonly string[] InoExtensions = [".ino", ".pde"];
    private static readonly string[] SourceExtensions = [".cpp", ".c", ".cc"];

    public string MainFile => InoFiles[0];

    /// <summary>
    /// Reads a sketch folder the way arduino-cli does: the main file first, the other .ino files
    /// alphabetically; .cpp/.c next to them and anything under <c>src/</c> compile as-is.
    /// </summary>
    public static Sketch Load(string directory)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (!System.IO.Directory.Exists(full))
        {
            throw new SketchException($"Sketch folder not found: {full}");
        }

        string folderName = Path.GetFileName(full);
        List<string> inos = System.IO.Directory.EnumerateFiles(full)
            .Where(f => InoExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (inos.Count == 0)
        {
            throw new SketchException($"No .ino file in {full}");
        }

        string? main = inos.FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), folderName, StringComparison.OrdinalIgnoreCase));
        if (main is null)
        {
            main = inos.Count == 1
                ? inos[0]
                : throw new SketchException($"{full} has several .ino files but none is named {folderName}.ino, so the main file is ambiguous.");
        }

        var ordered = new List<string> { main };
        ordered.AddRange(inos.Where(f => f != main).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase));

        var sources = new List<string>();
        AddSources(sources, full, SearchOption.TopDirectoryOnly);
        string src = Path.Combine(full, "src");
        if (System.IO.Directory.Exists(src))
        {
            AddSources(sources, src, SearchOption.AllDirectories);
        }

        return new Sketch(full, Path.GetFileNameWithoutExtension(main), ordered, sources);
    }

    private static void AddSources(List<string> sources, string directory, SearchOption option)
    {
        foreach (string file in System.IO.Directory.EnumerateFiles(directory, "*", option).Order(StringComparer.OrdinalIgnoreCase))
        {
            string extension = Path.GetExtension(file);
            if (string.Equals(extension, ".S", StringComparison.Ordinal))
            {
                throw new SketchException($"{file}: assembly sources are not supported by the simulator.");
            }

            if (SourceExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                sources.Add(file);
            }
        }
    }
}

public sealed class SketchException : Exception
{
    public SketchException()
    {
    }

    public SketchException(string message)
        : base(message)
    {
    }

    public SketchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
