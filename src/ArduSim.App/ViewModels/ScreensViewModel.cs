using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using ArduSim.App.Localization;
using ArduSim.Core.Boards;
using ArduSim.Core.Components;
using ArduSim.Core.Projects;
using ArduSim.Core.Screens;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArduSim.App.ViewModels;

/// <summary>What the screen editor needs from the rest of the app.</summary>
public interface IScreensHost
{
    string ProjectDirectory { get; }

    string ProjectTitle { get; }

    /// <summary>The sketch's own folder.</summary>
    string SketchDirectory { get; }

    BoardProfile ScreenBoard { get; }

    SceneDocument SceneDocument { get; }

    /// <summary>Every source of the sketch with its current text: an open editor's, not the file's.</summary>
    IReadOnlyList<(string Path, string Text)> SketchSources();

    /// <summary>A file of the sketch, an open editor's text first; null if there is none.</summary>
    string? ReadSketchFile(string path);

    /// <summary>Writes a file of the sketch, through its editor if one is open.</summary>
    void WriteSketchFile(string path, string text);

    /// <summary>Replaces a token in a source, through its document, so that the editor can undo it. False if the file has changed since the scan.</summary>
    bool ApplySourceEdit(SourceEdit edit);

    /// <summary>Inserts text into the sketch's main file, through its document.</summary>
    void InsertIntoMainSketch(IReadOnlyList<(int Offset, string Text)> insertions);

    string MainSketchText();

    void GoToSource(SourceLocation location);

    /// <summary>Opens a file of the sketch in the code tabs.</summary>
    void OpenSketchFile(string path);

    /// <summary>Puts the selection's fields in the right panel; the same <paramref name="key"/> and fields update in place, so that the focus stays.</summary>
    void ShowProperties(object key, string title, string subtitle, IReadOnlyList<PropertyField> fields);
}

/// <summary>A screen in the left panel's list.</summary>
public sealed record ScreenRow(string Id, string Title, bool IsStart, int Transitions, bool DrawnElsewhere);

/// <summary>A transition in the right panel: from a screen, or from any screen when <see cref="From"/> is null.</summary>
public sealed partial class TransitionRow : ObservableObject
{
    private readonly ScreensViewModel _owner;
    private bool _loaded;

    public TransitionRow(ScreensViewModel owner, string? from, int index)
    {
        _owner = owner;
        From = from;
        Index = index;
    }

    public string? From { get; }

    public int Index { get; }

    public ScreenTransition Transition { get; private set; } = new();

    [ObservableProperty]
    public partial string TriggerText { get; private set; } = "";

    [ObservableProperty]
    public partial string TargetText { get; private set; } = "";

    /// <summary>The other condition around it in code, and its line.</summary>
    [ObservableProperty]
    public partial string Tip { get; private set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<OptionItem> Triggers { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<OptionItem> Targets { get; private set; } = [];

    [ObservableProperty]
    public partial bool CanEditTrigger { get; private set; }

    [ObservableProperty]
    public partial bool CanEditTarget { get; private set; }

    [ObservableProperty]
    public partial bool CanRemove { get; private set; }

    [ObservableProperty]
    public partial bool HasCode { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsValue))]
    public partial OptionItem? SelectedTrigger { get; set; }

    [ObservableProperty]
    public partial OptionItem? SelectedTarget { get; set; }

    /// <summary>A timeout's milliseconds or an event's name.</summary>
    [ObservableProperty]
    public partial string TriggerValue { get; set; } = "";

    public bool NeedsValue => SelectedTrigger?.Value is ScreensViewModel.TimeoutOption or ScreensViewModel.EventOption;

    /// <summary>Takes what the model says now, without treating it as an edit.</summary>
    public void Update(ScreenTransition transition, ScreenNode? from, IReadOnlyList<OptionItem> triggers, IReadOnlyList<OptionItem> targets, bool generated)
    {
        _loaded = false;
        Transition = transition;
        TriggerText = ScreensViewModel.TriggerText(transition.Trigger, from);
        TargetText = transition.Target;
        Tip = ScreensViewModel.TipOf(transition);
        Triggers = triggers;
        Targets = targets;
        CanEditTrigger = generated;
        CanEditTarget = generated || transition.TargetSpan is not null;
        CanRemove = generated;
        HasCode = transition.Location is not null;
        TriggerValue = transition.Trigger.Value;
        SelectedTrigger = triggers.FirstOrDefault(o => o.Value == ScreensViewModel.OptionOf(transition.Trigger));
        SelectedTarget = targets.FirstOrDefault(o => o.Value == transition.Target);
        _loaded = true;
    }

    partial void OnSelectedTriggerChanged(OptionItem? value) => Edited(value is not null);

    partial void OnSelectedTargetChanged(OptionItem? value) => Edited(value is not null);

    partial void OnTriggerValueChanged(string value) => Edited(value != Transition.Trigger.Value);

    private void Edited(bool changed)
    {
        if (_loaded && changed)
        {
            _owner.ChangeTransition(this);
        }
    }
}

/// <summary>A menu item in the right panel.</summary>
public sealed partial class MenuRow : ObservableObject
{
    private readonly ScreensViewModel _owner;
    private bool _loaded;

    public MenuRow(ScreensViewModel owner, int index)
    {
        _owner = owner;
        Index = index;
    }

    public int Index { get; }

    public ScreenMenuItem Item { get; private set; } = new();

    [ObservableProperty]
    public partial IReadOnlyList<OptionItem> Targets { get; private set; } = [];

    [ObservableProperty]
    public partial bool CanEditText { get; private set; }

    [ObservableProperty]
    public partial bool CanEditTarget { get; private set; }

    [ObservableProperty]
    public partial bool CanRemove { get; private set; }

    [ObservableProperty]
    public partial string TargetText { get; private set; } = "";

    [ObservableProperty]
    public partial string Text { get; set; } = "";

