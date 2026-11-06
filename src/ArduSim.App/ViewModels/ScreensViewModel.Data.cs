using System.Collections.ObjectModel;
using System.Globalization;
using ArduSim.App.Localization;
using ArduSim.Core.Components;
using ArduSim.Core.Screens;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArduSim.App.ViewModels;

/// <summary>A kind of element in the left panel's palette. What cannot go on the selected screen is there all the same, and says why.</summary>
/// <param name="Glyph">Its icon, from the icon font.</param>
public sealed record ElementTool(ElementKind Kind, string Title, string Tip, string Glyph, bool IsAvailable);

/// <summary>A variable in the left panel's list.</summary>
public sealed record VariableRow(string Name, string KindText, bool Saved);

/// <summary>One thing on the screen, in the right panel's list: what hides under another can be reached from there.</summary>
public sealed record ElementRow(string Id, string Title);

/// <summary>A symbol in the left panel: built in, or the project's own.</summary>
public sealed record GlyphRow(string Name, IReadOnlyList<byte> Rows, bool IsOwn);

/// <summary>One step of what a transition, a menu item or arriving on a screen does.</summary>
public sealed partial class ActionRow : ObservableObject
{
    private readonly ActionList _owner;
    private bool _loaded;

    public ActionRow(ActionList owner, int index)
    {
        _owner = owner;
        Index = index;
    }

    public int Index { get; }

    [ObservableProperty]
    public partial IReadOnlyList<OptionItem> Kinds { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<OptionItem> Targets { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsTarget), nameof(NeedsValue), nameof(ValueLabel))]
    public partial OptionItem? SelectedKind { get; set; }

    [ObservableProperty]
    public partial OptionItem? SelectedTarget { get; set; }

    [ObservableProperty]
    public partial string Value { get; set; } = "";

    public ActionKind Kind => Enum.TryParse(SelectedKind?.Value, out ActionKind kind) ? kind : ActionKind.Set;

    public bool NeedsTarget => Kind is not (ActionKind.InputCommit or ActionKind.InputErase or ActionKind.InputClear or ActionKind.Hook);

    public bool NeedsValue => Kind is ActionKind.Set or ActionKind.Add or ActionKind.Beep or ActionKind.Output or ActionKind.Hook;

    public string ValueLabel => Kind switch
    {
        ActionKind.Set => Strings.Get("Screens_ActionValueSet"),
        ActionKind.Add => Strings.Get("Screens_ActionValueAdd"),
        ActionKind.Beep => Strings.Get("Screens_ActionValueBeep"),
        ActionKind.Output => Strings.Get("Screens_ActionValueOutput"),
        ActionKind.Hook => Strings.Get("Screens_ActionValueHook"),
        _ => "",
    };

    public void Update(ScreenAction action, IReadOnlyList<OptionItem> kinds, IReadOnlyList<OptionItem> targets)
    {
        _loaded = false;
        Kinds = kinds;
        Targets = targets;
        SelectedKind = kinds.FirstOrDefault(k => k.Value == action.Kind.ToString());
        SelectedTarget = targets.FirstOrDefault(t => t.Value == action.Target);
        Value = action.Kind == ActionKind.Hook ? action.Target : action.Value;
        _loaded = true;
    }

    public ScreenAction ToAction() => Kind == ActionKind.Hook
        ? new ScreenAction(ActionKind.Hook, ScreenCodeGenerator.Identifier(Value.Trim()))
        : new ScreenAction(Kind, NeedsTarget ? SelectedTarget?.Value ?? "" : "", NeedsValue ? Value.Trim() : "");

    partial void OnSelectedKindChanged(OptionItem? value)
    {
        if (_loaded && value is not null)
        {
            _owner.KindChanged(this);
        }
    }

    partial void OnSelectedTargetChanged(OptionItem? value)
    {
        if (_loaded && value is not null)
        {
            _owner.Edited();
        }
    }

    partial void OnValueChanged(string value)
    {
        if (_loaded)
        {
            _owner.Edited();
        }
    }
}

/// <summary>The steps of one thing that can act, in the right panel. The rows stay as they are while the model follows them, so that a dropdown is not removed under the pointer.</summary>
public sealed partial class ActionList : ObservableObject
{
    private readonly ScreensViewModel _model;
    private readonly Action<IReadOnlyList<ScreenAction>> _commit;

    public ActionList(ScreensViewModel model, Action<IReadOnlyList<ScreenAction>> commit)
    {
        _model = model;
        _commit = commit;
    }

    public ObservableCollection<ActionRow> Rows { get; } = [];

