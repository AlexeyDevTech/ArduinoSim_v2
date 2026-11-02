using System.Windows;
using ArduSim.App.ViewModels;

namespace ArduSim.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
