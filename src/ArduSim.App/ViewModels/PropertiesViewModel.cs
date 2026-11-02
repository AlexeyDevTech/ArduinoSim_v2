using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows.Media;
using ArduSim.App.Localization;
using ArduSim.Core.Components;
using ArduSim.Core.Editing;
using ArduSim.Core.Projects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArduSim.App.ViewModels;

public enum PropertiesMode
{
    Project,
    Part,
    Parts,
    Wire,
}

public sealed record OptionItem(string Value, string Label, Color? Swatch);

/// <summary>One editable property of the selected part (docs/UI.md §2.4).</summary>
public sealed partial class PropertyField : ObservableObject
{
    private readonly Action<PropertyField, string> _commit;
    private bool _loading;

    public PropertyField(string name, string label, PropertyKind kind, string value, string? unit, IReadOnlyList<OptionItem> options, Action<PropertyField, string> commit)
    {
        Name = name;
        Label = label;
        Kind = kind;
        Unit = unit;
        Options = options;
        _commit = commit;
        _loading = true;
        Text = value;
        SelectedOption = options.FirstOrDefault(o => o.Value == value);
        _loading = false;
    }

    public string Name { get; }

    public string Label { get; }

    public PropertyKind Kind { get; }

    public string? Unit { get; }

    public IReadOnlyList<OptionItem> Options { get; }

    public bool IsChoice => Kind == PropertyKind.Choice;

    public bool HasSwatches => Options.Any(o => o.Swatch is not null);

    public bool IsKey => Name == "key";

    /// <summary>The editor kind (docs/UI.md §2.4): "swatches", "segmented" (≤ 4 choices), "dropdown", "key" or "text".</summary>
    public string Editor => IsChoice
        ? HasSwatches ? "swatches" : Options.Count <= 4 ? "segmented" : "dropdown"
        : IsKey ? "key" : "text";

    [ObservableProperty]
    public partial string Text { get; set; }

    [ObservableProperty]
    public partial OptionItem? SelectedOption { get; set; }

    [ObservableProperty]
    public partial string Error { get; set; } = "";

    [RelayCommand]
    public void Commit() => _commit(this, Text);

    partial void OnSelectedOptionChanged(OptionItem? value)
    {
        if (!_loading && value is not null)
        {
            _commit(this, value.Value);
        }
    }
}

public sealed record PinRow(string Pin, string Connection);

/// <summary>The right-hand panel: the selection's properties, or the project's when nothing is selected.</summary>
public sealed partial class PropertiesViewModel : ObservableObject
{
    private static readonly int[] BaudRates = [300, 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200];

    private readonly SceneViewModel _scene;
    private readonly Func<ProjectSettings> _settings;
    private readonly Action<ProjectSettings> _updateSettings;

