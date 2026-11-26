namespace TravelPlanner.Api.Places;

// The fields we keep from one Google Places result.
public sealed record PlaceMatch(
    string GooglePlaceId,
    string Name,
    string? FormattedAddress,
    double Latitude,
    double Longitude,
    string? PrimaryCategory,
    string? GoogleMapsUri,
    IReadOnlyList<string>? OpeningHours);

// What the enricher asks for one stop: the stop's name plus the trip's city.
public sealed record PlaceQuery(string Text, string? RegionCode, double? NearLatitude, double? NearLongitude);

// Looks up a real place for a suggested stop. Returns null when nothing usable is found.
// Kept as an interface so tests (and a future provider) can replace Google.
public interface IPlaceSearch
{
    bool IsConfigured { get; }
    Task<PlaceMatch?> FindAsync(PlaceQuery query, CancellationToken cancellationToken = default);
}
