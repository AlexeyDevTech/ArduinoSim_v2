using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using ArduSim.App.ViewModels;

namespace ArduSim.App.Views;

/// <summary>A pin's name as placed on its part, in part units: the letters' box after <see cref="Fit"/>.</summary>
/// <param name="Fit">The horizontal squeeze that keeps the name clear of its neighbours, at most 1.</param>
public readonly record struct PlacedPinLabel(string Text, double Left, double Top, double Width, double Height, double Fit);

/// <summary>
/// Where pin names go, worked out from the pins alone, so a name cannot drift from its pin (docs/UI.md §3.6).
/// A row of pins has its names inside the part, centered on each pin; a column has them beside the pins,
/// away from a twin column if it has one. Names too wide for a row are squeezed, the widest first.
/// </summary>
public static class PinLabelLayout
{
    /// <summary>Between two names of a row, and between a name and the part's edge.</summary>
    private const double Gap = 0.8;

    private enum Side
    {
        Above,
        Below,
        Before,
        After,
    }

    /// <param name="inset">From the pin to the near edge of its name.</param>
    /// <param name="capsHeight">The height of the letters, as a share of the font size.</param>
    /// <param name="measure">The width of a text at <paramref name="fontSize"/>.</param>
    public static IReadOnlyList<PlacedPinLabel> Place(
        IReadOnlyList<PinViewModel> pins,
        double width,
        double height,
        double inset,
        double fontSize,
        double capsHeight,
        Func<string, double> measure)
    {
        ArgumentNullException.ThrowIfNull(pins);
        ArgumentNullException.ThrowIfNull(measure);
        Side[] sides = [.. pins.Select(pin => SideOf(pin, pins, width, height))];
        double[] natural = [.. pins.Select(pin => measure(pin.Label))];
        double[] shown = [.. natural];

        for (int i = 0; i < pins.Count; i++)
        {
            double room = sides[i] switch
            {
                Side.After => width - pins[i].X - inset - Gap,
                Side.Before => pins[i].X - inset - Gap,
                _ => 2 * (Math.Min(pins[i].X, width - pins[i].X) - Gap),
            };
            shown[i] = room > 0 ? Math.Min(shown[i], room) : shown[i];
        }

        FitRows(pins, sides, shown);

        double letters = capsHeight * fontSize;
        var placed = new PlacedPinLabel[pins.Count];
        for (int i = 0; i < pins.Count; i++)
        {
            PinViewModel pin = pins[i];
            double left = sides[i] switch
            {
                Side.After => pin.X + inset,
                Side.Before => pin.X - inset - shown[i],
                _ => pin.X - (shown[i] / 2),
            };
            double top = sides[i] switch
            {
                Side.Above => pin.Y - inset - letters,
                Side.Below => pin.Y + inset,
                _ => pin.Y - (letters / 2),
            };
            placed[i] = new PlacedPinLabel(pin.Label, left, top, shown[i], letters, natural[i] > 0 ? shown[i] / natural[i] : 1);
        }

        return placed;
    }

    private static Side SideOf(PinViewModel pin, IReadOnlyList<PinViewModel> pins, double width, double height)
    {
        const double Edge = 0.5;
        if (pin.X <= Edge)
        {
            return Side.After;
        }

        if (pin.X >= width - Edge)
        {
            return Side.Before;
        }

        bool onRow = pin.Y <= Edge || pin.Y >= height - Edge
            || pins.Count(p => p.Y == pin.Y) >= pins.Count(p => p.X == pin.X);
        if (onRow)
        {
            return pin.Y > height / 2 ? Side.Above : Side.Below;
        }

        // A column: away from its twin on the double header, otherwise toward the nearer edge.
        PinViewModel? twin = pins.FirstOrDefault(p => p.Y == pin.Y && p != pin && Math.Abs(p.X - pin.X) <= 15);
        if (twin is not null)
        {
            return twin.X > pin.X ? Side.Before : Side.After;
        }

        return width - pin.X < pin.X ? Side.After : Side.Before;
    }

    // Neighbours on a row share the distance between their pins: a short name leaves room for a long one.
    private static void FitRows(IReadOnlyList<PinViewModel> pins, Side[] sides, double[] shown)
    {
        IEnumerable<IGrouping<(double, Side), int>> rows = Enumerable.Range(0, pins.Count)
            .Where(i => sides[i] is Side.Above or Side.Below)
            .GroupBy(i => (pins[i].Y, sides[i]));
        foreach (IGrouping<(double, Side), int> row in rows)
        {
            int[] order = [.. row.OrderBy(i => pins[i].X)];
            for (int pass = 0; pass < 4; pass++)
            {
                for (int at = 0; at + 1 < order.Length; at++)
                {
                    int a = order[at];
                    int b = order[at + 1];
                    double limit = (2 * (pins[b].X - pins[a].X)) - (2 * Gap);
                    if (shown[a] + shown[b] <= limit)
                    {
                        continue;
                    }

                    (int wider, int other) = shown[a] >= shown[b] ? (a, b) : (b, a);
                    shown[wider] = Math.Max(limit - shown[other], limit / 2);
                    shown[other] = Math.Min(shown[other], limit - shown[wider]);
                }
            }
        }
    }
}

