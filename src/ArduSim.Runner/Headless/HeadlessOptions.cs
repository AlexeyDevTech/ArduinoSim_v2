using System.Globalization;

namespace ArduSim.Runner.Headless;

/// <summary>Command line of the headless runner (brief §4, "Headless CLI").</summary>
internal sealed record HeadlessOptions(
    string ProjectDirectory,
    double? RunForMilliseconds,
    double Speed,
    string? DumpSerial,
    int Seed,
    bool TracePins,
    string? BuildDirectory)
{
    public const string Usage = """
        Usage: ArduSim.Runner --project <folder> [options]

          --project <folder>     project folder with project.simproj.json (required)
          --run-for <ms>         stop after this much virtual time (default: until Ctrl+C)
          --speed <x|max>        pacing: 1 = real time, 0.1, 10, ... or max (default: max)
          --dump-serial <file>   write everything the sketch sends on Serial to a file
          --dump-display <file>  write the display contents as PNG (available from M4)
          --seed <n>             seed for floating inputs and analog noise (default: 1)
          --quiet                do not print pin changes
          --build-dir <folder>   where to put firmware.dll (default: %LOCALAPPDATA%\ArduSim\builds\...)
          --version              print the version
        """;

    public static HeadlessOptions Parse(IReadOnlyList<string> args)
    {
        string? project = null;
        double? runFor = null;
        double speed = double.PositiveInfinity;
        string? dumpSerial = null;
        int seed = 1;
        bool trace = true;
        string? buildDirectory = null;
        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--project":
                    project = Value(args, ref i, arg);
                    break;
                case "--run-for":
                    runFor = Number(Value(args, ref i, arg), arg);
                    break;
                case "--speed":
                    string s = Value(args, ref i, arg);
                    speed = s == "max" ? double.PositiveInfinity : Number(s, arg);
                    break;
                case "--dump-serial":
                    dumpSerial = Value(args, ref i, arg);
                    break;
                case "--dump-display":
                    throw new UsageException("--dump-display: displays are simulated from milestone M4; this build has none.");
                case "--seed":
                    seed = (int)Number(Value(args, ref i, arg), arg);
                    break;
                case "--quiet":
                    trace = false;
                    break;
                case "--build-dir":
                    buildDirectory = Path.GetFullPath(Value(args, ref i, arg));
                    break;
                default:
                    throw new UsageException($"Unknown argument: {arg}");
            }
        }

        if (project is null)
        {
            throw new UsageException("--project is required.");
        }

        if (runFor is <= 0 || speed <= 0)
        {
            throw new UsageException("--run-for and --speed must be positive.");
        }

        return new HeadlessOptions(Path.GetFullPath(project), runFor, speed, dumpSerial is null ? null : Path.GetFullPath(dumpSerial), seed, trace, buildDirectory);
    }

    private static string Value(IReadOnlyList<string> args, ref int i, string name) =>
        ++i < args.Count ? args[i] : throw new UsageException($"{name} needs a value.");

    private static double Number(string text, string name) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : throw new UsageException($"{name}: '{text}' is not a number.");
}

internal sealed class UsageException : Exception
{
    public UsageException()
    {
    }

    public UsageException(string message)
        : base(message)
    {
    }

    public UsageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
