using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TravelPlanner.Core.Dtos;
using TravelPlanner.Core.Services;

namespace TravelPlanner.Core.ViewModels;

public partial class LoginViewModel(ISessionService session) : ObservableObject
{
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string email = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string password = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(LoginCommand)), NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool isBusy;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;
    public bool IsNotBusy => !IsBusy;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    private bool CanLogin() => !IsBusy && !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrEmpty(Password);

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return;
        ErrorMessage = null;
        var request = new LoginRequest { Email = Email.Trim(), Password = Password };
        if (!Validator.TryValidateObject(request, new ValidationContext(request), null, validateAllProperties: true))
        {
            ErrorMessage = "Enter a valid email address and password.";
            return;
        }
        IsBusy = true;
        try { await session.SignInAsync(request, cancellationToken); Password = ""; }
        catch (AuthException ex) { ErrorMessage = ex.Message; }
        catch (HttpRequestException) { ErrorMessage = "Cannot reach TravelPlanner. Check your connection and try again."; }
        catch (OperationCanceledException) { ErrorMessage = "Sign-in timed out or was cancelled. Please try again."; }
        finally { IsBusy = false; }
    }
}
