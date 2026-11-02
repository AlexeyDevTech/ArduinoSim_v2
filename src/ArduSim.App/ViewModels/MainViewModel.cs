using ArduSim.App.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArduSim.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Status { get; set; } = Strings.Get("Status_Skeleton");
}
