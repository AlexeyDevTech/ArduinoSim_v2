using System.Windows;
using ArduSim.App.ViewModels;

namespace ArduSim.App.Views;

/// <summary>Параметры (docs/UI.md §2.2): the theme; a choice applies at once.</summary>
public partial class SettingsDialog : Window
{
    private SettingsDialog() => InitializeComponent();

    public static void Open(Window owner, MainViewModel viewModel)
    {
        var dialog = new SettingsDialog { Owner = owner, DataContext = viewModel };
        dialog.ShowDialog();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
