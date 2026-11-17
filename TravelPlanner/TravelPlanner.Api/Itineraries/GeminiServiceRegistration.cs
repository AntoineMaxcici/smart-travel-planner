using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TravelPlanner.Core.Services;

namespace TravelPlanner.Api.Itineraries;

public static class GeminiServiceRegistration
{
    public static IServiceCollection AddGeminiItineraryGeneration(this IServiceCollection services)
    {
        // A missing key disables generation without preventing login or other API features from starting.
        services.AddOptions<GeminiOptions>().BindConfiguration(GeminiOptions.SectionName)
            .Validate(options => Regex.IsMatch(options.Model ?? "", "^gemini-[a-z0-9.-]{1,90}$"), "Gemini:Model must be a Gemini model ID.")
            .Validate(options => options.TimeoutSeconds is >= 5 and <= 120, "Gemini:TimeoutSeconds must be between 5 and 120.")
            .Validate(options => options.MaxOutputTokens is >= 1024 and <= 65536, "Gemini:MaxOutputTokens must be between 1024 and 65536.")
            .ValidateOnStart();
        services.AddHttpClient<IItineraryGenerator, GeminiItineraryGenerator>((provider, client) =>
            client.Timeout = TimeSpan.FromSeconds(provider.GetRequiredService<IOptions<GeminiOptions>>().Value.TimeoutSeconds))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .RedactLoggedHeaders(["x-goog-api-key"]);
        return services;
    }
}
