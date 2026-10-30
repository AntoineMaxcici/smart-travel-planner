using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TravelPlanner.Core.Dtos;
using TravelPlanner.Core.Services;

namespace TravelPlanner.Core.ViewModels;

public partial class RegisterViewModel(IAuthApi api) : ObservableObject
{
    public const string PasswordHint =
        "At least 8 characters, with an uppercase letter, a lowercase letter, a number and a symbol.";

    [ObservableProperty]
    private string displayName = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
    private string email = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
    private string password = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
    private string confirmPassword = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(RegisterCommand)), NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool isBusy;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;
    public bool IsNotBusy => !IsBusy;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public string PasswordRequirements => PasswordHint;

    /// <summary>Raised when the user wants to go back to the sign-in page. The UI layer handles navigation.</summary>
    public event EventHandler? LoginRequested;
    /// <summary>Raised with the email address after the account is created; the user must now enter the emailed code.</summary>
    public event EventHandler<string>? ConfirmationRequired;

    private bool CanRegister() => !IsBusy && !string.IsNullOrWhiteSpace(Email)
        && !string.IsNullOrEmpty(Password) && !string.IsNullOrEmpty(ConfirmPassword);

    [RelayCommand(CanExecute = nameof(CanRegister))]
    private async Task RegisterAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return;
        ErrorMessage = null;
        var request = new RegisterRequest
        {
            Email = Email.Trim(),
            Password = Password,
            DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName.Trim()
        };
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true))
        {
            ErrorMessage = results.Any(r => r.MemberNames.Contains(nameof(RegisterRequest.Email)))
                ? "Enter a valid email address."
                : results.Any(r => r.MemberNames.Contains(nameof(RegisterRequest.Password)))
                    ? $"Password must be at least {RegisterRequest.MinimumPasswordLength} characters."
                    : "Your name must be 100 characters or fewer.";
            return;
        }
        if (Password != ConfirmPassword)
        {
            ErrorMessage = "Passwords do not match.";
            return;
        }
        IsBusy = true;
        try
        {
            // The server enforces the full password policy and returns a readable message if it fails.
            var response = await api.RegisterAsync(request, cancellationToken);
            Password = "";
            ConfirmPassword = "";
            ConfirmationRequired?.Invoke(this, response.Email);
        }
        catch (AuthException ex) { ErrorMessage = ex.Message; }
        catch (HttpRequestException) { ErrorMessage = "Cannot reach TravelPlanner. Check your connection and try again."; }
        catch (OperationCanceledException) { ErrorMessage = "Registration timed out or was cancelled. Please try again."; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void GoToLogin()
    {
        if (IsBusy) return;
        LoginRequested?.Invoke(this, EventArgs.Empty);
    }
}
