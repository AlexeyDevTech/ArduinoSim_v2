using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArduSim.Core.Projects;

/// <summary><c>project.simproj.json</c>, schema v1 (ADR-008).</summary>
public sealed record ProjectFile
{
    public const int CurrentSchemaVersion = 1;
    public const string FileName = "project.simproj.json";
    public const string EepromFileName = "eeprom.bin";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public int SchemaVersion { get; init; }

    public string Board { get; init; } = "";

    public string SketchPath { get; init; } = "sketch";

    public SceneDocument Scene { get; init; } = new();

    public ProjectSettings Settings { get; init; } = new();

    /// <summary>Members this version does not know, kept so a save does not drop them.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Unknown { get; init; }

    public static ProjectFile Load(string projectDirectory)
    {
        string path = Path.Combine(projectDirectory, FileName);
        if (!File.Exists(path))
        {
            throw new ProjectFormatException($"{path} not found.");
        }

        ProjectFile? project;
        try
        {
            project = JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(path), Options);
        }
        catch (JsonException e)
        {
            throw new ProjectFormatException($"{path}: {e.Message}", e);
        }

        if (project is null)
        {
            throw new ProjectFormatException($"{path} is empty.");
        }

        if (project.SchemaVersion > CurrentSchemaVersion)
        {
            throw new ProjectFormatException($"{path} uses schema version {project.SchemaVersion}; this ArduSim reads up to version {CurrentSchemaVersion}. Update ArduSim to open it.");
        }

        if (project.SchemaVersion < 1)
        {
            throw new ProjectFormatException($"{path} has no valid schemaVersion.");
        }

        if (string.IsNullOrWhiteSpace(project.Board))
        {
            throw new ProjectFormatException($"{path} does not name a board.");
        }

        return project;
    }

    public string SketchDirectory(string projectDirectory) => Path.GetFullPath(Path.Combine(projectDirectory, SketchPath));

    public IEnumerable<string> LibraryDirectories(string projectDirectory) =>
        Settings.LibraryPaths.Select(p => Path.GetFullPath(Path.Combine(projectDirectory, p)));
}

public sealed record SceneDocument
{
    public IReadOnlyList<ComponentDocument> Components { get; init; } = [];

    public IReadOnlyList<WireDocument> Wires { get; init; } = [];
}

/// <param name="X">Canvas units of 0.01 inch.</param>
public sealed record ComponentDocument
{
    public string Id { get; init; } = "";

    public string Type { get; init; } = "";

    public double X { get; init; }

    public double Y { get; init; }

    public int Rotation { get; init; }

    public bool Flip { get; init; }

    public IReadOnlyDictionary<string, JsonElement>? Properties { get; init; }
}

public sealed record PinReference(string Component, string Pin);

public sealed record WireDocument
{
    public string Id { get; init; } = "";

    public PinReference From { get; init; } = new("", "");

    public PinReference To { get; init; } = new("", "");

    public IReadOnlyList<IReadOnlyList<double>> Waypoints { get; init; } = [];

    public string Color { get; init; } = "auto";
}

public sealed record ProjectSettings
{
    public double Speed { get; init; } = 1.0;

    public SerialSettings Serial { get; init; } = new();

    public IReadOnlyList<string> LibraryPaths { get; init; } = [];
}

public sealed record SerialSettings
{
    public int Baud { get; init; } = 9600;

    public string LineEnding { get; init; } = "LF";
}

public sealed class ProjectFormatException : Exception
{
    public ProjectFormatException()
    {
    }

    public ProjectFormatException(string message)
        : base(message)
    {
    }

    public ProjectFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
