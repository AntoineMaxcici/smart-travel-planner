namespace TravelPlanner.Core.Services;

/// <summary>Page navigation inside the signed-in app. The MAUI project implements it with Shell routes.</summary>
public interface INavigationService
{
    Task GoToNewTripAsync();
    Task GoBackAsync();
}

/// <summary>Native dialogs. The MAUI project implements it with Shell's alerts.</summary>
public interface IDialogService
{
    /// <returns>True when the user chose <paramref name="accept"/>.</returns>
    Task<bool> ConfirmAsync(string title, string message, string accept, string cancel);
}
