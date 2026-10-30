using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TravelPlanner.Core.Dtos;
using TravelPlanner.Core.Services;

namespace TravelPlanner.Core.ViewModels;

public partial class ConfirmEmailViewModel(ISessionService session, IAuthApi api) : ObservableObject
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Instructions))]
    private string email = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string code = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ConfirmCommand), nameof(ResendCommand)), NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool isBusy;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasInfo))]
    private string? infoMessage;
    public bool IsNotBusy => !IsBusy;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasInfo => !string.IsNullOrWhiteSpace(InfoMessage);
    public string Instructions => $"We sent a 6-digit code to {Email}. Enter it below to finish creating your account.";

    /// <summary>Raised when the user wants to go back to the sign-in page. The UI layer handles navigation.</summary>
    public event EventHandler? LoginRequested;

    private bool CanConfirm() => !IsBusy && !string.IsNullOrWhiteSpace(Code);
    private bool CanResend() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return;
        ErrorMessage = null;
        InfoMessage = null;
        // Accept codes pasted with spaces or dashes, e.g. "123 456".
        var digits = new string(Code.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        if (digits.Length != 6 || !digits.All(char.IsAsciiDigit))
        {
            ErrorMessage = "Enter the 6-digit code from the email.";
            return;
        }
        IsBusy = true;
        try
        {
            await session.ConfirmEmailAsync(new ConfirmEmailRequest { Email = Email, Code = digits }, cancellationToken);
            Code = "";
        }
        catch (AuthException ex) { ErrorMessage = ex.Message; }
        catch (HttpRequestException) { ErrorMessage = "Cannot reach TravelPlanner. Check your connection and try again."; }
        catch (OperationCanceledException) { ErrorMessage = "Confirmation timed out or was cancelled. Please try again."; }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanResend))]
    private async Task ResendAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return;
        ErrorMessage = null;
        InfoMessage = null;
        IsBusy = true;
        try
        {
            await api.ResendConfirmationAsync(new ResendConfirmationRequest { Email = Email }, cancellationToken);
            InfoMessage = $"We've sent a new code to {Email}. It can take a minute to arrive; check your spam folder too.";
        }
        catch (AuthException ex) { ErrorMessage = ex.Message; }
        catch (HttpRequestException) { ErrorMessage = "Cannot reach TravelPlanner. Check your connection and try again."; }
        catch (OperationCanceledException) { ErrorMessage = "Sending a new code timed out. Please try again."; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void GoToLogin()
    {
        if (IsBusy) return;
        LoginRequested?.Invoke(this, EventArgs.Empty);
    }
}
