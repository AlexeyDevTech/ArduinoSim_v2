using ArduSim.Core.Projects;

namespace ArduSim.Core.Tests;

public sealed class ProjectTemplateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ardusim-template-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Create_WritesAProjectThatLoads()
    {
        string directory = Path.Combine(_root, "My Lamp");

        ProjectTemplate.Create(directory, "arduino-nano-v3");

        ProjectFile project = ProjectFile.Load(directory);
        Assert.Equal("arduino-nano-v3", project.Board);
        ComponentDocument board = Assert.Single(project.Scene.Components);
        Assert.Equal(ProjectTemplate.BoardId, board.Id);
        string sketch = File.ReadAllText(Path.Combine(directory, "sketch", "My_Lamp.ino"));
        Assert.Contains("void setup()", sketch, StringComparison.Ordinal);
        Assert.Contains("void loop()", sketch, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_RefusesAnExistingProject()
    {
        string directory = Path.Combine(_root, "Twice");
        ProjectTemplate.Create(directory, "arduino-nano-v3");

        Assert.Throws<IOException>(() => ProjectTemplate.Create(directory, "arduino-nano-v3"));
    }

    [Theory]
    [InlineData("Blink", "Blink")]
    [InlineData("my-project 2", "my_project_2")]
    [InlineData("2024 lamp", "sketch_2024_lamp")]
    [InlineData("Проект", "sketch")]
    public void SketchName_IsAValidIdentifier(string folder, string expected) =>
        Assert.Equal(expected, ProjectTemplate.SketchName(Path.Combine(_root, folder)));
}
