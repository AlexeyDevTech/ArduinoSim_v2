using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArduSim.Core.Screens;

/// <summary>Where a project's screens come from (ADR-019).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ScreenSource>))]
public enum ScreenSource
{
    /// <summary>The model in <c>screens.json</c>; <c>ui_screens.*</c> are generated from it.</summary>
    Generated,

    /// <summary>Recognised in the sketch's own code, which stays the source of truth.</summary>
    Code,
}

[JsonConverter(typeof(JsonStringEnumConverter<DisplayFamily>))]
public enum DisplayFamily
{
    /// <summary>HD44780: positions and sizes in character cells.</summary>
    CharLcd,

    /// <summary>Positions and sizes in pixels.</summary>
    Oled,
}

[JsonConverter(typeof(JsonStringEnumConverter<DisplayLibrary>))]
public enum DisplayLibrary
{
    LiquidCrystal,
    LiquidCrystalI2c,
    AdafruitSsd1306,
    AdafruitSh110x,
    U8g2,
    GyverOled,
}

/// <param name="Width">Columns of a character LCD, or pixels of an OLED.</param>
/// <param name="Height">Rows of a character LCD, or pixels of an OLED.</param>
/// <param name="Variable">The name of the display object in code.</param>
/// <param name="Part">The display part on the schematic; empty when there is none.</param>
/// <param name="Existing">The sketch already has this object and begins it: generated code only uses it.</param>
/// <param name="TypeName">An existing object's type as the sketch writes it, which <c>extern</c> has to repeat.</param>
/// <param name="TextFromBaseline">Text is placed by its baseline, not its top: U8g2 in code that was read. Generated code asks for the top.</param>
public sealed record ScreenDisplay(DisplayFamily Family, int Width, int Height, DisplayLibrary Library, string Variable, string Part = "", bool Existing = false, string TypeName = "", bool TextFromBaseline = false);

[JsonConverter(typeof(JsonStringEnumConverter<ElementKind>))]
public enum ElementKind
{
    Text,

    /// <summary>Known only at run time: a hook in generated code, an expression in code that was read.</summary>
    Value,

    Line,

    /// <summary>A rectangle's outline.</summary>
    Frame,

    /// <summary>A filled rectangle.</summary>
    Box,
}

/// <summary>Exactly one token of source that a scan read: what an in-place edit may replace.</summary>
/// <param name="Offset">Characters from the start of the file.</param>
/// <param name="Expected">The token's text as scanned; an edit is refused if the file no longer has it there.</param>
public sealed record SourceSpan(string File, int Offset, int Length, string Expected);

/// <summary>Where something is in the source, for going to it.</summary>
public sealed record SourceLocation(string File, int Line);

/// <summary>A place in the source where new code can go.</summary>
/// <param name="Offset">Characters from the start of the file.</param>
/// <param name="Indent">What a new line starts with.</param>
/// <param name="Inline">The place is inside a line: what goes there stays on it.</param>
public sealed record SourceInsert(string File, int Offset, string Indent, bool Inline);

/// <summary>
/// How a sketch keeps its screens, as far as a scan could tell (ADR-019): what the constructor needs to
/// add a screen or a transition in the sketch's own style. What is missing is what cannot be added.
/// </summary>
public sealed record ScreenCodeShape
{
    /// <summary>Where a new constant goes in the screens' enum.</summary>
    public SourceInsert? NewConstant { get; init; }

    /// <summary>How the constant is written there: <c>, {0}</c>, <c>{0}, </c>.</summary>
    public string ConstantFormat { get; init; } = ", {0}";

    /// <summary>Where a new <c>case</c> goes in the switch that draws.</summary>
    public SourceInsert? NewDrawCase { get; init; }

    /// <summary>The object drawn on, with its accessor: <c>lcd.</c></summary>
    public string Receiver { get; init; } = "";

    /// <summary>Where a new transition goes: the start of what handles a key.</summary>
    public SourceInsert? NewTransition { get; init; }

    /// <summary>The variable that holds the key there.</summary>
    public string KeyVariable { get; init; } = "";

    /// <summary>The screen variable as the code writes it.</summary>
    public string StateExpression { get; init; } = "";

    /// <summary>How the sketch goes to a screen: <c>enterScreen({0});</c></summary>
    public string GotoFormat { get; init; } = "";

