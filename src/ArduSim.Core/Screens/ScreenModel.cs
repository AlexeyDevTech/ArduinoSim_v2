using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

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
/// <param name="OwnFont">In code that was read, text goes through a class of the sketch's own that decodes UTF-8: it shows letters the display's ROM does not have.</param>
public sealed record ScreenDisplay(DisplayFamily Family, int Width, int Height, DisplayLibrary Library, string Variable, string Part = "", bool Existing = false, string TypeName = "", bool TextFromBaseline = false, [property: JsonIgnore] bool OwnFont = false);

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

    /// <summary>A value in digits two rows high on a character display, in a large text size on an OLED.</summary>
    BigNumber,

    /// <summary>How full something is: <see cref="ScreenElement.Text"/> out of <see cref="ScreenElement.Maximum"/>.</summary>
    Bar,

    /// <summary>One symbol, named by <see cref="ScreenElement.Text"/>.</summary>
    Symbol,

    /// <summary>Digits typed on a keypad, for the variable named by <see cref="ScreenElement.Text"/>.</summary>
    Input,

    /// <summary>Drawn by the project's own code: <see cref="ScreenElement.Text"/> names the hook.</summary>
    Drawn,
}

/// <summary>Where text lies in its field. On an input, left means digits fill from the left, right that they come in from the right.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ElementAlign>))]
public enum ElementAlign
{
    Left,
    Center,
    Right,
}

/// <summary>How a number reads on a screen.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ValueFormat>))]
public enum ValueFormat
{
    /// <summary>What suits the variable: a flag's or a choice's word, a time, otherwise the number.</summary>
    Auto,

    /// <summary>At least two digits: 07.</summary>
    TwoDigits,

    /// <summary>Seconds as 12:34 (hours and minutes).</summary>
    HourMinute,

    /// <summary>Seconds as 12:34:56.</summary>
    HourMinuteSecond,

    /// <summary>Seconds as 34:56 (minutes and seconds).</summary>
    MinuteSecond,

    /// <summary>Milliseconds as 34:56.78.</summary>
    MinuteSecondCentis,

    /// <summary>A date written as yyyymmdd, as 31.12.2026.</summary>
    DateDayFirst,

    /// <summary>A date written as yyyymmdd, as 12.31.2026.</summary>
    DateMonthFirst,

    /// <summary>A date written as yyyymmdd, as 2026-12-31.</summary>
    DateYearFirst,

    /// <summary>The element's own list of words, by number from 0.</summary>
    List,
}

/// <summary>What a variable of a generated project holds.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VariableKind>))]
public enum VariableKind
{
    /// <summary>A whole number between <see cref="ScreenVariable.Min"/> and <see cref="ScreenVariable.Max"/>.</summary>
    Number,

    /// <summary>On or off; its options are the two words for that.</summary>
    Flag,

    /// <summary>One of its options, by number from 0.</summary>
    Choice,

    /// <summary>A time of day in seconds.</summary>
    Time,

    /// <summary>Counts up while running; reads as seconds, and in expressions <c>name_ms</c> and <c>name_on</c>.</summary>
    Stopwatch,

    /// <summary>Counts down to zero while running, and says when it gets there; reads as seconds.</summary>
    Countdown,
}

/// <summary>A piece of a generated project's data.</summary>
public sealed record ScreenVariable
{
    public string Name { get; init; } = "";

    public VariableKind Kind { get; init; }

    /// <summary>What it starts as: a number, 0 or 1, an option's number, or seconds.</summary>
    public int Initial { get; init; }

    public int Min { get; init; }

    public int Max { get; init; } = 100;

    /// <summary>Going past one end comes back at the other, instead of stopping there.</summary>
    public bool Wrap { get; init; }

    public IReadOnlyList<string> Options { get; init; } = [];

    /// <summary>Kept in EEPROM: it is still there after a power cut.</summary>
    public bool Saved { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<ActionKind>))]
public enum ActionKind
{
    /// <summary>The variable <see cref="ScreenAction.Target"/> takes the expression <see cref="ScreenAction.Value"/>.</summary>
    Set,

    /// <summary>The expression is added to the variable.</summary>
    Add,

    /// <summary>A flag flips; a choice goes to its next option.</summary>
    Toggle,

    Start,
    Stop,

