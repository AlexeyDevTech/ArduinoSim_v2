using System.Collections.ObjectModel;
using ArduSim.App.Localization;
using ArduSim.Core.Components;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArduSim.App.ViewModels;

/// <param name="Preset">Preset id when the row places a part with preset properties (e.g. "LCD 1602 I2C").</param>
public sealed record PaletteItem(string Type, string? Preset, string Name, string Category)
{
    /// <summary>What a drag from the palette carries: <c>type</c> or <c>type#preset</c>.</summary>
    public string Request => Preset is null ? Type : Type + "#" + Preset;

    public static (string Type, string? Preset) Parse(string request)
    {
        ArgumentNullException.ThrowIfNull(request);
        int hash = request.IndexOf('#', StringComparison.Ordinal);
        return hash < 0 ? (request, null) : (request[..hash], request[(hash + 1)..]);
    }
}

public sealed partial class PaletteGroup(string name, IEnumerable<PaletteItem> items) : ObservableObject
{
    public string Name { get; } = name;

    public ObservableCollection<PaletteItem> Items { get; } = [.. items];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;
}

/// <summary>Parts by category with live search (docs/UI.md §2.3). Boards are chosen in the toolbar, not placed; presets are rows of their own.</summary>
public sealed partial class PaletteViewModel : ObservableObject
{
    private static readonly string[] CategoryOrder = ["output", "input", "sensor", "display", "passive"];
    private readonly List<PaletteItem> _all;

    public PaletteViewModel(ComponentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _all = registry.Definitions
            .Where(d => d.Category != "board")
            .SelectMany(d => d.Presets.Count == 0
                ? [new PaletteItem(d.Type, null, Strings.Get(d.NameKey), d.Category)]
                : d.Presets.Select(p => new PaletteItem(d.Type, p.Id, Strings.Get(p.NameKey), d.Category)))
            .OrderBy(i => Array.IndexOf(CategoryOrder, i.Category) is var index && index < 0 ? 99 : index)
            .ThenBy(i => i.Name, StringComparer.CurrentCulture)
            .ToList();
        Rebuild();
    }

    public ObservableCollection<PaletteGroup> Groups { get; } = [];

    [ObservableProperty]
    public partial string Search { get; set; } = "";

    partial void OnSearchChanged(string value) => Rebuild();

    private void Rebuild()
    {
        Groups.Clear();
        string query = Search.Trim();
        foreach (IGrouping<string, PaletteItem> group in _all
            .Where(i => query.Length == 0 || i.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || i.Type.Contains(query, StringComparison.OrdinalIgnoreCase))
            .GroupBy(i => i.Category))
        {
            Groups.Add(new PaletteGroup(Strings.Get("Category_" + group.Key), group));
        }
    }
}
