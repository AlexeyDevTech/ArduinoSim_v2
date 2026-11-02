using System.Text;

namespace ArduSim.Build.Compilation;

/// <summary>Reads the Makefile-style dependency files clang writes with <c>-MD -MF</c>.</summary>
public static class DependencyFile
{
    public static IReadOnlyList<string> Parse(string text)
    {
        string joined = text.Replace("\\\r\n", " ", StringComparison.Ordinal).Replace("\\\n", " ", StringComparison.Ordinal);
        int colon = joined.IndexOf(": ", StringComparison.Ordinal);
        if (colon < 0)
        {
            return [];
        }

        var files = new List<string>();
        var current = new StringBuilder();
        string body = joined[(colon + 2)..];
        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];
            if (c == '\\' && i + 1 < body.Length && body[i + 1] is ' ' or '#')
            {
                current.Append(body[++i]);
            }
            else if (c == '$' && i + 1 < body.Length && body[i + 1] == '$')
            {
                current.Append('$');
                i++;
            }
            else if (char.IsWhiteSpace(c))
            {
                Flush();
            }
            else
            {
                current.Append(c);
            }
        }

        Flush();
        return files;

        void Flush()
        {
            if (current.Length > 0)
            {
                files.Add(current.ToString());
                current.Clear();
            }
        }
    }
}