    public void Update(IReadOnlyList<ScreenAction> actions)
    {
        while (Rows.Count > actions.Count)
        {
            Rows.RemoveAt(Rows.Count - 1);
        }

        IReadOnlyList<OptionItem> kinds = _model.ActionKinds();
        for (int i = 0; i < actions.Count; i++)
        {
            if (i == Rows.Count)
            {
                Rows.Add(new ActionRow(this, i));
            }

            Rows[i].Update(actions[i], kinds, _model.ActionTargets(actions[i].Kind));
        }
    }

    internal void Edited() => _commit([.. Rows.Select(r => r.ToAction())]);

    // Another kind of step wants another kind of target: the first it can use, or what it had if that still fits.
    internal void KindChanged(ActionRow row)
    {
        IReadOnlyList<OptionItem> targets = _model.ActionTargets(row.Kind);
        string target = targets.Any(t => t.Value == row.SelectedTarget?.Value) ? row.SelectedTarget!.Value : targets.Count > 0 ? targets[0].Value : "";
        ScreenAction[] actions = [.. Rows.Select(r => r.ToAction())];
        actions[row.Index] = new ScreenAction(row.Kind, row.NeedsTarget ? target : "", DefaultValue(row.Kind, row.Value));
        _commit(actions);
    }

    private static string DefaultValue(ActionKind kind, string typed) => kind switch
    {
        ActionKind.Beep => "1000 200",
        ActionKind.Set or ActionKind.Add or ActionKind.Output => typed.Trim().Length > 0 && !typed.Contains(' ', StringComparison.Ordinal) ? typed.Trim() : "1",
        _ => "",
    };

    [RelayCommand]
    private void Add()
    {
        IReadOnlyList<OptionItem> targets = _model.ActionTargets(ActionKind.Set);
        _commit([.. Rows.Select(r => r.ToAction()), new ScreenAction(ActionKind.Set, targets.Count > 0 ? targets[0].Value : "", "1")]);
    }

    [RelayCommand]
    private void Remove(ActionRow? row)
    {
        if (row is not null)
        {
            _commit([.. Rows.Where(r => r != row).Select(r => r.ToAction())]);
        }
    }
}

public sealed partial class ScreensViewModel
{
    private const string Yes = "1";
    private const string No = "0";
    private const string ConditionKey = "condition";
    private const string AnyKeyKey = "anykey";

    private ActionList? _enterActions;
    private IReadOnlyList<ElementTool> _tools = [];
    private (bool Generated, bool Oled, bool CanPlace, bool Has) _toolsFor;

    /// <summary>The palette: what can go on a screen of this display, in this kind of project.</summary>
    public IReadOnlyList<ElementTool> ElementTools
    {
        get
        {
            // The same list while nothing about it changed: a tile must not be replaced under a click.
            (bool, bool, bool, bool) key = (IsGenerated, IsOled, CanPlaceElements, HasDocument);
            if (key != _toolsFor)
            {
                _toolsFor = key;
                _tools = BuildTools();
            }

            return _tools;
        }
    }

    /// <summary>An element can go on the selected screen: in the model, or as code where its drawing ends.</summary>
    public bool CanPlaceElements => SelectedScreen is { } screen && (IsGenerated || screen.DrawInsert is not null);

    /// <summary>What the palette cannot do in this project, in one line.</summary>
    public string ToolsNote =>
        Document is null ? ""
        : !CanPlaceElements ? Strings.Get("Screens_ToolsNoPlace")
        : IsFromCode ? Strings.Get("Screens_ToolsFromCode")
        : "";

    [ObservableProperty]
    public partial bool ToolsExpanded { get; set; } = true;

    [ObservableProperty]
    public partial bool ScreensExpanded { get; set; } = true;

    [ObservableProperty]
    public partial bool DataExpanded { get; set; } = true;

    [ObservableProperty]
    public partial bool SymbolsExpanded { get; set; } = true;

    public string ScreensHeader => Strings.Format("Screens_GroupScreens", Rows.Count);

    public string DataHeader => Strings.Format("Screens_GroupData", VariableRows.Count);

    public string SymbolsHeader => Strings.Format("Screens_GroupSymbols", GlyphRows.Count);

