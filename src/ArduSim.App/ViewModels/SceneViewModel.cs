using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using ArduSim.Core.Boards;
using ArduSim.Core.Components;
using ArduSim.Core.Projects;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArduSim.App.ViewModels;

/// <summary>The project's scene as the canvas shows it: parts with live state and wires (fixed layout until M3).</summary>
public sealed class SceneViewModel
{
    private const double Margin = 40;
    private readonly Dictionary<string, PartViewModel> _parts;

    public SceneViewModel(ProjectFile project, BoardProfile board, ComponentRegistry registry, Action<string, string, double> sendInput)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(registry);
        List<ComponentDocument> documents = [.. project.Scene.Components];
        if (!documents.Any(d => d.Type == board.Id))
        {
            documents.Insert(0, new ComponentDocument { Id = "board", Type = board.Id });
        }

        Parts = [.. documents.Select(d => new PartViewModel(d, registry.Find(d.Type), sendInput))];
        _parts = Parts.ToDictionary(p => p.Id, StringComparer.Ordinal);
        Wires = [.. project.Scene.Wires.Select(w => WireViewModel.Create(w, _parts)).OfType<WireViewModel>()];

        double left = Parts.Min(p => p.X) - Margin;
        double top = Parts.Min(p => p.Y) - Margin;
        double right = Math.Max(Parts.Max(p => p.X + p.Width), Wires.SelectMany(w => w.Points).DefaultIfEmpty().Max(p => p.X)) + Margin;
        double bottom = Math.Max(Parts.Max(p => p.Y + p.Height), Wires.SelectMany(w => w.Points).DefaultIfEmpty().Max(p => p.Y)) + Margin;
        Origin = new TranslateTransform(-left, -top);
        Width = right - left;
        Height = bottom - top;
    }

    public ObservableCollection<PartViewModel> Parts { get; }

    public ObservableCollection<WireViewModel> Wires { get; }

    public double Width { get; }

    public double Height { get; }

    /// <summary>Shifts scene coordinates so the content starts at the canvas origin.</summary>
    public Transform Origin { get; }

    public PartViewModel? Find(string id) => _parts.GetValueOrDefault(id);

    public void ResetState()
    {
        foreach (PartViewModel part in Parts)
        {
            part.Apply(new Dictionary<string, double>(), reset: true);
        }
    }
}

/// <summary>One part on the canvas. The view picks its template by <see cref="Type"/>.</summary>
public sealed partial class PartViewModel : ObservableObject
{
    private readonly Action<string, string, double> _sendInput;
    private readonly ComponentDocument _document;
    private bool _applying;

    public PartViewModel(ComponentDocument document, ComponentDefinition? definition, Action<string, string, double> sendInput)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
        _sendInput = sendInput;
        Definition = definition;
        Width = definition?.Width ?? 60;
        Height = definition?.Height ?? 40;
        _applying = true;
        Value = Number("value", 0.5);
        SwitchRight = Number("position", 0) != 0;
        _applying = false;
    }

    public ComponentDefinition? Definition { get; }

    public string Id => _document.Id;

    /// <summary>Template key; boards share one ("board") and unknown types show a placeholder.</summary>
    public string Type => Definition is null ? "unknown" : Definition.Category == "board" ? "board" : _document.Type;

    public double X => _document.X;

    public double Y => _document.Y;

    public double Width { get; }

    public double Height { get; }

    public double Rotation => _document.Rotation;

    public double FlipScale => _document.Flip ? -1 : 1;

    public string Key => Text("key", "");

    public Color LedColor => Text("color", "red") switch
    {
        "green" => Color.FromRgb(0x3A, 0xE0, 0x6A),
        "blue" => Color.FromRgb(0x3E, 0x8B, 0xFF),
        "yellow" => Color.FromRgb(0xFF, 0xD8, 0x3A),
        "orange" => Color.FromRgb(0xFF, 0x96, 0x2E),
        "white" => Color.FromRgb(0xF2, 0xF4, 0xFF),
        "black" => Color.FromRgb(0x30, 0x30, 0x34),
        _ => Color.FromRgb(0xFF, 0x3B, 0x3B),
    };

    public string ResistanceText
    {
        get
        {
            double ohms = Number("resistance", 220);
            return ohms >= 1e6 ? (ohms / 1e6).ToString("0.##", CultureInfo.InvariantCulture) + " МΩ"
                : ohms >= 1e3 ? (ohms / 1e3).ToString("0.##", CultureInfo.InvariantCulture) + " кΩ"
                : ohms.ToString("0.##", CultureInfo.InvariantCulture) + " Ω";
        }
    }

    /// <summary>Board pins with their positions, for the header drawing.</summary>
    public IEnumerable<PinViewModel> Pins => Definition?.Pins.Select(p => new PinViewModel(p.Name, p.X, p.Y)) ?? [];

    [ObservableProperty]
    public partial double Brightness { get; set; }

    [ObservableProperty]
    public partial double OnboardLed { get; set; }

    [ObservableProperty]
    public partial Color RgbColor { get; set; } = Colors.Black;

    [ObservableProperty]
    public partial double RgbIntensity { get; set; }

    [ObservableProperty]
    public partial bool Pressed { get; set; }

    [ObservableProperty]
    public partial bool SwitchRight { get; set; }

    [ObservableProperty]
    public partial double Frequency { get; set; }

    [ObservableProperty]
    public partial double Value { get; set; }

    [ObservableProperty]
    public partial bool Highlighted { get; set; }

    public double KnobAngle => -135 + (270 * Value);

    public void Apply(IReadOnlyDictionary<string, double> state, bool reset = false)
    {
        _applying = true;
        try
        {
            double Get(string key, double fallback) => state.TryGetValue(key, out double v) ? v : reset ? fallback : double.NaN;
            void Set(string key, double fallback, Action<double> apply)
            {
                double v = Get(key, fallback);
                if (!double.IsNaN(v))
                {
                    apply(v);
                }
            }

            Set("brightness", 0, v => Brightness = v);
            Set("L", 0, v => OnboardLed = v);
            Set("pressed", 0, v => Pressed = v != 0);
            Set("position", Number("position", 0), v => SwitchRight = v != 0);
            Set("frequency", 0, v => Frequency = v);
            Set("value", Number("value", 0.5), v => Value = v);
            if (state.ContainsKey("r") || state.ContainsKey("g") || state.ContainsKey("b") || reset)
            {
                double r = Get("r", 0), g = Get("g", 0), b = Get("b", 0);
                r = double.IsNaN(r) ? 0 : r;
                g = double.IsNaN(g) ? 0 : g;
                b = double.IsNaN(b) ? 0 : b;
                double max = Math.Max(r, Math.Max(g, b));
                RgbIntensity = max;
                RgbColor = max > 0 ? Color.FromRgb((byte)(255 * r / max), (byte)(255 * g / max), (byte)(255 * b / max)) : Colors.Black;
            }
        }
        finally
        {
            _applying = false;
        }
    }

    public void Press()
    {
        Pressed = true;
        _sendInput(Id, "pressed", 1);
    }

    public void Release()
    {
        if (Pressed)
        {
            Pressed = false;
            _sendInput(Id, "pressed", 0);
        }
    }

    public void Toggle()
    {
        SwitchRight = !SwitchRight;
        _sendInput(Id, "position", SwitchRight ? 1 : 0);
    }

    partial void OnValueChanged(double value)
    {
        OnPropertyChanged(nameof(KnobAngle));
        if (!_applying)
        {
            _sendInput(Id, "value", value);
        }
    }

    private string Text(string name, string fallback) =>
        _document.Properties is { } p && p.TryGetValue(name, out JsonElement e)
            ? e.ValueKind == JsonValueKind.String ? e.GetString() ?? fallback : e.GetRawText()
            : Definition?.Properties.FirstOrDefault(d => d.Name == name)?.Default ?? fallback;

    private double Number(string name, double fallback) =>
        double.TryParse(Text(name, fallback.ToString(CultureInfo.InvariantCulture)), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;
}

