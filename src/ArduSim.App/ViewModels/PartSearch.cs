using System.Globalization;
using System.Text;

namespace ArduSim.App.ViewModels;

/// <summary>
/// Forgiving search for the palette and the examples gallery: case, ё and × do not matter, hyphens
/// split words, and a query in the wrong keyboard layout or with a typo still finds the part.
/// </summary>
public static class PartSearch
{
    private const string Latin = "qwertyuiop[]asdfghjkl;'zxcvbnm,.";
    private const string Cyrillic = "йцукенгшщзхъфывапролджэячсмитьбю";

    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = new StringBuilder(text.Length);
        foreach (char raw in text.ToLower(CultureInfo.CurrentCulture))
        {
            result.Append(raw switch
            {
                'ё' => 'е',
                '×' or 'х' when IsDimension(text) => 'x',
                '-' or '_' or ',' or '.' or '/' or '"' or '(' or ')' => ' ',
                _ => raw,
            });
        }

        return result.ToString();
    }

    // "8х8" typed with a Cyrillic х means 8x8; in words х stays a letter.
    private static bool IsDimension(string text) => text.Any(char.IsDigit);

    /// <summary>Which rows match: the strongest tier that finds anything wins, so a typo never buries exact hits.</summary>
    public static List<T> Filter<T>(IReadOnlyList<T> items, Func<T, string> haystack, string query)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(haystack);
        ArgumentNullException.ThrowIfNull(query);
        string[] tokens = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return [.. items];
        }

        List<(T Item, string Text, string[] Words)> rows = [.. items.Select(i =>
        {
            // A hyphen both splits and joins: hc-sr04 answers to hc, sr04 and hcsr.
            string raw = haystack(i);
            string text = Normalize(raw) + " " + Normalize(raw.Replace("-", "", StringComparison.Ordinal));
            return (i, text, text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        })];
        List<T> found = Tiers(rows, tokens);
        if (found.Count > 0)
        {
            return found;
        }

        // The query typed in the other keyboard layout.
        return Tiers(rows, [.. tokens.Select(SwapLayout)]);
    }

    private static List<T> Tiers<T>(List<(T Item, string Text, string[] Words)> rows, string[] tokens)
    {
        foreach (Func<string, string[], string, bool> tier in new Func<string, string[], string, bool>[] { WordPrefix, Substring, Stem, Fuzzy })
        {
            List<T> hits = [.. rows.Where(r => tokens.All(t => tier(t, r.Words, r.Text))).Select(r => r.Item)];
            if (hits.Count > 0)
            {
                return hits;
            }
        }

        return [];
    }

    private static bool WordPrefix(string token, string[] words, string text) => words.Any(w => w.StartsWith(token, StringComparison.Ordinal));

    private static bool Substring(string token, string[] words, string text) => text.Contains(token, StringComparison.Ordinal);

    private static bool Stem(string token, string[] words, string text)
    {
        string stem = StemOf(token);
        return words.Any(w => StemOf(w).StartsWith(stem, StringComparison.Ordinal) || (w.Length >= 5 && token.StartsWith(w, StringComparison.Ordinal)));
    }

    private static string StemOf(string word)
    {
        if (word.Length < 5)
        {
            return word;
        }

        foreach (string ending in (string[])["ами", "ями", "ов", "ей", "ая", "ые", "ый", "ой", "ы", "и", "а", "я", "у", "ю", "е"])
        {
            if (word.EndsWith(ending, StringComparison.Ordinal) && word.Length - ending.Length >= 4)
            {
                return word[..^ending.Length];
            }
        }

        return word;
    }

    private static bool Fuzzy(string token, string[] words, string text)
    {
        if (token.Length < 4)
        {
            return false;
        }

        int allowed = token.Length >= 7 ? 2 : 1;
        return words.Any(w => Math.Abs(w.Length - token.Length) <= allowed && Distance(w, token) <= allowed);
    }

    private static int Distance(string a, string b)
    {
        int[] previous = new int[b.Length + 1];
        int[] current = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    private static string SwapLayout(string token)
    {
        var swapped = new StringBuilder(token.Length);
        foreach (char c in token)
        {
            int latin = Latin.IndexOf(c, StringComparison.Ordinal);
            int cyrillic = Cyrillic.IndexOf(c, StringComparison.Ordinal);
            swapped.Append(latin >= 0 ? Cyrillic[latin] : cyrillic >= 0 ? Latin[cyrillic] : c);
        }

        return swapped.ToString();
    }
}
