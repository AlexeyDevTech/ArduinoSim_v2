using System.Globalization;

namespace ArduSim.Core.Screens;

/// <summary>Every edit of a generated project's model, with undo: UI-free, as <c>SceneEditor</c> is for the schematic (ADR-019).</summary>
public sealed class ScreenEditor(ScreenDocument document)
{
    private const int UndoLimit = 200;

    private readonly Stack<ScreenDocument> _undo = new();
    private readonly Stack<ScreenDocument> _redo = new();

    public event Action? Changed;

    public ScreenDocument Document { get; private set; } = document;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>A new project's screens: one, showing the project's name.</summary>
    public static ScreenDocument Starting(ScreenDisplay display, string title, string screenTitle = "Home")
    {
        ArgumentNullException.ThrowIfNull(display);
        return new ScreenDocument
        {
            Source = ScreenSource.Generated,
            Display = display,
            Start = "home",
            Screens =
            [
                new ScreenNode
                {
                    Id = "home",
                    Title = screenTitle,
                    Elements = [new ScreenElement { Id = "e1", Kind = ElementKind.Text, X = 0, Y = 0, Text = title }],
                },
            ],
        };
    }

    /// <summary>The hardware changed on the schematic: not an edit to undo, and nothing happens if it is the same.</summary>
    public void SetDisplay(ScreenDisplay display)
    {
        if (display != Document.Display)
        {
            Apply(Document with { Display = display }, checkpoint: false);
        }
    }

    public void Undo() => Swap(_undo, _redo);

    public void Redo() => Swap(_redo, _undo);

    private void Swap(Stack<ScreenDocument> from, Stack<ScreenDocument> to)
    {
        if (from.TryPop(out ScreenDocument? previous))
        {
            to.Push(Document);
            Document = previous;
            Changed?.Invoke();
        }
    }

    private void Apply(ScreenDocument next, bool checkpoint = true)
    {
        if (checkpoint)
        {
            _undo.Push(Document);
            _redo.Clear();
            if (_undo.Count > UndoLimit)
            {
                ScreenDocument[] kept = [.. _undo.Take(UndoLimit)];
                _undo.Clear();
                foreach (ScreenDocument old in kept.Reverse())
                {
                    _undo.Push(old);
                }
            }
        }

        Document = next;
        Changed?.Invoke();
    }

    private void Replace(string id, Func<ScreenNode, ScreenNode> change, bool checkpoint = true) =>
        Apply(Document with { Screens = [.. Document.Screens.Select(s => s.Id == id ? change(s) : s)] }, checkpoint);

    private string FreshId(string stem)
    {
        for (int i = 1; ; i++)
        {
            string id = stem + i.ToString(CultureInfo.InvariantCulture);
            if (Document.Find(id) is null)
            {
                return id;
            }
        }
    }

    public ScreenNode AddScreen(string title, double mapX, double mapY, bool asMenu = false)
    {
        string id = FreshId(asMenu ? "menu" : "screen");
        var screen = new ScreenNode
        {
            Id = id,
            Title = title,
            MapX = mapX,
            MapY = mapY,
            Menu = asMenu ? new ScreenMenu { Items = [new ScreenMenuItem { Text = "Item 1" }], Top = Document.Display.Family == DisplayFamily.CharLcd ? 1 : 12, Rows = Math.Max(1, Document.Display.Family == DisplayFamily.CharLcd ? Document.Display.Height - 1 : (Document.Display.Height - 12) / 8) } : null,
        };
        Apply(Document with { Screens = [.. Document.Screens, screen], Start = Document.Start.Length > 0 ? Document.Start : id });
        return screen;
    }

    /// <summary>A copy of a screen beside it, with its elements and menu; transitions stay with the original.</summary>
    public ScreenNode? DuplicateScreen(string id, string title)
    {
        if (Document.Find(id) is not { } original)
        {
            return null;
        }

        var copy = original with { Id = FreshId("screen"), Title = title, Transitions = [], MapX = original.MapX + 40, MapY = original.MapY + 40 };
        Apply(Document with { Screens = [.. Document.Screens, copy] });
        return copy;
    }

