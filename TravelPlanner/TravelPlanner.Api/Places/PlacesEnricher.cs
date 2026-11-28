using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TravelPlanner.Api.Data;
using TravelPlanner.Api.Data.Entities;
using TravelPlanner.Api.Itineraries;

namespace TravelPlanner.Api.Places;

// PB-5 + PB-6. Runs inside the generation transaction (see TripGenerationService), after the new
// revision's days and stops are saved:
// 1. Looks up each stop with Google Places and links it to a row in Places (reused by Google place id).
// 2. Copies the real name, address and coordinates onto the stop. Stops without a match keep the AI's text
//    and PlaceId stays empty, so one bad suggestion never fails the whole trip (FR-4).
// 3. Re-orders each day's stops to cut backtracking (StopOrdering).
public sealed class PlacesEnricher(AppDbContext database, IPlaceSearch search, IOptions<GooglePlacesOptions> options,
    TimeProvider clock, ILogger<PlacesEnricher> logger) : IPlacesEnricher
{
    // Temporary SortOrder offset: (ItineraryDayId, SortOrder) is unique, so swapping 1 and 2 directly would clash.
    private const int SortOrderOffset = 1000;

    public async Task EnrichAsync(Itinerary itinerary, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (itinerary.Trip is null)
            await database.Entry(itinerary).Reference(i => i.Trip).LoadAsync(cancellationToken);
        var stops = itinerary.Days.SelectMany(d => d.Stops).ToList();

        if (search.IsConfigured && stops.Count > 0)
        {
            var matches = await LookUpAsync(itinerary.Trip!, stops, cancellationToken);
            var placeIds = new Dictionary<string, int>();
            foreach (var stop in stops)
            {
                if (matches[stop] is not { } match) continue;
                if (!placeIds.TryGetValue(match.GooglePlaceId, out var placeId))
                    placeIds[match.GooglePlaceId] = placeId = await UpsertPlaceAsync(match, cancellationToken);
                Apply(stop, match, placeId);
            }
            logger.LogInformation("Places enrichment matched {Matched} of {Total} stops for itinerary {ItineraryId}.",
                stops.Count(s => matches[s] is not null), stops.Count, itinerary.Id);
        }

        var reordered = itinerary.Days.Where(StopOrdering.Optimize).ToList();
        if (reordered.Count > 0)
        {
            var moved = reordered.SelectMany(d => d.Stops).ToList();
            foreach (var stop in moved) stop.SortOrder += SortOrderOffset;
            await database.SaveChangesAsync(cancellationToken);
            foreach (var stop in moved) stop.SortOrder -= SortOrderOffset;
        }
        await database.SaveChangesAsync(cancellationToken);
    }

    // One lookup per distinct stop name, a few at a time, all within LookupBudgetSeconds.
    private async Task<Dictionary<ItineraryStop, PlaceMatch?>> LookUpAsync(Trip trip, List<ItineraryStop> stops,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var timer = new CancellationTokenSource(TimeSpan.FromSeconds(settings.LookupBudgetSeconds), clock);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(timer.Token, cancellationToken);
        using var gate = new SemaphoreSlim(settings.MaxConcurrentLookups);

        async Task<PlaceMatch?> FindAsync(ItineraryStop stop)
        {
            var query = new PlaceQuery($"{stop.DisplayName}, {trip.DestinationName}", trip.DestinationCountryCode,
                stop.DisplayLatitude, stop.DisplayLongitude);
            try
            {
                await gate.WaitAsync(budget.Token);
                try
                {
                    var match = await search.FindAsync(query, budget.Token);
                    return IsPlausible(stop, match, settings) ? match : null;
                }
                finally { gate.Release(); }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeouts, network and parsing errors fall back to the AI's text for this stop.
                // Never log the exception: it can contain the request URL or provider payload.
                return null;
            }
        }

        var lookups = stops.GroupBy(s => s.DisplayName.Trim().ToLowerInvariant())
            .Select(group => (Stops: group.ToList(), Task: FindAsync(group.First()))).ToList();
        await Task.WhenAll(lookups.Select(l => l.Task));
        cancellationToken.ThrowIfCancellationRequested();
        return lookups.SelectMany(l => l.Stops.Select(stop => (stop, match: l.Task.Result)))
            .ToDictionary(pair => pair.stop, pair => pair.match);
    }

    // Rejects a result that is far away from where the AI said the stop is (usually a same-name place elsewhere).
    private static bool IsPlausible(ItineraryStop stop, PlaceMatch? match, GooglePlacesOptions settings) =>
        match is not null && (stop.DisplayLatitude is not double lat || stop.DisplayLongitude is not double lng ||
            StopOrdering.DistanceKm(lat, lng, match.Latitude, match.Longitude) <= settings.MaxDistanceFromSuggestionKm);

    private static void Apply(ItineraryStop stop, PlaceMatch match, int placeId)
    {
        stop.PlaceId = placeId;
        stop.DisplayName = Truncate(match.Name, 200)!;
        stop.DisplayAddress = Truncate(match.FormattedAddress, 300) ?? stop.DisplayAddress;
        stop.DisplayLatitude = match.Latitude;
        stop.DisplayLongitude = match.Longitude;
        stop.Category ??= Truncate(match.PrimaryCategory, 100);
    }

    // Insert-or-update in one statement, so two trips generating at once can't both insert the same place.
    private async Task<int> UpsertPlaceAsync(PlaceMatch match, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var name = Truncate(match.Name, 200)!;
        var address = Truncate(match.FormattedAddress, 300);
        var category = Truncate(match.PrimaryCategory, 100);
        var mapsUri = Truncate(match.GoogleMapsUri, 500);
        var hours = match.OpeningHours is { Count: > 0 } lines ? JsonSerializer.Serialize(lines) : null;
        var ids = await database.Database.SqlQuery<int>($"""
            INSERT INTO "Places" ("GooglePlaceId", "Name", "FormattedAddress", "Latitude", "Longitude", "PrimaryCategory",
                                  "GoogleMapsUri", "OpeningHours", "LastEnrichedAtUtc", "CreatedAtUtc", "UpdatedAtUtc")
            VALUES ({match.GooglePlaceId}, {name}, {address}, {match.Latitude}, {match.Longitude}, {category},
                    {mapsUri}, CAST({hours} AS jsonb), {now}, {now}, {now})
            ON CONFLICT ("GooglePlaceId") DO UPDATE SET
                "Name" = EXCLUDED."Name", "FormattedAddress" = EXCLUDED."FormattedAddress",
                "Latitude" = EXCLUDED."Latitude", "Longitude" = EXCLUDED."Longitude",
                "PrimaryCategory" = EXCLUDED."PrimaryCategory", "GoogleMapsUri" = EXCLUDED."GoogleMapsUri",
                "OpeningHours" = EXCLUDED."OpeningHours", "LastEnrichedAtUtc" = EXCLUDED."LastEnrichedAtUtc",
                "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc"
            RETURNING "Id" AS "Value"
            """).ToListAsync(cancellationToken);
        return ids[0];
    }

    private static string? Truncate(string? value, int length)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.Length <= length ? value : value[..length];
    }
}