    /// <summary>A stopwatch or countdown that runs stops, and one that is stopped runs.</summary>
    StartStop,

    Reset,

    /// <summary>What the screen's input holds goes to its variable. If it makes no sense the input empties, and nothing after this happens.</summary>
    InputCommit,

    /// <summary>The input's last digit goes.</summary>
    InputErase,

    InputClear,

    /// <summary>A buzzer part sounds: <see cref="ScreenAction.Value"/> is the hertz and the milliseconds.</summary>
    Beep,

    /// <summary>An LED, relay or other output part follows the expression: on when it is not zero.</summary>
    Output,

    /// <summary>Runs <c>uiAction_name()</c> in <c>ui_hooks.cpp</c>.</summary>
    Hook,
}

/// <summary>One step of what a transition, a menu item or entering a screen does.</summary>
/// <param name="Target">A variable, a part or a hook; see <see cref="ActionKind"/>.</param>
/// <param name="Value">An expression; see <see cref="ActionKind"/>.</param>
public sealed record ScreenAction(ActionKind Kind, string Target = "", string Value = "");

/// <summary>Characters of source that a scan read: what an in-place edit may replace.</summary>
/// <param name="Offset">Characters from the start of the file.</param>
/// <param name="Expected">The token's text as scanned; an edit is refused if the file no longer has it there.</param>
public sealed record SourceSpan(string File, int Offset, int Length, string Expected);

/// <summary>A number the code does not write as a plain number: a name, a local variable, an expression.</summary>
/// <param name="Text">As written.</param>
/// <param name="Span">Its characters, which typing can replace; null in code other screens share.</param>
/// <param name="IsLocked">A drag must not replace it: it changes while the sketch runs (a loop's variable, a field, a call), or other screens share it. Only typing replaces it, if anything does.</param>
/// <param name="Dead">The declarations nothing else uses once a number takes its place, which go with it.</param>
public sealed record CodeExpression(string Text, SourceSpan? Span, bool IsLocked, IReadOnlyList<SourceSpan> Dead);

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

    /// <summary>Where a text or value lies in its field, which starts at X and is <see cref="Width"/> wide, or to the display's edge when that is 0.</summary>
    public ElementAlign Align { get; init; }

    /// <summary>How a value, big number or input reads.</summary>
    public ValueFormat Format { get; init; }

    /// <summary>The words of <see cref="ValueFormat.List"/>.</summary>
    public IReadOnlyList<string> Options { get; init; } = [];

    /// <summary>Drawn only while this expression is not zero, in a generated project; empty for always.</summary>
    public string When { get; init; } = "";

    /// <summary>A bar's full value: an expression.</summary>
    public string Maximum { get; init; } = "";

    [JsonIgnore]
    public SourceSpan? TextSpan { get; init; }

    /// <summary>What a number can replace to move it across, whether the code writes a number there or not.</summary>
    [JsonIgnore]
    public SourceSpan? XSpan { get; init; }

    /// <summary>What a number can replace to move it down.</summary>
    [JsonIgnore]
    public SourceSpan? YSpan { get; init; }

    /// <summary>A shape's width or a line's end X: what a number can replace.</summary>
    [JsonIgnore]
    public SourceSpan? WidthSpan { get; init; }

    /// <summary>A shape's height or a line's end Y: what a number can replace.</summary>
    [JsonIgnore]
    public SourceSpan? HeightSpan { get; init; }

    /// <summary>The position across, when the code does not write it as a plain number.</summary>
    [JsonIgnore]
    public CodeExpression? XCode { get; init; }

    [JsonIgnore]
    public CodeExpression? YCode { get; init; }

    [JsonIgnore]
    public CodeExpression? WidthCode { get; init; }

    [JsonIgnore]
    public CodeExpression? HeightCode { get; init; }

    /// <summary>Printed where the last thing ended, or where shared code left the cursor: where a cursor of its own can go.</summary>
    [JsonIgnore]
    public SourceInsert? CursorInsert { get; init; }

    /// <summary>The object it draws on there, with its accessor.</summary>
    [JsonIgnore]
    public string CursorReceiver { get; init; } = "";

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

    /// <summary>Any key, button or encoder press that nothing else on the screen took.</summary>
    AnyKey,

    /// <summary>The expression <see cref="ScreenTrigger.Value"/> becomes true.</summary>
    Condition,

    /// <summary>The countdown named by <see cref="ScreenTrigger.Value"/> reaches zero.</summary>
    Finished,
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

    /// <summary>The screen it leads to; <see cref="ScreenDocument.Back"/> for the one before; empty to stay.</summary>
    public string Target { get; init; } = "";

    /// <summary>What it does first, in a generated project.</summary>
    public IReadOnlyList<ScreenAction> Actions { get; init; } = [];

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

    /// <summary>What choosing it does first, in generated projects.</summary>
    public IReadOnlyList<ScreenAction> Actions { get; init; } = [];

    /// <summary>What shows at the row's right edge: an expression or a variable; empty for nothing.</summary>
    public string Value { get; init; } = "";

    /// <summary>How <see cref="Value"/> reads.</summary>
    public ValueFormat Format { get; init; }

    /// <summary>A star shows at the row's right edge while this expression is not zero; empty for never.</summary>
    public string Mark { get; init; } = "";

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

    /// <summary>The row above the list says which item this is, as 3/7.</summary>
    public bool Counter { get; init; }

    /// <summary>What marks the current item, a symbol's <c>{name}</c> included; empty for &gt;.</summary>
    public string Marker { get; init; } = "";
}