    /// <summary>The handler returns nothing, so a new transition can end it.</summary>
    public bool CanReturn { get; init; }

    /// <summary>The constant that says which screen comes first.</summary>
    public SourceSpan? StartSpan { get; init; }

    /// <summary>Key characters the sketch has a name for.</summary>
    public IReadOnlyDictionary<string, string> KeyNames { get; init; } = new Dictionary<string, string>();
}

/// <summary>One thing on a screen.</summary>
public sealed record ScreenElement
{
    public string Id { get; init; } = "";

    public ElementKind Kind { get; init; }

    /// <summary>Column or pixel. For a line, its start.</summary>
    public int X { get; init; }

    /// <summary>Row or pixel. For a line, its start.</summary>
    public int Y { get; init; }

    /// <summary>What a text says. For a value: the hook's name, or the expression in code that was read.</summary>
    public string Text { get; init; } = "";

    /// <summary>A value's field width in characters, a shape's width; a line's end X.</summary>
    public int Width { get; init; }

    /// <summary>A shape's height; a line's end Y.</summary>
    public int Height { get; init; }

    /// <summary>An OLED text's scale.</summary>
    public int Size { get; init; } = 1;

    /// <summary>A value's text for the preview, when the code names it (a ternary of two literals).</summary>
    public string? Sample { get; init; }

    /// <summary>Drawn only under some condition in code.</summary>
    public bool IsConditional { get; init; }

    /// <summary>One of several things drawn in the same place (an <c>else</c> branch, a leading zero): listed, not previewed.</summary>
    public bool IsAlternative { get; init; }

    /// <summary>The position could not be worked out from the code.</summary>
    public bool IsPlaced { get; init; } = true;

    [JsonIgnore]
    public SourceSpan? TextSpan { get; init; }

    [JsonIgnore]
    public SourceSpan? XSpan { get; init; }

    [JsonIgnore]
    public SourceSpan? YSpan { get; init; }

    /// <summary>A shape's width or a line's end X, as one number in code.</summary>
    [JsonIgnore]
    public SourceSpan? WidthSpan { get; init; }

    /// <summary>A shape's height or a line's end Y, as one number in code.</summary>
    [JsonIgnore]
    public SourceSpan? HeightSpan { get; init; }

    /// <summary>A value's expression, when it is written where it is printed.</summary>
    [JsonIgnore]
    public SourceSpan? ExpressionSpan { get; init; }

    /// <summary>What takes it out of the code: its statements. Empty when that cannot be done on its own.</summary>
    [JsonIgnore]
    public IReadOnlyList<SourceSpan> RemoveSpans { get; init; } = [];

    [JsonIgnore]
    public SourceLocation? Location { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<TriggerKind>))]
public enum TriggerKind
{
    /// <summary>A key of a keypad part or, in code that was read, a key constant. <see cref="ScreenTrigger.Value"/> is the character.</summary>
    Key,

    /// <summary>A button part pressed.</summary>
    Button,

    EncoderLeft,

    EncoderRight,

    EncoderPress,

    /// <summary>Time on the screen. <see cref="ScreenTrigger.Value"/> is the milliseconds.</summary>
    Timeout,

    /// <summary>Sent by the sketch's own code. <see cref="ScreenTrigger.Value"/> is its name.</summary>
    Event,

    /// <summary>A condition in code that was read, as written there.</summary>
    Code,
}

/// <summary>What makes a transition happen.</summary>
/// <param name="Part">The input part, in generated projects.</param>
/// <param name="Value">See <see cref="TriggerKind"/>.</param>
/// <param name="Name">How the code calls it: <c>KEY_OK</c>.</param>
/// <param name="MenuItem">The menu item it depends on, from 0; -1 for none.</param>
/// <param name="When">Another condition in the code around it, as written there.</param>
public sealed record ScreenTrigger(TriggerKind Kind, string Part = "", string Value = "", string Name = "", int MenuItem = -1, string When = "");

public sealed record ScreenTransition
{
    public ScreenTrigger Trigger { get; init; } = new(TriggerKind.Code);

    /// <summary>The screen it leads to.</summary>
    public string Target { get; init; } = "";

    [JsonIgnore]
    public SourceSpan? TargetSpan { get; init; }

    /// <summary>The key it compares with, when that comparison is this transition's alone.</summary>
    [JsonIgnore]
    public SourceSpan? KeySpan { get; init; }