    private List<ElementTool> BuildTools()
    {
        var tools = new List<ElementTool>();
        void Add(ElementKind kind, string glyph, string name)
        {
            bool allowed = IsKindAllowed(kind);
            string why = !allowed ? "\n" + Strings.Get("Screens_ToolOnlyGenerated") : !CanPlaceElements ? "\n" + Strings.Get("Screens_ToolsNoPlace") : "";
            tools.Add(new ElementTool(kind, Strings.Get("Screens_Add" + name), Strings.Get("Screens_Add" + name + "Tip") + why, glyph, allowed && CanPlaceElements));
        }

        if (Document is null)
        {
            return tools;
        }

        Add(ElementKind.Text, "\uE8D2", "Text");
        Add(ElementKind.Value, "\uE8EF", "Value");
        Add(ElementKind.BigNumber, "\uE8E9", "Big");
        Add(ElementKind.Bar, "\uE9D2", "Bar");
        Add(ElementKind.Symbol, "\uE734", "Symbol");
        Add(ElementKind.Input, "\uE765", "Input");
        Add(ElementKind.Drawn, "\uE943", "Drawn");
        if (IsOled)
        {
            Add(ElementKind.Line, "\uE738", "Line");
            Add(ElementKind.Frame, "\uE739", "Frame");
            Add(ElementKind.Box, "\uE73B", "Box");
        }

        return tools;
    }

    // What this kind of project can hold. A shape needs pixels; the rest of the new kinds need the model's data, which code that was read does not have.
    private bool IsKindAllowed(ElementKind kind) => kind switch
    {
        ElementKind.Text or ElementKind.Value => true,
        ElementKind.Line or ElementKind.Frame or ElementKind.Box => IsOled,
        _ => IsGenerated,
    };

    /// <summary>A click on a tile: on the selected screen, at the next free place, with the screen in view to see it.</summary>
    [RelayCommand]
    private void PlaceElement(ElementTool? tool)
    {
        if (tool is { IsAvailable: true } && SelectedScreen is { } screen)
        {
            _host.ShowScreens();
            IsFace = true;
            AddElementAt(tool.Kind, 0, NextFree(screen));
        }
    }

    /// <summary>Whether a dragged element can be dropped on a card of the map.</summary>
    public bool CanPlaceOn(string screen, ElementKind kind) =>
        IsKindAllowed(kind) && Document?.Find(screen) is { } node && (IsGenerated || node.DrawInsert is not null);

    /// <summary>A tile dropped on a card of the map: that screen opens, with the element at its next free place.</summary>
    public void PlaceOn(string screen, ElementKind kind)
    {
        if (CanPlaceOn(screen, kind))
        {
            SelectScreen(screen);
            IsFace = true;
            AddElementAt(kind, 0, NextFree(SelectedScreen!));
        }
    }

    public ObservableCollection<VariableRow> VariableRows { get; } = [];

    public ObservableCollection<GlyphRow> GlyphRows { get; } = [];

    public ObservableCollection<ElementRow> ElementRows { get; } = [];

    public ElementRow? SelectedElementRow
    {
        get => ElementRows.FirstOrDefault(r => r.Id == SelectedElementId);
        set
        {
            if (value is not null && value.Id != SelectedElementId)
            {
                SelectElement(value.Id);
            }
        }
    }

    /// <summary>The screen's elements are listed: in a generated project, where conditions can stack them in one place.</summary>
    public bool ShowsElementList => IsGenerated && IsAboutScreen && ElementRows.Count > 0;

    /// <summary>What arriving on the selected screen does.</summary>
    public ActionList EnterActions => _enterActions ??= new ActionList(this, actions =>
    {
        if (SelectedScreenId is { } id)
        {
            _editor?.UpdateScreen(id, s => s with { OnEnter = actions });
        }
    });

    [ObservableProperty]
    public partial string? SelectedVariableName { get; private set; }

    [ObservableProperty]
    public partial string? SelectedGlyphName { get; private set; }

    public VariableRow? SelectedVariableRow
    {
        get => VariableRows.FirstOrDefault(r => r.Name == SelectedVariableName);
        set
        {
            if (value is not null && value.Name != SelectedVariableName)
            {
                SelectVariable(value.Name);
            }
        }
    }

    public GlyphRow? SelectedGlyphRow
    {
        get => GlyphRows.FirstOrDefault(r => r.Name == SelectedGlyphName);
        set
        {
            if (value is not null && value.Name != SelectedGlyphName)
            {
                SelectGlyph(value.Name);
            }
        }
    }

    public ScreenVariable? SelectedVariable => Document?.Variables.FirstOrDefault(v => v.Name == SelectedVariableName);

    public ScreenGlyph? SelectedGlyph => Document is { } document && SelectedGlyphName is { } name ? ScreenFont.Find(document, name) : null;

    public IReadOnlyList<byte> SelectedGlyphRows => SelectedGlyph?.Rows ?? new byte[8];

    public bool HasSelectedGlyph => IsGenerated && SelectedGlyph is not null;

    /// <summary>Nothing of the data is selected: the panel is about the screen or its element.</summary>
    private bool IsAboutScreen => SelectedVariableName is null && SelectedGlyphName is null;

    public bool ShowsEnterActions => IsGenerated && ShowsScreenPanel;

