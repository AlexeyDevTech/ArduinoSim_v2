namespace ArduSim.Core.Editing;

/// <summary>
/// Wires between two rows of pins that face each other, laid out together as a bundle (ADR-020), the way a
/// channel router on a circuit board does it, instead of one by one where the first ones take the good tracks.
/// Each wire goes straight out of its near pin, along a track of its own, and straight into its far pin.
/// A wire whose near pin stands right above another's far pin must turn off first. When two wires need that of
/// each other, as in a bundle wired in reverse order, one of them steps aside half a pitch right after its pin.
/// The order of the tracks is the one with the fewest crossings.
/// </summary>
internal static class WireChannel
{
    private const double HalfPitch = 5;

    /// <summary>One wire: where it starts on the near row and where it ends on the far row, along the rows.</summary>
    public readonly record struct Ends(double Near, double Far);

    /// <summary>Each wire's corners as (along the rows, across them), or null if the tracks do not fit.</summary>
    /// <param name="wires">The wires; no two share a pin.</param>
    /// <param name="first">The track nearest to the near row that is still clear of its part.</param>
    /// <param name="last">The same on the far row's side.</param>
    public static List<(double Along, double Across)>[]? Lay(IReadOnlyList<Ends> wires, double first, double last)
    {
        int count = wires.Count;
        int[] moving = [.. Enumerable.Range(0, count).Where(i => wires[i].Near != wires[i].Far)];
        var corners = new List<(double Along, double Across)>[count];
        for (int i = 0; i < count; i++)
        {
            corners[i] = [];
        }

        if (moving.Length == 0)
        {
            return corners;
        }

        // Under[a] = b: b's far pin stands right under a's near pin, so a's track comes before b's.
        int[] under = new int[count];
        Array.Fill(under, -1);
        foreach (int a in moving)
        {
            foreach (int b in moving)
            {
                if (a != b && wires[a].Near == wires[b].Far)
                {
                    under[a] = b;
                }
            }
        }

        var aside = new double?[count];
        if (!StepAside(wires, moving, under, aside))
        {
            return null;
        }

        int[] order = TrackOrder(wires, moving, under, aside);
        int[] track = new int[count];
        for (int at = 0; at < order.Length; at++)
        {
            int wire = order[at];
            for (int before = 0; before < at; before++)
            {
                int other = order[before];
                if (under[other] == wire || SpansMeet(wires, aside, other, wire))
                {
                    track[wire] = Math.Max(track[wire], track[other] + 1);
                }
            }
        }

        // The steps aside have a track of their own, before the others.
        int shift = moving.Any(i => aside[i] is not null) ? 1 : 0;
        int tracks = moving.Max(i => track[i]) + 1 + shift;
        if (Positions(first, last, tracks) is not { } positions)
        {
            return null;
        }

        foreach (int wire in moving)
        {
            double across = positions[track[wire] + shift];
            if (aside[wire] is { } column)
            {
                corners[wire].Add((wires[wire].Near, positions[0]));
                corners[wire].Add((column, positions[0]));
                corners[wire].Add((column, across));
            }
            else
            {
                corners[wire].Add((wires[wire].Near, across));
            }

            corners[wire].Add((wires[wire].Far, across));
        }

        return corners;
    }

    // Wires that need to turn off before each other go round in a circle: one of each circle steps aside, which frees its column.
    private static bool StepAside(IReadOnlyList<Ends> wires, int[] moving, int[] under, double?[] aside)
    {
        var seen = new HashSet<int>();
        foreach (int start in moving)
        {
            var path = new List<int>();
            int at = start;
            while (at >= 0 && seen.Add(at))
            {
                path.Add(at);
                at = under[at];
            }

            if (at != start || path.Count == 0)
            {
                continue;
            }

            // Leftward wires first: then they all step the same way, and no two want one column.
            int chosen = path
                .OrderBy(i => wires[i].Far < wires[i].Near ? 0 : 1)
                .ThenBy(i => wires[i].Near)
                .Where(i => IsFree(wires, aside, i))
                .DefaultIfEmpty(-1)
                .First();
            if (chosen < 0)
            {
                return false;
            }

            aside[chosen] = wires[chosen].Near + (Math.Sign(wires[chosen].Far - wires[chosen].Near) * HalfPitch);
            under[chosen] = -1;
        }

        return true;
    }