    /// <summary>What the item runs when it opens no screen: the name of a hook.</summary>
    [ObservableProperty]
    public partial string Action { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsAction))]
    public partial OptionItem? SelectedTarget { get; set; }

    public bool NeedsAction => CanEditTarget && SelectedTarget is { Value: "" };

    public void Update(ScreenMenuItem item, IReadOnlyList<OptionItem> targets, bool generated)
    {
        _loaded = false;
        Item = item;
        Targets = targets;
        CanEditText = generated || item.TextSpan is not null;
        CanEditTarget = generated;
        CanRemove = generated;
        TargetText = item.Target;
        Text = item.Text;
        Action = item.Action;
        SelectedTarget = targets.FirstOrDefault(t => t.Value == item.Target);
        OnPropertyChanged(nameof(NeedsAction));
        _loaded = true;
    }

    partial void OnSelectedTargetChanged(OptionItem? value) => Edited(value is not null);

    partial void OnTextChanged(string value) => Edited(value != Item.Text);

    partial void OnActionChanged(string value) => Edited(value != Item.Action);

    private void Edited(bool changed)
    {
        if (_loaded && changed)
        {
            _owner.ChangeMenuItem(this);
        }
    }
}

/// <summary>
/// The Экраны tab (ADR-019): a generated project's model, or what was recognised in a hand-written
/// sketch. Selection, the right panel's contents and every edit go through here; the two views draw it.
/// </summary>
public sealed partial class ScreensViewModel : ObservableObject
{
    public const string TimeoutOption = "timeout";
    public const string EventOption = "event";
    private const string NoTarget = "";

    private static readonly string[] KeypadKeys = ["1", "2", "3", "A", "4", "5", "6", "B", "7", "8", "9", "C", "*", "0", "#", "D"];

    private readonly IScreensHost _host;
    private readonly Stack<SourceEdit> _codeUndo = new();
    private readonly Stack<SourceEdit> _codeRedo = new();
    private ScreenEditor? _editor;
    private ScreenWiring? _wiring;
    private DateTime _savedAt;
    private bool _dragging;

