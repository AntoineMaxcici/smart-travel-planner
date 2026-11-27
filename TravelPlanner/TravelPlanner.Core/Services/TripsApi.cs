using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TravelPlanner.Core.Dtos;

namespace TravelPlanner.Core.Services;

/// <summary>
/// Calls /api/trips and /api/interests. Give it the app's authenticated HttpClient, whose BearerTokenHandler
/// adds the access token and signs the user out when the API answers 401.
/// </summary>
public sealed class TripsApi(HttpClient client) : ITripsApi
{
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 50;

    public async Task<PagedResult<TripSummaryDto>> GetTripsAsync(int page = 1, int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, MaximumPageSize);
        using var response = await client.GetAsync($"api/trips?page={page}&pageSize={pageSize}", cancellationToken);
        await EnsureSuccessAsync(response, "Your trips couldn't be loaded. Please try again.", cancellationToken);
        return await ReadAsync<PagedResult<TripSummaryDto>>(response, cancellationToken);
    }

    public async Task<TripDetailDto> GetTripAsync(int id, CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync($"api/trips/{id}", cancellationToken);
        await EnsureSuccessAsync(response, "This trip couldn't be loaded. Please try again.", cancellationToken);
        return await ReadAsync<TripDetailDto>(response, cancellationToken);
    }

    public async Task<TripDetailDto> CreateTripAsync(TripRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await client.PostAsJsonAsync("api/trips", request, cancellationToken);
        await EnsureSuccessAsync(response, "Your trip couldn't be created. Please try again.", cancellationToken);
        return await ReadAsync<TripDetailDto>(response, cancellationToken);
    }

    public async Task<TripDetailDto> SaveTripAsync(int id, TripRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await client.PutAsJsonAsync($"api/trips/{id}", request, cancellationToken);
        await EnsureSuccessAsync(response, "Your changes couldn't be saved. Please try again.", cancellationToken);
        return await ReadAsync<TripDetailDto>(response, cancellationToken);
    }

    public async Task DeleteTripAsync(int id, CancellationToken cancellationToken = default)
    {
        using var response = await client.DeleteAsync($"api/trips/{id}", cancellationToken);
        await EnsureSuccessAsync(response, "This trip couldn't be deleted. Please try again.", cancellationToken);
    }

    public async Task<IReadOnlyList<InterestDto>> GetInterestsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync("api/interests", cancellationToken);
        await EnsureSuccessAsync(response, "Interests couldn't be loaded. Please try again.", cancellationToken);
        return await ReadAsync<List<InterestDto>>(response, cancellationToken);
    }

    // 401 means the session ended: BearerTokenHandler has already signed the user out, so the app returns to login.
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string fallback, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new UnauthorizedSessionException(),
            HttpStatusCode.NotFound => new TripsApiException(await ReadErrorAsync(response, "Trip not found.", cancellationToken)),
            HttpStatusCode.BadRequest => new TripsApiException(await ReadErrorAsync(response, "Check the trip details and try again.", cancellationToken)),
            HttpStatusCode.TooManyRequests => new TripsApiException("Too many requests. Please wait a moment and try again."),
            _ => new TripsApiException(fallback)
        };
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) where T : class
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                ?? throw new TripsApiException("The server returned an invalid response.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new TripsApiException("The server returned an invalid response.");
        }
    }

    // Reads either the API's { "message": "..." } body or a validation problem's
    // { "errors": { "StartDate": ["..."] } }, falling back when the body is missing or another shape.
    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, string fallback, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return fallback;
            if (TryGetString(root, "message", out var message)) return message;
            if (TryGetProperty(root, "errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                var messages = errors.EnumerateObject()
                    .Where(field => field.Value.ValueKind == JsonValueKind.Array)
                    .SelectMany(field => field.Value.EnumerateArray())
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString()!)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .Distinct()
                    .ToList();
                if (messages.Count > 0) return string.Join(" ", messages);
            }
            return TryGetString(root, "title", out var title) && !title.StartsWith("One or more validation errors", StringComparison.Ordinal)
                ? title : fallback;
        }
        catch (JsonException) { return fallback; }
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = property.Value;
            return true;
        }
        value = default;
        return false;
    }

    private static bool TryGetString(JsonElement root, string name, out string value)
    {
        value = TryGetProperty(root, name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? "" : "";
        return !string.IsNullOrWhiteSpace(value);
    }
}
