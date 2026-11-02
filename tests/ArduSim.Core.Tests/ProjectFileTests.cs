using ArduSim.Core.Projects;

namespace ArduSim.Core.Tests;

public sealed class ProjectFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ardusim-project-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Write(string json) => File.WriteAllText(Path.Combine(_dir, ProjectFile.FileName), json);

    [Fact]
    public void Loads_the_adr_008_example()
    {
        Write("""
            {
              "schemaVersion": 1,
              "board": "arduino-nano-v3",
              "sketchPath": "sketch",
              "scene": {
                "components": [
                  { "id": "board", "type": "arduino-nano-v3", "x": 0, "y": 0, "rotation": 0, "flip": false },
                  { "id": "led1", "type": "led", "x": 400, "y": 120, "rotation": 90, "properties": { "color": "red" } }
                ],
                "wires": [
                  { "id": "w1", "from": { "component": "board", "pin": "D13" }, "to": { "component": "led1", "pin": "A" },
                    "waypoints": [[300, 40], [300, 200]], "color": "auto" }
                ]
              },
              "settings": { "speed": 1.0, "serial": { "baud": 115200, "lineEnding": "LF" }, "libraryPaths": ["libs"] },
              "futureField": { "kept": true }
            }
            """);

        ProjectFile project = ProjectFile.Load(_dir);

        Assert.Equal("arduino-nano-v3", project.Board);
        Assert.Equal(Path.Combine(_dir, "sketch"), project.SketchDirectory(_dir));
        Assert.Equal(2, project.Scene.Components.Count);
        Assert.Equal("red", project.Scene.Components[1].Properties!["color"].GetString());
        Assert.Equal([300.0, 200.0], project.Scene.Wires[0].Waypoints[1]);
        Assert.Equal(115200, project.Settings.Serial.Baud);
        Assert.Equal([Path.Combine(_dir, "libs")], project.LibraryDirectories(_dir));
        Assert.True(project.Unknown!.ContainsKey("futureField"));
    }

    [Fact]
    public void Defaults_fill_in_what_a_minimal_file_leaves_out()
    {
        Write("""{ "schemaVersion": 1, "board": "arduino-nano-v3" }""");

        ProjectFile project = ProjectFile.Load(_dir);

        Assert.Equal("sketch", project.SketchPath);
        Assert.Empty(project.Scene.Components);
        Assert.Equal(9600, project.Settings.Serial.Baud);
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 2, "board": "x" }""", "schema version 2")]
    [InlineData("""{ "board": "x" }""", "schemaVersion")]
    [InlineData("""{ "schemaVersion": 1 }""", "board")]
    [InlineData("""{ "schemaVersion": 1, "board": """, "project.simproj.json")]
    public void Rejects_files_it_cannot_read_with_a_clear_message(string json, string expected)
    {
        Write(json);

        var error = Assert.Throws<ProjectFormatException>(() => ProjectFile.Load(_dir));
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }
}