public sealed record PinViewModel(string Name, double X, double Y)
{
    /// <summary>Duplicate pins (GND.2, RST.2) are labelled like their twin.</summary>
    public string Label => Name.EndsWith(".2", StringComparison.Ordinal) ? Name[..^2] : Name;

    /// <summary>Label offset from the pin: top-row labels go below it, bottom-row labels above.</summary>
    public double LabelOffset => Y < 35 ? 4 : -10;
}

/// <summary>A wire as an orthogonal polyline through its waypoints, colored by what it carries.</summary>
public sealed class WireViewModel
{
    private WireViewModel(PointCollection points, Brush stroke)
    {
        Points = points;
        Stroke = stroke;
    }

    public PointCollection Points { get; }

    public Brush Stroke { get; }

    public static WireViewModel? Create(WireDocument wire, IReadOnlyDictionary<string, PartViewModel> parts)
    {
        ArgumentNullException.ThrowIfNull(wire);
        if (PinPosition(wire.From, parts) is not { } from || PinPosition(wire.To, parts) is not { } to)
        {
            return null;
        }

        var corners = new List<Point> { from };
        corners.AddRange(wire.Waypoints.Where(w => w.Count >= 2).Select(w => new Point(w[0], w[1])));
        corners.Add(to);
        var points = new PointCollection { corners[0] };
        for (int i = 1; i < corners.Count; i++)
        {
            Point previous = corners[i - 1];
            Point next = corners[i];
            if (previous.X != next.X && previous.Y != next.Y)
            {
                points.Add(new Point(next.X, previous.Y));
            }

            points.Add(next);
        }

        points.Freeze();
        return new WireViewModel(points, StrokeFor(wire));
    }

    private static Point? PinPosition(PinReference end, IReadOnlyDictionary<string, PartViewModel> parts)
    {
        if (!parts.TryGetValue(end.Component, out PartViewModel? part) || part.Definition?.FindPin(end.Pin) is not { } pin)
        {
            return null;
        }

        (double x, double y) = ComponentGeometry.PinPosition(part.Definition, pin, part.X, part.Y, (int)part.Rotation, part.FlipScale < 0);
        return new Point(x, y);
    }

    private static SolidColorBrush StrokeFor(WireDocument wire)
    {
        string[] pins = [wire.From.Pin, wire.To.Pin];
        Color color = pins.Any(p => p.StartsWith("GND", StringComparison.Ordinal)) ? Color.FromRgb(0x5A, 0x60, 0x6E)
            : pins.Any(p => p is "5V" or "3V3" or "VIN") ? Color.FromRgb(0xE5, 0x48, 0x48)
            : Color.FromRgb(0x2B, 0xC4, 0xA9);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
