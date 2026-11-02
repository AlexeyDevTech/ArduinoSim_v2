using System.Collections.ObjectModel;
using ArduSim.App.Localization;
using ArduSim.Core.Components;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArduSim.App.ViewModels;

public sealed record PaletteItem(string Type, string Name, string Category);

public sealed partial class PaletteGroup(string name, IEnumerable<PaletteItem> items) : ObservableObject
{
    public string Name { get; } = name;

    public ObservableCollection<PaletteItem> Items { get; } = [.. items];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;
}

/// <summary>Parts by category with live search (docs/UI.md §2.3). Boards are chosen in the toolbar, not placed.</summary>
public sealed partial class PaletteViewModel : ObservableObject
{
    private static readonly string[] CategoryOrder = ["output", "input", "passive", "display"];
    private readonly List<PaletteItem> _all;

    public PaletteViewModel(ComponentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _all = registry.Definitions
            .Where(d => d.Category != "board")
            .Select(d => new PaletteItem(d.Type, Strings.Get(d.NameKey), d.Category))
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