    /// <summary>What will not work as it reads, in words: an empty string when all is well.</summary>
    public string ProblemsText
    {
        get
        {
            if (Document is not { Source: ScreenSource.Generated } document)
            {
                return "";
            }

            ScreenProblem[] problems = [.. ScreenCheck.Of(document, Wiring)];
            IEnumerable<string> lines = problems.Take(4).Select(p => ProblemText(document, p));
            string text = string.Join("\n", lines);
            return problems.Length > 4 ? text + "\n" + Strings.Format("Screens_ProblemsMore", problems.Length - 4) : text;
        }
    }

    private static string ProblemText(ScreenDocument document, ScreenProblem problem)
    {
        string text = Strings.Format("Screens_Problem_" + problem.Kind, problem.Detail);
        if (problem.Screen.Length == 0)
        {
            return Strings.Format("Screens_ProblemAnywhere", text);
        }

        ScreenNode? screen = document.Find(problem.Screen);
        return Strings.Format("Screens_ProblemOn", screen?.Title.Length > 0 ? screen.Title : problem.Screen, text);
    }

    private void RebuildElementRows()
    {
        ElementRows.Clear();
        foreach (ScreenElement element in IsGenerated ? SelectedScreen?.Elements ?? [] : [])
        {
            string kind = Strings.Get("Screens_Element_" + element.Kind);
            ElementRows.Add(new ElementRow(element.Id, element.Kind == ElementKind.Text ? element.Text : kind + ": " + element.Text));
        }
    }

    private void RebuildData(ScreenDocument? document)
    {
        VariableRows.Clear();
        GlyphRows.Clear();
        if (document is not { Source: ScreenSource.Generated })
        {
            SelectedVariableName = null;
            SelectedGlyphName = null;
            return;
        }

        foreach (ScreenVariable variable in document.Variables)
        {
            VariableRows.Add(new VariableRow(variable.Name, Strings.Get("Screens_VariableKind_" + variable.Kind), variable.Saved));
        }

        foreach (ScreenGlyph glyph in ScreenFont.BuiltIn)
        {
            ScreenGlyph own = document.Glyphs.FirstOrDefault(g => g.Name == glyph.Name) ?? glyph;
            GlyphRows.Add(new GlyphRow(glyph.Name, own.Rows, !ReferenceEquals(own, glyph)));
        }

        foreach (ScreenGlyph glyph in document.Glyphs.Where(g => ScreenFont.BuiltIn.All(b => b.Name != g.Name)))
        {
            GlyphRows.Add(new GlyphRow(glyph.Name, glyph.Rows, true));
        }

        if (SelectedVariableName is not null && SelectedVariable is null)
        {
            SelectedVariableName = null;
        }

        if (SelectedGlyphName is not null && SelectedGlyph is null)
        {
            SelectedGlyphName = null;
        }
    }

    public void SelectVariable(string? name)
    {
        Notice = "";
        SelectedVariableName = name;
        SelectedGlyphName = null;
        SelectedElementId = null;
        SelectedTransition = null;
        RaiseAll();
    }

    public void SelectGlyph(string? name)
    {
        Notice = "";
        SelectedGlyphName = name;
        SelectedVariableName = null;
        SelectedElementId = null;
        SelectedTransition = null;
        RaiseAll();
    }

    [RelayCommand]
    private void AddVariable()
    {
        if (_editor is not null)
        {
            SelectVariable(_editor.AddVariable(VariableKind.Number, "value", []).Name);
        }
    }

    [RelayCommand]
    private void AddGlyph()
    {
        if (_editor is not null)
        {
            SelectGlyph(_editor.AddGlyph("symbol").Name);
        }
    }

    /// <summary>A dot of the selected symbol. A drag over several dots undoes as one, and is written when it ends.</summary>
    public void SetGlyphDot(int column, int row, bool on, bool finished)
    {
        if (_editor is null || SelectedGlyph is not { } glyph || column is < 0 or > 4 || row is < 0 or > 7)
        {
            return;
        }

        byte[] rows = [.. glyph.Rows];
        byte bit = (byte)(0x10 >> column);
        rows[row] = (byte)(on ? rows[row] | bit : rows[row] & ~bit);
        bool first = !_resizing;
        _resizing = !finished;
        _editor.SetGlyphRows(glyph.Name, rows, checkpoint: first);
        OnPropertyChanged(nameof(SelectedGlyphRows));
        if (finished)
        {
            Save();
            SetDocument(_editor.Document);
        }
    }

    // ---- what an action can be and act on --------------------------------------------------------------

