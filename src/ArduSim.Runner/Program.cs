using System.Reflection;

namespace ArduSim.Runner;

internal static class Program
{
    private const int ExitUsage = 64;

    private static int Main(string[] args)
    {
        if (args is ["--version"])
        {
            string version = typeof(Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
            Console.WriteLine($"ArduSim.Runner {version}");
            return 0;
        }

        Console.Error.WriteLine("ArduSim.Runner: simulation is not implemented yet (milestone M1). Only --version is available.");
        return ExitUsage;
    }
}
