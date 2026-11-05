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
    private string _committed;

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
        _committed = value;
        SelectedOption = options.FirstOrDefault(o => o.Value == value);
        _loading = false;
    }

    /// <summary>How far ↑, ↓ and the focused wheel move a live reading; also the slider's small step.</summary>
    public double SliderStep { get; init; } = 1;

    public double SliderLargeStep => SliderStep * 10;

    /// <summary>The model changed (this field's commit, undo): show the value without committing it back.</summary>
    public void Sync(string value)
    {
        _loading = true;
        Text = value;
        _committed = value;
        SelectedOption = Options.FirstOrDefault(o => o.Value == value);
        Error = "";
        _loading = false;
    }

    /// <summary>Esc: back to the last committed value (docs/UI.md §2.4).</summary>
    public void Revert()
    {
        _loading = true;
        Text = _committed;
        Error = "";
        _loading = false;
    }

    /// <summary>↑, ↓ or the wheel on a number: one step, committed at once. False when this is not a number.</summary>
    public bool Step(int direction)
    {
        if (Kind != PropertyKind.Number || !IsEnabled || ParseNumber(Text) is not { } number)
        {
            return false;
        }

        double step = IsLive ? SliderStep : Math.Abs(Maximum - Minimum) is > 0 and <= 1 ? 0.01 : number == 0 ? 1 : Math.Max(1, Math.Pow(10, Math.Floor(Math.Log10(Math.Abs(number))) - (Math.Abs(number) < 100 ? 0 : 1)));
        double next = Math.Round((number + (direction * step)) / step) * step;
        if (Maximum > Minimum)
        {
            next = Math.Clamp(next, Minimum, Maximum);
        }

        Text = next.ToString("0.###", CultureInfo.CurrentCulture);
        Commit();
        return true;
    }

    /// <summary>A number as people type it: a decimal comma, and multipliers as on a schematic (10k, 4,7к, 1М, 100m).</summary>
    public static double? ParseNumber(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string trimmed = text.Trim().Replace(',', '.');
        foreach (string unit in (string[])["Ohm", "ohm", "Ом", "ом", "Ω", "Гц", "Hz"])
        {
            if (trimmed.EndsWith(unit, StringComparison.Ordinal))
            {
                trimmed = trimmed[..^unit.Length].TrimEnd();
                break;
            }
        }

        double scale = 1;
        if (trimmed.Length > 1)
        {
            scale = trimmed[^1] switch
            {
                'k' or 'K' or 'к' or 'К' => 1e3,
                'M' or 'М' => 1e6,
                'm' or 'м' => 1e-3,
                'u' or 'µ' or 'μ' => 1e-6,
                _ => 1,
            };
            if (scale != 1)
            {
                trimmed = trimmed[..^1].TrimEnd();
            }
        }

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number * scale : null;
    }

    /// <summary>A sensor reading while the simulation runs: a slider beside the text, applied at once (ADR-011).</summary>
    public bool IsLive { get; init; }

    public double Minimum { get; init; }

    public double Maximum { get; init; }

    /// <summary>The field can be edited: always for live readings, otherwise only while the scene is not locked.</summary>
    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = true;

    [ObservableProperty]
    public partial double SliderValue { get; set; }

    /// <summary>The simulation (or a drag on the part) changed the reading: show it without committing it back.</summary>
    public void Show(double value)
    {
        _loading = true;
        SliderValue = value;
        Text = value.ToString("0.##", CultureInfo.CurrentCulture);
        _committed = Text;
        _loading = false;
    }

    partial void OnSliderValueChanged(double value)
    {
        if (!_loading)
        {
            _commit(this, value.ToString(CultureInfo.InvariantCulture));
        }
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
    public void Commit()
    {
        if (Text == _committed && Error.Length == 0)
        {
            return;
        }

        _commit(this, Text);
        if (Error.Length == 0)
        {
            _committed = Text;
        }
    }

    partial void OnSelectedOptionChanged(OptionItem? value)
    {
        if (!_loading && value is not null)
        {
            _commit(this, value.Value);
        }
    }
}

