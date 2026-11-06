using System.Globalization;
using ArduSim.App.Localization;

namespace ArduSim.App.ViewModels;

/// <summary>Voltages and currents as text for the view, in the user's number format.</summary>
internal static class MeterFormat
{
    /// <summary>A peak this far above the average is worth saying (PWM).</summary>
    public const double PeakRatio = 1.05;

    private const double SmallestAmps = 0.5e-6;

    /// <summary>Below half a microamp a current is shown as none: it is leakage, not a reading.</summary>
    public static bool IsVisible(double amps) => Math.Abs(amps) >= SmallestAmps;

    public static string Volts(double volts) =>
        string.Create(CultureInfo.CurrentCulture, $"{volts:0.00} {Strings.Get("Unit_V")}");

    public static string Amps(double amps)
    {
        double size = Math.Abs(amps);
        return size >= 0.001
            ? string.Create(CultureInfo.CurrentCulture, $"{size * 1000:0.0} {Strings.Get("Unit_mA")}")
            : string.Create(CultureInfo.CurrentCulture, $"{size * 1e6:0} {Strings.Get("Unit_uA")}");
    }
}
