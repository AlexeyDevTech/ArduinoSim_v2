using System.Windows;
using System.Windows.Threading;

namespace ArduSim.App.Views;

/// <summary>
/// Scrolls an element into view when the bound value turns true, so that the open file's row can be
/// found in a long list. A row that is only being created does not count: rebuilding the list must
/// not pull the view away from where the user has scrolled to.
/// </summary>
public static class Reveal
{
    public static readonly DependencyProperty WhenProperty = DependencyProperty.RegisterAttached(
        "When",
        typeof(bool),
        typeof(Reveal),
        new PropertyMetadata(false, OnWhenChanged));

    public static bool GetWhen(DependencyObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return (bool)target.GetValue(WhenProperty);
    }

    public static void SetWhen(DependencyObject target, bool value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(WhenProperty, value);
    }

    private static void OnWhenChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && target is FrameworkElement { IsLoaded: true } element)
        {
            // After layout: a folder that has just unfolded for this row has to be measured first.
            _ = element.Dispatcher.BeginInvoke(element.BringIntoView, DispatcherPriority.Background);
        }
    }
}
