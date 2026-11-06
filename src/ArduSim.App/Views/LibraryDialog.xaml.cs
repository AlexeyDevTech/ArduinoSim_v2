using System.ComponentModel;
using System.Windows;
using ArduSim.App.Localization;
using ArduSim.App.ViewModels;

namespace ArduSim.App.Views;

/// <summary>Библиотеки (docs/UI.md §2.9, ADR-018): the catalog's search, and adding from a folder or a ZIP.</summary>
public partial class LibraryDialog : Window
{
    private readonly LibraryDialogViewModel _viewModel;

    private LibraryDialog(LibraryDialogViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.PickFolder = () =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Strings.Get("Library_AddFolderTitle") };
            return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
        };
        viewModel.PickZip = () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = Strings.Get("Library_AddZipTitle"), Filter = Strings.Get("Library_AddZipFilter") };
            return dialog.ShowDialog(this) == true ? dialog.FileName : null;
        };
    }

    public static void Open(Window owner, LibraryDialogViewModel viewModel)
    {
        var dialog = new LibraryDialog(viewModel) { Owner = owner };
        dialog.ShowDialog();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.CaretIndex = SearchBox.Text.Length;
        await _viewModel.LoadAsync();
    }

    private void OnClosing(object? sender, CancelEventArgs e) => _viewModel.CancelWork();

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
