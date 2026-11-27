using TravelPlanner.Core.ViewModels;

namespace TravelPlanner.Maui.Views;

public partial class NewTripPage : ContentPage
{
    private readonly NewTripViewModel viewModel;
    private bool introPlayed;

    public NewTripPage(NewTripViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (viewModel.LoadInterestsCommand.CanExecute(null)) viewModel.LoadInterestsCommand.Execute(null);
        if (introPlayed) return;
        introPlayed = true;
        Form.Opacity = 0;
        Form.TranslationY = 32;
        await Task.WhenAll(Form.FadeTo(1, 450, Easing.CubicOut), Form.TranslateTo(0, 0, 450, Easing.CubicOut));
    }
}
