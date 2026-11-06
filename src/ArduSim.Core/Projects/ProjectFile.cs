using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArduSim.Core.Projects;

/// <summary><c>project.simproj.json</c>, schema v1 (ADR-008).</summary>
public sealed record ProjectFile
{
    public const int CurrentSchemaVersion = 1;
    public const string FileName = "project.simproj.json";
    public const string EepromFileName = "eeprom.bin";

    /// <summary>The ESP Preferences store (ADR-009).</summary>
    public const string PreferencesFileName = "preferences.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static JsonSerializerOptions JsonOptions => Options;

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

    /// <summary>Writes the project file (indented JSON, unknown members kept), replacing the old one atomically.</summary>
    public void Save(string projectDirectory)
    {
        Directory.CreateDirectory(projectDirectory);
        string path = Path.Combine(projectDirectory, FileName);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this with { SchemaVersion = CurrentSchemaVersion }, Options) + "\n");
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Where a project keeps the libraries added to it, relative to its folder.</summary>
    public const string LibrariesFolder = "libs";

    public string SketchDirectory(string projectDirectory) => Path.GetFullPath(Path.Combine(projectDirectory, SketchPath));

    /// <summary>The project's <see cref="LibrariesFolder"/>, which needs no setting (ADR-018), then what the file names.</summary>
    public IEnumerable<string> LibraryDirectories(string projectDirectory) =>
        new[] { LibrariesFolder }.Concat(Settings.LibraryPaths).Select(p => Path.GetFullPath(Path.Combine(projectDirectory, p))).Distinct(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<string> IncludeDirectories(string projectDirectory) =>
        (Settings.IncludePaths ?? []).Select(p => Path.GetFullPath(Path.Combine(projectDirectory, p)));
}

public sealed record SceneDocument
{
    public IReadOnlyList<ComponentDocument> Components { get; init; } = [];

    public IReadOnlyList<WireDocument> Wires { get; init; } = [];
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Unknown { get; init; }
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
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Unknown { get; init; }
}

public sealed record PinReference(string Component, string Pin);

public sealed record WireDocument
{
    public string Id { get; init; } = "";

    public PinReference From { get; init; } = new("", "");

    public PinReference To { get; init; } = new("", "");

    /// <summary>Corners set by hand; none means the editor lays the wire out (ADR-020).</summary>
    public IReadOnlyList<IReadOnlyList<double>> Waypoints { get; init; } = [];

    /// <summary>In an undo step only: these corners are the editor's. Never in a file.</summary>
    [JsonIgnore]
    public bool AutoRouted { get; init; }

    public string Color { get; init; } = "auto";
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Unknown { get; init; }
}

public sealed record ProjectSettings
{
    public double Speed { get; init; } = 1.0;

    public SerialSettings Serial { get; init; } = new();

    public IReadOnlyList<string> LibraryPaths { get; init; } = [];

    /// <summary>Extra compiler macros (<c>NAME</c> or <c>NAME=value</c>), from a PlatformIO project's build flags; absent means none (ADR-015).</summary>
    public IReadOnlyList<string>? Defines { get; init; }

    /// <summary>Header folders for the sketch's own sources, relative to the project; absent means none (ADR-015).</summary>
    public IReadOnlyList<string>? IncludePaths { get; init; }

    /// <summary>The simulated Wi-Fi networks of ESP boards (ADR-010); absent means one open network, "ArduSim".</summary>
    public WifiSettings? Wifi { get; init; }

    /// <summary>Whether ESP sketches reach the real network (ADR-010); absent means yes.</summary>
    public NetworkSettings? Network { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Unknown { get; init; }
}

public sealed record WifiSettings
{
    public IReadOnlyList<WifiNetworkSettings> Networks { get; init; } = [];
}

/// <param name="Password">Empty for an open network.</param>
public sealed record WifiNetworkSettings(string Ssid, string Password = "", int Rssi = -55, int Channel = 6);

public sealed record NetworkSettings
{
    public bool Real { get; init; } = true;
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
