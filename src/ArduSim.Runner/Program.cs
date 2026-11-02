using System.Globalization;
using System.Reflection;
using System.Text;
using ArduSim.Runner.Headless;
using ArduSim.Runner.Session;

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

        try
        {
            if (args.Length > 0 && args[0] == "--ipc")
            {
                return await PipeServer.RunAsync(ParseIpc(args.Skip(1).ToList()));
            }

            return await HeadlessRun.RunAsync(HeadlessOptions.Parse(args));
        }
        catch (UsageException e)
        {
            Console.Error.WriteLine(e.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(HeadlessOptions.Usage);
            return ProcessExit.Usage;
        }
    }

    /// <summary><c>--ipc --pipe --crash-block --project --firmware [--speed] [--seed]</c>, as PipeSimulationSession passes them.</summary>
    private static PipeServer.Options ParseIpc(List<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i + 1 < args.Count; i += 2)
        {
            values[args[i]] = args[i + 1];
        }

        string Required(string name) => values.TryGetValue(name, out string? value) ? value : throw new UsageException($"--ipc needs {name}.");
        string speed = values.GetValueOrDefault("--speed", "max");
        return new PipeServer.Options(
            Required("--pipe"),
            Required("--crash-block"),
            Path.GetFullPath(Required("--project")),
            Path.GetFullPath(Required("--firmware")),
            speed == "max" ? double.PositiveInfinity : double.Parse(speed, CultureInfo.InvariantCulture),
            int.Parse(values.GetValueOrDefault("--seed", "1"), CultureInfo.InvariantCulture));
    }
}
