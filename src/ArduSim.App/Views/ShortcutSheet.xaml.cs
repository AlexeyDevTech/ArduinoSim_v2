using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ArduSim.App.Localization;
using ArduSim.App.ViewModels;

namespace ArduSim.App.Views;

/// <summary>The keys and gestures sheet (F1): one list, filled from <c>Shortcuts_&lt;group&gt;_&lt;n&gt;</c> strings as "keys|action".</summary>
public partial class ShortcutSheet : Window
{
    private static readonly string[] GroupNames = ["Run", "Canvas", "Wires", "Editor", "Panels", "Running"];
    private static ShortcutSheet? _open;

    private readonly List<Group> _groups;

    private ShortcutSheet()
    {
        InitializeComponent();
        _groups = [.. GroupNames.Select(g => new Group(Strings.Get("Shortcuts_" + g), Read(g)))];
        Groups.ItemsSource = _groups;
    }

    private static List<Entry> Read(string group)
    {
        var entries = new List<Entry>();
        for (int i = 1; Strings.TryGet($"Shortcuts_{group}_{i}") is { } text; i++)
        {
            string[] parts = text.Split('|', 2);
            entries.Add(new Entry(parts[0], parts.Length > 1 ? parts[1] : ""));
        }

        return entries;
    }

    public static void Open(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_open is not null)
        {
            _open.Close();
            return;
        }

        _open = new ShortcutSheet { Owner = owner };
        _open.Closed += (_, _) => _open = null;
        _open.PlaceOver(owner);
        _open.Show();
        _open.Filter.Focus();
    }

    private void PlaceOver(Window owner)
    {
        const double TitleBar = 48;
        Point corner = owner.PointToScreen(new Point(0, 0));
        if (PresentationSource.FromVisual(owner)?.CompositionTarget is { } target)
        {
            corner = target.TransformFromDevice.Transform(corner);
        }

        Width = Math.Min(Width, owner.ActualWidth);
        Left = corner.X + owner.ActualWidth - Width;
        Top = corner.Y + TitleBar;
        Height = Math.Max(200, owner.ActualHeight - TitleBar);
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e)
    {
        string[] tokens = PartSearch.Normalize(Filter.Text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        FilterHint.Visibility = Filter.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Groups.ItemsSource = tokens.Length == 0
            ? _groups
            : _groups
                .Select(g => g with { Items = [.. g.Items.Where(i => tokens.All(t => PartSearch.Normalize($"{g.Title} {i.Keys} {i.Action}").Contains(t, StringComparison.Ordinal)))] })
                .Where(g => g.Items.Count > 0)
                .ToList();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.F1)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private sealed record Group(string Title, List<Entry> Items);

    private sealed record Entry(string Keys, string Action);
}
