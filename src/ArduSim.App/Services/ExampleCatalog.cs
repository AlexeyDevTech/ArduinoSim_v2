namespace ArduSim.App.Services;

/// <summary>
/// How the gallery groups the bundled examples: topic and order, and the bottom tab each one reports
/// to. The titles, descriptions and what-to-try lines are <c>Example_&lt;name&gt;</c> strings.
/// </summary>
public static class ExampleCatalog
{
    public const int SerialPanel = 0;
    public const int PlotterPanel = 1;
    public const int LogPanel = 3;
    public const int DiagramPanel = 4;

    public static readonly IReadOnlyList<string> Topics = ["start", "output", "input", "sensors", "displays", "storage", "boards", "tools"];

    private static readonly Dictionary<string, (string Topic, int Order, int Panel)> Entries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Blink"] = ("start", 0, SerialPanel),
        ["Button"] = ("start", 1, SerialPanel),
        ["Potentiometer"] = ("start", 2, PlotterPanel),
        ["BuzzerMelody"] = ("start", 3, SerialPanel),
        ["LedCurrents"] = ("start", 4, SerialPanel),
        ["Relay"] = ("output", 0, SerialPanel),
        ["ServoKnob"] = ("output", 1, SerialPanel),
        ["NeoPixelRing"] = ("output", 2, SerialPanel),
        ["FastLedMatrix"] = ("output", 3, SerialPanel),
        ["RotaryEncoder"] = ("input", 0, SerialPanel),
        ["Keypad4x4"] = ("input", 1, SerialPanel),
        ["Joystick"] = ("input", 2, PlotterPanel),
        ["IrRemote"] = ("input", 3, SerialPanel),
        ["Dht22"] = ("sensors", 0, SerialPanel),
        ["Ds18b20"] = ("sensors", 1, SerialPanel),
        ["Ultrasonic"] = ("sensors", 2, SerialPanel),
        ["Ds3231Clock"] = ("sensors", 3, SerialPanel),
        ["ScreensMenu"] = ("displays", -2, SerialPanel),
        ["ScreensKitchenTimer"] = ("displays", -1, SerialPanel),
        ["LcdI2c"] = ("displays", 0, SerialPanel),
        ["LcdHello"] = ("displays", 1, SerialPanel),
        ["LcdCustomChars"] = ("displays", 2, SerialPanel),
        ["LcdA02"] = ("displays", 3, SerialPanel),
        ["OledAdafruit"] = ("displays", 4, SerialPanel),
        ["OledU8g2"] = ("displays", 5, SerialPanel),
        ["OledGyver"] = ("displays", 6, SerialPanel),
        ["Sh1106"] = ("displays", 7, SerialPanel),
        ["OledSsd1309"] = ("displays", 8, SerialPanel),
        ["OledSsd1327Esp32"] = ("displays", 9, SerialPanel),
        ["Esp32Oled"] = ("displays", 10, SerialPanel),
        ["Esp8266Oled"] = ("displays", 11, SerialPanel),
        ["TftIli9341"] = ("displays", 12, SerialPanel),
        ["TftSt7735"] = ("displays", 13, SerialPanel),
        ["TftSt7789Esp32"] = ("displays", 14, SerialPanel),
        ["Nokia5110"] = ("displays", 15, SerialPanel),
        ["SevenSegment"] = ("displays", 16, SerialPanel),
        ["Tm1637"] = ("displays", 17, SerialPanel),
        ["MatrixParola"] = ("displays", 18, SerialPanel),
        ["MatrixLedControl"] = ("displays", 19, SerialPanel),
        ["Esp32WiFi"] = ("storage", 0, SerialPanel),
        ["Esp8266WiFi"] = ("storage", 1, SerialPanel),
        ["Esp32Preferences"] = ("storage", 2, SerialPanel),
        ["Esp8266Eeprom"] = ("storage", 3, SerialPanel),
        ["SdCardLog"] = ("storage", 4, SerialPanel),
        ["UnoBlink"] = ("boards", 0, SerialPanel),
        ["Mega2560Ports"] = ("boards", 1, SerialPanel),
        ["ProMini3v3"] = ("boards", 2, SerialPanel),
        ["Esp32Blink"] = ("boards", 3, SerialPanel),
        ["Esp32Fade"] = ("boards", 4, SerialPanel),
        ["Esp32Tasks"] = ("boards", 5, SerialPanel),
        ["Esp8266Blink"] = ("boards", 6, SerialPanel),
        ["Esp8266Fade"] = ("boards", 7, SerialPanel),
        ["LogicAnalyzerDemo"] = ("tools", 0, DiagramPanel),
        ["FloatingInput"] = ("tools", 1, LogPanel),
    };

    /// <summary>An example not in the table is listed last, under Инструменты.</summary>
    public static (string Topic, int Order, int Panel) Find(string name) =>
        Entries.TryGetValue(name, out (string Topic, int Order, int Panel) entry) ? entry : ("tools", 99, SerialPanel);

    public static IReadOnlyCollection<string> Names => Entries.Keys;
}
