using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TravelPlanner.Core.Services;

namespace TravelPlanner.Core.ViewModels;

public partial class HomeViewModel(ISessionService session) : ObservableObject
{
    public string WelcomeMessage => $"Welcome, {session.User?.DisplayName ?? session.User?.Email ?? "traveler"}";
    public string? Email => session.User?.Email;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SignOutCommand))]
    private bool isBusy;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    private bool CanSignOut() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSignOut))]
    private async Task SignOutAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        try { await session.SignOutAsync(); }
        catch (AuthException ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}
