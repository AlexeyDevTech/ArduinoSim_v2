using System.Globalization;
using System.Resources;

namespace ArduSim.App.Localization;

/// <summary>UI strings from Resources/Strings.resx (neutral language: Russian).</summary>
public static class Strings
{
    private static readonly ResourceManager Manager = new("ArduSim.App.Resources.Strings", typeof(Strings).Assembly);

    /// <summary>Returns the localized string, or <c>!key!</c> so a missing key is visible on screen.</summary>
    public static string Get(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? $"!{key}!";
}