    public PropertiesViewModel(SceneViewModel scene, Func<ProjectSettings> settings, Action<ProjectSettings> updateSettings)
    {
        _scene = scene;
        _settings = settings;
        _updateSettings = updateSettings;
        scene.SelectionChanged += Rebuild;
        scene.Editor.Changed += Rebuild;
        scene.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SceneViewModel.IsLocked))
            {
                OnPropertyChanged(nameof(IsEditable));
            }
        };
        Rebuild();
    }

    public ObservableCollection<PropertyField> Fields { get; } = [];

    public ObservableCollection<PinRow> Pins { get; } = [];

    public IReadOnlyList<OptionItem> WireColors { get; } =
        [.. WirePalette.Swatches.Select(s => new OptionItem(s, Strings.Get("Color_" + s), WirePalette.SwatchColor(s)))];

    public bool IsEditable => !_scene.IsLocked;

    [ObservableProperty]
    public partial PropertiesMode Mode { get; private set; }

    [ObservableProperty]
    public partial string Title { get; private set; } = "";

    [ObservableProperty]
    public partial string Subtitle { get; private set; } = "";

    [ObservableProperty]
    public partial string IdText { get; set; } = "";

    [ObservableProperty]
    public partial string IdError { get; private set; } = "";

    [ObservableProperty]
    public partial OptionItem? WireColor { get; set; }

    [RelayCommand]
    private void Rename()
    {
        if (_scene.SinglePart is { } part && IdText != part.Id)
        {
            string id = IdText.Trim();
            if (_scene.Editor.RenamePart(part.Id, id))
            {
                _scene.Select(id, additive: false);
            }
            else
            {
                IdError = Strings.Get("Properties_BadId");
            }
        }
    }

    [RelayCommand]
    private void Rotate() => _scene.RotateSelection(clockwise: true);

    [RelayCommand]
    private void Flip() => _scene.FlipSelection();

    [RelayCommand]
    private void Delete() => _scene.DeleteSelection();

    partial void OnWireColorChanged(OptionItem? value)
    {
        if (value is not null && _scene.SelectedWire is { } wire)
        {
            _scene.Editor.SetWireColor(wire, value.Value);
        }
    }

    private void Rebuild()
    {
        Fields.Clear();
        Pins.Clear();
        IdError = "";
        if (_scene.SelectedWire is { } wireId && _scene.Editor.Scene.FindWire(wireId) is { } wire)
        {
            Mode = PropertiesMode.Wire;
            Title = wireId;
            Subtitle = Strings.Get("Properties_Wire");
            Pins.Add(new PinRow(Strings.Get("Properties_From"), Name(wire.From)));
            Pins.Add(new PinRow(Strings.Get("Properties_To"), Name(wire.To)));
            WireColor = WireColors.FirstOrDefault(c => c.Value == wire.Color);
            return;
        }

        if (_scene.SinglePart is { } part)
        {
            Mode = PropertiesMode.Part;
            Title = part.Id;
            IdText = part.Id;
            Subtitle = part.DisplayName;
            foreach (PropertyDefinition definition in part.Definition?.Properties ?? [])
            {
                if (definition.ShownWhen is not { } condition || part.Text(condition.Property, "") == condition.Value)
                {
                    Fields.Add(Field(part, definition));
                }
            }

            foreach (PinDefinition pin in part.Definition?.Pins ?? [])
            {
                var reference = new PinReference(part.Id, pin.Name);
                string[] ends = _scene.Editor.Scene.Wires
                    .Where(w => w.From == reference || w.To == reference)
                    .Select(w => Name(w.From == reference ? w.To : w.From))
                    .ToArray();
                if (part.Type != "board" || ends.Length > 0)
                {
                    Pins.Add(new PinRow(pin.Name, ends.Length == 0 ? "—" : string.Join(", ", ends)));
                }
            }

            return;
        }

        if (_scene.SelectedParts.Count > 1)
        {
            Mode = PropertiesMode.Parts;
            Title = Strings.Format("Properties_Many", _scene.SelectedParts.Count);
            Subtitle = "";
            return;
        }

        Mode = PropertiesMode.Project;
        Title = Strings.Get("Properties_Project");
        Subtitle = _scene.Board.DisplayName;
        ProjectSettings settings = _settings();
        Fields.Add(new PropertyField("baud", Strings.Get("Property_baud"), PropertyKind.Choice, settings.Serial.Baud.ToString(CultureInfo.InvariantCulture), null,
            [.. BaudRates.Select(b => new OptionItem(b.ToString(CultureInfo.InvariantCulture), b.ToString(CultureInfo.InvariantCulture), null))],
            (f, v) => _updateSettings(_settings() with { Serial = _settings().Serial with { Baud = int.Parse(v, CultureInfo.InvariantCulture) } })));
        Fields.Add(new PropertyField("libraryPaths", Strings.Get("Property_libraryPaths"), PropertyKind.Text, string.Join("; ", settings.LibraryPaths), null, [],
            (f, v) => _updateSettings(_settings() with { LibraryPaths = [.. v.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)] })));
    }

    private static string Name(PinReference pin) => $"{pin.Component}.{pin.Pin}";

    private PropertyField Field(PartViewModel part, PropertyDefinition definition)
    {
        string current = part.Text(definition.Name, definition.Default);
        IReadOnlyList<OptionItem> options = definition.Options?.Select(o => new OptionItem(
            o,
            Strings.TryGet(definition.Name == "color" ? "Color_" + o : $"Option_{definition.Name}_{o}") ?? o,
            definition.Name == "color" ? WirePalette.PartColor(o) : null)).ToList() ?? [];
        return new PropertyField(definition.Name, Strings.Get("Property_" + definition.Name), definition.Kind, current, definition.Unit, options, (field, text) =>
        {
            field.Error = "";
            JsonElement value;
            if (definition.Kind == PropertyKind.Number)
            {
                if (!double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ||
                    number < definition.Minimum || number > definition.Maximum)
                {
                    field.Error = Strings.Format("Properties_Range", definition.Minimum, definition.Maximum);
                    return;
                }

                value = JsonSerializer.SerializeToElement(number);
            }
            else
            {
                value = JsonSerializer.SerializeToElement(text);
            }

            _scene.Editor.SetProperty(part.Id, definition.Name, value);
        });
    }
}