public sealed record ScreenNode
{
    public string Id { get; init; } = "";

    public string Title { get; init; } = "";

    public IReadOnlyList<ScreenElement> Elements { get; init; } = [];

    public ScreenMenu? Menu { get; init; }

    public IReadOnlyList<ScreenTransition> Transitions { get; init; } = [];

    /// <summary>What arriving here does, unless it is coming back from a screen further on.</summary>
    public IReadOnlyList<ScreenAction> OnEnter { get; init; } = [];

    /// <summary>Keys nothing else took on this screen go to <c>uiKey_id(key)</c> in <c>ui_hooks.cpp</c>.</summary>
    public bool KeyHook { get; init; }

    /// <summary><c>uiTick_id()</c> in <c>ui_hooks.cpp</c> runs on every pass while this screen shows.</summary>
    public bool TickHook { get; init; }

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
    public const int CurrentSchemaVersion = 2;

    /// <summary>A transition's target that means the screen it came from.</summary>
    public const string Back = "@back";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { OmitWhatIsEmpty } },
    };

    // A file says what was decided: what no one has set stays out of it, and reads back as its default.
    private static void OmitWhatIsEmpty(JsonTypeInfo type)
    {
        foreach (JsonPropertyInfo property in type.Properties)
        {
            if (property.PropertyType == typeof(string))
            {
                property.ShouldSerialize = static (_, value) => value is not "";
            }
            else if (property.PropertyType == typeof(ElementAlign) || property.PropertyType == typeof(ValueFormat) || property.PropertyType == typeof(bool))
            {
                property.ShouldSerialize = OnlyIfNotDefault(property);
            }
            else if (property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            {
                property.ShouldSerialize = static (_, value) => value is not System.Collections.ICollection { Count: 0 };
            }
        }
    }

    // The same, for an enum or a flag; IsPlaced is the one whose default is true.
    private static Func<object, object?, bool> OnlyIfNotDefault(JsonPropertyInfo property)
    {
        object? plain = property.Name == "isPlaced" ? true : Activator.CreateInstance(property.PropertyType);
        return (_, value) => !Equals(value, plain);
    }

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public ScreenSource Source { get; init; }

    public ScreenDisplay Display { get; init; } = new(DisplayFamily.CharLcd, 16, 2, DisplayLibrary.LiquidCrystalI2c, "lcd");

    /// <summary>The screen shown first.</summary>
    public string Start { get; init; } = "";

    public IReadOnlyList<ScreenNode> Screens { get; init; } = [];

    /// <summary>Transitions that work from any screen.</summary>
    public IReadOnlyList<ScreenTransition> FromAnywhere { get; init; } = [];

    /// <summary>A generated project's data.</summary>
    public IReadOnlyList<ScreenVariable> Variables { get; init; } = [];

    /// <summary>A generated project's own symbols. <see cref="ScreenFont.BuiltIn"/> are there without being here.</summary>
    public IReadOnlyList<ScreenGlyph> Glyphs { get; init; } = [];

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
