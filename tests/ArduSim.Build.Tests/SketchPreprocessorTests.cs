using ArduSim.Build.Sketches;

namespace ArduSim.Build.Tests;

public sealed class SketchPreprocessorTests
{
    private const string Main = "C:/sk/Blink/Blink.ino";

    private static PreprocessedSketch Run(params (string Path, string Text)[] files) => SketchPreprocessor.Preprocess(files);

    private static string[] Lines(PreprocessedSketch result) => result.Code.Split('\n');

    [Fact]
    public void Includes_Arduino_h_and_maps_lines_back_to_the_ino()
    {
        PreprocessedSketch result = Run((Main, "void setup() {\n}\n\nvoid loop() {\n}\n"));

        string[] lines = Lines(result);
        Assert.Equal("#include <Arduino.h>", lines[0]);
        Assert.Equal("#line 1 \"C:/sk/Blink/Blink.ino\"", lines[1]);
        Assert.Equal(["void setup()", "void loop()"], result.Prototypes.Select(p => p.Declaration));
        Assert.Equal([1, 4], result.Prototypes.Select(p => p.Line));
    }

    [Fact]
    public void Prototypes_go_before_the_first_function_and_restore_its_line()
    {
        PreprocessedSketch result = Run((Main, "int led = 13;\n\nvoid setup() { helper(); }\nvoid loop() {}\nvoid helper() {}\n"));

        string code = result.Code;
        int prototype = code.IndexOf("void helper();", StringComparison.Ordinal);
        int global = code.IndexOf("int led = 13;", StringComparison.Ordinal);
        int definition = code.IndexOf("void setup() { helper(); }", StringComparison.Ordinal);
        Assert.True(global < prototype && prototype < definition);
        Assert.Contains("#line 5 \"C:/sk/Blink/Blink.ino\"\nvoid helper();\n", code, StringComparison.Ordinal);
        Assert.Contains("#line 3 \"C:/sk/Blink/Blink.ino\"\nvoid setup() { helper(); }", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Declared_functions_get_no_second_prototype()
    {
        PreprocessedSketch result = Run((Main, "void helper(int x);\nvoid setup() {}\nvoid loop() {}\nvoid helper(int x) {}\n"));

        Assert.DoesNotContain(result.Prototypes, p => p.Declaration.Contains("helper", StringComparison.Ordinal));
    }

    [Fact]
    public void Default_arguments_move_into_the_prototype_and_keep_columns()
    {
        PreprocessedSketch result = Run((Main, "void setup() {}\nvoid loop() {}\nvoid blink(int times = 3, int ms = 100) { }\n"));

        Assert.Contains(result.Prototypes, p => p.Declaration == "void blink(int times = 3, int ms = 100)");
        Assert.Contains("\nvoid blink(int times    , int ms      ) { }\n", result.Code, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("#ifdef DEBUG\nvoid trace() {}\n#endif\n")]
    [InlineData("struct Foo { void bar(); };\nvoid Foo::bar() {}\n")]
    [InlineData("template <typename T> T twice(T v) { return v * 2; }\n")]
    [InlineData("bool operator==(const A& a, const A& b) { return true; }\n")]
    [InlineData("auto f = []() { return 1; };\n")]
    [InlineData("int table[] = { 1, 2, 3 };\n")]
    [InlineData("ISR(TIMER1_COMPA_vect) { }\n")]
    [InlineData("// void commented() {}\n/* void blocked() {} */\nconst char* s = \"void quoted() {}\";\n")]
    public void Skips_things_that_are_not_plain_top_level_functions(string code)
    {
        PreprocessedSketch result = Run((Main, "void setup() {}\nvoid loop() {}\n" + code));

        Assert.Equal(["void setup()", "void loop()"], result.Prototypes.Select(p => p.Declaration));
    }

    [Fact]
    public void Braces_inside_literals_and_raw_strings_do_not_confuse_the_scanner()
    {
        PreprocessedSketch result = Run((Main, "const char* a = \"{\";\nconst char* b = R\"x(}{)x\";\nchar c = '}';\nvoid setup() {}\nvoid loop() {}\n"));

        Assert.Equal(["void setup()", "void loop()"], result.Prototypes.Select(p => p.Declaration));
    }

    [Fact]
    public void Other_ino_files_follow_the_main_one_and_share_the_prototype_block()
    {
        PreprocessedSketch result = Run((Main, "void setup() { other(); }\nvoid loop() {}\n"), ("C:/sk/Blink/b_other.ino", "\nvoid other() {}\n"));

        Assert.Contains(result.Prototypes, p => p.Declaration == "void other()" && p.File == "C:/sk/Blink/b_other.ino" && p.Line == 2);
        int prototype = result.Code.IndexOf("void other();", StringComparison.Ordinal);
        int setup = result.Code.IndexOf("void setup() { other(); }", StringComparison.Ordinal);
        int secondFile = result.Code.IndexOf("#line 1 \"C:/sk/Blink/b_other.ino\"", StringComparison.Ordinal);
        Assert.True(prototype < setup && setup < secondFile);
    }

    [Fact]
    public void Text_before_the_first_function_on_the_same_line_stays_put()
    {
        PreprocessedSketch result = Run((Main, "/* banner\n   comment */ void setup() {}\nvoid loop() {}\n"));

        Assert.Contains("comment */ \n", result.Code, StringComparison.Ordinal);
        Assert.Contains("#line 2 \"C:/sk/Blink/Blink.ino\"\n              void setup() {}", result.Code, StringComparison.Ordinal);
    }

    [Fact]
    public void Static_and_attributed_functions_keep_their_specifiers()
    {
        PreprocessedSketch result = Run((Main, "void setup() {}\nvoid loop() {}\nstatic inline unsigned long elapsed(const char *label) { return 0; }\n__attribute__((noinline)) int slow() { return 1; }\n"));

        Assert.Contains(result.Prototypes, p => p.Declaration == "static inline unsigned long elapsed(const char * label)");
        Assert.Contains(result.Prototypes, p => p.Declaration == "__attribute__((noinline)) int slow()");
    }

    [Fact]
    public void Windows_line_endings_are_normalized()
    {
        PreprocessedSketch result = Run((Main, "void setup() {}\r\nvoid loop() {}\r\n"));

        Assert.DoesNotContain("\r", result.Code, StringComparison.Ordinal);
        Assert.Equal(2, result.Prototypes.Count);
    }
}
