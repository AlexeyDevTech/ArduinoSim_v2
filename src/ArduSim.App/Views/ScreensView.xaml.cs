using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ArduSim.App.ViewModels;
using ArduSim.Core.Screens;

namespace ArduSim.App.Views;

/// <summary>The Экраны tab (docs/UI.md, ADR-019): the map and the screen. Undo is the window's Ctrl+Z, which follows this tab.</summary>
public partial class ScreensView : UserControl
{
    private Point? _pressedAt;
    private string? _editing;

    public ScreensView()
    {
        InitializeComponent();
        Face.EditTextRequested += OnEditText;
    }

    private void OnFit(object sender, RoutedEventArgs e) => Map.Fit();

    // The toolbar's element buttons: a click adds at the next free place, a drag where it is dropped.
    private void OnPalettePress(object sender, MouseButtonEventArgs e) => _pressedAt = e.GetPosition(this);

    private void OnPaletteMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pressedAt is not { } start || sender is not FrameworkElement { Tag: string kind } button)
        {
            return;
        }

        Vector moved = e.GetPosition(this) - start;
        if (Math.Abs(moved.X) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(moved.Y) >= SystemParameters.MinimumVerticalDragDistance)
        {
            _pressedAt = null;
            DragDrop.DoDragDrop(button, new DataObject(ScreenFaceView.ElementFormat, kind), DragDropEffects.Copy);
        }
    }

    // Text is edited where it is: a box over the element, at the display's own scale.
    private void OnEditText(ScreenElement element, Rect at)
    {
        _editing = element.Id;
        InlineEditor.Text = element.Text;
        Point origin = Face.TranslatePoint(new Point(at.X, at.Y), (UIElement)InlineEditor.Parent);
        Canvas.SetLeft(InlineEditor, origin.X - 2);
        Canvas.SetTop(InlineEditor, origin.Y - 2);
        InlineEditor.MinWidth = Math.Max(at.Width + 16, 80);
        InlineEditor.Height = Math.Max(at.Height + 4, 26);
        InlineEditor.FontSize = Math.Clamp(at.Height * 0.6, 12, 40);
        InlineEditor.Visibility = Visibility.Visible;
        InlineEditor.Focus();
        InlineEditor.SelectAll();
    }

    private void FinishEditing(bool keep)
    {
        if (_editing is not { } id)
        {
            return;
        }

        _editing = null;
        string text = InlineEditor.Text;
        InlineEditor.Visibility = Visibility.Collapsed;
        if (keep && DataContext is ScreensViewModel model)
        {
            model.SetElementText(id, text);
        }

        Face.Focus();
    }

    private void OnInlineKey(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape)
        {
            FinishEditing(keep: e.Key == Key.Enter);
            e.Handled = true;
        }
    }

    private void OnInlineLostFocus(object sender, KeyboardFocusChangedEventArgs e) => FinishEditing(keep: true);
}
