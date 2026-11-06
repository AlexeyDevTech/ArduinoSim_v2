namespace ArduSim.Core.Screens;

/// <summary>What is known at a point in code because of the branch it sits in.</summary>
/// <param name="Variable">The variable compared or switched on, by its last name (<c>state.screen</c> is <c>screen</c>); empty for anything else.</param>
/// <param name="Values">What it equals here: <c>a == X || a == Y</c>, or the labels of a <c>case</c>.</param>
/// <param name="Excluded">In a <c>default:</c>, the labels it is not.</param>
/// <param name="Text">The condition as written, without spaces.</param>
internal sealed record Condition(string Variable, IReadOnlyList<CodeToken> Values, IReadOnlyList<string> Excluded, string Text)
{
    public bool IsDefault => Excluded.Count > 0;
}

/// <summary>Walks a function's statements in order: branches, switches and loops, down to simple statements.</summary>
internal abstract class BodyWalker(IReadOnlyList<CodeToken> tokens)
{
    protected IReadOnlyList<CodeToken> Tokens { get; } = tokens;

    /// <summary>The branches around the statement being walked, innermost last.</summary>
    protected List<Condition> Conditions { get; } = [];

    protected int Loops { get; private set; }

    public void Walk(int start, int end)
    {
        int known = Conditions.Count;
        int i = start;
        while (i < end && i < Tokens.Count)
        {
            i = Statement(i, end);
        }

        // What an early return ruled out holds to the end of this block, and no further.
        Conditions.RemoveRange(known, Conditions.Count - known);
    }

    /// <summary>A statement with no branches of its own, from <paramref name="from"/> up to its semicolon.</summary>
    protected abstract void Simple(int from, int to);

    /// <summary>Before an <c>if</c>'s first branch.</summary>
    protected virtual void BranchStart()
    {
    }

    /// <summary>Between an <c>if</c>'s branches.</summary>
    protected virtual void BranchElse()
    {
    }

    /// <summary>After an <c>if</c>, with or without an <c>else</c>.</summary>
    protected virtual void BranchEnd(bool hadElse)
    {
    }

    /// <summary>A <c>switch</c> whose body and labels are about to be walked.</summary>
    /// <param name="variable">Its last name.</param>
    /// <param name="expression">As written in the parentheses.</param>
    /// <param name="bodyEnd">The index of its closing brace.</param>
    /// <param name="cases">The labels and statements.</param>
    protected virtual void SwitchSeen(string variable, string expression, int bodyEnd, IReadOnlyList<(IReadOnlyList<CodeToken> Labels, bool IsDefault, int Start, int End)> cases)
    {
    }

    /// <summary>How many times to walk a <c>for</c> loop's body: once, unless a walker can tell. Always followed by <see cref="LoopDone"/>.</summary>
    /// <param name="start">The first token in its parentheses.</param>
    /// <param name="close">Its closing parenthesis.</param>
    protected virtual int LoopPasses(int start, int close) => 1;

    /// <summary>Whether to walk this case of a <c>switch</c>: all of them, unless a walker can tell.</summary>
    protected virtual bool EntersCase(string variable, int index, IReadOnlyList<(IReadOnlyList<CodeToken> Labels, bool IsDefault, int Start, int End)> cases) => true;

    /// <summary>After a case that was walked.</summary>
    protected virtual void CaseDone(int index)
    {
    }

    /// <summary>After the last case of a <c>switch</c>.</summary>
    protected virtual void SwitchDone()
    {
    }

    /// <summary>Before each walk of a <c>for</c> loop's body.</summary>
    protected virtual void LoopPass(int pass)
    {
    }

    protected virtual void LoopDone()
    {
    }

    /// <summary>What an <c>if</c>'s condition comes to, if a walker can tell: then only that branch is walked.</summary>
    protected virtual bool? Decide(int start, int end) => null;

