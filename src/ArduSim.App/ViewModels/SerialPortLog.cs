using System.Text;

namespace ArduSim.App.ViewModels;

/// <summary>
/// One serial port's text in the Monitor. Snapshots cut the byte stream anywhere, so decoding and the
/// line state carry over from chunk to chunk. When the text outgrows the limit its oldest whole lines
/// go, and enough of them that this stays rare: every trim makes the Monitor replace its whole text.
/// </summary>
public sealed class SerialPortLog(int limit)
{
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly StringBuilder _text = new();
    private bool _endsWithReturn;
    private bool _atLineStart = true;

    public string Text => _text.ToString();

    /// <summary>The text these bytes complete, with CR LF as LF: a letter or a line end cut by the chunk's end waits for the next one.</summary>
    public string Decode(ReadOnlySpan<byte> data)
    {
        var chars = new char[_decoder.GetCharCount(data, flush: false)];
        int count = _decoder.GetChars(data, chars, flush: false);
        string text = (_endsWithReturn ? "\r" : "") + new string(chars, 0, count);
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        _endsWithReturn = text.EndsWith('\r');
        return _endsWithReturn ? text[..^1] : text;
    }

    /// <summary>Adds decoded text, with <paramref name="stamp"/> before every new line when given.</summary>
    /// <returns>What was added, and whether older lines went to make room.</returns>
    public (string Added, bool Trimmed) Append(string text, string? stamp)
    {
        ArgumentNullException.ThrowIfNull(text);
        string added = stamp is null ? text : Stamped(text, stamp);
        _text.Append(added);
        if (text.Length > 0)
        {
            _atLineStart = text[^1] == '\n';
        }

        return (added, Trim());
    }

    public void Clear() => _text.Clear();

    private string Stamped(string text, string stamp)
    {
        var stamped = new StringBuilder();
        bool atLineStart = _atLineStart;
        foreach (char c in text)
        {
            if (atLineStart && c != '\n')
            {
                stamped.Append(stamp);
                atLineStart = false;
            }

            stamped.Append(c);
            atLineStart |= c == '\n';
        }

        return stamped.ToString();
    }

    private bool Trim()
    {
        if (_text.Length <= limit)
        {
            return false;
        }

        string all = _text.ToString();
        int cut = all.Length - (limit / 4 * 3);
        int lineEnd = all.IndexOf('\n', cut);
        _text.Remove(0, lineEnd >= 0 && lineEnd + 1 < all.Length ? lineEnd + 1 : cut);
        return true;
    }
}
