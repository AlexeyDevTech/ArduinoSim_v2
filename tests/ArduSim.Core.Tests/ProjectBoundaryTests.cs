using System.Reflection;
using System.Xml.Linq;

namespace ArduSim.Core.Tests;

/// <summary>
/// Guards the project boundaries from docs/ARCHITECTURE.md: the engine stays
/// UI-agnostic (an Avalonia port must stay possible) and contracts stay free of
/// engine types.
/// </summary>
public sealed class ProjectBoundaryTests
{
    private static readonly string[] UiAssemblies = ["PresentationFramework", "PresentationCore", "WindowsBase", "System.Windows.Forms"];

    public static TheoryData<string, string[]> AllowedProjectReferences => new()
    {
        { "ArduSim.Core", [] },
        { "ArduSim.Boards", ["ArduSim.Core"] },
        { "ArduSim.Build", [] },
        { "ArduSim.Ipc", [] },
    };

    [Theory]
    [MemberData(nameof(AllowedProjectReferences))]
    public void Project_references_only_allowed_projects(string project, string[] allowed)
    {
        string csproj = Path.Combine(RepositoryRoot(), "src", project, project + ".csproj");
        string[] actual = XDocument.Load(csproj)
            .Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension((string)e.Attribute("Include")!))
            .ToArray();

        Assert.Empty(actual.Except(allowed));
    }

    [Theory]
    [InlineData("ArduSim.Core")]
    [InlineData("ArduSim.Boards")]
    [InlineData("ArduSim.Build")]
    [InlineData("ArduSim.Ipc")]
    public void Assembly_does_not_reference_ui_frameworks(string assemblyName)
    {
        string[] references = Assembly.Load(assemblyName).GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        Assert.Empty(references.Intersect(UiAssemblies));
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ArduSim.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("ArduSim.sln not found above " + AppContext.BaseDirectory);
    }
}