    // Past a statement without looking inside it.
    private int Past(int i, int end)
    {
        if (i >= end)
        {
            return i;
        }

        if (Tokens[i].Is("{"))
        {
            return Math.Min(SketchScanner.Matching(Tokens, i, "{", "}"), end) + 1;
        }

        if (Tokens[i].Is("if") || Tokens[i].Is("for") || Tokens[i].Is("while") || Tokens[i].Is("switch"))
        {
            int close = i + 1 < end && Tokens[i + 1].Is("(") ? SketchScanner.Matching(Tokens, i + 1, "(", ")") : i;
            int next = Past(close + 1, end);
            return Tokens[i].Is("if") && next < end && Tokens[next].Is("else") ? Past(next + 1, end) : next;
        }

        int k = i;
        int nested = 0;
        for (; k < end; k++)
        {
            nested += Tokens[k].Is("(") || Tokens[k].Is("{") ? 1 : Tokens[k].Is(")") || Tokens[k].Is("}") ? -1 : 0;
            if (nested <= 0 && Tokens[k].Is(";"))
            {
                break;
            }
        }

        return k + 1;
    }

    protected int Statement(int i, int end)
    {
        CodeToken token = Tokens[i];
        if (token.Is("{"))
        {
            int close = Math.Min(SketchScanner.Matching(Tokens, i, "{", "}"), end);
            Walk(i + 1, close);
            return close + 1;
        }

        if (token.Is(";") || token.Is("}"))
        {
            return i + 1;
        }

        if (token.Is("if"))
        {
            return If(i, end);
        }

        if (token.Is("switch"))
        {
            return Switch(i, end);
        }

        if (token.Is("for") || token.Is("while"))
        {
            int close = i + 1 < end && Tokens[i + 1].Is("(") ? SketchScanner.Matching(Tokens, i + 1, "(", ")") : i;
            int passes = token.Is("for") ? LoopPasses(i + 2, close) : 1;
            Loops++;
            int next = close + 1;
            for (int pass = 0; pass < passes; pass++)
            {
                if (token.Is("for"))
                {
                    LoopPass(pass);
                }

                next = close + 1 < end ? Statement(close + 1, end) : close + 1;
            }

            Loops--;
            if (token.Is("for"))
            {
                LoopDone();
            }

            return next;
        }

        if (token.Is("do"))
        {
            Loops++;
            int next = i + 1 < end ? Statement(i + 1, end) : i + 1;
            Loops--;
            if (next < end && Tokens[next].Is("while") && next + 1 < end)
            {
                next = SketchScanner.Matching(Tokens, next + 1, "(", ")") + 1;
            }

            return next;
        }

        if (token.Is("else"))
        {
            return i + 1;
        }

        return Plain(i, end);
    }

    private int Plain(int i, int end)
    {
        int nested = 0;
        int k = i;
        for (; k < end; k++)
        {
            CodeToken token = Tokens[k];
            if (token.Is("(") || token.Is("[") || token.Is("{"))
            {
                nested++;
            }
            else if (token.Is(")") || token.Is("]") || token.Is("}"))
            {
                nested--;
            }
            else if (token.Is(";") && nested <= 0)
            {
                break;
            }
        }

        Simple(i, k);
        return k + 1;
    }

