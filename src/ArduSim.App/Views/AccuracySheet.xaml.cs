using System.Windows;
using System.Windows.Media;
using ArduSim.App.Localization;

namespace ArduSim.App.Views;

/// <summary>"О точности симуляции": a modal side sheet over the right edge of the owner (docs/UI.md §2.2).</summary>
public partial class AccuracySheet : Window
{
    /// <summary>The limits, <c>Accuracy_N_Title</c> / <c>Accuracy_N_Body</c> in the strings, in this order.</summary>
    private const int Limits = 10;

    private AccuracySheet()
    {
        InitializeComponent();
        Items.ItemsSource = Enumerable.Range(1, Limits)
            .Select(i => new Limit(Strings.Get($"Accuracy_{i}_Title"), Strings.Get($"Accuracy_{i}_Body")))
            .ToList();
    }

    public static void Open(Window owner)
    {
        var sheet = new AccuracySheet { Owner = owner };
        sheet.PlaceOver(owner);
        sheet.ShowDialog();
    }

    private void PlaceOver(Window owner)
    {
        // The owner's client area in device-independent screen units, below its 48 px title bar.
        const double TitleBar = 48;
        Point corner = owner.PointToScreen(new Point(0, 0));
        if (PresentationSource.FromVisual(owner)?.CompositionTarget is { } target)
        {
            corner = target.TransformFromDevice.Transform(corner);
        }

        double width = Math.Min(Width, owner.ActualWidth);
        Width = width;
        Left = corner.X + owner.ActualWidth - width;
        Top = corner.Y + TitleBar;
        Height = Math.Max(200, owner.ActualHeight - TitleBar);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private sealed record Limit(string Title, string Body);
}
