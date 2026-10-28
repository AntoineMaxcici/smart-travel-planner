using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TravelPlanner.Core.Services;
using TravelPlanner.Core.ViewModels;
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
        });
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
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<AppShell>();
#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
