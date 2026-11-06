using System.Windows;
using System.Windows.Controls;

namespace ArduSim.App.Views;

/// <summary>The Экраны tab (docs/UI.md, ADR-019): the map and the screen. Undo is the window's Ctrl+Z, which follows this tab.</summary>
public partial class ScreensView : UserControl
{
    public ScreensView() => InitializeComponent();

    private void OnFit(object sender, RoutedEventArgs e) => Map.Fit();
}