    /// <summary>Removes a screen and what leads to it. The start moves to the first that is left.</summary>
    public void RemoveScreen(string id)
    {
        ScreenNode[] left = [.. Document.Screens.Where(s => s.Id != id).Select(s => s with
        {
            Transitions = [.. s.Transitions.Where(t => t.Target != id)],
            Menu = s.Menu is null ? null : s.Menu with { Items = [.. s.Menu.Items.Select(i => i.Target == id ? i with { Target = "" } : i)] },
        })];
        Apply(Document with
        {
            Screens = left,
            Start = Document.Start == id ? left.FirstOrDefault()?.Id ?? "" : Document.Start,
            FromAnywhere = [.. Document.FromAnywhere.Where(t => t.Target != id)],
        });
    }

    public void SetTitle(string id, string title) => Replace(id, s => s with { Title = title });

    public void SetStart(string id)
    {
        if (Document.Find(id) is not null)
        {
            Apply(Document with { Start = id });
        }
    }

    /// <summary>Dragging on the map: not an undo step until <paramref name="checkpoint"/>.</summary>
    public void MoveOnMap(string id, double x, double y, bool checkpoint) => Replace(id, s => s with { MapX = x, MapY = y }, checkpoint);

    public ScreenElement AddElement(string screen, ElementKind kind, int x, int y, string text = "")
    {
        ScreenNode node = Document.Find(screen) ?? throw new ArgumentException($"No screen {screen}.", nameof(screen));
        int next = node.Elements.Select(e => int.TryParse(e.Id.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
        var element = new ScreenElement
        {
            Id = "e" + next.ToString(CultureInfo.InvariantCulture),
            Kind = kind,
            X = x,
            Y = y,
            Text = text,
            // A line's width and height are the far end's coordinates.
            Width = kind switch
            {
                ElementKind.Value => 5,
                ElementKind.Text => 0,
                ElementKind.Line => x + 20,
                _ => 20,
            },
            Height = kind switch
            {
                ElementKind.Line => y,
                ElementKind.Frame or ElementKind.Box => 10,
                _ => 0,
            },
        };
        Replace(screen, s => s with { Elements = [.. s.Elements, element] });
        return element;
    }

    /// <summary>One element changed. A drag passes <paramref name="checkpoint"/> false after its first step, so that it undoes as one.</summary>
    public void UpdateElement(string screen, string element, Func<ScreenElement, ScreenElement> change, bool checkpoint = true) =>
        Replace(screen, s => s with { Elements = [.. s.Elements.Select(e => e.Id == element ? change(e) : e)] }, checkpoint);

    public void RemoveElement(string screen, string element) =>
        Replace(screen, s => s with { Elements = [.. s.Elements.Where(e => e.Id != element)] });

    public void SetMenu(string screen, ScreenMenu? menu) => Replace(screen, s => s with { Menu = menu });

    /// <summary>A transition from a screen, or from any screen when <paramref name="from"/> is null.</summary>
    public void AddTransition(string? from, ScreenTrigger trigger, string target)
    {
        var transition = new ScreenTransition { Trigger = trigger, Target = target };
        if (from is null)
        {
            Apply(Document with { FromAnywhere = [.. Document.FromAnywhere, transition] });
        }
        else
        {
            Replace(from, s => s with { Transitions = [.. s.Transitions, transition] });
        }
    }

    public void UpdateTransition(string? from, int index, ScreenTransition transition)
    {
        if (from is null)
        {
            Apply(Document with { FromAnywhere = [.. Document.FromAnywhere.Select((t, i) => i == index ? transition : t)] });
        }
        else
        {
            Replace(from, s => s with { Transitions = [.. s.Transitions.Select((t, i) => i == index ? transition : t)] });
        }
    }

    public void RemoveTransition(string? from, int index)
    {
        if (from is null)
        {
            Apply(Document with { FromAnywhere = [.. Document.FromAnywhere.Where((_, i) => i != index)] });
        }
        else
        {
            Replace(from, s => s with { Transitions = [.. s.Transitions.Where((_, i) => i != index)] });
        }
    }
}