    /// <summary>What takes it out of the code. Empty when that cannot be done on its own.</summary>
    [JsonIgnore]
    public IReadOnlyList<SourceSpan> RemoveSpans { get; init; } = [];

    [JsonIgnore]
    public SourceLocation? Location { get; init; }
}

public sealed record ScreenMenuItem
{
    public string Text { get; init; } = "";

    /// <summary>The screen it opens; empty when it runs <see cref="Action"/> or nothing.</summary>
    public string Target { get; init; } = "";

    /// <summary>The hook it runs, in generated projects.</summary>
    public string Action { get; init; } = "";

    [JsonIgnore]
    public SourceSpan? TextSpan { get; init; }
}

/// <summary>A list the cursor moves through; it scrolls in the rows it is given.</summary>
public sealed record ScreenMenu
{
    public IReadOnlyList<ScreenMenuItem> Items { get; init; } = [];

    /// <summary>The first row (or pixel, on an OLED) of the list.</summary>
    public int Top { get; init; }

    /// <summary>How many items show at once.</summary>
    public int Rows { get; init; } = 1;

    public ScreenTrigger? Up { get; init; }

    public ScreenTrigger? Down { get; init; }

    public ScreenTrigger? Select { get; init; }
}

public sealed record ScreenNode
{
    public string Id { get; init; } = "";

    public string Title { get; init; } = "";

    public IReadOnlyList<ScreenElement> Elements { get; init; } = [];

    public ScreenMenu? Menu { get; init; }

    public IReadOnlyList<ScreenTransition> Transitions { get; init; } = [];

    /// <summary>Where it lies on the map.</summary>
    public double MapX { get; init; }

    public double MapY { get; init; }

    /// <summary>Its drawing is code that was not read: an application that draws itself through something unknown.</summary>
    public bool DrawnElsewhere { get; init; }

    /// <summary>Where a new element's code goes.</summary>
    [JsonIgnore]
    public SourceInsert? DrawInsert { get; init; }

    /// <summary>The object it draws on there, with its accessor.</summary>
    [JsonIgnore]
    public string Receiver { get; init; } = "";

    /// <summary>Every place its constant is written, for renaming it.</summary>
    [JsonIgnore]
    public IReadOnlyList<SourceSpan> UseSpans { get; init; } = [];

    /// <summary>What takes the screen out of the code. Empty when something else still uses it.</summary>
    [JsonIgnore]
    public IReadOnlyList<SourceSpan> RemoveSpans { get; init; } = [];

    [JsonIgnore]
    public SourceLocation? Location { get; init; }
}

/// <summary>A project's screens: <c>screens.json</c>, or what a scan of the sketch found (ADR-019).</summary>
public sealed record ScreenDocument
{
    public const string FileName = "screens.json";
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public ScreenSource Source { get; init; }

    public ScreenDisplay Display { get; init; } = new(DisplayFamily.CharLcd, 16, 2, DisplayLibrary.LiquidCrystalI2c, "lcd");

    /// <summary>The screen shown first.</summary>
    public string Start { get; init; } = "";

    public IReadOnlyList<ScreenNode> Screens { get; init; } = [];

    /// <summary>Transitions that work from any screen.</summary>
    public IReadOnlyList<ScreenTransition> FromAnywhere { get; init; } = [];

    /// <summary>In a project from code, how to add to it.</summary>
    [JsonIgnore]
    public ScreenCodeShape? Shape { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Unknown { get; init; }

    public ScreenNode? Find(string id) => Screens.FirstOrDefault(s => s.Id == id);

    public static ScreenDocument? Load(string projectDirectory)
    {
        string path = Path.Combine(projectDirectory, FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        ScreenDocument document = JsonSerializer.Deserialize<ScreenDocument>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException($"{path} is empty.");
        return document.SchemaVersion > CurrentSchemaVersion
            ? throw new InvalidDataException($"{path} has schema version {document.SchemaVersion}; this ArduSim reads up to {CurrentSchemaVersion}.")
            : document;
    }

    /// <summary>Writes the file, replacing the old one atomically.</summary>
    public void Save(string projectDirectory)
    {
        string path = Path.Combine(projectDirectory, FileName);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this with { SchemaVersion = CurrentSchemaVersion }, Options) + "\n");
        File.Move(temp, path, overwrite: true);
    }
}