    public IReadOnlyList<OptionItem> ActionKinds()
    {
        var kinds = new List<ActionKind> { ActionKind.Set, ActionKind.Add, ActionKind.Toggle, ActionKind.Start, ActionKind.Stop, ActionKind.StartStop, ActionKind.Reset, ActionKind.InputCommit, ActionKind.InputErase, ActionKind.InputClear };
        if (Wiring.Outputs.Any(o => o.IsBuzzer))
        {
            kinds.Add(ActionKind.Beep);
        }

        if (Wiring.Outputs.Any(o => !o.IsBuzzer))
        {
            kinds.Add(ActionKind.Output);
        }

        kinds.Add(ActionKind.Hook);
        return [.. kinds.Select(k => new OptionItem(k.ToString(), Strings.Get("Screens_ActionKind_" + k), null))];
    }

    public IReadOnlyList<OptionItem> ActionTargets(ActionKind kind)
    {
        IReadOnlyList<ScreenVariable> variables = Document?.Variables ?? [];
        OptionItem Item(string name) => new(name, name, null);
        switch (kind)
        {
            case ActionKind.Set or ActionKind.Add:
                return [.. variables.Select(v => Item(v.Name)), Item("clock"), Item("date")];
            case ActionKind.Toggle:
                return [.. variables.Where(v => v.Kind is VariableKind.Flag or VariableKind.Choice).Select(v => Item(v.Name))];
            case ActionKind.Start or ActionKind.Stop or ActionKind.StartStop or ActionKind.Reset:
                return [.. variables.Where(v => v.Kind is VariableKind.Stopwatch or VariableKind.Countdown).Select(v => Item(v.Name))];
            case ActionKind.Beep:
                return [.. Wiring.Outputs.Where(o => o.IsBuzzer).Select(o => Item(o.Part))];
            case ActionKind.Output:
                return [.. Wiring.Outputs.Where(o => !o.IsBuzzer).Select(o => Item(o.Part))];
            default:
                return [];
        }
    }

    // ---- fields ---------------------------------------------------------------------------------------

    private string ExpressionError(string text)
    {
        if (Document is not { } document)
        {
            return "";
        }

        ExpressionCheck check = ScreenExpression.Check(document, text);
        return check.Fault switch
        {
            ExpressionFault.Syntax => Strings.Format("Screens_ExpressionBad", check.Name.Length > 0 ? check.Name : text.Trim()),
            ExpressionFault.UnknownName => Strings.Format("Screens_ExpressionUnknown", check.Name),
            _ => "",
        };
    }

    private List<string> Suggestions()
    {
        var names = new List<string>();
        foreach (ScreenVariable variable in Document?.Variables ?? [])
        {
            names.Add(variable.Name);
            if (variable.Kind is VariableKind.Stopwatch or VariableKind.Countdown)
            {
                names.Add(variable.Name + "_ms");
                names.Add(variable.Name + "_on");
            }
        }

        names.AddRange(ScreenExpression.BuiltIn.Where(n => n is not ("true" or "false")));
        return names;
    }

    // An expression: what is wrong with it is said under the field, and it is not taken until it is right.
    private PropertyField ExpressionField(string name, string label, string value, Action<string> commit, bool allowHook = false) =>
        new(name, label, PropertyKind.Text, value, null, [], (field, typed) =>
        {
            string text = typed.Trim();
            string error = allowHook && Document is { } document && ScreenExpression.IsHookName(document, text) ? "" : ExpressionError(text);
            field.Error = error;
            if (error.Length == 0)
            {
                commit(text);
            }
        })
        {
            Suggestions = Suggestions(),
        };

    private static PropertyField Choice(string name, string label, string value, IReadOnlyList<OptionItem> options, Action<string> commit, bool dropdown = true) =>
        new(name, label, PropertyKind.Choice, value, null, options, (_, v) => commit(v)) { AsDropdown = dropdown };

    private static PropertyField YesNo(string name, string label, bool value, Action<bool> commit) =>
        Choice(name, label, value ? Yes : No, [new OptionItem(No, Strings.Get("Screens_No"), null), new OptionItem(Yes, Strings.Get("Screens_Yes"), null)], v => commit(v == Yes), dropdown: false);

    private static OptionItem AlignOption(ElementAlign align) => new(align.ToString(), Strings.Get("Screens_Align_" + align), null);

    private static PropertyField AlignField(ScreenElement element, Action<ElementAlign> commit) =>
        Choice("align", Strings.Get("Screens_FieldAlign"), element.Align.ToString(), [AlignOption(ElementAlign.Left), AlignOption(ElementAlign.Center), AlignOption(ElementAlign.Right)], v => commit(Enum.Parse<ElementAlign>(v)), dropdown: false);

    private static OptionItem FormatOption(ValueFormat format) => new(format.ToString(), Strings.Get("Screens_Format_" + format), null);

