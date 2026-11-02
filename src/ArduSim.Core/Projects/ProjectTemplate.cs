using System.Text;

namespace ArduSim.Core.Projects;

/// <summary>A new project: the board alone on the scene and an empty sketch (docs/UI.md §2.7).</summary>
public static class ProjectTemplate
{
    public const string BoardId = "board";

    private const string Sketch = """
        void setup() {
          // Runs once after reset.
        }

        void loop() {
          // Runs over and over.
        }

        """;

    /// <summary>
    /// Creates <c>project.simproj.json</c> and <c>sketch/&lt;name&gt;.ino</c> in <paramref name="directory"/>.
    /// An existing sketch file of the same name is kept.
    /// </summary>
    public static ProjectFile Create(string directory, string boardType)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentException.ThrowIfNullOrEmpty(boardType);
        if (File.Exists(Path.Combine(directory, ProjectFile.FileName)))
        {
            throw new IOException($"{directory} already holds a project.");
        }

        var project = new ProjectFile
        {
            SchemaVersion = ProjectFile.CurrentSchemaVersion,
            Board = boardType,
            Scene = new SceneDocument { Components = [new ComponentDocument { Id = BoardId, Type = boardType, X = 100, Y = 300 }] },
        };

        string sketchDirectory = project.SketchDirectory(directory);
        Directory.CreateDirectory(sketchDirectory);
        string sketch = Path.Combine(sketchDirectory, SketchName(directory) + ".ino");
        if (!File.Exists(sketch))
        {
            File.WriteAllText(sketch, Sketch.ReplaceLineEndings("\n"), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        project.Save(directory);
        return project;
    }

    /// <summary>The folder name with characters a sketch name cannot hold replaced by <c>_</c>.</summary>
    public static string SketchName(string directory)
    {
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)));
        var builder = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_');
        }

        string result = builder.ToString().Trim('_');
        return result.Length == 0 ? "sketch" : char.IsAsciiDigit(result[0]) ? "sketch_" + result : result;
    }
}