    private static bool IsFree(IReadOnlyList<Ends> wires, double?[] aside, int wire)
    {
        double column = wires[wire].Near + (Math.Sign(wires[wire].Far - wires[wire].Near) * HalfPitch);
        return Math.Abs(wires[wire].Far - wires[wire].Near) > HalfPitch
            && !wires.Any(w => w.Near == column || w.Far == column)
            && !aside.Contains(column);
    }

    private static int[] TrackOrder(IReadOnlyList<Ends> wires, int[] moving, int[] under, double?[] aside)
    {
        var waiting = moving.ToDictionary(i => i, _ => 0);
        foreach (int wire in moving)
        {
            if (under[wire] >= 0)
            {
                waiting[under[wire]]++;
            }
        }

        var left = new List<int>(moving);
        var order = new List<int>(moving.Length);
        while (left.Count > 0)
        {
            int next = left
                .Where(i => waiting[i] == 0)
                .OrderBy(i => left.Sum(other => other == i ? 0 : Crossings(wires, aside, i, other) - Crossings(wires, aside, other, i)))
                .ThenBy(i => Tidiness(wires[i]))
                .ThenBy(i => wires[i].Near)
                .First();
            order.Add(next);
            left.Remove(next);
            if (under[next] >= 0)
            {
                waiting[under[next]]--;
            }
        }

        return [.. order];
    }

    // Crossings between two wires if the first one's track comes first.
    private static int Crossings(IReadOnlyList<Ends> wires, double?[] aside, int first, int second)
    {
        (double low, double high) one = Span(wires, aside, first);
        (double low, double high) two = Span(wires, aside, second);
        double upper = aside[second] ?? wires[second].Near;
        double lower = wires[first].Far;
        return (upper > one.low && upper < one.high ? 1 : 0) + (lower > two.low && lower < two.high ? 1 : 0);
    }

    // Where nothing else decides: rightward wires first and the widest of them outermost, then leftward ones the other way round, an hourglass.
    private static double Tidiness(Ends wire)
    {
        double width = Math.Abs(wire.Far - wire.Near);
        return wire.Far > wire.Near ? -width - 1_000_000 : width;
    }

    private static (double Low, double High) Span(IReadOnlyList<Ends> wires, double?[] aside, int wire)
    {
        double upper = aside[wire] ?? wires[wire].Near;
        return (Math.Min(upper, wires[wire].Far), Math.Max(upper, wires[wire].Far));
    }

    private static bool SpansMeet(IReadOnlyList<Ends> wires, double?[] aside, int a, int b)
    {
        (double low, double high) one = Span(wires, aside, a);
        (double low, double high) two = Span(wires, aside, b);
        return one.low <= two.high && two.low <= one.high;
    }

    // The tracks: on the 0.1" grid if they fit, on the half grid otherwise, in the middle of the channel.
    private static double[]? Positions(double first, double last, int tracks)
    {
        double low = Math.Min(first, last);
        double high = Math.Max(first, last);
        int direction = last >= first ? 1 : -1;
        foreach (double pitch in (ReadOnlySpan<double>)[2 * HalfPitch, HalfPitch])
        {
            double start = Math.Ceiling(low / pitch) * pitch;
            double end = Math.Floor(high / pitch) * pitch;
            int room = (int)Math.Round((end - start) / pitch) + 1;
            if (room >= tracks)
            {
                double from = (direction > 0 ? start : end) + (direction * pitch * ((room - tracks) / 2));
                return [.. Enumerable.Range(0, tracks).Select(i => from + (direction * pitch * i))];
            }
        }

        return null;
    }
}
