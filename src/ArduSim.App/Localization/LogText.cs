using System.Globalization;
using ArduSim.Ipc;

namespace ArduSim.App.Localization;

/// <summary>
/// Simulation log messages in the UI language: the pattern <c>Log_&lt;key&gt;</c> filled with the
/// entry's arguments; the English text when there is no pattern (docs/UI.md §5).
/// </summary>
public static class LogText
{
    public static string Localize(LogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Args is null || Strings.TryGet("Log_" + (entry.Key ?? entry.Code)) is not { } pattern)
        {
            return entry.Message;
        }

        try
        {
            return string.Format(CultureInfo.CurrentCulture, pattern, [.. entry.Args]);
        }
        catch (FormatException)
        {
            return entry.Message;
        }
    }
}
