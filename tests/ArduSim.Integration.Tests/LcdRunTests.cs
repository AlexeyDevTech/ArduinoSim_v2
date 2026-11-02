using System.Text.RegularExpressions;

namespace ArduSim.Integration.Tests;

/// <summary>LCD examples and the libraries' own examples, built and run headlessly; the Runner prints each display's rows when they change.</summary>
public sealed partial class LcdRunTests(ToolchainFixture toolchain)
{
    private readonly ToolchainFixture _toolchain = toolchain;

    [Fact]
    public async Task Lcd_hello_on_a_2004_in_4_bit_mode_shows_four_rows_and_counts_seconds()
    {
        Assert.True(_toolchain.Toolchain.IsComplete);
        string project = RunnerProcess.CopyProject("examples/LcdHello");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "3500", "--quiet");

        Assert.True(result.ExitCode == 0, result.ToString());
        List<(double Ms, string[] Rows)> frames = Frames(result, "lcd1");
        string[] last = frames[^1].Rows;
        Assert.Equal("hello, world!       ", last[0]);
        Assert.Equal("3                   ", last[1]);
        Assert.Equal("20x4, 4-bit mode    ", last[2]);
        Assert.Equal("I \\0 Arduino         ", last[3]); // the createChar glyph in CGRAM slot 0
        Assert.Equal(["0", "1", "2", "3"], frames.Select(f => f.Rows[1].Trim()).Where(r => r.Length > 0).Distinct());
        Assert.DoesNotContain("warning", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lcd_i2c_example_shows_hello_world_and_counts_seconds()
    {
        string project = RunnerProcess.CopyProject("examples/LcdI2c");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "3500", "--quiet");

        Assert.True(result.ExitCode == 0, result.ToString());
        List<(double Ms, string[] Rows)> frames = Frames(result, "lcd1");
        double shown = frames.First(f => f.Rows[0].StartsWith("hello, world!", StringComparison.Ordinal)).Ms;
        Assert.InRange(shown, 1000, 1200); // LiquidCrystal_I2C::init() waits 1 s first
        Assert.Equal(["hello, world!   ", "3               "], frames[^1].Rows);
        Assert.Equal(["1", "2", "3"], frames.Select(f => f.Rows[1].Trim()).Where(r => r.Length > 0).Distinct());
        Assert.DoesNotContain("No device answers", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Custom_chars_example_renders_glyphs_and_scrolls_left()
    {
        string project = RunnerProcess.CopyProject("examples/LcdCustomChars");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "2500", "--quiet");

        Assert.True(result.ExitCode == 0, result.ToString());
        List<string> firstRows = [.. Frames(result, "lcd1").Select(f => f.Rows[0].TrimEnd()).Distinct()];
        int start = firstRows.IndexOf("Glyphs: \\0\\1\\2");
        Assert.True(start >= 0, string.Join(Environment.NewLine, firstRows));
        Assert.Equal(["lyphs: \\0\\1\\2", "yphs: \\0\\1\\2", "phs: \\0\\1\\2"], firstRows.Skip(start + 1).Take(3));
    }

    [Fact]
    public async Task The_standard_liquidcrystal_i2c_hello_world_fills_a_2004()
    {
        string project = WithLibraryExample("tests/ArduSim.Integration.Tests/Projects/Lcd2004I2c", "libraries/LiquidCrystal_I2C/examples/HelloWorld/HelloWorld.pde");

        // The sketch calls init() twice, and each waits a second.
        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "3000", "--quiet");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal(
            ["   Hello, world!    ", "  Ywrobot Arduino!  ", "Arduino LCM IIC 2004", "  Power By Ec-yuan! "],
            Frames(result, "lcd1")[^1].Rows);
    }

    [Fact]
    public async Task The_liquidcrystal_scroll_example_moves_the_text_off_and_back()
    {
        string project = WithLibraryExample("tests/ArduSim.Integration.Tests/Projects/Lcd1602Parallel", "libraries/LiquidCrystal/examples/Scroll/Scroll.ino");

        // 1 s, 13 steps left, then right: the text is back at column 3 after 16 more steps (150 ms each).
        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "6000", "--quiet");

        Assert.True(result.ExitCode == 0, result.ToString());
        List<string> rows = [.. Frames(result, "lcd1").Select(f => f.Rows[0])];
        Assert.Contains("hello, world!   ", rows);
        Assert.Contains("ello, world!    ", rows);  // one step left
        Assert.Contains("                ", rows);  // scrolled off to the left
        Assert.Contains("   hello, world!", rows);  // coming back from the right
    }

    [Fact]
    public async Task The_liquidcrystal_custom_character_example_animates_its_glyphs()
    {
        string project = WithLibraryExample("tests/ArduSim.Integration.Tests/Projects/Lcd1602Parallel", "libraries/LiquidCrystal/examples/CustomCharacter/CustomCharacter.ino");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "3000", "--quiet");

        Assert.True(result.ExitCode == 0, result.ToString());
        List<(double Ms, string[] Rows)> frames = Frames(result, "lcd1");
        Assert.Equal("I \\0 Arduino! \\1  ", frames[^1].Rows[0]);
        Assert.Contains(frames, f => f.Rows[1] == "    \\3" + new string(' ', 11));
        Assert.Contains(frames, f => f.Rows[1] == "    \\4" + new string(' ', 11));
    }

    /// <summary>A fixture scene with a library's example copied in, unmodified, as the sketch.</summary>
    private static string WithLibraryExample(string fixture, string example)
    {
        string project = RunnerProcess.CopyProject(fixture);
        string sketch = Path.Combine(project, "sketch");
        Directory.CreateDirectory(sketch);
        File.Copy(Path.Combine(RunnerProcess.RepositoryRoot, example), Path.Combine(sketch, "sketch.ino"));
        return project;
    }

    /// <summary>The display lines <c>lcd1: "row 0" "row 1"</c> in order.</summary>
    private static List<(double Ms, string[] Rows)> Frames(RunResult result, string id) =>
        [.. result.Timeline
            .Where(e => e.Text.StartsWith(id + ": \"", StringComparison.Ordinal))
            .Select(e => (e.Milliseconds, QuotedRow().Matches(e.Text).Select(m => m.Groups[1].Value).ToArray()))];

    [GeneratedRegex("\"([^\"]*)\"")]
    private static partial Regex QuotedRow();
}