    public ScreensViewModel(IScreensHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    /// <summary>Raised when what the views draw has changed.</summary>
    public event Action? Changed;

    public ScreenDocument? Document { get; private set; }

    public ObservableCollection<ScreenRow> Rows { get; } = [];

    public ObservableCollection<TransitionRow> TransitionRows { get; } = [];

    public ObservableCollection<TransitionRow> AnywhereRows { get; } = [];

    public ObservableCollection<MenuRow> MenuRows { get; } = [];

    public bool IsFromCode => Document?.Source == ScreenSource.Code;

    public bool IsGenerated => Document?.Source == ScreenSource.Generated;

    public bool HasDocument => Document is not null;

    public bool IsEmptyProject => Document is null;

    public bool IsOled => Document?.Display.Family == DisplayFamily.Oled;

    /// <summary>What the status line says about where this comes from.</summary>
    public string SourceText => Document is not { } document ? ""
        : document.Source == ScreenSource.Generated ? Strings.Get("Screens_Generated")
        : Strings.Format("Screens_FromCode", document.Screens.Count, document.Screens.Sum(s => s.Transitions.Count) + document.FromAnywhere.Count);

    public string DisplayText => Document is not { } document ? "" : document.Display.Family == DisplayFamily.CharLcd
        ? Strings.Format("Screens_DisplayLcd", document.Display.Width, document.Display.Height, document.Display.Library)
        : Strings.Format("Screens_DisplayOled", document.Display.Width, document.Display.Height, document.Display.Library);

    public bool CanUndoEdit => IsGenerated ? _editor?.CanUndo == true : _codeUndo.Count > 0;

    public bool CanRedoEdit => IsGenerated ? _editor?.CanRedo == true : _codeRedo.Count > 0;

    [ObservableProperty]
    public partial string? SelectedScreenId { get; private set; }

    [ObservableProperty]
    public partial string? SelectedElementId { get; private set; }

    /// <summary>The transition picked on the map, as (from, index); from is null for one from any screen.</summary>
    public (string? From, int Index)? SelectedTransition { get; private set; }

    /// <summary>The selected screen's face rather than the map.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMap), nameof(FaceIndex), nameof(CanAddElements))]
    public partial bool IsFace { get; set; }

    partial void OnIsFaceChanged(bool value) => RaiseCommands();

    public bool IsMap => !IsFace;

    /// <summary>For the segmented switch: 0 the map, 1 the screen.</summary>
    public int FaceIndex
    {
        get => IsFace ? 1 : 0;
        set => IsFace = value == 1;
    }

    /// <summary>A message about the last action: why an edit could not be made.</summary>
    [ObservableProperty]
    public partial string Notice { get; set; } = "";

    public ScreenRow? SelectedRow
    {
        get => Rows.FirstOrDefault(r => r.Id == SelectedScreenId);
        set
        {
            if (value is not null && value.Id != SelectedScreenId)
            {
                SelectScreen(value.Id);
            }
        }
    }

    public ScreenNode? SelectedScreen => SelectedScreenId is { } id ? Document?.Find(id) : null;

    public ScreenElement? SelectedElement => SelectedElementId is { } id ? SelectedScreen?.Elements.FirstOrDefault(e => e.Id == id) : null;

    public bool CanAddScreens => IsGenerated;

    public bool CanAddElements => IsGenerated && IsFace && SelectedScreen is not null;

    public bool CanRemoveSelected => IsGenerated && (SelectedElementId is not null || SelectedTransition is not null || SelectedScreen is not null);

    public bool CanSetStart => IsGenerated && SelectedScreen is { } screen && Document?.Start != screen.Id;

    public bool CanAddTransition => IsGenerated && SelectedScreen is not null && SelectedElementId is null;

    public bool CanAddMenuItem => IsGenerated && SelectedScreen?.Menu is not null && SelectedElementId is null;

    public bool ShowsScreenPanel => SelectedScreen is not null && SelectedElementId is null;

    public bool HasTransitions => TransitionRows.Count > 0;

    public bool HasAnywhere => AnywhereRows.Count > 0;

    public bool ShowsAnywhere => (IsGenerated || HasAnywhere) && SelectedElementId is null;

    public bool HasMenu => SelectedScreen?.Menu is not null && SelectedElementId is null;

    public bool HasSourceLink => SelectedElement?.Location is not null || (SelectedElementId is null && SelectedScreen?.Location is not null);

    /// <summary>Why a selected thing cannot be edited here: what is computed at run time, a screen that draws itself.</summary>
    public string ElementNote =>
        SelectedElement is { Kind: ElementKind.Value } && IsFromCode ? Strings.Get("Screens_ComputedNote")
        : SelectedElement is { IsConditional: true } ? Strings.Get("Screens_ConditionalNote")
        : SelectedElementId is null && SelectedScreen is { DrawnElsewhere: true } ? Strings.Get("Screens_DrawnElsewhereNote")
        : "";

    // ---- loading ---------------------------------------------------------------------------------

    /// <summary>The model if there is one, otherwise what the sketch's code says. Positions on the map and the undo history are kept.</summary>
    public void Reload()
    {
        string path = Path.Combine(_host.ProjectDirectory, ScreenDocument.FileName);
        DateTime stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default;
        if (_editor is not null && stamp == _savedAt)
        {
            FollowSchematic();
            SetDocument(_editor.Document);
            return;
        }

        ScreenDocument? saved = null;
        try
        {
            saved = ScreenDocument.Load(_host.ProjectDirectory);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            Notice = Strings.Format("Screens_FileUnreadable", e.Message);
        }

        _wiring = null;
        _savedAt = stamp;
        if (saved is { Source: ScreenSource.Generated })
        {
            UseEditor(saved);
            FollowSchematic();
            SetDocument(_editor!.Document);
            return;
        }

        _editor = null;
        SetDocument(Rescan(saved ?? Document));
    }

    private ScreenDocument? Rescan(ScreenDocument? positions)
    {
        ScreenDocument? read = ScreenCodeAnalyzer.Analyze(_host.SketchSources(), ScreenHardware.DisplayOf(_host.SceneDocument));
        if (read is null || positions is null)
        {
            return read;
        }

        return read with
        {
            Screens = [.. read.Screens.Select(s => positions.Find(s.Id) is { } known ? s with { MapX = known.MapX, MapY = known.MapY } : s)],
        };
    }

    /// <summary>The schematic knows the hardware: a resized or renamed display, or another library for its bus, carries over to the model.</summary>
    private void FollowSchematic()
    {
        _wiring = null;
        if (_editor is not { } editor)
        {
            return;
        }

        ScreenDisplay? scene = ScreenHardware.DisplayOf(_host.SceneDocument);
        ScreenDisplay current = editor.Document.Display;
        if (scene is null || scene.Family != current.Family)
        {
            Notice = Strings.Get("Screens_DisplayDiffers");
            return;
        }

        editor.SetDisplay(current.Existing
            ? current with { Part = scene.Part, Width = scene.Width, Height = scene.Height }
            : current with { Part = scene.Part, Width = scene.Width, Height = scene.Height, Library = scene.Library });
    }

    private void UseEditor(ScreenDocument document)
    {
        if (_editor is not null)
        {
            _editor.Changed -= OnEditorChanged;
        }

        _editor = new ScreenEditor(document);
        _editor.Changed += OnEditorChanged;
    }

    private void OnEditorChanged()
    {
        // A card being dragged: only the map draws anew, and the file waits for the drop.
        if (_dragging)
        {
            Document = _editor!.Document;
            Changed?.Invoke();
            return;
        }

        Save();
        SetDocument(_editor!.Document);
    }

    private void Save()
    {
        try
        {
            _editor?.Document.Save(_host.ProjectDirectory);
            _savedAt = File.GetLastWriteTimeUtc(Path.Combine(_host.ProjectDirectory, ScreenDocument.FileName));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Notice = Strings.Format("Screens_SaveFailed", e.Message);
        }
    }

    private void SetDocument(ScreenDocument? document)
    {
        Document = document;
        Rows.Clear();
        foreach (ScreenNode screen in document?.Screens ?? [])
        {
            Rows.Add(new ScreenRow(screen.Id, screen.Title.Length > 0 ? screen.Title : screen.Id, document!.Start == screen.Id, LinksOf(screen).Count, screen.DrawnElsewhere));
        }

        if (SelectedScreenId is not null && document?.Find(SelectedScreenId) is null)
        {
            SelectedScreenId = null;
        }

        SelectedScreenId ??= document?.Find(document.Start)?.Id ?? (document is { Screens.Count: > 0 } ? document.Screens[0].Id : null);
        if (SelectedElementId is not null && SelectedElement is null)
        {
            SelectedElementId = null;
        }

        if (SelectedTransition is { } selected && TransitionAt(selected) is null)
        {
            SelectedTransition = null;
        }

        RaiseAll();
    }

    private void RaiseAll()
    {
        OnPropertyChanged(string.Empty);
        RebuildPanel();
        RaiseCommands();
        Changed?.Invoke();
    }

    private void RaiseCommands()
    {
        UndoEditCommand.NotifyCanExecuteChanged();
        RedoEditCommand.NotifyCanExecuteChanged();
        AddScreenCommand.NotifyCanExecuteChanged();
        AddMenuScreenCommand.NotifyCanExecuteChanged();
        RemoveSelectedCommand.NotifyCanExecuteChanged();
        SetStartCommand.NotifyCanExecuteChanged();
        AddTransitionCommand.NotifyCanExecuteChanged();
        AddAnywhereCommand.NotifyCanExecuteChanged();
        AddMenuItemCommand.NotifyCanExecuteChanged();
        AddElementCommand.NotifyCanExecuteChanged();
        GoToCodeCommand.NotifyCanExecuteChanged();
        CreateCommand.NotifyCanExecuteChanged();
    }

    // ---- selecting -------------------------------------------------------------------------------

    public void SelectScreen(string? id)
    {
        Notice = "";
        SelectedScreenId = id;
        SelectedElementId = null;
        SelectedTransition = null;
        RaiseAll();
    }

    public void SelectElement(string? id)
    {
        SelectedElementId = id;
        SelectedTransition = null;
        RaiseAll();
    }

    public void SelectTransition(string? from, int index)
    {
        SelectedTransition = (from, index);
        SelectedElementId = null;
        if (from is not null)
        {
            SelectedScreenId = from;
        }

        RaiseAll();
    }

    private ScreenTransition? TransitionAt((string? From, int Index) at)
    {
        IReadOnlyList<ScreenTransition>? list = at.From is null ? Document?.FromAnywhere : Document?.Find(at.From)?.Transitions;
        return list is not null && at.Index >= 0 && at.Index < list.Count ? list[at.Index] : null;
    }

    // ---- what a trigger is called ------------------------------------------------------------------

    public static string TriggerText(ScreenTrigger trigger, ScreenNode? from)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        string text = trigger.Kind switch
        {
            TriggerKind.Key => trigger.Value,
            TriggerKind.Button => trigger.Part,
            TriggerKind.EncoderLeft => trigger.Part + " ←",
            TriggerKind.EncoderRight => trigger.Part + " →",
            TriggerKind.EncoderPress => Strings.Format("Screens_TriggerPress", trigger.Part),
            TriggerKind.Timeout => Strings.Format("Screens_TriggerTimeout", trigger.Value),
            TriggerKind.Event => trigger.Value,
            _ => Strings.Get("Screens_TriggerCode"),
        };
        if (trigger.MenuItem >= 0)
        {
            string item = from?.Menu is { } menu && trigger.MenuItem < menu.Items.Count ? menu.Items[trigger.MenuItem].Text : trigger.MenuItem.ToString(CultureInfo.CurrentCulture);
            text = Strings.Format("Screens_TriggerItem", text, item);
        }

        return text;
    }

