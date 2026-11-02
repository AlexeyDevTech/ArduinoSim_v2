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
    string? BuildDirectory,
    IReadOnlyList<ScheduledInput> Inputs,
    string? DumpDisplay = null)
{
    public const string Usage = """
        Usage: ArduSim.Runner --project <folder> [options]

          --project <folder>     project folder with project.simproj.json (required)
          --run-for <ms>         stop after this much virtual time (default: until Ctrl+C)
          --speed <x|max>        pacing: 1 = real time, 0.1, 10, ... or max (default: max)
          --dump-serial <file>   write everything the sketch sends on Serial to a file
          --dump-display <file>  at the end, write the display as a grayscale PNG (several displays: <file>-<part>.png)
          --seed <n>             seed for floating inputs and analog noise (default: 1)
          --input <ms>:<part>:<name>=<value>
                                 apply a part input at a virtual time, e.g. 500:btn1:pressed=1 (repeatable)
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
        string? dumpDisplay = null;
        int seed = 1;
        bool trace = true;
        string? buildDirectory = null;
        var inputs = new List<ScheduledInput>();
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
                    dumpDisplay = Path.GetFullPath(Value(args, ref i, arg));
                    break;
                case "--seed":
                    seed = (int)Number(Value(args, ref i, arg), arg);
                    break;
                case "--quiet":
                    trace = false;
                    break;
                case "--input":
                    inputs.Add(ScheduledInput.Parse(Value(args, ref i, arg)));
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

        return new HeadlessOptions(Path.GetFullPath(project), runFor, speed, dumpSerial is null ? null : Path.GetFullPath(dumpSerial), seed, trace, buildDirectory, inputs, dumpDisplay);
    }

    private static string Value(IReadOnlyList<string> args, ref int i, string name) =>
        ++i < args.Count ? args[i] : throw new UsageException($"{name} needs a value.");

    private static double Number(string text, string name) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : throw new UsageException($"{name}: '{text}' is not a number.");
}

/// <summary>A part input applied at a virtual time (headless stand-in for a click in the app).</summary>
internal sealed record ScheduledInput(double Milliseconds, string Component, string Name, double Value)
{
    public static ScheduledInput Parse(string text)
    {
        string[] parts = text.Split(':');
        string[] assignment = parts.Length == 3 ? parts[2].Split('=') : [];
        if (assignment.Length != 2 ||
            !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double ms) ||
            !double.TryParse(assignment[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            throw new UsageException($"--input '{text}': expected <ms>:<part>:<name>=<value>, e.g. 500:btn1:pressed=1.");
        }

        return new ScheduledInput(ms, parts[1], assignment[0], value);
    }
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
