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
