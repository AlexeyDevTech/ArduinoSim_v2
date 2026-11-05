using System.Windows;

namespace ArduSim.App.Services;

/// <summary>
/// The app's color theme (docs/UI.md §3.1): dark by default, light on request. Tokens live in
/// <c>Themes/Dark.xaml</c> and <c>Themes/Light.xaml</c> with the same keys; switching replaces the
/// first merged dictionary, and views that draw with fixed colors listen to <see cref="Changed"/>.
/// </summary>
public static class Theme
{
    private static readonly Uri DarkTokens = new("pack://application:,,,/ArduSim.App;component/Themes/Dark.xaml");
    private static readonly Uri LightTokens = new("pack://application:,,,/ArduSim.App;component/Themes/Light.xaml");

    public static event Action? Changed;

    public static bool IsLight { get; private set; }

    private static readonly (string Key, double Size)[] FontSizes = [("FontSize.Caption", 11), ("FontSize.Small", 12), ("FontSize.Body", 13), ("FontSize.Title", 15), ("FontSize.Display", 28)];

    /// <summary>Windows' "Make text bigger", 1.0 to 2.25; 1 when it cannot be read.</summary>
    public static double SystemTextScale()
    {
        object? percent = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Accessibility", "TextScaleFactor", 100);
        return percent is int value ? Math.Clamp(value / 100.0, 1, 2.25) : 1;
    }

    /// <summary>Scales the text sizes: set above the theme's tokens, so a theme switch keeps them.</summary>
    public static void ApplyTextScale(double scale)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        foreach ((string key, double size) in FontSizes)
        {
            app.Resources[key] = size * scale;
        }
    }

    public static void Apply(bool light)
    {
        if (Application.Current is not { } app || light == IsLight)
        {
            return;
        }

        IsLight = light;
        app.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = light ? LightTokens : DarkTokens };
        Changed?.Invoke();
    }
}