/// <summary>
/// A part's pin names, and for a part without leads the rings its wires land on, drawn from the pins
/// themselves. Templates in Parts.xaml use this rather than writing pin names by hand.
/// </summary>
public sealed class PinLabels : FrameworkElement
{
    public static readonly DependencyProperty PinsProperty = DependencyProperty.Register(
        nameof(Pins), typeof(IEnumerable<PinViewModel>), typeof(PinLabels), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PartWidthProperty = DependencyProperty.Register(
        nameof(PartWidth), typeof(double), typeof(PinLabels), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PartHeightProperty = DependencyProperty.Register(
        nameof(PartHeight), typeof(double), typeof(PinLabels), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty InsetProperty = DependencyProperty.Register(
        nameof(Inset), typeof(double), typeof(PinLabels), new FrameworkPropertyMetadata(DefaultInset, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RingsProperty = DependencyProperty.Register(
        nameof(Rings), typeof(bool), typeof(PinLabels), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    // Their own properties, not TextElement's: those inherit, and a name would take the window's text color.
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(PinLabels), new FrameworkPropertyMetadata(DefaultForeground(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(
        nameof(FontSize), typeof(double), typeof(PinLabels), new FrameworkPropertyMetadata(4.2, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(
        typeof(PinLabels), new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UprightProperty = PartText.UprightProperty.AddOwner(
        typeof(PinLabels), new FrameworkPropertyMetadata(Transform.Identity, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The inset of a name beside a ring: the ring's radius and a little air.</summary>
    public const double DefaultInset = 4.5;

    private static readonly Brush RingFill = Frozen(new SolidColorBrush(Color.FromRgb(0x11, 0x13, 0x17)));
    private static readonly Pen RingPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0xD9, 0xB4, 0x5A))), 1.2));

    public PinLabels()
    {
        IsHitTestVisible = false;
        SetBinding(PinsProperty, new Binding(nameof(PartViewModel.Pins)));
        SetBinding(PartWidthProperty, new Binding(nameof(PartViewModel.Width)));
        SetBinding(PartHeightProperty, new Binding(nameof(PartViewModel.Height)));
        SetResourceReference(FontSizeProperty, "Font.PinLabel");
    }

    public IEnumerable<PinViewModel>? Pins
    {
        get => (IEnumerable<PinViewModel>?)GetValue(PinsProperty);
        set => SetValue(PinsProperty, value);
    }

    public double PartWidth
    {
        get => (double)GetValue(PartWidthProperty);
        set => SetValue(PartWidthProperty, value);
    }

    public double PartHeight
    {
        get => (double)GetValue(PartHeightProperty);
        set => SetValue(PartHeightProperty, value);
    }

    /// <summary>From a pin to the near edge of its name: past the lead, onto the part's body.</summary>
    public double Inset
    {
        get => (double)GetValue(InsetProperty);
        set => SetValue(InsetProperty, value);
    }

    /// <summary>Draws a ring on each pin, for parts with no leads of their own.</summary>
    public bool Rings
    {
        get => (bool)GetValue(RingsProperty);
        set => SetValue(RingsProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        PinViewModel[] pins = [.. Pins ?? []];
        if (pins.Length == 0)
        {
            return;
        }

        if (Rings)
        {
            foreach (PinViewModel pin in pins)
            {
                drawingContext.DrawEllipse(RingFill, RingPen, new Point(pin.X, pin.Y), 2.4, 2.4);
            }
        }

        var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double size = FontSize;
        FormattedText Text(string text) => new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, Foreground, dip);

        IReadOnlyList<PlacedPinLabel> placed = PinLabelLayout.Place(pins, PartWidth, PartHeight, Inset, size, typeface.CapsHeight, text => Text(text).WidthIncludingTrailingWhitespace);
        Matrix upright = ((Transform)GetValue(UprightProperty)).Value;
        foreach (PlacedPinLabel label in placed)
        {
            FormattedText text = Text(label.Text);
            double cx = label.Left + (label.Width / 2);
            double cy = label.Top + (label.Height / 2);
            Matrix matrix = Matrix.Identity;
            matrix.ScaleAt(label.Fit, 1, cx, cy);
            if (!upright.IsIdentity)
            {
                matrix.Translate(-cx, -cy);
                matrix.Append(upright);
                matrix.Translate(cx, cy);
            }

            drawingContext.PushTransform(new MatrixTransform(matrix));
            drawingContext.DrawText(text, new Point(cx - (text.WidthIncludingTrailingWhitespace / 2), label.Top + label.Height - text.Baseline));
            drawingContext.Pop();
        }
    }

    private static SolidColorBrush DefaultForeground() => Frozen(new SolidColorBrush(Color.FromRgb(0xE3, 0xEA, 0xF6)));

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

/// <summary>
/// Keeps text on a part readable however the part is placed: the canvas sets these on each part, and
/// they inherit down to its art. A part is flipped and then turned; text undoes the flip and, when the
/// part is turned past a quarter, a half turn, so it is never mirrored and never upside down.
/// </summary>
public static class PartText
{
    public static readonly DependencyProperty UprightProperty = DependencyProperty.RegisterAttached(
        "Upright", typeof(Transform), typeof(PartText), new FrameworkPropertyMetadata(Transform.Identity, FrameworkPropertyMetadataOptions.Inherits, OnUprightChanged));

    public static readonly DependencyProperty UnmirroredProperty = DependencyProperty.RegisterAttached(
        "Unmirrored", typeof(Transform), typeof(PartText), new FrameworkPropertyMetadata(Transform.Identity, FrameworkPropertyMetadataOptions.Inherits, OnUnmirroredChanged));

    /// <summary>Set by the Art.Text style: this element applies <see cref="UprightProperty"/> as its render transform.</summary>
    public static readonly DependencyProperty KeepsUprightProperty = DependencyProperty.RegisterAttached(
        "KeepsUpright", typeof(bool), typeof(PartText), new PropertyMetadata(false, OnKeepsUprightChanged));

    /// <summary>Set by the Art.Screen style: this element applies <see cref="UnmirroredProperty"/> as its render transform.</summary>
    public static readonly DependencyProperty KeepsUnmirroredProperty = DependencyProperty.RegisterAttached(
        "KeepsUnmirrored", typeof(bool), typeof(PartText), new PropertyMetadata(false, OnKeepsUnmirroredChanged));

    private static readonly Transform Flip = Frozen(new ScaleTransform(-1, 1));
    private static readonly Transform HalfTurn = Frozen(new RotateTransform(180));
    private static readonly Transform FlipAndHalfTurn = Frozen(new ScaleTransform(1, -1));

    public static Transform GetUpright(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (Transform)element.GetValue(UprightProperty);
    }

    public static void SetUpright(DependencyObject element, Transform value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(UprightProperty, value);
    }

    public static Transform GetUnmirrored(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (Transform)element.GetValue(UnmirroredProperty);
    }

    public static void SetUnmirrored(DependencyObject element, Transform value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(UnmirroredProperty, value);
    }

    public static bool GetKeepsUpright(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(KeepsUprightProperty);
    }

    public static void SetKeepsUpright(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(KeepsUprightProperty, value);
    }

    public static bool GetKeepsUnmirrored(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(KeepsUnmirroredProperty);
    }

    public static void SetKeepsUnmirrored(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(KeepsUnmirroredProperty, value);
    }

    /// <summary>What text applies on a part placed like this.</summary>
    public static Transform UprightFor(bool flipped, double rotation)
    {
        int turn = ((((int)Math.Round(rotation)) % 360) + 360) % 360;
        bool halfTurn = turn is 180 or 270;
        return (flipped, halfTurn) switch
        {
            (false, false) => Transform.Identity,
            (true, false) => Flip,
            (false, true) => HalfTurn,
            (true, true) => FlipAndHalfTurn,
        };
    }

    /// <summary>What a display's picture applies: it undoes the flip, and turns with the part as real glass does.</summary>
    public static Transform UnmirroredFor(bool flipped) => flipped ? Flip : Transform.Identity;

    private static void OnUprightChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is UIElement target && GetKeepsUpright(target))
        {
            target.RenderTransform = (Transform)e.NewValue;
        }
    }

    private static void OnUnmirroredChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is UIElement target && GetKeepsUnmirrored(target))
        {
            target.RenderTransform = (Transform)e.NewValue;
        }
    }

    private static void OnKeepsUprightChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is UIElement target && (bool)e.NewValue)
        {
            target.RenderTransform = GetUpright(target);
        }
    }

    private static void OnKeepsUnmirroredChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is UIElement target && (bool)e.NewValue)
        {
            target.RenderTransform = GetUnmirrored(target);
        }
    }

    private static Transform Frozen(Transform transform)
    {
        transform.Freeze();
        return transform;
    }
}
