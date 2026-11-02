using System.Windows.Markup;

namespace ArduSim.App.Localization;

/// <summary>XAML access to UI strings: <c>Text="{l:Loc App_Title}"</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => Strings.Get(Key);
}
