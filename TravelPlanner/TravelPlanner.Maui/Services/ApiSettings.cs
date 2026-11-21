namespace TravelPlanner.Maui.Services;

public static class ApiSettings
{
    /// <summary>Change to true to run the app with FakeTripsApi's sample trips instead of calling the API.</summary>
    public const bool UseFakeTripsByDefault = false;

    /// <summary>True when the app should use sample trips: set TRAVELPLANNER_FAKE_TRIPS=1, or UseFakeTripsByDefault.</summary>
    public static bool UseFakeTrips =>
        UseFakeTripsByDefault || Environment.GetEnvironmentVariable("TRAVELPLANNER_FAKE_TRIPS") == "1";

    public static Uri BaseAddress
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("TRAVELPLANNER_API_URL");
#if DEBUG
            var fallback = DeviceInfo.Platform == DevicePlatform.Android
                ? "http://10.0.2.2:5082/" : "http://localhost:5082/";
#else
            const string fallback = "https://localhost:7255/";
#endif
            var address = new Uri((configured ?? fallback).TrimEnd('/') + "/", UriKind.Absolute);
            if (address.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeHttp)
                throw new InvalidOperationException("The API address must use HTTP or HTTPS.");
#if !DEBUG
            if (address.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("Release builds require an HTTPS API address.");
#endif
            return address;
        }
    }
}