    private static PropertyField FormatField(ValueFormat current, IEnumerable<ValueFormat> formats, Action<ValueFormat> commit) =>
        Choice("format", Strings.Get("Screens_FieldFormat"), current.ToString(), [.. formats.Select(FormatOption)], v => commit(Enum.Parse<ValueFormat>(v)));

    private static readonly ValueFormat[] ValueFormats = [ValueFormat.Auto, ValueFormat.TwoDigits, ValueFormat.HourMinute, ValueFormat.HourMinuteSecond, ValueFormat.MinuteSecond, ValueFormat.MinuteSecondCentis, ValueFormat.DateDayFirst, ValueFormat.DateMonthFirst, ValueFormat.DateYearFirst, ValueFormat.List];

    private static readonly ValueFormat[] BigFormats = [ValueFormat.Auto, ValueFormat.TwoDigits, ValueFormat.HourMinute, ValueFormat.HourMinuteSecond, ValueFormat.MinuteSecond];

    private static readonly ValueFormat[] InputFormats = [ValueFormat.Auto, ValueFormat.HourMinute, ValueFormat.HourMinuteSecond, ValueFormat.MinuteSecond, ValueFormat.DateDayFirst, ValueFormat.DateMonthFirst, ValueFormat.DateYearFirst];

    // Words are kept apart by a bar, which no text on a small display needs.
    private static IReadOnlyList<string> Words(string text) => [.. text.Split('|').Select(w => w.Trim()).Where(w => w.Length > 0)];

