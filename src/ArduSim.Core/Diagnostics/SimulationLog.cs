namespace ArduSim.Core.Diagnostics;

public enum SimSeverity
{
    Info,
    Warning,
    Error,
}

/// <param name="Time">Virtual time in nanoseconds.</param>
/// <param name="Code">Stable machine-readable kind, e.g. <c>floating-input</c>.</param>
/// <param name="ComponentId">Scene component the message points at, when there is one.</param>
public sealed record SimDiagnostic(ulong Time, SimSeverity Severity, string Code, string Message, string? ComponentId);

/// <summary>The simulation log: warnings point at components, and most are reported once per run.</summary>
public sealed class SimulationLog(Func<ulong> clock)
{
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private readonly List<SimDiagnostic> _entries = [];

    public event Action<SimDiagnostic>? Reported;

    public IReadOnlyList<SimDiagnostic> Entries => _entries;

    public void Report(SimSeverity severity, string code, string message, string? componentId = null)
    {
        var diagnostic = new SimDiagnostic(clock(), severity, code, message, componentId);
        _entries.Add(diagnostic);
        Reported?.Invoke(diagnostic);
    }

    /// <summary>Reports only the first time <paramref name="key"/> is seen in this run.</summary>
    public bool ReportOnce(string key, SimSeverity severity, string code, string message, string? componentId = null)
    {
        if (!_reported.Add(key))
        {
            return false;
        }

        Report(severity, code, message, componentId);
        return true;
    }
}
