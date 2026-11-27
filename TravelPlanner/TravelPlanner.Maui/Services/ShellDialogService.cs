using TravelPlanner.Core.Services;

namespace TravelPlanner.Maui.Services;

public sealed class ShellDialogService : IDialogService
{
    public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel) =>
        Shell.Current.DisplayAlert(title, message, accept, cancel);
}
