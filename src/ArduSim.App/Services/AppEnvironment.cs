using System.IO;
using ArduSim.Ipc;
using ArduSim.Runner.Session;

namespace ArduSim.App.Services;

/// <summary>Where things are, and which session kind to use (<c>--inproc</c> dev switch, ADR-003).</summary>
public sealed class AppEnvironment(bool inProcess)
{
    public bool InProcess { get; } = inProcess;

    public static string ExamplesDirectory => Path.Combine(AppContext.BaseDirectory, "examples");

    public static string RunnerPath => Path.Combine(AppContext.BaseDirectory, "ArduSim.Runner.exe");

    /// <summary>Bundled examples open as copies here, so the originals stay pristine (ADR-008).</summary>
    public static string ProjectsDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ArduSim", "Projects");

    public ISimulationSession CreateSession() =>
        InProcess ? new InProcessSimulationSession() : new PipeSimulationSession(RunnerPath);

    public static IEnumerable<(string Name, string Path)> Examples() =>
        Directory.Exists(ExamplesDirectory)
            ? Directory.EnumerateDirectories(ExamplesDirectory)
                .Where(d => File.Exists(Path.Combine(d, ArduSim.Core.Projects.ProjectFile.FileName)))
                .Select(d => (Path.GetFileName(d), d))
                .OrderBy(e => e.Item1, StringComparer.OrdinalIgnoreCase)
            : [];

    /// <summary>The user's copy of a bundled example, created on first open.</summary>
    public static string OpenExample(string examplePath)
    {
        string target = Path.Combine(ProjectsDirectory, Path.GetFileName(examplePath));
        if (!Directory.Exists(target))
        {
            foreach (string file in Directory.EnumerateFiles(examplePath, "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(target, Path.GetRelativePath(examplePath, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
        }

        return target;
    }
}
