using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ArduSim.App.ViewModels;

namespace ArduSim.App;

/// <summary>View glue only: mouse and keyboard gestures on parts become view-model calls.</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _closing;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closing)
        {
            return;
        }

        // Stop the Runner before the window goes away; then close for real.
        e.Cancel = true;
        _closing = true;
        await _viewModel.DisposeAsync();
        Close();
    }

    private static PartViewModel? Part(object sender) => (sender as FrameworkElement)?.DataContext as PartViewModel;

    private void OnPartPress(object sender, MouseButtonEventArgs e)
    {
        Part(sender)?.Press();
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void OnPartRelease(object sender, MouseEventArgs e)
    {
        if (e is MouseButtonEventArgs || Mouse.Captured != sender)
        {
            ((UIElement)sender).ReleaseMouseCapture();
            Part(sender)?.Release();
        }
    }

    private void OnPartToggle(object sender, MouseButtonEventArgs e) => Part(sender)?.Toggle();

    private void OnKnobWheel(object sender, MouseWheelEventArgs e)
    {
        if (Part(sender) is { } part)
        {
            part.Value = Math.Clamp(part.Value + (e.Delta > 0 ? 0.02 : -0.02), 0, 1);
            e.Handled = true;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is not TextBox && !e.IsRepeat && _viewModel.OnKey(e.Key.ToString(), down: true))
        {
            e.Handled = true;
        }
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is not TextBox && _viewModel.OnKey(e.Key.ToString(), down: false))
        {
            e.Handled = true;
        }
    }

    private void OnSerialInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _viewModel.SendSerialCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnSerialTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_viewModel.AutoScroll)
        {
            SerialOutput.ScrollToEnd();
        }
    }
}
