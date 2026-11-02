using System.Windows;
using ArduSim.App.Localization;
using ArduSim.App.ViewModels;

namespace ArduSim.App.Views;

/// <summary>"Save changes to …?" — Сохранить / Не сохранять / Отмена (docs/UI.md §4.6).</summary>
public partial class UnsavedDialog : Window
{
    private UnsavedDialog(string name)
    {
        InitializeComponent();
        Heading.Text = Strings.Format("Dialog_UnsavedTitle", name);
    }

    public UnsavedChoice Choice { get; private set; } = UnsavedChoice.Cancel;

    public static UnsavedChoice Ask(Window owner, string name)
    {
        var dialog = new UnsavedDialog(name) { Owner = owner };
        dialog.ShowDialog();
        return dialog.Choice;
    }

    private void OnSave(object sender, RoutedEventArgs e) => Finish(UnsavedChoice.Save);

    private void OnDiscard(object sender, RoutedEventArgs e) => Finish(UnsavedChoice.Discard);

    private void OnCancel(object sender, RoutedEventArgs e) => Finish(UnsavedChoice.Cancel);

    private void Finish(UnsavedChoice choice)
    {
        Choice = choice;
        DialogResult = choice != UnsavedChoice.Cancel;
    }
}
