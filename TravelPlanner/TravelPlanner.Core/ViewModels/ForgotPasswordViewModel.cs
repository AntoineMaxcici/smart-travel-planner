using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TravelPlanner.Core.Dtos;
using TravelPlanner.Core.Services;

namespace TravelPlanner.Core.ViewModels;

/// <summary>
/// Forgot password in two steps on one page: enter the email to get a 6-digit code, then enter the code and a new
/// password. A successful reset signs the user in.
/// </summary>
public partial class ForgotPasswordViewModel(IAuthApi api, ISessionService session) : ObservableObject
{
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SendCodeCommand)), NotifyPropertyChangedFor(nameof(Instructions))]
    private string email = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    private string code = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    private string newPassword = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    private string confirmPassword = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsEmailStep), nameof(Title), nameof(Instructions))]
    private bool isCodeStep;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsNotBusy)),
     NotifyCanExecuteChangedFor(nameof(SendCodeCommand), nameof(ResetCommand), nameof(ResendCommand))]
    private bool isBusy;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasInfo))]
    private string? infoMessage;

    public bool IsEmailStep => !IsCodeStep;
    public bool IsNotBusy => !IsBusy;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasInfo => !string.IsNullOrWhiteSpace(InfoMessage);
    public string PasswordRequirements => RegisterViewModel.PasswordHint;
    public string Title => IsCodeStep ? "Choose a new password" : "Forgot your password?";
    public string Instructions => IsCodeStep
        ? $"Enter the 6-digit code we sent to {Email.Trim()} and your new password."
        : "Enter the email you signed up with and we'll send you a 6-digit code to reset your password.";

    /// <summary>Raised when the user wants to go back to the sign-in page. The UI layer handles navigation.</summary>
    public event EventHandler? LoginRequested;

    private bool CanSendCode() => !IsBusy && !string.IsNullOrWhiteSpace(Email);
    private bool CanReset() => !IsBusy && !string.IsNullOrWhiteSpace(Code)
        && !string.IsNullOrEmpty(NewPassword) && !string.IsNullOrEmpty(ConfirmPassword);
    private bool CanResend() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSendCode))]
    private async Task SendCodeAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return;
        var request = new ForgotPasswordRequest { Email = Email.Trim() };
        if (!Validator.TryValidateObject(request, new ValidationContext(request), null, validateAllProperties: true))
        {
            ErrorMessage = "Enter a valid email address.";
            return;
        }
        if (await RunAsync(() => api.ForgotPasswordAsync(request, cancellationToken), "Sending the code"))
        {
            Email = request.Email;
            IsCodeStep = true;
            InfoMessage = $"If an account uses {request.Email}, a 6-digit code is on its way. It can take a minute; check Spam too.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanResend))]
    private async Task ResendAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return;
        if (await RunAsync(() => api.ForgotPasswordAsync(new ForgotPasswordRequest { Email = Email.Trim() }, cancellationToken), "Sending the code"))
            InfoMessage = $"We've sent a new code to {Email.Trim()}.";
    }

    [RelayCommand(CanExecute = nameof(CanReset))]
    private async Task ResetAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return;
        // Accept codes pasted with spaces or dashes, e.g. "123 456".
        var digits = new string(Code.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        if (digits.Length != 6 || !digits.All(char.IsAsciiDigit))
        {
            ErrorMessage = "Enter the 6-digit code from the email.";
            return;
        }
        if (NewPassword.Length < RegisterRequest.MinimumPasswordLength)
        {
            ErrorMessage = $"Password must be at least {RegisterRequest.MinimumPasswordLength} characters.";
            return;
        }
        if (NewPassword != ConfirmPassword)
        {
            ErrorMessage = "Passwords do not match.";
            return;
        }
        var request = new ResetPasswordRequest { Email = Email.Trim(), Code = digits, NewPassword = NewPassword };
        // The server checks the full password policy and returns a readable message if it fails.
        if (await RunAsync(() => session.ResetPasswordAsync(request, cancellationToken), "Resetting your password"))
        {
            Code = "";
            NewPassword = "";
            ConfirmPassword = "";
        }
    }

    /// <summary>Goes back to the first step to correct the email.</summary>
    [RelayCommand]
    private void ChangeEmail()
    {
        if (IsBusy) return;
        IsCodeStep = false;
        ErrorMessage = null;
        InfoMessage = null;
    }

    [RelayCommand]
    private void GoToLogin()
    {
        if (IsBusy) return;
        LoginRequested?.Invoke(this, EventArgs.Empty);
    }

    // Runs one API call with the busy flag and turns failures into messages. Returns true on success.
    private async Task<bool> RunAsync(Func<Task> call, string action)
    {
        IsBusy = true;
        ErrorMessage = null;
        InfoMessage = null;
        try
        {
            await call();
            return true;
        }
        catch (AuthException ex) { ErrorMessage = ex.Message; }
        catch (HttpRequestException) { ErrorMessage = "Cannot reach TravelPlanner. Check your connection and try again."; }
        catch (OperationCanceledException) { ErrorMessage = $"{action} timed out. Please try again."; }
        finally { IsBusy = false; }
        return false;
    }
}
