using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArduSim.App.ViewModels;

/// <summary>One plotted value: its name (a label, or its position on the line) and its samples.</summary>
public sealed class PlotSeries(string name)
{
    /// <summary>Hidden from the lines and from the scale by a click on the legend.</summary>
    public bool Visible { get; set; } = true;

    public string Name { get; } = name;

    /// <summary>Samples by line number; NaN where a line did not carry this value.</summary>
    public List<double> Values { get; } = [];
}

/// <summary>
/// The Serial Plotter (brief §4, docs/UI.md §2.6): every Serial line with numbers is one sample per
/// value, in the Arduino Serial Plotter format — values separated by spaces, tabs or commas,
/// optionally named <c>label:value</c>. The last <see cref="Capacity"/> lines are kept.
/// </summary>
public sealed partial class PlotterViewModel : ObservableObject
{
    public const int Capacity = 500;

    private static readonly char[] Separators = [' ', '\t', ','];

    private string _partial = "";
    private bool _skipped;

    public ObservableCollection<PlotSeries> Series { get; } = [];

    /// <summary>Lines plotted so far, oldest first (at most <see cref="Capacity"/>).</summary>
    public int Count { get; private set; }

    /// <summary>Grows on every change, so the view knows when to redraw.</summary>
    [ObservableProperty]
    public partial long Version { get; private set; }

    [ObservableProperty]
    public partial bool Paused { get; set; }

    /// <summary>Text the sketch sent (any chunk, not necessarily whole lines).</summary>
    public void Append(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string buffered = _partial + text;
        int end = buffered.LastIndexOf('\n');
        if (end < 0)
        {
            _partial = buffered;
            return;
        }

        _partial = buffered[(end + 1)..];
        if (Paused)
        {
            _skipped = true;
            return;
        }

        bool changed = false;
        foreach (string line in buffered[..end].Split('\n'))
        {
            changed |= AddLine(line.TrimEnd('\r'));
        }

        if (changed)
        {
            Version++;
        }
    }

    [RelayCommand]
    public void Clear()
    {
        Series.Clear();
        Count = 0;
        _partial = "";
        _skipped = false;
        Version++;
    }

    [RelayCommand]
    private void TogglePause() => Paused = !Paused;

    // Lines went by during the pause: the plot shows a break there, not a line joining what was on both sides.
    partial void OnPausedChanged(bool value)
    {
        if (!value && _skipped && Count > 0)
        {
            AddSample([]);
            Version++;
        }

        _skipped = false;
    }

    private bool AddLine(string line)
    {
        var values = new List<(string Name, double Value)>();
        int position = 0;
        foreach (string token in line.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = token.LastIndexOf(':');
            string name = colon > 0 ? token[..colon] : "";
            string number = colon >= 0 ? token[(colon + 1)..] : token;
            if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                position++;
                values.Add((name.Length > 0 ? name : position.ToString(CultureInfo.InvariantCulture), value));
            }
        }

        if (values.Count == 0)
        {
            return false;
        }

        foreach ((string name, double _) in values)
        {
            if (!Series.Any(s => s.Name == name))
            {
                var series = new PlotSeries(name);
                series.Values.AddRange(Enumerable.Repeat(double.NaN, Count));
                Series.Add(series);
            }
        }

        AddSample(values);
        return true;
    }

    private void AddSample(List<(string Name, double Value)> values)
    {
        foreach (PlotSeries series in Series)
        {
            series.Values.Add(values.FirstOrDefault(v => v.Name == series.Name) is { Name: not null } hit ? hit.Value : double.NaN);
            if (series.Values.Count > Capacity)
            {
                series.Values.RemoveAt(0);
            }
        }

        Count = Math.Min(Count + 1, Capacity);
    }
}
