using System.Reflection;
using System.Text;
using ArduSim.Runner.Headless;

namespace ArduSim.Runner;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        if (args is ["--version"])
        {
            string version = typeof(Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
            Console.WriteLine($"ArduSim.Runner {version}");
            return ProcessExit.Success;
        }

        HeadlessOptions options;
        try
        {
            options = HeadlessOptions.Parse(args);
        }
        catch (UsageException e)
        {
            Console.Error.WriteLine(e.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(HeadlessOptions.Usage);
            return ProcessExit.Usage;
        }

        return await HeadlessRun.RunAsync(options);
    }
}
