using System.Globalization;
using System.Text.RegularExpressions;

namespace ArduSim.Build.Import;

/// <summary>What a <c>platformio.ini</c> says about one environment, in ArduSim's terms (ADR-015).</summary>
/// <param name="Environment">The <c>[env:...]</c> taken: the first of <c>default_envs</c>, else the first in the file.</param>
/// <param name="BoardId">The ArduSim board.</param>
/// <param name="SourceDirectory">The sources, relative to the project (<c>src_dir</c>).</param>
/// <param name="LibraryDirectories">Library roots, relative: <c>lib_dir</c> and the libraries PlatformIO downloaded.</param>
/// <param name="IncludeDirectories">Header folders, relative: <c>include_dir</c> and <c>-I</c> flags.</param>
/// <param name="Defines">From <c>-D</c> build flags, as <c>NAME</c> or <c>NAME=value</c>.</param>
/// <param name="LibraryDependencies">The library names of <c>lib_deps</c>, without owner and version.</param>
/// <param name="MonitorSpeed">The Serial Monitor's baud rate, when set.</param>
public sealed partial record PlatformIoProject(
    string Environment,
    string BoardId,
    string SourceDirectory,
    IReadOnlyList<string> LibraryDirectories,
    IReadOnlyList<string> IncludeDirectories,
    IReadOnlyList<string> Defines,
    IReadOnlyList<string> LibraryDependencies,
    int? MonitorSpeed)
{
    public const string FileName = "platformio.ini";

    private static readonly Dictionary<string, string> Boards = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nanoatmega328"] = "arduino-nano-v3",
        ["nanoatmega328new"] = "arduino-nano-v3",
        ["uno"] = "arduino-uno-r3",
        ["megaatmega2560"] = "arduino-mega-2560",
        ["pro16MHzatmega328"] = "arduino-pro-mini-5v",
        ["pro8MHzatmega328"] = "arduino-pro-mini-3v3",
        ["esp32dev"] = "esp32-devkit-v1",
        ["esp32doit-devkit-v1"] = "esp32-devkit-v1",
        ["nodemcuv2"] = "nodemcu-v3",
        ["nodemcu"] = "nodemcu-v3",
        ["d1_mini"] = "wemos-d1-mini",
        ["d1_mini_lite"] = "wemos-d1-mini",
        ["d1_mini_pro"] = "wemos-d1-mini",
    };

    public static bool ExistsIn(string directory) => File.Exists(Path.Combine(directory, FileName));

    /// <summary>Reads the project's <c>platformio.ini</c>.</summary>
    /// <exception cref="PlatformIoException">The file has no environment ArduSim can run, with the reason.</exception>
    public static PlatformIoProject Read(string directory) => Parse(File.ReadAllText(Path.Combine(directory, FileName)), directory);

    /// <summary>Parses the text of a <c>platformio.ini</c>; <paramref name="directory"/> is where downloaded libraries are looked for.</summary>
    public static PlatformIoProject Parse(string text, string? directory = null)
    {
        Dictionary<string, Dictionary<string, string>> sections = Sections(text);
        string? name = First(Value(sections, "platformio", "default_envs")) ?? sections.Keys.FirstOrDefault(k => k.StartsWith("env:", StringComparison.Ordinal))?[4..];
        if (name is null)
        {
            throw new PlatformIoException("platformio.ini has no [env:...] section.");
        }

        if (!sections.ContainsKey("env:" + name))
        {
            throw new PlatformIoException($"platformio.ini: default_envs names {name}, which has no [env:{name}] section.");
        }

        string Option(string key, string fallback = "") => EnvValue(sections, name, key) ?? fallback;

        string framework = Option("framework");
        if (!Items(framework).Contains("arduino", StringComparer.OrdinalIgnoreCase))
        {
            throw new PlatformIoException($"Environment {name} uses framework '{framework}'; ArduSim simulates the Arduino framework only.");
        }

        string board = Option("board");
        if (!Boards.TryGetValue(board, out string? boardId))
        {
            throw new PlatformIoException($"Environment {name} is for board '{board}', which ArduSim does not simulate. It knows: {string.Join(", ", Boards.Keys)}.");
        }

        var includes = new List<string> { Value(sections, "platformio", "include_dir") ?? "include" };
        var defines = new List<string>();
        foreach (Match flag in Flags().Matches(Option("build_flags")))
        {
            string value = flag.Groups["value"].Value;
            if (value.Length >= 2 && value[0] is '"' or '\'' && value[^1] == value[0])
            {
                value = value[1..^1];
            }

            value = value.Replace("\\\"", "\"", StringComparison.Ordinal); // a string macro: -DNAME=\"text\"
            if (flag.Groups["kind"].Value == "D")
            {
                defines.Add(value);
            }
            else
            {
                includes.Add(value);
            }
        }

        var libraries = new List<string> { Value(sections, "platformio", "lib_dir") ?? "lib" };
        string downloaded = Path.Combine(".pio", "libdeps", name);
        if (directory is not null && Directory.Exists(Path.Combine(directory, downloaded)))
        {
            libraries.Add(downloaded);
        }

        foreach (string extra in Items(Option("lib_extra_dirs")))
        {
            libraries.Add(extra);
        }

        int? speed = int.TryParse(Option("monitor_speed"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int baud) ? baud : null;
        return new PlatformIoProject(
            name,
            boardId,
            Value(sections, "platformio", "src_dir") ?? "src",
            libraries,
            includes,
            defines,
            [.. Items(Option("lib_deps")).Select(DependencyName)],
            speed);
    }

    /// <summary>
    /// The <c>lib_deps</c> no library folder under these roots answers to, by folder name or
    /// <c>library.properties</c> name, for a warning: the build itself decides by the sketch's includes.
    /// </summary>
    public IReadOnlyList<string> MissingLibraries(IEnumerable<string> roots)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (string root in roots.Where(Directory.Exists))
        {
            foreach (string library in Directory.EnumerateDirectories(root))
            {
                known.Add(Normalize(Path.GetFileName(library)));
                string properties = Path.Combine(library, "library.properties");
                if (File.Exists(properties))
                {
                    string? line = File.ReadLines(properties).FirstOrDefault(l => l.StartsWith("name=", StringComparison.Ordinal));
                    if (line is not null)
                    {
                        known.Add(Normalize(line[5..]));
                    }
                }
            }
        }

        return [.. LibraryDependencies.Where(d => !known.Contains(Normalize(d)))];
    }

    [GeneratedRegex(@"-(?<kind>[DI])\s*(?<value>(?:\\""(?:[^""\\]|\\.)*?\\""|""[^""]*""|'[^']*'|\S)+)")]
    private static partial Regex Flags();

    private static string Normalize(string name) =>
        new([.. name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);

    // "adafruit/DHT sensor library@^1.4.6" and "OneWire @ 2.3.7" and a git URL: the library's name.
    private static string DependencyName(string dependency)
    {
        string name = dependency.Trim();
        int at = name.IndexOf('@', StringComparison.Ordinal);
        if (at >= 0 && !name.Contains("://", StringComparison.Ordinal))
        {
            name = name[..at].Trim();
        }

        int slash = name.LastIndexOf('/');
        if (slash >= 0)
        {
            name = name[(slash + 1)..];
        }

        return name.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    private static string? First(string? list) => list is null ? null : Items(list).FirstOrDefault();

    private static string[] Items(string list) =>
        list.Split(['\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? Value(Dictionary<string, Dictionary<string, string>> sections, string section, string key) =>
        sections.TryGetValue(section, out Dictionary<string, string>? values) && values.TryGetValue(key, out string? value) ? value : null;

    // An environment's option: its own, those of what it extends, then the common [env]; ${section.key} expanded.
    private static string? EnvValue(Dictionary<string, Dictionary<string, string>> sections, string name, string key)
    {
        string section = "env:" + name;
        for (int depth = 0; depth < 8; depth++)
        {
            if (Value(sections, section, key) is { } value)
            {
                return Expand(sections, value);
            }

            if (First(Value(sections, section, "extends")) is not { } parent)
            {
                break;
            }

            section = parent;
        }

        return Value(sections, "env", key) is { } common ? Expand(sections, common) : null;
    }

    private static string Expand(Dictionary<string, Dictionary<string, string>> sections, string value) =>
        Regex.Replace(value, @"\$\{([^}.]+)\.([^}]+)\}", m => Value(sections, m.Groups[1].Value, m.Groups[2].Value) ?? "", RegexOptions.None, TimeSpan.FromSeconds(1));

    private static Dictionary<string, Dictionary<string, string>> Sections(string text)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        Dictionary<string, string>? current = null;
        string? key = null;
        foreach (string raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            string line = raw.TrimEnd();
            string trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] is ';' or '#')
            {
                continue;
            }

            if (char.IsWhiteSpace(line[0]) && current is not null && key is not null)
            {
                current[key] += "\n" + trimmed; // a continuation line
            }
            else if (trimmed[0] == '[' && trimmed.Contains(']', StringComparison.Ordinal))
            {
                string name = trimmed[1..trimmed.IndexOf(']', StringComparison.Ordinal)].Trim();
                sections[name] = current = new Dictionary<string, string>(StringComparer.Ordinal);
                key = null;
            }
            else if (current is not null && trimmed.Contains('=', StringComparison.Ordinal))
            {
                int equals = trimmed.IndexOf('=', StringComparison.Ordinal);
                key = trimmed[..equals].Trim();
                current[key] = trimmed[(equals + 1)..].Trim();
            }
        }

        return sections;
    }
}

public sealed class PlatformIoException : Exception
{
    public PlatformIoException()
    {
    }

    public PlatformIoException(string message)
        : base(message)
    {
    }

    public PlatformIoException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
