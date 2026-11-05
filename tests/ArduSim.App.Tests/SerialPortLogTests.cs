using System.Text;
using ArduSim.App.ViewModels;

namespace ArduSim.App.Tests;

/// <summary>A port's Monitor text: snapshots cut the bytes anywhere, and the oldest lines go whole.</summary>
public sealed class SerialPortLogTests
{
    private static string Feed(SerialPortLog log, params byte[][] chunks) => string.Concat(chunks.Select(c => log.Decode(c)));

    [Fact]
    public void A_letter_cut_by_a_snapshot_is_not_garbled()
    {
        var log = new SerialPortLog(1000);
        byte[] bytes = Encoding.UTF8.GetBytes("Привет");

        Assert.Equal("", log.Decode(bytes.AsSpan(..1)));
        Assert.Equal("Привет", log.Decode(bytes.AsSpan(1..)));
    }

    [Fact]
    public void Each_port_keeps_its_own_half_letter()
    {
        var first = new SerialPortLog(1000);
        var second = new SerialPortLog(1000);
        byte[] letter = Encoding.UTF8.GetBytes("Ж");

        Assert.Equal("", first.Decode(letter.AsSpan(..1)));
        Assert.Equal("x", second.Decode("x"u8));
        Assert.Equal("Ж", first.Decode(letter.AsSpan(1..)));
    }

    [Fact]
    public void A_line_end_cut_by_a_snapshot_is_one_line_end()
    {
        var log = new SerialPortLog(1000);

        Assert.Equal("a\n", Feed(log, "a\r"u8.ToArray(), "\n"u8.ToArray()));
        Assert.Equal("b\n", log.Decode("b\r\n"u8));
    }

    [Fact]
    public void A_lone_carriage_return_waits_and_then_stays()
    {
        var log = new SerialPortLog(1000);

        Assert.Equal("a", log.Decode("a\r"u8));
        Assert.Equal("\rb", log.Decode("b"u8));
    }

    [Fact]
    public void Stamps_go_at_line_starts_across_chunks_and_not_on_blank_lines()
    {
        var log = new SerialPortLog(1000);

        Assert.Equal("[1] ab", log.Append("ab", "[1] ").Added);
        Assert.Equal("c\n\n[2] d", log.Append("c\n\nd", "[2] ").Added);
        Assert.Equal("[1] abc\n\n[2] d", log.Text);
    }

    [Fact]
    public void Text_added_without_stamps_still_tracks_the_line()
    {
        var log = new SerialPortLog(1000);

        log.Append("a", null);
        Assert.Equal("b\n", log.Append("b\n", "[1] ").Added);
        Assert.Equal("[2] c", log.Append("c", "[2] ").Added);
    }

    [Fact]
    public void Trimming_drops_whole_lines_and_leaves_room_for_many_more()
    {
        var log = new SerialPortLog(100);
        string line = new string('x', 9) + "\n";
        for (int i = 0; i < 10; i++)
        {
            Assert.False(log.Append(line, null).Trimmed);
        }

        (string added, bool trimmed) = log.Append(line, null);

        Assert.True(trimmed);
        Assert.Equal(line, added);
        Assert.Equal(70, log.Text.Length); // 75 kept at most, from the next line start
        Assert.True(log.Text.Length % line.Length == 0);
        Assert.False(log.Append(line, null).Trimmed);
    }

    [Fact]
    public void A_single_endless_line_is_cut_anyway()
    {
        var log = new SerialPortLog(100);

        Assert.True(log.Append(new string('x', 200), null).Trimmed);

        Assert.Equal(75, log.Text.Length);
    }

    [Fact]
    public void Clearing_keeps_a_half_read_letter()
    {
        var log = new SerialPortLog(100);
        byte[] letter = Encoding.UTF8.GetBytes("Ж");
        log.Append("old", null);
        log.Decode(letter.AsSpan(..1));

        log.Clear();

        Assert.Equal("", log.Text);
        Assert.Equal("Ж", log.Decode(letter.AsSpan(1..)));
    }
}