    private int If(int i, int end)
    {
        int open = i + 1;
        if (open >= end || !Tokens[open].Is("("))
        {
            return i + 1;
        }

        int close = Math.Min(SketchScanner.Matching(Tokens, open, "(", ")"), end);
        if (Decide(open + 1, close) is { } known)
        {
            int afterThen = known ? (close + 1 < end ? Statement(close + 1, end) : close + 1) : Past(close + 1, end);
            if (afterThen < end && Tokens[afterThen].Is("else"))
            {
                return known ? Past(afterThen + 1, end) : (afterThen + 1 < end ? Statement(afterThen + 1, end) : afterThen + 1);
            }

            return afterThen;
        }

        List<Condition> conditions = Parse(open + 1, close);
        Conditions.AddRange(conditions);
        BranchStart();
        int next = close + 1 < end ? Statement(close + 1, end) : close + 1;
        Conditions.RemoveRange(Conditions.Count - conditions.Count, conditions.Count);
        bool hadElse = next < end && Tokens[next].Is("else");
        if (hadElse)
        {
            BranchElse();
            Conditions.Add(new Condition("", [], [], "else"));
            next = next + 1 < end ? Statement(next + 1, end) : next + 1;
            Conditions.RemoveAt(Conditions.Count - 1);
        }

        BranchEnd(hadElse);

        // if (screen == A) { ...; return; }: below this, the screen is not A.
        if (!hadElse && Returns(close + 1, next))
        {
            foreach (Condition condition in conditions.Where(c => c.Variable.Length > 0 && c.Values.Count > 0))
            {
                Conditions.Add(new Condition(condition.Variable, [], [.. condition.Values.Select(v => v.Text)], "!" + condition.Text));
            }
        }

        return next;
    }

    private int Switch(int i, int end)
    {
        int open = i + 1;
        if (open >= end || !Tokens[open].Is("("))
        {
            return i + 1;
        }

        int close = SketchScanner.Matching(Tokens, open, "(", ")");
        string variable = LastName(open + 1, close);
        if (close + 1 >= end || !Tokens[close + 1].Is("{"))
        {
            return close + 1;
        }

        int bodyEnd = Math.Min(SketchScanner.Matching(Tokens, close + 1, "{", "}"), end);
        var cases = Cases(close + 2, bodyEnd);
        SwitchSeen(variable, Text(open + 1, close), bodyEnd, cases);
        string[] labels = [.. cases.SelectMany(c => c.Labels).Select(l => l.Text)];
        int index = 0;
        foreach ((IReadOnlyList<CodeToken> caseLabels, bool isDefault, int start, int stop) in cases)
        {
            if (EntersCase(variable, index, cases))
            {
                Conditions.Add(isDefault
                    ? new Condition(variable, [], labels.Length > 0 ? labels : ["?"], "default")
                    : new Condition(variable, caseLabels, [], variable + "==" + string.Join('|', caseLabels.Select(l => l.Text))));
                Walk(start, stop);
                Conditions.RemoveAt(Conditions.Count - 1);
                CaseDone(index);
            }

            index++;
        }

        SwitchDone();
        return bodyEnd + 1;
    }

    private bool Returns(int start, int end)
    {
        for (int k = end - 1; k >= start && k >= end - 3; k--)
        {
            if (Tokens[k].Is("return"))
            {
                return true;
            }
        }

        return false;
    }

    // The labels at this switch's own level, with the statements that follow each group of them.
    private List<(IReadOnlyList<CodeToken> Labels, bool IsDefault, int Start, int End)> Cases(int start, int end)
    {
        var cases = new List<(IReadOnlyList<CodeToken>, bool, int, int)>();
        List<CodeToken> labels = [];
        bool isDefault = false;
        int bodyStart = -1;
        int nested = 0;
        for (int k = start; k < end; k++)
        {
            CodeToken token = Tokens[k];
            if (token.Is("{") || token.Is("("))
            {
                nested++;
            }
            else if (token.Is("}") || token.Is(")"))
            {
                nested--;
            }
            else if (nested == 0 && (token.Is("case") || token.Is("default")))
            {
                if (bodyStart >= 0 && (labels.Count > 0 || isDefault))
                {
                    cases.Add((labels, isDefault, bodyStart, k));
                    labels = [];
                    isDefault = false;
                    bodyStart = -1;
                }

                int colon = k + 1;
                while (colon < end && !Tokens[colon].Is(":"))
                {
                    colon++;
                }

                if (token.Is("default"))
                {
                    isDefault = true;
                }
                else if (colon > k + 1)
                {
                    // Foo::BAR: the last name. A number or a character: itself.
                    labels.Add(Tokens[colon - 1]);
                }

                k = colon;
                bodyStart = colon + 1;
            }
        }

        if (bodyStart >= 0 && (labels.Count > 0 || isDefault))
        {
            cases.Add((labels, isDefault, bodyStart, end));
        }

        return cases;
    }

