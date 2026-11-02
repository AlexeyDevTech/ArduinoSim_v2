using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace ArduSim.Integration.Tests;

/// <summary>
/// OLED libraries on SSD1306 and SH1106, over I2C, hardware SPI and software SPI: every pattern
/// sketch draws a 16x8 box at the top-left corner and a pixel at (127, 63); the Runner's
/// <c>--dump-display</c> PNG shows whether the picture is exactly there.
/// </summary>
public sealed partial class OledRunTests(ToolchainFixture toolchain)
{
    private const string Fixtures = "tests/ArduSim.Integration.Tests/Projects/Oled";

    private readonly ToolchainFixture _toolchain = toolchain;

    [Theory]
    [InlineData("ssd1306-i2c", "u8g2-ssd1306-i2c")]
    [InlineData("sh1106-i2c", "u8g2-sh1106-i2c")]
    [InlineData("ssd1306-spi", "u8g2-ssd1306-spi")]
    [InlineData("sh1106-spi", "u8g2-sh1106-spi")]
    [InlineData("ssd1306-i2c", "gyver-ssd1306-i2c")]
    [InlineData("sh1106-i2c", "gyver-sh1106-i2c")]
    [InlineData("ssd1306-spi", "gyver-ssd1306-spi")]
    [InlineData("sh1106-spi", "gyver-sh1106-spi")]
    [InlineData("ssd1306-spi", "adafruit-ssd1306-spi")]
    [InlineData("sh1106-i2c", "adafruit-sh1106-i2c")]
    public async Task Pattern_lands_exactly_where_the_library_drew_it(string scene, string sketch)
    {
        Assert.True(_toolchain.Toolchain.IsComplete);
        string project = WithSketch(scene, Path.Combine(Fixtures, "sketches", sketch + ".ino"));
        string png = Path.Combine(project, "display.png");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "1500", "--quiet", "--dump-display", png);

        Assert.True(result.ExitCode == 0, result.ToString());
        (int width, int height, byte[] pixels) = ReadPng(png);
        Assert.Equal((128, 64), (width, height));
        bool Lit(int x, int y) => pixels[(y * width) + x] > 128;
        Assert.True(Lit(0, 0) && Lit(15, 7) && Lit(127, 63), Describe(result, width, pixels));
        Assert.False(Lit(16, 0) || Lit(0, 8) || Lit(126, 63) || Lit(127, 62), Describe(result, width, pixels));
        Assert.Equal((16 * 8) + 1, pixels.Count(p => p > 128));
        Assert.DoesNotContain("No device answers", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Adafruit_ssd1306_128x64_i2c_example_animates_without_nacks()
    {
        string project = RunnerProcess.CopyProject("examples/OledAdafruit");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "12000", "--quiet");

        Assert.True(result.ExitCode == 0, result.ToString());
        List<(double Ms, int Lit)> frames = Frames(result, "oled1");
        Assert.True(frames.Count > 50, $"only {frames.Count} display changes in 12 s");
        Assert.Contains(frames, f => f.Lit > 500);                 // the splash and filled shapes
        Assert.True(frames.Select(f => f.Lit).Distinct().Count() > 20, "the picture should keep changing");
        Assert.DoesNotContain("No device answers", result.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("warning", result.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Adafruit_software_spi_constructor_drives_the_display()
    {
        // The library's own ssd1306_128x64_spi example uses the bit-banged constructor (MOSI 9, CLK 10, DC 11, CS 12, RESET 13).
        string project = WithSketch("ssd1306-swspi", "libraries/Adafruit_SSD1306/examples/ssd1306_128x64_spi/ssd1306_128x64_spi.ino");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "4000", "--quiet");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Contains(Frames(result, "oled1"), f => f.Lit > 500);
    }

    [Theory]
    [InlineData("examples/OledU8g2")]
    [InlineData("examples/OledGyver")]
    [InlineData("examples/Sh1106")]
    public async Task Oled_examples_draw_and_keep_drawing(string example)
    {
        string project = RunnerProcess.CopyProject(example);

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "3000", "--quiet");

        Assert.True(result.ExitCode == 0, result.ToString());
        List<(double Ms, int Lit)> frames = Frames(result, "oled1");
        Assert.True(frames.Count >= 3, result.Stdout);
        Assert.All(frames.Skip(1), f => Assert.True(f.Lit > 100, $"{f.Lit} px at {f.Ms} ms"));
        Assert.DoesNotContain("warning", result.Stdout, StringComparison.Ordinal);
    }

    private static string WithSketch(string scene, string sketch)
    {
        string project = RunnerProcess.CopyProject(Path.Combine(Fixtures, scene));
        Directory.CreateDirectory(Path.Combine(project, "sketch"));
        File.Copy(Path.Combine(RunnerProcess.RepositoryRoot, sketch), Path.Combine(project, "sketch", "sketch.ino"));
        return project;
    }

    private static List<(double Ms, int Lit)> Frames(RunResult result, string id) =>
        [.. result.Timeline
            .Select(e => (e.Milliseconds, Match: LitLine().Match(e.Text)))
            .Where(e => e.Match.Success && e.Match.Groups["id"].Value == id)
            .Select(e => (e.Milliseconds, int.Parse(e.Match.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture)))];

    private static string Describe(RunResult result, int width, byte[] pixels)
    {
        var lit = new List<string>();
        for (int i = 0; i < pixels.Length && lit.Count < 12; i++)
        {
            if (pixels[i] > 128)
            {
                lit.Add($"({i % width},{i / width})");
            }
        }

        return $"first lit pixels: {string.Join(" ", lit)}\n{result}";
    }

    /// <summary>Reads the Runner's 8-bit grayscale PNG (filter 0 rows, one IDAT).</summary>
    private static (int Width, int Height, byte[] Pixels) ReadPng(string path)
    {
        byte[] file = File.ReadAllBytes(path);
        int offset = 8;
        int width = 0;
        int height = 0;
        using var idat = new MemoryStream();
        while (offset < file.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(offset));
            string type = System.Text.Encoding.ASCII.GetString(file, offset + 4, 4);
            ReadOnlySpan<byte> data = file.AsSpan(offset + 8, length);
            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(data);
                height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
            }
            else if (type == "IDAT")
            {
                idat.Write(data);
            }

            offset += 12 + length;
        }

        idat.Position = 0;
        using var zlib = new ZLibStream(idat, CompressionMode.Decompress);
        var raw = new byte[(width + 1) * height];
        zlib.ReadExactly(raw);
        var pixels = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            Array.Copy(raw, (y * (width + 1)) + 1, pixels, y * width, width);
        }

        return (width, height, pixels);
    }

    [GeneratedRegex(@"^(?<id>[a-z0-9-]+): (?<n>\d+) px on$")]
    private static partial Regex LitLine();
}