    private void GeneratedElementFields(List<PropertyField> fields, string screen, ScreenElement element)
    {
        string id = element.Id;
        bool lcd = !IsOled;
        void Edit(Func<ScreenElement, ScreenElement> change) => _editor?.UpdateElement(screen, id, change);
        PropertyField Column() => Number("x", Strings.Get(lcd ? "Screens_FieldColumn" : "Screens_FieldX"), element.X, CanMoveAcross(element), v => MoveElement(id, v, SelectedElement?.Y ?? 0));
        PropertyField Row() => Number("y", Strings.Get(lcd ? "Screens_FieldRow" : "Screens_FieldY"), element.Y, true, v => MoveElement(id, SelectedElement?.X ?? 0, v));
        PropertyField Across(string key) => Number("width", Strings.Get(key), element.Width, true, v => Edit(e => e with { Width = Math.Max(lcd ? 1 : 2, v) }));
        PropertyField Size() => Number("size", Strings.Get("Screens_FieldSize"), element.Size, true, v => Edit(e => e with { Size = Math.Clamp(v, 1, 4) }));
        PropertyField When() => ExpressionField("when", Strings.Get("Screens_FieldWhen"), element.When, v => Edit(e => e with { When = v }));
        switch (element.Kind)
        {
            case ElementKind.Text:
                fields.Add(new PropertyField("text", Strings.Get("Screens_FieldText"), PropertyKind.Text, element.Text, null, [], (_, v) => SetText(screen, id, v)));
                fields.Add(Column());
                fields.Add(Row());
                fields.Add(AlignField(element, v => Edit(e => e with { Align = v })));
                break;
            case ElementKind.Value:
                fields.Add(ExpressionField("text", Strings.Get("Screens_FieldShows"), element.Text, v => Edit(e => e with { Text = v }), allowHook: true));
                fields.Add(FormatField(element.Format, ValueFormats, v => Edit(e => e with { Format = v })));
                if (element.Format == ValueFormat.List)
                {
                    fields.Add(new PropertyField("options", Strings.Get("Screens_FieldWords"), PropertyKind.Text, string.Join(" | ", element.Options), null, [], (_, v) => Edit(e => e with { Options = Words(v) })));
                }

                fields.Add(Column());
                fields.Add(Row());
                fields.Add(Across("Screens_FieldFieldWidth"));
                fields.Add(AlignField(element, v => Edit(e => e with { Align = v })));
                break;
            case ElementKind.BigNumber:
                fields.Add(ExpressionField("text", Strings.Get("Screens_FieldShows"), element.Text, v => Edit(e => e with { Text = v }), allowHook: true));
                fields.Add(FormatField(element.Format, BigFormats, v => Edit(e => e with { Format = v })));
                fields.Add(Column());
                fields.Add(Row());
                break;
            case ElementKind.Bar:
                fields.Add(ExpressionField("text", Strings.Get("Screens_FieldShows"), element.Text, v => Edit(e => e with { Text = v })));
                fields.Add(ExpressionField("maximum", Strings.Get("Screens_FieldMaximum"), element.Maximum, v => Edit(e => e with { Maximum = v })));
                fields.Add(Column());
                fields.Add(Row());
                fields.Add(Across(lcd ? "Screens_FieldFieldWidth" : "Screens_FieldWidth"));
                if (!lcd)
                {
                    fields.Add(Number("height", Strings.Get("Screens_FieldHeight"), element.Height, true, v => Edit(e => e with { Height = Math.Max(3, v) })));
                }

                break;
            case ElementKind.Symbol:
                fields.Add(Choice("text", Strings.Get("Screens_FieldSymbol"), element.Text, [.. GlyphRows.Select(g => new OptionItem(g.Name, g.Name, null))], v => Edit(e => e with { Text = v })));
                fields.Add(Column());
                fields.Add(Row());
                break;
            case ElementKind.Input:
                fields.Add(Choice("text", Strings.Get("Screens_FieldInputFor"), element.Text, [.. ActionTargets(ActionKind.Set)], v => Edit(e => e with { Text = v })));
                fields.Add(FormatField(element.Format, InputFormats, v => Edit(e => e with { Format = v })));
                if (element.Format == ValueFormat.Auto)
                {
                    fields.Add(Number("width", Strings.Get("Screens_FieldDigits"), element.Width, true, v => Edit(e => e with { Width = Math.Clamp(v, 1, 9) })));
                }

                fields.Add(Choice("align", Strings.Get("Screens_FieldFill"), element.Align == ElementAlign.Right ? nameof(ElementAlign.Right) : nameof(ElementAlign.Left), [new OptionItem(nameof(ElementAlign.Left), Strings.Get("Screens_FillLeft"), null), new OptionItem(nameof(ElementAlign.Right), Strings.Get("Screens_FillRight"), null)], v => Edit(e => e with { Align = Enum.Parse<ElementAlign>(v) }), dropdown: false));
                fields.Add(Column());
                fields.Add(Row());
                break;
            case ElementKind.Drawn:
                fields.Add(new PropertyField("text", Strings.Get("Screens_FieldDrawnHook"), PropertyKind.Text, element.Text, null, [], (_, v) => Edit(e => e with { Text = ScreenCodeGenerator.Identifier(v.Trim()) })));
                fields.Add(Column());
                fields.Add(Row());
                fields.Add(Across(lcd ? "Screens_FieldFieldWidth" : "Screens_FieldWidth"));
                fields.Add(Number("height", Strings.Get(lcd ? "Screens_FieldRows" : "Screens_FieldHeight"), element.Height, true, v => Edit(e => e with { Height = Math.Max(1, v) })));
                break;
            default:
                fields.Add(Coordinate("x", Strings.Get(lcd ? "Screens_FieldColumn" : "Screens_FieldX"), element.X, element.XCode, true, v => MoveElement(id, v, SelectedElement?.Y ?? 0)));
                fields.Add(Coordinate("y", Strings.Get(lcd ? "Screens_FieldRow" : "Screens_FieldY"), element.Y, element.YCode, true, v => MoveElement(id, SelectedElement?.X ?? 0, v)));
                bool line = element.Kind == ElementKind.Line;
                fields.Add(Coordinate("width", Strings.Get(line ? "Screens_FieldEndX" : "Screens_FieldWidth"), element.Width, null, true, v => ResizeElement(id, v, SelectedElement?.Height ?? 0, finished: true)));
                fields.Add(Coordinate("height", Strings.Get(line ? "Screens_FieldEndY" : "Screens_FieldHeight"), element.Height, null, true, v => ResizeElement(id, SelectedElement?.Width ?? 0, v, finished: true)));
                break;
        }

        if (!lcd && element.Kind is ElementKind.Text or ElementKind.Value or ElementKind.BigNumber or ElementKind.Input or ElementKind.Symbol)
        {
            fields.Add(Size());
        }

        fields.Add(When());
    }

    private void ScreenFields(List<PropertyField> fields, ScreenNode screen)
    {
        string id = screen.Id;
        fields.Add(YesNo("keyHook", Strings.Get("Screens_FieldKeyHook"), screen.KeyHook, v => _editor?.UpdateScreen(id, s => s with { KeyHook = v })));
        fields.Add(YesNo("tickHook", Strings.Get("Screens_FieldTickHook"), screen.TickHook, v => _editor?.UpdateScreen(id, s => s with { TickHook = v })));
    }

    private void MenuExtraFields(List<PropertyField> fields, ScreenMenu menu)
    {
        fields.Add(YesNo("counter", Strings.Get("Screens_MenuCounter"), menu.Counter, v => EditMenu(m => m with { Counter = v })));
        fields.Add(new PropertyField("marker", Strings.Get("Screens_MenuMarker"), PropertyKind.Text, menu.Marker, null, [], (_, v) => EditMenu(m => m with { Marker = v.Trim() })));
    }

