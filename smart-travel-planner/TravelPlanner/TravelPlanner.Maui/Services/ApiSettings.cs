namespace TravelPlanner.Maui.Services;

public static class ApiSettings
{
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
