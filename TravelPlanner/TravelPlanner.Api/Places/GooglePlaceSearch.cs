using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace TravelPlanner.Api.Places;

// Calls Places API (New) Text Search: POST https://places.googleapis.com/v1/places:searchText
// The field mask asks only for the fields we store, which keeps the response small and the cost predictable.
public sealed class GooglePlaceSearch(HttpClient client, IOptions<GooglePlacesOptions> options) : IPlaceSearch
{
    public const string Endpoint = "https://places.googleapis.com/v1/places:searchText";
    public const string FieldMask = "places.id,places.displayName,places.formattedAddress,places.location," +
        "places.primaryTypeDisplayName,places.googleMapsUri,places.regularOpeningHours.weekdayDescriptions";
    private const int MaximumResponseBytes = 256 * 1024;
    private const double BiasRadiusMeters = 10_000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.ApiKey) && !options.Value.ApiKey.Any(char.IsControl);

    public async Task<PlaceMatch?> FindAsync(PlaceQuery query, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return null;
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Add("X-Goog-Api-Key", options.Value.ApiKey);
        request.Headers.Add("X-Goog-FieldMask", FieldMask);
        request.Content = JsonContent.Create(new SearchTextRequest(
            query.Text, PageSize: 1, LanguageCode: "en", RegionCode: query.RegionCode,
            LocationBias: query.NearLatitude is double lat && query.NearLongitude is double lng
                ? new LocationBias(new Circle(new LatLng(lat, lng), BiasRadiusMeters)) : null), options: Json);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        // Quota, key and server errors are not fatal: the caller keeps the AI's text for this stop.
        if (!response.IsSuccessStatusCode) return null;
        if (response.Content.Headers.ContentLength > MaximumResponseBytes) return null;
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (body.Length > MaximumResponseBytes) return null;

        var result = JsonSerializer.Deserialize<SearchTextResponse>(body, Json);
        var place = result?.Places?.FirstOrDefault();
        if (place?.Id is not { Length: > 0 } id || place.Location is not { } location ||
            !double.IsFinite(location.Latitude) || !double.IsFinite(location.Longitude) ||
            location.Latitude is < -90 or > 90 || location.Longitude is < -180 or > 180)
            return null;

        var name = place.DisplayName?.Text;
        return new PlaceMatch(
            id,
            string.IsNullOrWhiteSpace(name) ? query.Text : name.Trim(),
            place.FormattedAddress,
            location.Latitude,
            location.Longitude,
            place.PrimaryTypeDisplayName?.Text,
            place.GoogleMapsUri,
            place.RegularOpeningHours?.WeekdayDescriptions);
    }

    // Request and response shapes, limited to the fields in FieldMask.
    private sealed record SearchTextRequest(string TextQuery, int PageSize, string LanguageCode, string? RegionCode, LocationBias? LocationBias);
    private sealed record LocationBias(Circle Circle);
    private sealed record Circle(LatLng Center, double Radius);
    private sealed record LatLng(double Latitude, double Longitude);
    private sealed record SearchTextResponse(List<GooglePlace>? Places);
    private sealed record GooglePlace(string? Id, LocalizedText? DisplayName, string? FormattedAddress, LatLng? Location,
        LocalizedText? PrimaryTypeDisplayName, string? GoogleMapsUri, OpeningHours? RegularOpeningHours);
    private sealed record LocalizedText(string? Text);
    private sealed record OpeningHours(List<string>? WeekdayDescriptions);
}
