using TravelPlanner.Core.ViewModels;

namespace TravelPlanner.Maui.Views;

public partial class MyTripsPage : ContentPage
{
    private readonly MyTripsViewModel viewModel;
    private bool introPlayed;

    public MyTripsPage(MyTripsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }

    // Reloads every time the page appears, so a trip created on the New Trip page shows up on return.
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (viewModel.LoadCommand.CanExecute(null)) viewModel.LoadCommand.Execute(null);
        if (introPlayed) return;
        introPlayed = true;
        Hero.Opacity = 0;
        Hero.TranslationY = -24;
        NewTripButton.Scale = 0;
        await Task.WhenAll(Hero.FadeTo(1, 450, Easing.CubicOut), Hero.TranslateTo(0, 0, 450, Easing.CubicOut));
        await NewTripButton.ScaleTo(1, 380, Easing.SpringOut);
    }

    // The template's tour-card hover: the card lifts 10px and its photo zooms to 105%.
    private void OnCardPointerEntered(object? sender, PointerEventArgs e) => AnimateCard(sender, hovered: true);
    private void OnCardPointerExited(object? sender, PointerEventArgs e) => AnimateCard(sender, hovered: false);

    private static void AnimateCard(object? sender, bool hovered)
    {
        if (sender is not VisualElement card) return;
        // Only stop the previous lift; cancelling everything would also freeze the entrance fade.
        card.AbortAnimation("TranslateTo");
        _ = card.TranslateTo(0, hovered ? -10 : 0, 300, Easing.CubicOut);
        if (card is Element element && element.FindByName<Image>("CardPhoto") is { } photo)
        {
            photo.AbortAnimation("ScaleTo");
            _ = photo.ScaleTo(hovered ? 1.05 : 1, 450, Easing.CubicOut);
        }
    }

    // Cards rise and fade in as they are first shown.
    private async void OnCardLoaded(object? sender, EventArgs e)
    {
        if (sender is not VisualElement card) return;
        card.Opacity = 0;
        card.TranslationY = 28;
        await Task.WhenAll(card.FadeTo(1, 420, Easing.CubicOut), card.TranslateTo(0, 0, 420, Easing.CubicOut));
    }
}
