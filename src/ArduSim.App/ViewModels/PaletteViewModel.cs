using System.Collections.ObjectModel;
using ArduSim.App.Localization;
using ArduSim.Core.Components;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArduSim.App.ViewModels;

/// <param name="Preset">Preset id when the row places a part with preset properties (e.g. "LCD 1602 I2C").</param>
public sealed record PaletteItem(string Type, string? Preset, string Name, string Category)
{
    /// <summary>What a drag from the palette carries: <c>type</c> or <c>type#preset</c>.</summary>
    public string Request => Preset is null ? Type : Type + "#" + Preset;

    /// <summary>What the search looks through besides the name: everyday words, the category.</summary>
    public string Keywords { get; init; } = "";

    /// <summary>The row's tooltip: what the part is, its pins and how to place it.</summary>
    public string Tip { get; init; } = "";

    public string Glyph => Category switch
    {
        "output" => "\uE781",
        "input" => "\uE7C9",
        "sensor" => "\uE9CA",
        "display" => "\uE7F4",
        "module" => "\uE964",
        _ => "\uE945",
    };

    public static (string Type, string? Preset) Parse(string request)
    {
        ArgumentNullException.ThrowIfNull(request);
        int hash = request.IndexOf('#', StringComparison.Ordinal);
        return hash < 0 ? (request, null) : (request[..hash], request[(hash + 1)..]);
    }
}

public sealed partial class PaletteGroup(string key, string name, IEnumerable<PaletteItem> items, bool isExpanded, Action<PaletteGroup>? toggled, Action<PaletteItem>? chosen = null) : ObservableObject
{
    public string Key { get; } = key;

    /// <summary>This group's row under the highlight. Each list binds its own: an item outside a list would reset that list's binding.</summary>
    [ObservableProperty]
    public partial PaletteItem? Selected { get; set; }

    partial void OnSelectedChanged(PaletteItem? value)
    {
        if (value is not null)
        {
            chosen?.Invoke(value);
        }
    }

    public ObservableCollection<PaletteItem> Items { get; } = [.. items];

    /// <summary>The category and how many rows it holds (or how many match the search).</summary>
    public string Header { get; } = $"{name} ({items.Count()})";

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = isExpanded;

    partial void OnIsExpandedChanged(bool value) => toggled?.Invoke(this);
}

/// <summary>Parts by category with live search (docs/UI.md §2.3). Boards are chosen in the toolbar, not placed; presets are rows of their own.</summary>
public sealed partial class PaletteViewModel : ObservableObject
{
    // By how often a beginner needs them: resistors come right after the LED they protect.
    private static readonly string[] CategoryOrder = ["output", "passive", "input", "sensor", "display", "module"];
    private static readonly string[] OutputRank = ["led", "rgb-led", "buzzer", "servo", "relay", "ws2812"];
    private static readonly string[] InputRank = ["pushbutton", "potentiometer", "slide-switch", "rotary-encoder", "joystick", "keypad", "ir-remote", "ir-receiver"];
    private readonly List<PaletteItem> _all;
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal) { "display" };
    private bool _userCollapsed;

    public PaletteViewModel(ComponentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _all = registry.Definitions
            .Where(d => d.Category != "board")
            .SelectMany(d => d.Presets.Count == 0
                ? [Row(d, null, d.NameKey, null)]
                : d.Presets.Select(p => Row(d, p.Id, p.NameKey, d.PresetValues(p))))
            .OrderBy(i => Array.IndexOf(CategoryOrder, i.Category) is var index && index < 0 ? 99 : index)
            .ThenBy(Rank)
            .ThenBy(i => i.Name, StringComparer.CurrentCulture)
            .ToList();
        Rebuild();
    }

    private static PaletteItem Row(ComponentDefinition definition, string? preset, string nameKey, Dictionary<string, System.Text.Json.JsonElement>? values)
    {
        string name = Strings.Get(nameKey);
        string keywords = $"{Strings.TryGet("Keywords_" + definition.Type)} {Strings.TryGet("Category_" + definition.Category)}";
        string pins = string.Join(", ", definition.Resolve(values).Pins.Select(p => p.Label ?? p.Name).Distinct());
        string description = Strings.TryGet("PartInfo_" + definition.Type) ?? "";
        return new PaletteItem(definition.Type, preset, name, definition.Category)
        {
            Keywords = keywords,
            Tip = string.Join(Environment.NewLine, new[] { name, description, Strings.Format("Palette_Pins", pins), Strings.Get("Palette_ItemTip") }.Where(l => l.Length > 0)),
        };
    }

    private static int Rank(PaletteItem item)
    {
        string[] ranks = item.Category == "output" ? OutputRank : item.Category == "input" ? InputRank : [];
        int index = Array.IndexOf(ranks, item.Type);
        return index < 0 ? 99 : index;
    }

    public ObservableCollection<PaletteGroup> Groups { get; } = [];

    /// <summary>A search that found nothing says so instead of leaving a blank panel.</summary>
    public bool HasNoMatches => Groups.Count == 0;

    /// <summary>The row Enter places, moved by ↑ and ↓; the first match while searching.</summary>
    [ObservableProperty]
    public partial PaletteItem? Highlighted { get; set; }

    [ObservableProperty]
    public partial string Search { get; set; } = "";

    /// <summary>What to remember in the user's state; null until the user folded something.</summary>
    public IReadOnlyList<string>? CollapsedCategories => _userCollapsed ? [.. _collapsed] : null;

    public void SetCollapsed(IReadOnlyList<string>? categories)
    {
        if (categories is not null)
        {
            _userCollapsed = true;
            _collapsed.Clear();
            _collapsed.UnionWith(categories);
            Rebuild();
        }
    }

    [RelayCommand]
    private void ClearSearch() => Search = "";

    /// <summary>↑ and ↓ in the search box: through the visible rows, around the ends.</summary>
    public void MoveHighlight(int direction)
    {
        List<PaletteItem> visible = [.. Groups.Where(g => g.IsExpanded).SelectMany(g => g.Items)];
        if (visible.Count > 0)
        {
            int index = Highlighted is null ? -1 : visible.IndexOf(Highlighted);
            Highlighted = visible[index < 0 ? (direction > 0 ? 0 : visible.Count - 1) : (index + direction + visible.Count) % visible.Count];
        }
    }

    partial void OnSearchChanged(string value) => Rebuild();

    partial void OnHighlightedChanged(PaletteItem? value)
    {
        foreach (PaletteGroup group in Groups)
        {
            group.Selected = value is not null && group.Items.Contains(value) ? value : null;
        }
    }

    private void Rebuild()
    {
        Groups.Clear();
        string query = Search.Trim();
        List<PaletteItem> found = PartSearch.Filter(_all, i => $"{i.Name} {i.Type} {i.Keywords}", query);
        foreach (IGrouping<string, PaletteItem> group in found.GroupBy(i => i.Category))
        {
            // Searching shows every match; folding while searching is not remembered.
            bool searching = query.Length > 0;
            Groups.Add(new PaletteGroup(group.Key, Strings.Get("Category_" + group.Key), group, searching || !_collapsed.Contains(group.Key), searching ? null : OnToggled, item => Highlighted = item));
        }

        Highlighted = query.Length > 0 ? found.FirstOrDefault() : null;
        OnPropertyChanged(nameof(HasNoMatches));
    }

    private void OnToggled(PaletteGroup group)
    {
        _userCollapsed = true;
        if (group.IsExpanded)
        {
            _collapsed.Remove(group.Key);
        }
        else
        {
            _collapsed.Add(group.Key);
        }
    }
}