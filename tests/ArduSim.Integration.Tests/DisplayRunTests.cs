using System.Buffers.Binary;
using System.IO.Compression;

namespace ArduSim.Integration.Tests;

/// <summary>The M10 display examples run headlessly with their libraries; the <c>--dump-display</c> PNG shows the glass (ADR-013).</summary>
public sealed class DisplayRunTests
{
    [Fact]
    public async Task TftIli9341_example_reads_the_controller_status_and_draws_its_test_card()
    {
        string project = RunnerProcess.CopyProject("examples/TftIli9341");
        string png = Path.Combine(project, "display.png");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "1500", "--quiet", "--dump-display", png);

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal(["Display power mode: 0x9C", "Pixel format: 0x55", "Test card drawn"], result.SerialLines);
        Image image = ReadPng(png);
        Assert.Equal((240, 320), (image.Width, image.Height));
        Assert.Equal((255, 255, 255), image[15, 60]);  // the white bar on the left
        Assert.Equal((255, 0, 0), image[165, 60]);     // red: the BGR panel gets its colors in order
        Assert.Equal((0, 0, 0), image[225, 60]);
        (int r, int g, int b) = image[60, 260];        // inside the orange disc
        Assert.True(r == 255 && g is > 140 and < 180 && b == 0, $"{r},{g},{b}");
    }

    [Fact]
    public async Task TftSt7735_example_shows_the_potentiometer_as_a_bar()
    {
        string project = RunnerProcess.CopyProject("examples/TftSt7735");
        string png = Path.Combine(project, "display.png");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "2500", "--quiet", "--input", "1800:pot1:value=0.1", "--dump-display", png);

        Assert.True(result.ExitCode == 0, result.ToString());
        string[] serial = [.. result.SerialLines];
        Assert.Equal(2, serial.Length);
        Assert.Equal("A0 = 409", serial[0]);
        Assert.StartsWith("A0 = 92", serial[1], StringComparison.Ordinal);
        Image image = ReadPng(png);
        Assert.Equal((128, 160), (image.Width, image.Height));
        Assert.Equal((0, 255, 0), image[100, 110]);
        Assert.Equal((0, 0, 0), image[118, 110]);
        Assert.Equal((255, 255, 255), image[4, 105]); // the bar's outline
    }

    [Fact]
    public async Task TftSt7789Esp32_example_draws_true_colors_on_the_ips_panel_without_cs()
    {
        string project = RunnerProcess.CopyProject("examples/TftSt7789Esp32");
        string png = Path.Combine(project, "display.png");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "1000", "--dump-display", png);

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal(["Rings drawn"], result.SerialLines);
        Assert.DoesNotContain("warning", result.Stdout, StringComparison.Ordinal);
        Image image = ReadPng(png);
        Assert.Equal((240, 240), (image.Width, image.Height));
        Assert.Equal((255, 0, 0), image[10, 120]);   // the outer ring is red, not cyan: INVON on an IPS panel
        Assert.Equal((0, 0, 0), image[120, 2]);
    }

    [Fact]
    public async Task Nokia5110_example_draws_dark_pixels_on_the_lcd_with_its_contrast()
    {
        string project = RunnerProcess.CopyProject("examples/Nokia5110");
        string png = Path.Combine(project, "display.png");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "1200", "--quiet", "--dump-display", png);

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal(["Ready"], result.SerialLines);
        Assert.Contains(result.Timeline, e => e.Text.StartsWith("disp1: ", StringComparison.Ordinal) && e.Text.EndsWith(" px dark", StringComparison.Ordinal));
        Image image = ReadPng(png);
        Assert.Equal((84, 48), (image.Width, image.Height));
        Assert.True(image[0, 30].R < 50, "the sine wave starts dark at (0, 30)");
        Assert.True(image[0, 47].R > 200, "the background stays light at V_OP 60");
    }

    [Fact]
    public async Task OledSsd1309_example_draws_its_frame_through_u8g2()
    {
        string project = RunnerProcess.CopyProject("examples/OledSsd1309");
        string png = Path.Combine(project, "display.png");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "500", "--dump-display", png);

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.DoesNotContain("warning", result.Stdout, StringComparison.Ordinal);
        Image image = ReadPng(png);
        Assert.Equal((128, 64), (image.Width, image.Height));
        foreach ((int x, int y) in new[] { (0, 0), (64, 0), (127, 63), (0, 40) })
        {
            Assert.Equal(255, image[x, y].R);
        }

        Assert.Equal(0, image[64, 60].R);
    }

    [Fact]
    public async Task OledSsd1327Esp32_example_shows_sixteen_gray_levels()
    {
        string project = RunnerProcess.CopyProject("examples/OledSsd1327Esp32");
        string png = Path.Combine(project, "display.png");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "500", "--quiet", "--dump-display", png);

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal(["Stripes drawn"], result.SerialLines);
        Image image = ReadPng(png);
        Assert.Equal((128, 128), (image.Width, image.Height));
        int[] levels = [.. Enumerable.Range(0, 16).Select(stripe => image[(stripe * 8) + 4, 80].R)];
        Assert.Equal(0, levels[0]);
        Assert.Equal(255, levels[15]);
        Assert.True(levels.Zip(levels.Skip(1)).All(p => p.First < p.Second), string.Join(",", levels));
    }

    [Fact]
    public async Task LcdA02_example_prints_cyrillic_and_latin_1_through_the_european_rom()
    {
        string project = RunnerProcess.CopyProject("examples/LcdA02");

        RunResult result = await RunnerProcess.RunProjectAsync(project, "--run-for", "1500", "--quiet");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Contains(result.Timeline, e => e.Text == @"lcd1: ""\x87P\x84BET, M\x84P!    "" ""23.5\xB0C  \xC4\xD6\xDC\xDF    """);
    }

    private sealed record Image(int Width, int Height, int Channels, byte[] Pixels)
    {
        public (int R, int G, int B) this[int x, int y]
        {
            get
            {
                int at = ((y * Width) + x) * Channels;
                return Channels == 3 ? (Pixels[at], Pixels[at + 1], Pixels[at + 2]) : (Pixels[at], Pixels[at], Pixels[at]);
            }
        }
    }

    /// <summary>Reads the Runner's 8-bit grayscale or RGB PNG (filter 0 rows, one IDAT).</summary>
    private static Image ReadPng(string path)
    {
        byte[] file = File.ReadAllBytes(path);
        int offset = 8;
        int width = 0;
        int height = 0;
        int channels = 1;
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
                channels = data[9] == 2 ? 3 : 1;
            }
            else if (type == "IDAT")
            {
                idat.Write(data);
            }

            offset += 12 + length;
        }

        idat.Position = 0;
        using var zlib = new ZLibStream(idat, CompressionMode.Decompress);
        int stride = width * channels;
        var raw = new byte[(stride + 1) * height];
        zlib.ReadExactly(raw);
        var pixels = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            Array.Copy(raw, (y * (stride + 1)) + 1, pixels, y * stride, stride);
        }

        return new Image(width, height, channels, pixels);
    }
}