    /// <summary>
    /// What leads away from a screen, as the map draws it: its transitions, and menu items that open a
    /// screen. In a generated project those live on the item; read from code they are transitions already.
    /// </summary>
    /// <returns>Each with the transition's index, or -1 for a menu item.</returns>
    public static IReadOnlyList<(string Target, string Label, int Index)> LinksOf(ScreenNode screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        var links = new List<(string, string, int)>();
        for (int i = 0; i < screen.Transitions.Count; i++)
        {
            links.Add((screen.Transitions[i].Target, TriggerText(screen.Transitions[i].Trigger, screen), i));
        }

        IReadOnlyList<ScreenMenuItem> items = screen.Menu?.Items ?? [];
        for (int i = 0; i < items.Count; i++)
        {
            int item = i;
            if (items[i].Target.Length > 0 && !screen.Transitions.Any(t => t.Trigger.MenuItem == item))
            {
                links.Add((items[i].Target, items[i].Text, -1));
            }
        }

        return links;
    }

    public static string TipOf(ScreenTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        var parts = new List<string>();
        if (transition.Trigger.Name.Length > 0)
        {
            parts.Add(transition.Trigger.Name);
        }

        string when = transition.Trigger.Kind == TriggerKind.Code ? transition.Trigger.Value : transition.Trigger.When;
        if (when.Length > 0)
        {
            parts.Add(Strings.Format("Screens_When", when));
        }

        if (transition.Location is { } location)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileName(location.File)}:{location.Line}"));
        }

        return string.Join(" · ", parts);
    }

    public static string OptionOf(ScreenTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        return trigger.Kind switch
        {
            TriggerKind.Key => "key:" + trigger.Part + ":" + trigger.Value,
            TriggerKind.Button => "button:" + trigger.Part,
            TriggerKind.EncoderLeft => "left:" + trigger.Part,
            TriggerKind.EncoderRight => "right:" + trigger.Part,
            TriggerKind.EncoderPress => "press:" + trigger.Part,
            TriggerKind.Timeout => TimeoutOption,
            TriggerKind.Event => EventOption,
            _ => "code",
        };
    }

    private static ScreenTrigger TriggerOf(string option, string value)
    {
        string[] parts = option.Split(':', 3);
        return parts[0] switch
        {
            "key" => new ScreenTrigger(TriggerKind.Key, parts[1], parts[2]),
            "button" => new ScreenTrigger(TriggerKind.Button, parts[1]),
            "left" => new ScreenTrigger(TriggerKind.EncoderLeft, parts[1]),
            "right" => new ScreenTrigger(TriggerKind.EncoderRight, parts[1]),
            "press" => new ScreenTrigger(TriggerKind.EncoderPress, parts[1]),
            TimeoutOption => new ScreenTrigger(TriggerKind.Timeout, Value: int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out int ms) && ms > 0 ? ms.ToString(CultureInfo.InvariantCulture) : "1000"),
            _ => new ScreenTrigger(TriggerKind.Event, Value: value.Length > 0 && !int.TryParse(value, out _) ? ScreenCodeGenerator.Identifier(value) : "myEvent"),
        };
    }

    private ScreenWiring Wiring => _wiring ??= ScreenHardware.WiringOf(_host.SceneDocument, _host.ScreenBoard, Document?.Display.Part ?? "");

    /// <summary>What the schematic's inputs can do, as dropdown options.</summary>
    private List<OptionItem> InputOptions()
    {
        var options = new List<OptionItem>();
        foreach (ScreenInput input in Wiring.Inputs)
        {
            switch (input.Kind)
            {
                case ScreenInputKind.Button:
                    options.Add(new OptionItem("button:" + input.Part, Strings.Format("Screens_OptionButton", input.Part), null));
                    break;
                case ScreenInputKind.Encoder:
                    options.Add(new OptionItem("left:" + input.Part, input.Part + " ←", null));
                    options.Add(new OptionItem("right:" + input.Part, input.Part + " →", null));
                    options.Add(new OptionItem("press:" + input.Part, Strings.Format("Screens_TriggerPress", input.Part), null));
                    break;
                default:
                    options.AddRange(KeypadKeys.Select(k => new OptionItem("key:" + input.Part + ":" + k, Strings.Format("Screens_OptionKey", input.Part, k), null)));
                    break;
            }
        }

        return options;
    }

    /// <summary>What can make a transition happen in this project: the schematic's inputs, time and the sketch's own events.</summary>
    public IReadOnlyList<OptionItem> TriggerOptions()
    {
        List<OptionItem> options = InputOptions();
        options.Add(new OptionItem(TimeoutOption, Strings.Get("Screens_OptionTimeout"), null));
        options.Add(new OptionItem(EventOption, Strings.Get("Screens_OptionEvent"), null));
        return options;
    }

    private List<OptionItem> TargetOptions(bool allowNone)
    {
        var options = new List<OptionItem>();
        if (allowNone)
        {
            options.Add(new OptionItem(NoTarget, Strings.Get("Screens_NoTarget"), null));
        }

        options.AddRange((Document?.Screens ?? []).Select(s => new OptionItem(s.Id, s.Title.Length > 0 ? s.Title : s.Id, null)));
        return options;
    }

    // ---- the right panel ---------------------------------------------------------------------------

    private void RebuildPanel()
    {
        ScreenNode? screen = SelectedElementId is null ? SelectedScreen : null;
        IReadOnlyList<OptionItem> triggers = IsGenerated ? TriggerOptions() : [];
        List<OptionItem> targets = TargetOptions(allowNone: false);
        Fill(TransitionRows, screen?.Transitions ?? [], screen?.Id, screen, triggers, targets);
        Fill(AnywhereRows, SelectedElementId is null ? Document?.FromAnywhere ?? [] : [], null, null, triggers, targets);

        IReadOnlyList<ScreenMenuItem> items = screen?.Menu?.Items ?? [];
        List<OptionItem> menuTargets = TargetOptions(allowNone: true);
        while (MenuRows.Count > items.Count)
        {
            MenuRows.RemoveAt(MenuRows.Count - 1);
        }

        for (int i = 0; i < items.Count; i++)
        {
            if (i == MenuRows.Count)
            {
                MenuRows.Add(new MenuRow(this, i));
            }

            MenuRows[i].Update(items[i], menuTargets, IsGenerated);
        }

        OnPropertyChanged(nameof(HasTransitions));
        OnPropertyChanged(nameof(HasAnywhere));
        OnPropertyChanged(nameof(ShowsAnywhere));
        OnPropertyChanged(nameof(HasMenu));
        _host.ShowProperties(
            (SelectedScreenId ?? "", SelectedElementId ?? "", IsGenerated),
            PanelTitle(SelectedScreen),
            PanelSubtitle(SelectedScreen),
            PanelFields(SelectedScreen));
    }

    private void Fill(ObservableCollection<TransitionRow> rows, IReadOnlyList<ScreenTransition> list, string? from, ScreenNode? screen, IReadOnlyList<OptionItem> triggers, IReadOnlyList<OptionItem> targets)
    {
        // The same rows when the screen is the same: an edit made from a dropdown must not remove the dropdown under it.
        if (rows.Count > 0 && rows[0].From != from)
        {
            rows.Clear();
        }

        while (rows.Count > list.Count)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        for (int i = 0; i < list.Count; i++)
        {
            if (i == rows.Count)
            {
                rows.Add(new TransitionRow(this, from, i));
            }

            rows[i].Update(list[i], screen, triggers, targets, IsGenerated);
        }
    }

    private string PanelTitle(ScreenNode? screen)
    {
        if (SelectedElement is { } element)
        {
            return Strings.Get("Screens_Element_" + element.Kind);
        }

        return screen is null ? Strings.Get("Screens_Title") : screen.Title.Length > 0 ? screen.Title : screen.Id;
    }

    private string PanelSubtitle(ScreenNode? screen)
    {
        if (SelectedElement is not null)
        {
            return screen?.Id ?? "";
        }

        return screen is null ? DisplayText : Document?.Start == screen.Id ? Strings.Get("Screens_IsStart") : "";
    }

    private List<PropertyField> PanelFields(ScreenNode? screen)
    {
        var fields = new List<PropertyField>();
        if (screen is null)
        {
            return fields;
        }

        if (SelectedElement is { } element)
        {
            ElementFields(fields, screen.Id, element);
        }
        else if (IsGenerated)
        {
            string id = screen.Id;
            fields.Add(new PropertyField("title", Strings.Get("Screens_FieldTitle"), PropertyKind.Text, screen.Title, null, [], (_, v) => _editor?.SetTitle(id, v.Trim())));
            if (screen.Menu is { } menu)
            {
                fields.Add(MenuControl("up", "Screens_MenuUp", menu.Up, (m, t) => m with { Up = t }));
                fields.Add(MenuControl("down", "Screens_MenuDown", menu.Down, (m, t) => m with { Down = t }));
                fields.Add(MenuControl("select", "Screens_MenuSelect", menu.Select, (m, t) => m with { Select = t }));
                fields.Add(Number("rows", Strings.Get("Screens_MenuRows"), menu.Rows, true, v => EditMenu(m => m with { Rows = Math.Clamp(v, 1, 16) })));
                fields.Add(Number("top", Strings.Get(IsOled ? "Screens_MenuTopOled" : "Screens_MenuTop"), menu.Top, true, v => EditMenu(m => m with { Top = Math.Max(v, 0) })));
            }
        }

        return fields;
    }

    private PropertyField MenuControl(string name, string label, ScreenTrigger? current, Func<ScreenMenu, ScreenTrigger?, ScreenMenu> change)
    {
        List<OptionItem> options = [new OptionItem(NoTarget, Strings.Get("Screens_NoControl"), null), .. InputOptions()];
        return new PropertyField(name, Strings.Get(label), PropertyKind.Choice, current is null ? NoTarget : OptionOf(current), null, options, (_, v) =>
            EditMenu(m => change(m, v.Length == 0 ? null : TriggerOf(v, ""))))
        {
            AsDropdown = true,
        };
    }

    private void EditMenu(Func<ScreenMenu, ScreenMenu> change)
    {
        if (SelectedScreen is { Menu: { } menu } screen)
        {
            _editor?.SetMenu(screen.Id, change(menu));
        }
    }

    private void ElementFields(List<PropertyField> fields, string screen, ScreenElement element)
    {
        string id = element.Id;
        bool lcd = !IsOled;
        void Edit(Func<ScreenElement, ScreenElement> change) => _editor?.UpdateElement(screen, id, change);
        if (element.Kind == ElementKind.Text)
        {
            fields.Add(new PropertyField("text", Strings.Get("Screens_FieldText"), PropertyKind.Text, element.Text, null, [], (_, v) => SetText(screen, id, v))
            {
                IsEnabled = IsGenerated || element.TextSpan is not null,
            });
        }
        else if (element.Kind == ElementKind.Value)
        {
            fields.Add(new PropertyField("text", Strings.Get(IsGenerated ? "Screens_FieldHook" : "Screens_FieldExpression"), PropertyKind.Text, element.Text, null, [], (_, v) => Edit(e => e with { Text = ScreenCodeGenerator.Identifier(v.Trim()) }))
            {
                IsEnabled = IsGenerated,
            });
        }

        fields.Add(Number("x", Strings.Get(lcd ? "Screens_FieldColumn" : "Screens_FieldX"), element.X, IsGenerated || element.XSpan is not null, v => MoveElement(id, v, SelectedElement?.Y ?? 0)));
        fields.Add(Number("y", Strings.Get(lcd ? "Screens_FieldRow" : "Screens_FieldY"), element.Y, IsGenerated || element.YSpan is not null, v => MoveElement(id, SelectedElement?.X ?? 0, v)));
        if (IsGenerated && element.Kind == ElementKind.Value)
        {
            fields.Add(Number("width", Strings.Get("Screens_FieldFieldWidth"), element.Width, true, v => Edit(e => e with { Width = Math.Max(1, v) })));
        }

        if (IsGenerated && element.Kind is ElementKind.Frame or ElementKind.Box or ElementKind.Line)
        {
            bool line = element.Kind == ElementKind.Line;
            fields.Add(Number("width", Strings.Get(line ? "Screens_FieldEndX" : "Screens_FieldWidth"), element.Width, true, v => Edit(e => e with { Width = v })));
            fields.Add(Number("height", Strings.Get(line ? "Screens_FieldEndY" : "Screens_FieldHeight"), element.Height, true, v => Edit(e => e with { Height = v })));
        }

        if (IsGenerated && IsOled && element.Kind is ElementKind.Text or ElementKind.Value)
        {
            fields.Add(Number("size", Strings.Get("Screens_FieldSize"), element.Size, true, v => Edit(e => e with { Size = Math.Clamp(v, 1, 4) })));
        }
    }

    private static PropertyField Number(string name, string label, int value, bool enabled, Action<int> commit) =>
        new(name, label, PropertyKind.Number, value.ToString(CultureInfo.CurrentCulture), null, [], (_, v) =>
        {
            if (PropertyField.ParseNumber(v) is { } number)
            {
                commit((int)Math.Round(number));
            }
        })
        {
            IsEnabled = enabled,
            Increment = 1,
        };

    // ---- edits: in the model, or in the code that was read ----------------------------------------------

    private void ApplyToCode(SourceEdit? edit)
    {
        if (edit is null)
        {
            Notice = Strings.Get("Screens_NotInCode");
            return;
        }

        if (!_host.ApplySourceEdit(edit))
        {
            Notice = Strings.Get("Screens_CodeChanged");
            Refresh();
            return;
        }

        Notice = "";
        _codeUndo.Push(edit.Inverse());
        _codeRedo.Clear();
        Refresh();
    }

    /// <summary>Reads the code again, keeping the selection where it can.</summary>
    [RelayCommand]
    public void Refresh()
    {
        if (!IsGenerated)
        {
            SetDocument(Rescan(Document));
        }
    }

    private void SetText(string screen, string id, string text)
    {
        // The display's font has no Cyrillic, on real hardware as here: it draws as other characters.
        Notice = text.Any(c => c > 127) ? Strings.Get("Screens_NonAscii") : "";
        if (IsGenerated)
        {
            _editor?.UpdateElement(screen, id, e => e with { Text = text });
        }
        else if (Document?.Find(screen)?.Elements.FirstOrDefault(e => e.Id == id) is { } element)
        {
            ApplyToCode(ScreenCodeEdits.SetText(element.TextSpan, text));
        }
    }

    /// <summary>A drag on the screen ended, or a number was typed: clamped to the display.</summary>
    public void MoveElement(string id, int x, int y)
    {
        if (SelectedScreen is not { } screen || Document is not { } document || screen.Elements.FirstOrDefault(e => e.Id == id) is not { } element)
        {
            return;
        }

        x = Math.Clamp(x, 0, Math.Max(0, document.Display.Width - 1));
        y = Math.Clamp(y, 0, Math.Max(0, document.Display.Height - 1));
        if (IsGenerated)
        {
            _editor?.UpdateElement(screen.Id, id, e => e with { X = x, Y = y });
            return;
        }

        // Moving in code is one or two tokens: whichever axis changed. Each edit shifts what follows, so the second is read afresh.
        string screenId = screen.Id;
        if (x != element.X)
        {
            ApplyToCode(ScreenCodeEdits.SetNumber(element.XSpan, x));
        }

        if (y != element.Y)
        {
            ApplyToCode(ScreenCodeEdits.SetNumber(Document?.Find(screenId)?.Elements.FirstOrDefault(e => e.Id == id)?.YSpan, y));
        }
    }

    /// <summary>Whether an element can be dragged at all.</summary>
    public bool CanMove(ScreenElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return IsGenerated || element.XSpan is not null || element.YSpan is not null;
    }

    public void ChangeTransition(TransitionRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        string target = row.SelectedTarget?.Value ?? row.Transition.Target;
        if (IsGenerated)
        {
            ScreenTrigger trigger = row.SelectedTrigger is { } option ? TriggerOf(option.Value, row.TriggerValue) : row.Transition.Trigger;
            _editor?.UpdateTransition(row.From, row.Index, row.Transition with { Trigger = trigger, Target = target });
        }
        else if (target != row.Transition.Target)
        {
            ApplyToCode(ScreenCodeEdits.SetTarget(row.Transition, target));
        }
    }

    public void ChangeMenuItem(MenuRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (SelectedScreen is not { Menu: { } menu } screen || row.Index >= menu.Items.Count)
        {
            return;
        }

        if (IsGenerated)
        {
            string target = row.SelectedTarget?.Value ?? "";
            ScreenMenuItem[] items = [.. menu.Items];
            items[row.Index] = items[row.Index] with { Text = row.Text, Target = target, Action = target.Length == 0 && row.Action.Trim().Length > 0 ? ScreenCodeGenerator.Identifier(row.Action.Trim()) : "" };
            _editor?.SetMenu(screen.Id, menu with { Items = items });
        }
        else if (row.Text != menu.Items[row.Index].Text)
        {
            ApplyToCode(ScreenCodeEdits.SetText(menu.Items[row.Index].TextSpan, row.Text));
        }
    }

    private bool CanUndo() => CanUndoEdit;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void UndoEdit()
    {
        if (IsGenerated)
        {
            _editor?.Undo();
        }
        else if (_codeUndo.TryPop(out SourceEdit? edit))
        {
            if (_host.ApplySourceEdit(edit))
            {
                _codeRedo.Push(edit.Inverse());
            }
            else
            {
                Notice = Strings.Get("Screens_CodeChanged");
            }

            Refresh();
        }
    }

    private bool CanRedo() => CanRedoEdit;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void RedoEdit()
    {
        if (IsGenerated)
        {
            _editor?.Redo();
        }
        else if (_codeRedo.TryPop(out SourceEdit? edit))
        {
            if (_host.ApplySourceEdit(edit))
            {
                _codeUndo.Push(edit.Inverse());
            }
            else
            {
                Notice = Strings.Get("Screens_CodeChanged");
            }

            Refresh();
        }
    }

    // ---- the map ------------------------------------------------------------------------------------

    /// <summary>A node dragged on the map; a project from code keeps only these positions.</summary>
    public void MoveOnMap(string id, double x, double y, bool finished)
    {
        if (_editor is not null)
        {
            bool first = !_dragging;
            _dragging = !finished;
            _editor.MoveOnMap(id, x, y, checkpoint: first);
        }
        else if (Document is { } document)
        {
            Document = document with { Screens = [.. document.Screens.Select(s => s.Id == id ? s with { MapX = x, MapY = y } : s)] };
            if (finished)
            {
                SavePositions();
            }

            Changed?.Invoke();
        }
    }

    private void SavePositions()
    {
        if (Document is not { Source: ScreenSource.Code } document)
        {
            return;
        }

        try
        {
            // Code is the source of truth: only where the nodes lie is kept.
            (document with
            {
                FromAnywhere = [],
                Screens = [.. document.Screens.Select(s => new ScreenNode { Id = s.Id, MapX = s.MapX, MapY = s.MapY })],
            }).Save(_host.ProjectDirectory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Notice = Strings.Format("Screens_SaveFailed", e.Message);
        }
    }

    /// <summary>A drag from one node to another, in a generated project.</summary>
    public void Connect(string from, string to)
    {
        ArgumentNullException.ThrowIfNull(from);
        if (_editor is null)
        {
            Notice = Strings.Get("Screens_NoNewTransitionInCode");
            return;
        }

        _editor.AddTransition(from, DefaultTrigger(), to);
        SelectScreen(from);
    }

    private ScreenTrigger DefaultTrigger()
    {
        string first = TriggerOptions()[0].Value;
        return TriggerOf(first, first == TimeoutOption ? "1000" : "");
    }

    // ---- commands -------------------------------------------------------------------------------------

    private bool CanCreate() => Document is null;

    /// <summary>Starts a generated project: the model, its display from the schematic, and the sketch connected to it.</summary>
    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create()
    {
        ScreenDisplay? display = ScreenHardware.DisplayOf(_host.SceneDocument);
        if (display is null)
        {
            Notice = Strings.Get("Screens_NoDisplay");
            return;
        }

        // A display the sketch already has and begins stays its own: generated code only uses it.
        if (ScreenCodeAnalyzer.DisplayIn(_host.SketchSources(), display) is { } own)
        {
            // U8g2 in page mode draws through a loop the generated code does not have.
            if (own.Library == DisplayLibrary.U8g2 && !own.TypeName.Contains("_F_", StringComparison.Ordinal))
            {
                Notice = Strings.Format("Screens_U8g2Page", own.TypeName);
                return;
            }

            display = own;
        }

        UseEditor(ScreenEditor.Starting(display, _host.ProjectTitle.Length > 0 ? AsciiOr(_host.ProjectTitle, "Hello") : "Hello", Strings.Get("Screens_HomeTitle")));
        Save();
        _wiring = null;
        _host.InsertIntoMainSketch(ScreenCodeGenerator.WiringEdits(_host.MainSketchText(), display.Existing));
        SetDocument(_editor!.Document);
        Generate();
        Notice = Strings.Get("Screens_Created");
    }

    private static string AsciiOr(string text, string fallback) => text.All(c => c is >= ' ' and <= '~') ? text : fallback;

    /// <summary>Writes the generated files, if this is a generated project: before each build, and when asked to show them.</summary>
    public void Generate()
    {
        if (_editor is null)
        {
            return;
        }

        try
        {
            FollowSchematic();
            string directory = _host.SketchDirectory;
            string hooksPath = Path.Combine(directory, ScreenCodeGenerator.HooksFile);
            GeneratedScreens generated = ScreenCodeGenerator.Generate(_editor.Document, Wiring, _host.ReadSketchFile(hooksPath));
            _host.WriteSketchFile(Path.Combine(directory, ScreenCodeGenerator.HeaderFile), generated.Header);
            _host.WriteSketchFile(Path.Combine(directory, ScreenCodeGenerator.SourceFile), generated.Source);
            _host.WriteSketchFile(hooksPath, generated.Hooks);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Notice = Strings.Format("Screens_SaveFailed", e.Message);
        }
    }

    [RelayCommand]
    private void ShowGeneratedCode()
    {
        Generate();
        _host.OpenSketchFile(Path.Combine(_host.SketchDirectory, ScreenCodeGenerator.HooksFile));
    }

    private bool CanAddScreen() => CanAddScreens;

    [RelayCommand(CanExecute = nameof(CanAddScreen))]
    private void AddScreen() => Added(_editor?.AddScreen(NextTitle("Screens_NewScreen"), NextX(), NextY()));

    [RelayCommand(CanExecute = nameof(CanAddScreen))]
    private void AddMenuScreen()
    {
        ScreenNode? screen = _editor?.AddScreen(NextTitle("Screens_NewMenu"), NextX(), NextY(), asMenu: true);
        if (screen?.Menu is { } menu)
        {
            (ScreenTrigger? up, ScreenTrigger? down, ScreenTrigger? select) = DefaultMenuControls();
            _editor!.SetMenu(screen.Id, menu with { Up = up, Down = down, Select = select });
        }

        Added(screen);
    }

    /// <summary>A menu works as soon as it is made: an encoder turns and presses, a keypad's 2, 8 and #, the buttons in their order.</summary>
    private (ScreenTrigger? Up, ScreenTrigger? Down, ScreenTrigger? Select) DefaultMenuControls()
    {
        IReadOnlyList<ScreenInput> inputs = Wiring.Inputs;
        if (inputs.FirstOrDefault(i => i.Kind == ScreenInputKind.Encoder) is { } encoder)
        {
            return (new ScreenTrigger(TriggerKind.EncoderLeft, encoder.Part), new ScreenTrigger(TriggerKind.EncoderRight, encoder.Part), new ScreenTrigger(TriggerKind.EncoderPress, encoder.Part));
        }

        if (inputs.FirstOrDefault(i => i.Kind == ScreenInputKind.Keypad) is { } keypad)
        {
            return (new ScreenTrigger(TriggerKind.Key, keypad.Part, "2"), new ScreenTrigger(TriggerKind.Key, keypad.Part, "8"), new ScreenTrigger(TriggerKind.Key, keypad.Part, "#"));
        }

        ScreenInput[] buttons = [.. inputs.Where(i => i.Kind == ScreenInputKind.Button)];
        ScreenTrigger Button(int index) => new(TriggerKind.Button, buttons[index].Part);
        return buttons.Length switch
        {
            0 => (null, null, null),
            1 => (null, Button(0), null),
            2 => (null, Button(0), Button(1)),
            _ => (Button(0), Button(1), Button(2)),
        };
    }

    private void Added(ScreenNode? screen)
    {
        if (screen is not null)
        {
            SelectScreen(screen.Id);
        }
    }

    private string NextTitle(string key) => Strings.Format(key, (Document?.Screens.Count ?? 0) + 1);

    private double NextX() => (Document?.Screens.Count ?? 0) % 4 * ScreenMapLayout.ColumnStep;

    private double NextY() => ((Document?.Screens.Count ?? 0) / 4) * ScreenMapLayout.RowStep;

    private bool CanAddElementNow(string? kind) => CanAddElements && (IsOled || kind is nameof(ElementKind.Text) or nameof(ElementKind.Value));

    /// <summary>Adds an element to the screen on view: at the first free row of a character display, or below the last one on an OLED.</summary>
    [RelayCommand(CanExecute = nameof(CanAddElementNow))]
    private void AddElement(string? kind)
    {
        if (SelectedScreen is not { } screen || _editor is null || !Enum.TryParse(kind, out ElementKind parsed))
        {
            return;
        }

        string text = parsed == ElementKind.Text ? Strings.Get("Screens_NewText") : parsed == ElementKind.Value ? "value" : "";
        ScreenElement element = _editor.AddElement(screen.Id, parsed, 0, NextFree(screen), text);
        SelectElement(element.Id);
    }

    private int NextFree(ScreenNode screen)
    {
        ScreenDisplay display = Document!.Display;
        if (display.Family == DisplayFamily.CharLcd)
        {
            for (int row = 0; row < display.Height; row++)
            {
                if (screen.Elements.All(e => e.Y != row))
                {
                    return row;
                }
            }

            return 0;
        }

        return Math.Min(screen.Elements.Select(e => e.Y + 10).DefaultIfEmpty(0).Max(), Math.Max(0, display.Height - 8));
    }

    private bool CanRemoveNow() => CanRemoveSelected;

    [RelayCommand(CanExecute = nameof(CanRemoveNow))]
    public void RemoveSelected()
    {
        if (_editor is null)
        {
            return;
        }

        if (SelectedElementId is { } element && SelectedScreenId is { } owner)
        {
            SelectedElementId = null;
            _editor.RemoveElement(owner, element);
        }
        else if (SelectedTransition is { } transition)
        {
            SelectedTransition = null;
            _editor.RemoveTransition(transition.From, transition.Index);
        }
        else if (SelectedScreenId is { } screen)
        {
            _editor.RemoveScreen(screen);
        }
    }

    private bool CanSetStartNow() => CanSetStart;

    [RelayCommand(CanExecute = nameof(CanSetStartNow))]
    private void SetStart()
    {
        if (SelectedScreenId is { } id)
        {
            _editor?.SetStart(id);
        }
    }

    private bool CanAddTransitionNow() => CanAddTransition;

    [RelayCommand(CanExecute = nameof(CanAddTransitionNow))]
    private void AddTransition()
    {
        if (SelectedScreenId is { } from && Document is { } document)
        {
            _editor?.AddTransition(from, DefaultTrigger(), document.Screens.FirstOrDefault(s => s.Id != from)?.Id ?? from);
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddScreen))]
    private void AddAnywhere()
    {
        if (Document is { } document)
        {
            _editor?.AddTransition(null, DefaultTrigger(), document.Start);
        }
    }

    [RelayCommand]
    private void RemoveTransition(TransitionRow? row)
    {
        if (row is not null)
        {
            _editor?.RemoveTransition(row.From, row.Index);
        }
    }

    private bool CanAddMenuItemNow() => CanAddMenuItem;

    [RelayCommand(CanExecute = nameof(CanAddMenuItemNow))]
    private void AddMenuItem() =>
        EditMenu(menu => menu with { Items = [.. menu.Items, new ScreenMenuItem { Text = Strings.Format("Screens_NewItem", menu.Items.Count + 1) }] });

    [RelayCommand]
    private void RemoveMenuItem(MenuRow? row)
    {
        if (row is not null)
        {
            EditMenu(menu => menu with { Items = [.. menu.Items.Where((_, i) => i != row.Index)] });
        }
    }

    private bool CanGoToCode() => HasSourceLink;

    [RelayCommand(CanExecute = nameof(CanGoToCode))]
    private void GoToCode()
    {
        if ((SelectedElement?.Location ?? SelectedScreen?.Location) is { } location)
        {
            _host.GoToSource(location);
        }
    }

    [RelayCommand]
    private void GoToTransition(TransitionRow? row)
    {
        if (row?.Transition.Location is { } location)
        {
            _host.GoToSource(location);
        }
    }
}