    private void VariableFields(List<PropertyField> fields, ScreenVariable variable)
    {
        string name = variable.Name;
        void Edit(Func<ScreenVariable, ScreenVariable> change) => _editor?.UpdateVariable(name, change);
        fields.Add(new PropertyField("name", Strings.Get("Screens_FieldName"), PropertyKind.Text, name, null, [], (field, typed) =>
        {
            string text = typed.Trim();
            if (text == name)
            {
                return;
            }

            if (_editor?.RenameVariable(name, text) == true)
            {
                SelectVariable(text);
            }
            else
            {
                field.Error = Strings.Format("Screens_BadName", text);
            }
        }));
        VariableKind[] kinds = [VariableKind.Number, VariableKind.Flag, VariableKind.Choice, VariableKind.Time, VariableKind.Stopwatch, VariableKind.Countdown];
        fields.Add(Choice("kind", Strings.Get("Screens_FieldKind"), variable.Kind.ToString(), [.. kinds.Select(k => new OptionItem(k.ToString(), Strings.Get("Screens_VariableKind_" + k), null))], v =>
        {
            VariableKind kind = Enum.Parse<VariableKind>(v);
            Edit(old => old with
            {
                Kind = kind,
                Options = old.Options.Count > 0 ? old.Options : kind == VariableKind.Flag ? [Strings.Get("Screens_FlagOff"), Strings.Get("Screens_FlagOn")] : kind == VariableKind.Choice ? [Strings.Get("Screens_ChoiceOne"), Strings.Get("Screens_ChoiceTwo")] : [],
            });
        }));
        PropertyField Initial(string key, string? unit = null) => new("initial", Strings.Get(key), PropertyKind.Number, variable.Initial.ToString(CultureInfo.CurrentCulture), unit, [], (_, v) =>
        {
            if (PropertyField.ParseNumber(v) is { } number)
            {
                Edit(old => old with { Initial = (int)Math.Round(number) });
            }
        })
        {
            Increment = 1,
        };
        PropertyField Saved() => YesNo("saved", Strings.Get("Screens_FieldSaved"), variable.Saved, v => Edit(old => old with { Saved = v }));
        PropertyField WordList() => new("options", Strings.Get(variable.Kind == VariableKind.Flag ? "Screens_FieldFlagWords" : "Screens_FieldWords"), PropertyKind.Text, string.Join(" | ", variable.Options), null, [], (_, v) => Edit(old => old with { Options = Words(v) }));
        switch (variable.Kind)
        {
            case VariableKind.Number:
                fields.Add(Initial("Screens_FieldInitial"));
                fields.Add(Number("min", Strings.Get("Screens_FieldMin"), variable.Min, true, v => Edit(old => old with { Min = v })));
                fields.Add(Number("max", Strings.Get("Screens_FieldMax"), variable.Max, true, v => Edit(old => old with { Max = v })));
                fields.Add(YesNo("wrap", Strings.Get("Screens_FieldWrap"), variable.Wrap, v => Edit(old => old with { Wrap = v })));
                fields.Add(Saved());
                break;
            case VariableKind.Flag:
                fields.Add(YesNo("initial", Strings.Get("Screens_FieldFlagInitial"), variable.Initial != 0, v => Edit(old => old with { Initial = v ? 1 : 0 })));
                fields.Add(WordList());
                fields.Add(Saved());
                break;
            case VariableKind.Choice:
                fields.Add(WordList());
                fields.Add(Initial("Screens_FieldChoiceInitial"));
                fields.Add(Saved());
                break;
            case VariableKind.Time:
                fields.Add(Initial("Screens_FieldInitial", Strings.Get("Screens_UnitSeconds")));
                fields.Add(Saved());
                break;
            case VariableKind.Countdown:
                fields.Add(Initial("Screens_FieldCountdownInitial", Strings.Get("Screens_UnitSeconds")));
                break;
            default:
                break;
        }
    }

    private void GlyphFields(List<PropertyField> fields, ScreenGlyph glyph)
    {
        string name = glyph.Name;
        bool builtIn = ScreenFont.BuiltIn.Any(g => g.Name == name);
        fields.Add(new PropertyField("name", Strings.Get("Screens_FieldName"), PropertyKind.Text, name, null, [], (field, typed) =>
        {
            string text = typed.Trim();
            if (text == name)
            {
                return;
            }

            if (_editor?.RenameGlyph(name, text) == true)
            {
                SelectGlyph(text);
            }
            else
            {
                field.Error = Strings.Format("Screens_BadName", text);
            }
        })
        {
            IsEnabled = !builtIn,
        });
    }

    /// <summary>What an element's expression may name, for the note under its fields.</summary>
    private string NamesNote => Strings.Format("Screens_NamesNote", string.Join(", ", Suggestions()));
}
