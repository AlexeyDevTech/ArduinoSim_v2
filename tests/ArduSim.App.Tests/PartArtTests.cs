using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;
using ArduSim.App.ViewModels;
using ArduSim.App.Views;
using ArduSim.Boards;
using ArduSim.Core.Components;
using ArduSim.Core.Editing;
using ArduSim.Core.Projects;
using Xunit;

namespace ArduSim.App.Tests;

/// <summary>
/// Keeps the part drawings in Themes/Parts.xaml and the parts' definitions together: names where the pins
/// are, leads that end on pins, text readable however a part is placed. Run against every layout a part has.
/// </summary>
public sealed class PartArtTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace ViewsNamespace = "clr-namespace:ArduSim.App.Views";
    private static readonly XElement Art = XDocument.Load(Path.Combine(RepositoryRoot(), "src", "ArduSim.App", "Themes", "Parts.xaml")).Root!;
    private static readonly ComponentRegistry Registry = ComponentRegistry.CreateDefault(BoardCatalog.All);
    private static readonly Typeface Face = new("Segoe UI");

    // Two like leads, or marks of its own in the art: a name on each pin would say nothing.
    private static readonly string[] Unnamed = ["pushbutton", "resistor", "led"];

    private static readonly int[] LedCounts = [1, 2, 8, 16, 24, 60, 144];

    public static TheoryData<double> LabelSizes => new() { 4.2, 5.4 };

    [Theory]
    [MemberData(nameof(LabelSizes))]
    public void Names_stay_clear_of_each_other_and_of_the_pins_and_inside_the_part(double size)
    {
        var problems = new List<string>();
        foreach (ComponentDefinition part in Layouts())
        {
            if (Labels(part) is not { } labels)
            {
                continue;
            }

            PinViewModel[] pins = Pins(part);
            double inset = labels.Attribute("Inset") is { } value ? double.Parse(value.Value, CultureInfo.InvariantCulture) : PinLabels.DefaultInset;
            IReadOnlyList<PlacedPinLabel> placed = PinLabelLayout.Place(pins, part.Width, part.Height, inset, size, Face.CapsHeight, text => Width(text, size));
            string name = $"{part.Type} {part.Width}x{part.Height}";
            for (int i = 0; i < placed.Count; i++)
            {
                PlacedPinLabel a = placed[i];
                if (a.Left < -0.01 || a.Top < -0.01 || a.Left + a.Width > part.Width + 0.01 || a.Top + a.Height > part.Height + 0.01)
                {
                    problems.Add($"{name}: {a.Text} leaves the part");
                }

                if (a.Fit < 0.55)
                {
                    problems.Add($"{name}: {a.Text} is squeezed to {a.Fit:0.00}");
                }

                for (int j = i + 1; j < placed.Count; j++)
                {
                    PlacedPinLabel b = placed[j];
                    if (a.Left < b.Left + b.Width && b.Left < a.Left + a.Width && a.Top < b.Top + b.Height && b.Top < a.Top + a.Height)
                    {
                        problems.Add($"{name}: {a.Text} ({pins[i].Name}) overlaps {b.Text} ({pins[j].Name})");
                    }
                }

                foreach (PinViewModel pin in pins)
                {
                    double dx = Math.Max(Math.Max(a.Left - pin.X, pin.X - (a.Left + a.Width)), 0);
                    double dy = Math.Max(Math.Max(a.Top - pin.Y, pin.Y - (a.Top + a.Height)), 0);
                    if (Math.Sqrt((dx * dx) + (dy * dy)) < 3)
                    {
                        problems.Add($"{name}: {a.Text} ({pins[i].Name}) lies on pin {pin.Name}");
                    }
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Each_name_is_centered_on_its_pin_in_a_row()
    {
        ComponentDefinition keypad = Registry.Find("keypad")!;
        PinViewModel[] pins = Pins(keypad);
        IReadOnlyList<PlacedPinLabel> placed = PinLabelLayout.Place(pins, keypad.Width, keypad.Height, 11.5, 5.4, Face.CapsHeight, text => Width(text, 5.4));
        for (int i = 0; i < pins.Length; i++)
        {
            Assert.Equal(pins[i].Label, placed[i].Text);
            Assert.Equal(pins[i].X, placed[i].Left + (placed[i].Width / 2), 3);
        }
    }

    [Fact]
    public void Every_part_with_pins_shows_their_names_unless_they_are_alike()
    {
        var missing = new List<string>();
        foreach (ComponentDefinition part in Layouts().Where(p => p.Pins.Count > 0))
        {
            bool named = Labels(part) is not null;
            if (named == Unnamed.Contains(part.Type))
            {
                missing.Add(part.Type + (named ? " should be off the list of unnamed parts" : " has no PinLabels"));
            }
        }

        Assert.True(missing.Count == 0, string.Join("\n", missing));
    }

    [Fact]
    public void No_part_writes_its_pin_names_by_hand()
    {
        var problems = new List<string>();
        foreach (ComponentDefinition part in Layouts())
        {
            HashSet<string> names = new(Pins(part).Select(p => p.Label), StringComparer.OrdinalIgnoreCase);
            foreach (XElement text in Template(part).Descendants(Wpf + "TextBlock"))
            {
                string[] words = (text.Attribute("Text")?.Value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Count(names.Contains) >= 2)
                {
                    problems.Add($"{part.Type}: \"{text.Attribute("Text")!.Value}\"");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Leads_end_on_pins_and_every_pin_without_a_ring_has_one()
    {
        var problems = new List<string>();
        foreach (ComponentDefinition part in Layouts().Where(p => p.Layout is null))
        {
            XElement canvas = Template(part).Element(Wpf + "Canvas") ?? new XElement("none");
            (double X1, double Y1, double X2, double Y2)[] leads =
            [
                .. canvas.Elements(Wpf + "Line").Select(line => (Number(line, "X1"), Number(line, "Y1"), Number(line, "X2"), Number(line, "Y2"))),
            ];
            foreach ((double x1, double y1, double x2, double y2) in leads)
            {
                if (!part.Pins.Any(p => (p.X == x1 && p.Y == y1) || (p.X == x2 && p.Y == y2)))
                {
                    problems.Add($"{part.Type}: the lead {x1},{y1} to {x2},{y2} ends on no pin");
                }
            }

            if (Labels(part)?.Attribute("Rings")?.Value == "True" || part.Category == "board")
            {
                continue;
            }

            foreach (PinDefinition pin in part.Pins)
            {
                if (!leads.Any(l => (l.X1 == pin.X && l.Y1 == pin.Y) || (l.X2 == pin.X && l.Y2 == pin.Y)))
                {
                    problems.Add($"{part.Type}: pin {pin.Name} at {pin.X},{pin.Y} has no lead");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void A_drawing_of_a_fixed_size_is_the_size_of_its_part()
    {
        var problems = new List<string>();
        foreach (ComponentDefinition part in Layouts().Where(p => p.Layout is null))
        {
            XElement? canvas = Template(part).Element(Wpf + "Canvas");
            if (canvas?.Attribute("Width") is { } width && double.TryParse(width.Value, CultureInfo.InvariantCulture, out double w)
                && (w != part.Width || Number(canvas, "Height") != part.Height))
            {
                problems.Add($"{part.Type}: drawn {w}x{Number(canvas, "Height")}, defined {part.Width}x{part.Height}");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void The_relay_contact_rests_on_NC_and_moves_to_NO()
    {
        ComponentDefinition relay = Registry.Find("relay")!;
        XElement template = Template(relay);
        XElement contact = template.Descendants(Wpf + "Line").Single(l => l.Attribute(Xaml + "Name")?.Value == "Contact");
        double left = Number(contact.Parent!, "Canvas.Left");
        XElement energized = template.Descendants(Wpf + "Setter").Single(s => s.Attribute("TargetName")?.Value == "Contact");

        Assert.Equal(relay.FindPin("COM")!.X, left + Number(contact, "X1"));
        Assert.Equal(relay.FindPin("NC")!.X, left + Number(contact, "X2"));
        Assert.Equal(relay.FindPin("NO")!.X, left + Number(energized, "Value"));
    }

    [Fact]
    public void Text_on_a_part_is_never_mirrored_or_upside_down()
    {
        foreach (bool flipped in new[] { false, true })
        {
            foreach (double rotation in new double[] { 0, 90, 180, 270 })
            {
                // What the canvas does to a part (flip, then turn) over what the text does to itself.
                Matrix net = PartText.UprightFor(flipped, rotation).Value;
                net.Append(new ScaleTransform(flipped ? -1 : 1, 1).Value);
                net.Append(new RotateTransform(rotation).Value);
                Vector reading = net.Transform(new Vector(1, 0));

                Assert.True(net.Determinant > 0, $"mirrored at flip {flipped}, {rotation}");
                Assert.True(reading.X > 0.5 || reading.Y > 0.5, $"upside down at flip {flipped}, {rotation}: {reading}");
            }
        }
    }

    [Theory]
    [InlineData("GND", null, "GND")]
    [InlineData("GND.2", null, "GND")]
    [InlineData("GND.5", null, "GND")]
    [InlineData("3V3.3", null, "3V3")]
    [InlineData("1", "+", "+")]
    public void A_pin_is_called_by_its_caption_or_its_name_without_the_twin_number(string name, string? caption, string expected)
    {
        Assert.Equal(expected, new PinViewModel(name, 0, 0, caption).Label);
    }

    [Fact]
    public void Parts_of_the_examples_do_not_overlap_after_their_drawings_changed_size()
    {
        string examples = Path.Combine(RepositoryRoot(), "examples");
        var problems = new List<string>();
        foreach (string folder in Directory.EnumerateDirectories(examples))
        {
            var scene = SceneModel.FromDocument(ProjectFile.Load(folder).Scene);
            var editor = new SceneEditor(scene, Registry);
            (string Id, Rect Box)[] parts = [.. scene.Parts.Select(part => (part.Id, Box(part, editor.Definition(part)!)))];
            for (int i = 0; i < parts.Length; i++)
            {
                for (int j = i + 1; j < parts.Length; j++)
                {
                    Rect shared = Rect.Intersect(parts[i].Box, parts[j].Box);
                    if (!shared.IsEmpty && shared.Width > 0 && shared.Height > 0)
                    {
                        problems.Add($"{Path.GetFileName(folder)}: {parts[i].Id} and {parts[j].Id}");
                    }
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    // A part turns about its center.
    private static Rect Box(PartModel part, ComponentDefinition definition)
    {
        bool sideways = part.Rotation is 90 or 270;
        double width = sideways ? definition.Height : definition.Width;
        double height = sideways ? definition.Width : definition.Height;
        return new Rect(part.X + ((definition.Width - width) / 2), part.Y + ((definition.Height - height) / 2), width, height);
    }

    // A part's definition for each layout it can have: as placed from the palette, and every choice that changes its size.
    private static IEnumerable<ComponentDefinition> Layouts()
    {
        var seen = new HashSet<ComponentDefinition>(ReferenceEqualityComparer.Instance);
        foreach (ComponentDefinition definition in Registry.Definitions)
        {
            var values = new List<Dictionary<string, JsonElement>> { new() };
            values.AddRange(definition.Presets.Select(definition.PresetValues));
            if (definition.Layout is not null)
            {
                foreach (PropertyDefinition property in definition.Properties.Where(p => p.Kind == PropertyKind.Choice))
                {
                    values.AddRange((property.Options ?? []).Select(option => new Dictionary<string, JsonElement> { [property.Name] = JsonSerializer.SerializeToElement(option) }));
                }
            }

            if (definition.Type == "ws2812")
            {
                foreach (string layout in new[] { "strip", "ring" })
                {
                    values.AddRange(LedCounts.Select(count => new Dictionary<string, JsonElement>
                    {
                        ["layout"] = JsonSerializer.SerializeToElement(layout),
                        ["count"] = JsonSerializer.SerializeToElement(count),
                    }));
                }
            }

            foreach (Dictionary<string, JsonElement> value in values)
            {
                ComponentDefinition resolved = definition.Resolve(value);
                if (seen.Add(resolved))
                {
                    yield return resolved;
                }
            }
        }
    }

    private static PinViewModel[] Pins(ComponentDefinition part) => [.. part.Pins.Select(p => new PinViewModel(p.Name, p.X, p.Y, p.Label))];

    // The part's template, or the shared one it shows.
    private static XElement Template(ComponentDefinition part)
    {
        string key = part.Category == "board"
            ? (Find("Part.board." + part.Type) is null ? "Part.board" : "Part.board." + part.Type)
            : "Part." + part.Type;
        XElement template = Find(key) ?? throw new InvalidOperationException("No template " + key);
        if (template.Element(Wpf + "ContentPresenter")?.Attribute("ContentTemplate")?.Value is { } shared)
        {
            return Find(shared.Replace("{StaticResource ", "", StringComparison.Ordinal).TrimEnd('}')) ?? throw new InvalidOperationException("No template " + shared);
        }

        return template;
    }

    private static XElement? Find(string key) =>
        Art.Elements(Wpf + "DataTemplate").FirstOrDefault(t => t.Attribute(Xaml + "Key")?.Value == key);

    private static XElement? Labels(ComponentDefinition part) => Template(part).Descendants(ViewsNamespace + "PinLabels").FirstOrDefault();

    private static double Number(XElement element, string attribute) =>
        double.Parse(element.Attribute(attribute)?.Value ?? throw new InvalidOperationException($"{element.Name} has no {attribute}"), CultureInfo.InvariantCulture);

    private static double Width(string text, double size) =>
        new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, Brushes.White, 1).WidthIncludingTrailingWhitespace;

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ArduSim.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("ArduSim.sln not found above " + AppContext.BaseDirectory);
    }
}
