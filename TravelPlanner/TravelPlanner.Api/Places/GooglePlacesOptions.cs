namespace TravelPlanner.Api.Places;

// Settings for the Google Places (New) Text Search lookups. Only the API key is secret:
// keep it in user-secrets locally and in App settings when deployed, never in appsettings.json.
public sealed class GooglePlacesOptions
{
    public const string SectionName = "GooglePlaces";

    // Empty means "not configured": stops keep the AI's text and are only re-ordered.
    public string ApiKey { get; set; } = "";

    // Time limit for one Text Search request.
    public int RequestTimeoutSeconds { get; set; } = 5;

    // Time limit for all lookups of one itinerary. Generation shares a 20-second deadline,
    // so stops still waiting when this runs out keep the AI's text instead of failing the trip.
    public int LookupBudgetSeconds { get; set; } = 8;

    // How many Text Search requests run at the same time.
    public int MaxConcurrentLookups { get; set; } = 4;

    // A match further than this from the AI's own coordinates is treated as the wrong place.
    public double MaxDistanceFromSuggestionKm { get; set; } = 30;
}
