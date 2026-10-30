using TravelPlanner.Core.ViewModels;

namespace TravelPlanner.Maui.Views;

public partial class ConfirmEmailPage : ContentPage
{
    public ConfirmEmailPage(ConfirmEmailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
