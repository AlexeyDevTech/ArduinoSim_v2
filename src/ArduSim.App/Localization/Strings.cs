using System.Globalization;
using System.Resources;

namespace ArduSim.App.Localization;

/// <summary>UI strings from Resources/Strings.resx (neutral language: Russian).</summary>
public static class Strings
{
    private static readonly ResourceManager Manager = new("ArduSim.App.Resources.Strings", typeof(Strings).Assembly);

    /// <summary>Returns the localized string, or <c>!key!</c> so a missing key is visible on screen.</summary>
    public static string Get(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? $"!{key}!";

    /// <summary>Formats a localized pattern with the current culture.</summary>
#pragma warning disable CA1863 // patterns come from resources, one call site per message
    public static string Format(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, Get(key), args);
#pragma warning restore CA1863
}
