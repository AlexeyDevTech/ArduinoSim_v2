using System.Globalization;
using System.Text.RegularExpressions;

namespace ArduSim.Core.Diagnostics;

public enum SimSeverity
{
    Info,
    Warning,
    Error,
}

/// <param name="Time">Virtual time in nanoseconds.</param>
/// <param name="Code">Stable machine-readable kind, e.g. <c>floating-input</c>.</param>
/// <param name="Message">The English text, for logs and tests.</param>
/// <param name="ComponentId">Scene component the message points at, when there is one.</param>
public sealed record SimDiagnostic(ulong Time, SimSeverity Severity, string Code, string Message, string? ComponentId)
{
    /// <summary>Tells apart messages that share a code (e.g. <c>scene</c>); null for the code's main message.</summary>
    public string? Variant { get; init; }

    /// <summary>The message's arguments, formatted invariantly, for a translated pattern.</summary>
    public IReadOnlyList<string> Args { get; init; } = [];

    /// <summary>The resource key of the message: the code, plus the variant when there is one.</summary>
    public string Key => Variant is null ? Code : $"{Code}.{Variant}";
}

/// <summary>
/// The simulation log: warnings point at components, and most are reported once per run. Messages
/// are interpolated strings so the UI can translate them: it looks up a pattern by
/// <see cref="SimDiagnostic.Key"/> and fills in <see cref="SimDiagnostic.Args"/>.
/// </summary>
public sealed partial class SimulationLog(Func<ulong> clock)
{
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private readonly List<SimDiagnostic> _entries = [];

    public event Action<SimDiagnostic>? Reported;

    public IReadOnlyList<SimDiagnostic> Entries => _entries;

    public void Report(SimSeverity severity, string code, FormattableString message, string? componentId = null, string? variant = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        var diagnostic = new SimDiagnostic(clock(), severity, code, message.ToString(CultureInfo.InvariantCulture), componentId)
        {
            Variant = variant,
            Args = FormatArguments(message),
        };
        _entries.Add(diagnostic);
        Reported?.Invoke(diagnostic);
    }

    /// <summary>Reports only the first time <paramref name="key"/> is seen in this run.</summary>
    public bool ReportOnce(string key, SimSeverity severity, string code, FormattableString message, string? componentId = null, string? variant = null)
    {
        if (!_reported.Add(key))
        {
            return false;
        }

        Report(severity, code, message, componentId, variant);
        return true;
    }

    /// <summary>Each argument formatted as its first placeholder asks (e.g. <c>{0:0.0#}</c>), invariantly.</summary>
    private static string[] FormatArguments(FormattableString message)
    {
        object?[] args = message.GetArguments();
        var result = new string[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            result[i] = Convert.ToString(args[i], CultureInfo.InvariantCulture) ?? "";
        }

        foreach (Match item in Placeholder().Matches(message.Format))
        {
            int index = int.Parse(item.Groups[1].Value, CultureInfo.InvariantCulture);
            if (index < args.Length && item.Groups[2].Success && args[index] is IFormattable formattable)
            {
                result[index] = formattable.ToString(item.Groups[2].Value, CultureInfo.InvariantCulture);
            }
        }

        return result;
    }

    [GeneratedRegex(@"\{(\d+)(?:,[^:}]*)?(?::([^}]*))?\}")]
    private static partial Regex Placeholder();
}
