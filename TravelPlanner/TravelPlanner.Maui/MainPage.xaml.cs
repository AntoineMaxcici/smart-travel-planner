using TravelPlanner.Core.ViewModels;

namespace TravelPlanner.Maui;

public partial class MainPage : ContentPage
{
    public MainPage(HomeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
