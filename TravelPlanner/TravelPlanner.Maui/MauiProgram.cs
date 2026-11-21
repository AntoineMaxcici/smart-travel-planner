using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TravelPlanner.Core.Services;
using TravelPlanner.Core.ViewModels;
using TravelPlanner.Maui.Controls;
using TravelPlanner.Maui.Services;
using TravelPlanner.Maui.Views;

namespace TravelPlanner.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>().ConfigureFonts(fonts =>
        {
            fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            // The Tourvex template's fonts (SIL Open Font License, see Licenses/OFL-Barlow.txt).
            fonts.AddFont("BarlowCondensed-SemiBold.ttf", "BarlowCondensedSemibold");
            fonts.AddFont("BarlowCondensed-Medium.ttf", "BarlowCondensedMedium");
            fonts.AddFont("BarlowCondensed-Regular.ttf", "BarlowCondensedRegular");
            fonts.AddFont("BarlowCondensed-Italic.ttf", "BarlowCondensedItalic");
            fonts.AddFont("BarlowSemiCondensed-Regular.ttf", "BarlowSemiRegular");
            fonts.AddFont("BarlowSemiCondensed-SemiBold.ttf", "BarlowSemiSemibold");
        });
        TvInputs.RemoveNativeBorders();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ITokenStore, SecureTokenStore>();
        // The auth client is separate to avoid recursing through the bearer handler during restore.
        builder.Services.AddSingleton<IAuthApi>(_ => new AuthApi(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = ApiSettings.BaseAddress, Timeout = TimeSpan.FromSeconds(20)
        }));
        builder.Services.AddSingleton<ISessionService, SessionService>();
        builder.Services.AddSingleton(services => new HttpClient(new BearerTokenHandler(
            services.GetRequiredService<ISessionService>(), ApiSettings.BaseAddress)
        {
            InnerHandler = new HttpClientHandler { AllowAutoRedirect = false }
        }) { BaseAddress = ApiSettings.BaseAddress, Timeout = TimeSpan.FromSeconds(20) });
        // Trips go through the authenticated client above, so BearerTokenHandler adds the token.
        // Set TRAVELPLANNER_FAKE_TRIPS=1 (or ApiSettings.UseFakeTripsByDefault) to use sample trips without the API.
        builder.Services.AddSingleton<ITripsApi>(services => ApiSettings.UseFakeTrips
            ? new FakeTripsApi(services.GetRequiredService<TimeProvider>(), delay: TimeSpan.FromMilliseconds(400))
            : new TripsApi(services.GetRequiredService<HttpClient>()));
        builder.Services.AddSingleton<INavigationService, ShellNavigationService>();
        builder.Services.AddSingleton<IDialogService, ShellDialogService>();
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<RegisterViewModel>();
        builder.Services.AddTransient<ConfirmEmailViewModel>();
        builder.Services.AddTransient<ForgotPasswordViewModel>();
        builder.Services.AddTransient<MyTripsViewModel>();
        builder.Services.AddTransient<NewTripViewModel>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<RegisterPage>();
        builder.Services.AddTransient<ConfirmEmailPage>();
        builder.Services.AddTransient<ForgotPasswordPage>();
        builder.Services.AddTransient<MyTripsPage>();
        builder.Services.AddTransient<NewTripPage>();
        builder.Services.AddTransient<AppShell>();
#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
