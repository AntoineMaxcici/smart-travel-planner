using TravelPlanner.Core.Services;

namespace TravelPlanner.Maui.Services;

public sealed class ShellNavigationService : INavigationService
{
    public Task GoToNewTripAsync() => Shell.Current.GoToAsync(AppRoutes.NewTrip);
    public Task GoBackAsync() => Shell.Current.GoToAsync("..");
}