    /// <summary>The last name in an expression: <c>this->state.screen</c> is <c>screen</c>; a call has none of its own.</summary>
    protected string LastName(int start, int end)
    {
        if (end - start >= 1 && Tokens[end - 1].Is(")") && end - start >= 3)
        {
            return "";
        }

        for (int k = end - 1; k >= start; k--)
        {
            if (Tokens[k].Kind == TokenKind.Identifier)
            {
                return Tokens[k].Text;
            }
        }

        return "";
    }

    private List<Condition> Parse(int start, int end)
    {
        var parts = new List<(int, int)>();
        int nested = 0;
        int from = start;
        for (int k = start; k < end; k++)
        {
            nested += Tokens[k].Is("(") ? 1 : Tokens[k].Is(")") ? -1 : 0;
            if (nested == 0 && Tokens[k].Is("&&"))
            {
                parts.Add((from, k));
                from = k + 1;
            }
        }

        parts.Add((from, end));
        return [.. parts.Select(p => ParseOne(p.Item1, p.Item2))];
    }

    // a == X, X == a, a == X || a == Y; anything else is kept as text.
    private Condition ParseOne(int start, int end)
    {
        (start, end) = Unwrapped(start, end);
        string text = Text(start, end);
        var values = new List<CodeToken>();
        string variable = "";
        int from = start;
        int nested = 0;
        for (int k = start; k <= end; k++)
        {
            if (k < end)
            {
                nested += Tokens[k].Is("(") ? 1 : Tokens[k].Is(")") ? -1 : 0;
            }

            if (k == end || (nested == 0 && Tokens[k].Is("||")))
            {
                int equals = -1;
                for (int m = from; m < k; m++)
                {
                    if (Tokens[m].Is("=="))
                    {
                        equals = m;
                        break;
                    }
                }

                if (equals < 0 || !TryCompare(from, equals, k, out string name, out CodeToken value) || (variable.Length > 0 && variable != name))
                {
                    return new Condition("", [], [], text);
                }

                variable = name;
                values.Add(value);
                from = k + 1;
            }
        }

        return new Condition(variable, values, [], text);
    }

    private (int, int) Unwrapped(int start, int end)
    {
        while (end - start >= 2 && Tokens[start].Is("(") && SketchScanner.Matching(Tokens, start, "(", ")") == end - 1)
        {
            start++;
            end--;
        }

        return (start, end);
    }

    private bool TryCompare(int from, int equals, int to, out string variable, out CodeToken value)
    {
        (int leftStart, int leftEnd) = Unwrapped(from, equals);
        (int rightStart, int rightEnd) = Unwrapped(equals + 1, to);
        if (rightEnd - rightStart == 1 && IsConstantLike(Tokens[rightStart]) && LastName(leftStart, leftEnd) is { Length: > 0 } left)
        {
            variable = left;
            value = Tokens[rightStart];
            return true;
        }

        if (leftEnd - leftStart == 1 && IsConstantLike(Tokens[leftStart]) && LastName(rightStart, rightEnd) is { Length: > 0 } right)
        {
            variable = right;
            value = Tokens[leftStart];
            return true;
        }

        variable = "";
        value = default;
        return false;
    }

    private static bool IsConstantLike(CodeToken token) => token.Kind is TokenKind.Identifier or TokenKind.Number or TokenKind.Char;

    protected string Text(int start, int end) => string.Concat(Tokens.Skip(start).Take(Math.Max(0, end - start)).Select(t => t.Text));
}
