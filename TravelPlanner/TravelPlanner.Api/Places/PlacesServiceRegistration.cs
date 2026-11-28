using Microsoft.Extensions.Options;
using TravelPlanner.Api.Itineraries;

namespace TravelPlanner.Api.Places;

public static class PlacesServiceRegistration
{
    public static IServiceCollection AddPlacesEnrichment(this IServiceCollection services)
    {
        // A missing key is allowed: stops keep the AI's text and are still re-ordered.
        services.AddOptions<GooglePlacesOptions>().BindConfiguration(GooglePlacesOptions.SectionName)
            .Validate(o => o.RequestTimeoutSeconds is >= 1 and <= 30, "GooglePlaces:RequestTimeoutSeconds must be between 1 and 30.")
            .Validate(o => o.LookupBudgetSeconds is >= 1 and <= 15, "GooglePlaces:LookupBudgetSeconds must be between 1 and 15.")
            .Validate(o => o.MaxConcurrentLookups is >= 1 and <= 10, "GooglePlaces:MaxConcurrentLookups must be between 1 and 10.")
            .Validate(o => o.MaxDistanceFromSuggestionKm is > 0 and <= 500, "GooglePlaces:MaxDistanceFromSuggestionKm must be between 0 and 500.")
            .ValidateOnStart();
        services.AddHttpClient<IPlaceSearch, GooglePlaceSearch>((provider, client) =>
                client.Timeout = TimeSpan.FromSeconds(provider.GetRequiredService<IOptions<GooglePlacesOptions>>().Value.RequestTimeoutSeconds))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .RedactLoggedHeaders(["X-Goog-Api-Key"]);
        services.AddScoped<IPlacesEnricher, PlacesEnricher>();
        return services;
    }
}