/// <summary>A pin of the selected part, or an end of the selected wire: what it joins, and its level while running.</summary>
public sealed partial class PinRow(string pin, string connection, PinReference? reference = null, string? tip = null) : ObservableObject
{
    public string Pin { get; } = pin;

    public string Connection { get; } = connection;

    public PinReference? Reference { get; } = reference;

    public string? Tip { get; } = tip;

    [ObservableProperty]
    public partial string Level { get; set; } = "";
}

/// <summary>The right-hand panel: the selection's properties, or the project's when nothing is selected.</summary>
public sealed partial class PropertiesViewModel : ObservableObject
{

    private readonly SceneViewModel _scene;
    private readonly Func<ProjectSettings> _settings;
    private readonly Action<ProjectSettings> _updateSettings;
    private readonly Action<string>? _openFolder;
    private Action? _unsubscribe;
    private (string Id, ComponentDefinition? Definition, bool Locked)? _fieldsFor;
    private (HashSet<string> Ids, bool Locked)? _commonFor;

    /// <param name="openFolder">Opens a folder of the project in Explorer (the SD card's), creating it; null without a project.</param>
    public PropertiesViewModel(SceneViewModel scene, Func<ProjectSettings> settings, Action<ProjectSettings> updateSettings, Action<string>? openFolder = null)
    {
        _scene = scene;
        _settings = settings;
        _updateSettings = updateSettings;
        _openFolder = openFolder;
        scene.SelectionChanged += Rebuild;
        scene.Editor.Changed += Rebuild;
        scene.LevelsChanged += UpdateLevels;
        scene.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SceneViewModel.IsLocked))
            {
                OnPropertyChanged(nameof(IsEditable));
                Rebuild();
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

    /// <summary>The selected part keeps files in a project folder (the SD card).</summary>
    public bool HasCardFolder => _openFolder is not null && _scene.SinglePart is { Type: "sd-card" };

    [RelayCommand]
    private void OpenCardFolder()
    {
        if (_scene.SinglePart is { Type: "sd-card" } card)
        {
            _openFolder?.Invoke(card.SdFolder);
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

    /// <summary>Takes in what is still being typed in a field: Save and Run do not wait for it to lose the focus.</summary>
    public void CommitPending()
    {
        foreach (PropertyField field in Fields.Where(f => f.Editor == "text" && !f.IsLive).ToList())
        {
            field.Commit();
        }
    }

    /// <summary>A row of the pin list was clicked: its net lights up on the schematic (docs/UI.md §2.4).</summary>
    public void HighlightPin(PinRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Reference is { } reference)
        {
            _scene.Hover(reference, null);
        }
    }

    private void UpdateLevels()
    {
        foreach (PinRow row in Pins)
        {
            row.Level = row.Reference is { } reference ? _scene.LevelText(reference) : "";
        }
    }

    /// <summary>The same part with the same fields (an edit, an undo): values change in place, so the focus stays in the field.</summary>
    private bool RefreshInPlace(PartViewModel part)
    {
        if (_fieldsFor is not { } shown || shown.Id != part.Id || !ReferenceEquals(shown.Definition, part.Definition) || shown.Locked != _scene.IsLocked)
        {
            return false;
        }

        List<PropertyDefinition> visible = VisibleProperties(part);
        if (!visible.Select(d => d.Name).SequenceEqual(Fields.Select(f => f.Name)))
        {
            return false;
        }

        foreach ((PropertyField field, PropertyDefinition definition) in Fields.Zip(visible))
        {
            if (!field.IsLive)
            {
                field.Sync(part.Text(definition.Name, definition.Default));
            }
        }

        return true;
    }

    private static List<PropertyDefinition> VisibleProperties(PartViewModel part) =>
        [.. (part.Definition?.Properties ?? []).Where(d => d.ShownWhen is not { } condition || part.Text(condition.Property, "") == condition.Value)];

    /// <summary>The same parts with the same common fields: values change in place, so the focus stays.</summary>
    private bool RefreshCommonInPlace()
    {
        if (_commonFor is not { } shown || shown.Locked != _scene.IsLocked || !shown.Ids.SetEquals(_scene.SelectedParts))
        {
            return false;
        }

        List<PartViewModel> parts = SelectedEditableParts();
        List<PropertyDefinition> common = CommonProperties(parts);
        if (!common.Select(d => d.Name).SequenceEqual(Fields.Select(f => f.Name)))
        {
            return false;
        }

        foreach ((PropertyField field, PropertyDefinition definition) in Fields.Zip(common))
        {
            field.Sync(CommonValue(parts, definition));
        }

        return true;
    }

    private List<PartViewModel> SelectedEditableParts() =>
        [.. _scene.SelectedParts.Select(_scene.Find).OfType<PartViewModel>().Where(p => p.Type != "board")];

    private static List<PropertyDefinition> CommonProperties(List<PartViewModel> parts) =>
        parts.Count < 2 ? [] : [.. (parts[0].Definition?.Properties ?? []).Where(definition => !definition.Live && definition.Name != "key"
            && parts.All(p => VisibleProperties(p).Any(d => d.Name == definition.Name && d.Kind == definition.Kind && (d.Options ?? []).SequenceEqual(definition.Options ?? []))))];

    private static string CommonValue(List<PartViewModel> parts, PropertyDefinition definition)
    {
        string[] values = [.. parts.Select(p => p.Text(definition.Name, definition.Default)).Distinct()];
        return values.Length == 1 ? values[0] : "—";
    }

    private void Rebuild()
    {
        OnPropertyChanged(nameof(HasCardFolder));
        bool keepFields = _scene.SelectedWire is null
            && (_scene.SinglePart is { } same ? RefreshInPlace(same) : _scene.SelectedParts.Count > 1 && RefreshCommonInPlace());
        if (!keepFields)
        {
            _unsubscribe?.Invoke();
            _unsubscribe = null;
            Fields.Clear();
            _fieldsFor = null;
            _commonFor = null;
        }

        Pins.Clear();
        IdError = "";
        if (_scene.SelectedWire is { } wireId && _scene.Editor.Scene.FindWire(wireId) is { } wire)
        {
            Mode = PropertiesMode.Wire;
            Title = wireId;
            Subtitle = Strings.Get("Properties_Wire");
            Pins.Add(new PinRow(Strings.Get("Properties_From"), Name(wire.From), wire.From, _scene.PinTooltip(wire.From)));
            Pins.Add(new PinRow(Strings.Get("Properties_To"), Name(wire.To), wire.To, _scene.PinTooltip(wire.To)));
            UpdateLevels();
            WireColor = WireColors.FirstOrDefault(c => c.Value == wire.Color);
            return;
        }

        if (_scene.SinglePart is { } part)
        {
            Mode = PropertiesMode.Part;
            Title = part.Id;
            IdText = part.Id;
            Subtitle = part.DisplayName;
            if (!keepFields)
            {
                foreach (PropertyDefinition definition in VisibleProperties(part))
                {
                    Fields.Add(definition.Live && _scene.IsLocked ? LiveField(part, definition) : Field(part, definition));
                }

                _fieldsFor = (part.Id, part.Definition, _scene.IsLocked);
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
                    Pins.Add(new PinRow(pin.Label ?? pin.Name, ends.Length == 0 ? "—" : string.Join(", ", ends), reference, _scene.PinTooltip(reference)));
                }
            }

            UpdateLevels();
            return;
        }

        if (_scene.SelectedParts.Count > 1)
        {
            Mode = PropertiesMode.Parts;
            Title = Strings.Format("Properties_Many", _scene.SelectedParts.Count);
            Subtitle = "";
            if (!keepFields)
            {
                AddCommonFields();
                _commonFor = ([.. _scene.SelectedParts], _scene.IsLocked);
            }

            return;
        }

        Mode = PropertiesMode.Project;
        Title = Strings.Get("Properties_Project");
        Subtitle = _scene.Board.DisplayName;
        ProjectSettings settings = _settings();
        Fields.Add(new PropertyField("libraryPaths", Strings.Get("Property_libraryPaths"), PropertyKind.Text, string.Join("; ", settings.LibraryPaths), null, [],
            (f, v) => _updateSettings(_settings() with { LibraryPaths = [.. v.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)] }))
        {
            IsEnabled = !_scene.IsLocked,
        });
    }

    /// <summary>Several parts: the properties they all have, "—" where the values differ; one edit changes all of them in one undo step (docs/UI.md §2.4).</summary>
    private void AddCommonFields()
    {
        List<PartViewModel> parts = SelectedEditableParts();
        foreach (PropertyDefinition definition in CommonProperties(parts))
        {
            IReadOnlyList<OptionItem> options = OptionsOf(definition);
            var field = new PropertyField(definition.Name, Strings.Get("Property_" + definition.Name), definition.Kind, CommonValue(parts, definition), definition.Unit, options, (field, text) =>
            {
                if (ValueOf(field, definition, text) is not { } value)
                {
                    return;
                }

                _scene.Editor.Checkpoint();
                foreach (PartViewModel part in parts)
                {
                    _scene.Editor.SetProperty(part.Id, definition.Name, value, checkpoint: false);
                }
            })
            {
                Minimum = definition.Minimum,
                Maximum = definition.Maximum,
                IsEnabled = !_scene.IsLocked,
            };
            Fields.Add(field);
        }
    }

    private static List<OptionItem> OptionsOf(PropertyDefinition definition) =>
        definition.Options?.Select(o => new OptionItem(
            o,
            Strings.TryGet(definition.Name == "color" ? "Color_" + o : $"Option_{definition.Name}_{o}") ?? o,
            definition.Name == "color" ? WirePalette.PartColor(o) : null)).ToList() ?? [];

    /// <summary>The typed text as the property's value, or null with the reason on the field.</summary>
    private static JsonElement? ValueOf(PropertyField field, PropertyDefinition definition, string text)
    {
        field.Error = "";
        if (definition.Kind != PropertyKind.Number)
        {
            return JsonSerializer.SerializeToElement(text);
        }

        if (PropertyField.ParseNumber(text) is not { } number || number < definition.Minimum || number > definition.Maximum)
        {
            field.Error = Strings.Format("Properties_Range", definition.Minimum, definition.Maximum);
            return null;
        }

        return JsonSerializer.SerializeToElement(number);
    }

    private static string Name(PinReference pin) => $"{pin.Component}.{pin.Pin}";

    // While the run goes on, a reading goes to the part as an input: not saved, not undoable.
    private PropertyField LiveField(PartViewModel part, PropertyDefinition definition)
    {
        var field = new PropertyField(definition.Name, Strings.Get("Property_" + definition.Name), definition.Kind, "", definition.Unit, [], (field, text) =>
        {
            field.Error = "";
            if (PropertyField.ParseNumber(text) is not { } number || number < definition.Minimum || number > definition.Maximum)
            {
                field.Error = Strings.Format("Properties_Range", definition.Minimum, definition.Maximum);
                return;
            }

            part.SetLive(definition.Name, number);
        })
        {
            IsLive = true,
            Minimum = definition.Minimum,
            Maximum = definition.Maximum,
            SliderStep = PartViewModel.LiveStep(definition.Name),
        };
        field.Show(part.LiveValue(definition.Name));
        void OnLive(string name, double value)
        {
            if (name == definition.Name)
            {
                field.Show(value);
            }
        }

        part.LiveChanged += OnLive;
        _unsubscribe += () => part.LiveChanged -= OnLive;
        return field;
    }

    private PropertyField Field(PartViewModel part, PropertyDefinition definition)
    {
        string current = part.Text(definition.Name, definition.Default);
        var field = new PropertyField(definition.Name, Strings.Get("Property_" + definition.Name), definition.Kind, current, definition.Unit, OptionsOf(definition), (field, text) =>
        {
            if (ValueOf(field, definition, text) is { } value)
            {
                _scene.Editor.SetProperty(part.Id, definition.Name, value);
            }
        })
        {
            Minimum = definition.Minimum,
            Maximum = definition.Maximum,
        };
        field.IsEnabled = !_scene.IsLocked;
        return field;
    }
}
