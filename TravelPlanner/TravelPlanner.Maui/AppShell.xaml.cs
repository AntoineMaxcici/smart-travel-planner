using TravelPlanner.Maui.Services;
using TravelPlanner.Maui.Views;

namespace TravelPlanner.Maui;

public partial class AppShell : Shell
{
    // Pages pushed on top of My Trips. Shell creates them through dependency injection (see MauiProgram).
    static AppShell()
    {
        Routing.RegisterRoute(AppRoutes.NewTrip, typeof(NewTripPage));
    }

    public AppShell(MyTripsPage myTripsPage)
    {
        InitializeComponent();
        MyTripsContent.Content = myTripsPage;
    }
}
