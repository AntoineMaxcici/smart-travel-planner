using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TravelPlanner.Api.Data;
using TravelPlanner.Api.Places;
using TravelPlanner.Core.Dtos;

namespace TravelPlanner.Api.Tests;

// Runs the real generate endpoint with a fake AI and a fake Places search, against PostgreSQL.
public sealed class PlacesEnrichmentTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private sealed class FakeSearch : IPlaceSearch
    {
        public bool IsConfigured { get; set; } = true;
        public ConcurrentBag<PlaceQuery> Queries { get; } = [];
        public Func<PlaceQuery, CancellationToken, Task<PlaceMatch?>> Find { get; set; } = (_, _) => Task.FromResult<PlaceMatch?>(null);
        public Task<PlaceMatch?> FindAsync(PlaceQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Find(query, cancellationToken);
        }
    }

    // Three stops per day with coordinates; the AI order zig-zags (0 km, 3 km, 0.75 km along one street).
    private static IReadOnlyList<ItineraryDayDto> Days(TripInput trip) => ItineraryTestData.Days(trip).Select(day => day with
    {
        Stops = day.Stops.Select((stop, index) => stop with
        {
            TimeSlot = ItineraryTimeSlot.Morning,
            // 8:00-8:45, 9:30-10:15, 11:00-11:45
            StartTimeLocal = new TimeOnly(8, 0).AddMinutes(index * 90), EndTimeLocal = new TimeOnly(8, 45).AddMinutes(index * 90),
            DisplayLatitude = 48.8566, DisplayLongitude = 2.30 + new[] { 0.0, 0.04, 0.01 }[index],
            TravelMinutesFromPrevious = index == 0 ? 0 : 20
        }).ToArray()
    }).ToArray();

    private static PlaceMatch Match(string id, string name, double lat = 48.8566, double lng = 2.30) =>
        new(id, name, $"{name} street, Paris", lat, lng, "Tourist attraction", $"https://maps.google.com/?q={id}", ["Monday: Open 24 hours"]);

    private WebApplicationFactory<Program> Host(FakeSearch search, Dictionary<string, string?>? settings = null) => factory.WithWebHostBuilder(builder =>
    {
        if (settings is not null) builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
        builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<TravelPlanner.Core.Services.IItineraryGenerator>(
                new FakeItineraryGenerator { Generate = Days }));
            services.Replace(ServiceDescriptor.Singleton<IPlaceSearch>(search));
        });
    });

    private static async Task<(HttpClient Client, int TripId)> SignInAndCreateTrip(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        using var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = AuthApiFactory.Email, Password = AuthApiFactory.Password });
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        var input = ItineraryTestData.Trip();
        using var created = await client.PostAsJsonAsync("/api/trips", new TripRequest
        {
            DestinationName = input.DestinationName, DestinationCountryCode = input.DestinationCountryCode,
            StartDate = input.StartDate, EndDate = input.EndDate, BudgetLevel = input.BudgetLevel, InterestIds = [1]
        });
        created.EnsureSuccessStatusCode();
        return (client, (await created.Content.ReadFromJsonAsync<TripDetailDto>())!.Id);
    }

    private static async Task<TripGenerationDto> Generate(HttpClient client, int tripId)
    {
        using var response = await client.PostAsync($"/api/trips/{tripId}/generate", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TripGenerationDto>())!;
    }

    [Fact]
    public async Task MatchedStopsAreLinkedToPlacesAndUnmatchedStopsKeepTheAiText()
    {
        var search = new FakeSearch
        {
            // Only the Eiffel Tower is found; everything else falls back.
            Find = (query, _) => Task.FromResult(query.Text.StartsWith("Eiffel Tower") ? Match("g-eiffel", "Tour Eiffel") : null)
        };
        using var host = Host(search);
        var (client, tripId) = await SignInAndCreateTrip(host);

        var result = await Generate(client, tripId);

        var first = result.Days[0].Stops[0];
        Assert.Equal("Tour Eiffel", first.DisplayName);
        Assert.Equal("Tour Eiffel street, Paris", first.DisplayAddress);
        Assert.Contains(search.Queries, q => q is { Text: "Eiffel Tower, Paris", RegionCode: "FR", NearLatitude: 48.8566 });

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stops = await db.ItineraryStops.Include(s => s.Place)
            .Where(s => s.ItineraryDay.Itinerary.Id == result.ItineraryId).ToListAsync();
        var linked = Assert.Single(stops, s => s.PlaceId is not null);
        Assert.Equal("g-eiffel", linked.Place!.GooglePlaceId);
        Assert.Equal("""["Monday: Open 24 hours"]""", linked.Place.OpeningHours);
        Assert.NotNull(linked.Place.LastEnrichedAtUtc);
        Assert.All(stops.Where(s => s.PlaceId is null), s => Assert.Null(s.DisplayAddress));
    }

    [Fact]
    public async Task RegeneratingReusesTheSamePlaceRow()
    {
        var search = new FakeSearch { Find = (query, _) => Task.FromResult<PlaceMatch?>(Match("g-" + query.Text.Split(',')[0], query.Text.Split(',')[0])) };
        using var host = Host(search);
        var (client, tripId) = await SignInAndCreateTrip(host);

        var first = await Generate(client, tripId);
        var second = await Generate(client, tripId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var placeIds = await db.ItineraryStops.Where(s => s.ItineraryDay.Itinerary.TripId == tripId)
            .Select(s => new { s.ItineraryDay.ItineraryId, s.PlaceId, s.Place!.GooglePlaceId }).ToListAsync();
        Assert.All(placeIds, p => Assert.NotNull(p.PlaceId));
        // Same Google id -> same Places row in both revisions, and only one row per Google id.
        var byGoogleId = placeIds.GroupBy(p => p.GooglePlaceId).ToList();
        Assert.All(byGoogleId, g => Assert.Single(g.Select(p => p.PlaceId).Distinct()));
        Assert.Equal(byGoogleId.Count, await db.Places.CountAsync(p => byGoogleId.Select(g => g.Key).Contains(p.GooglePlaceId)));
        Assert.NotEqual(first.ItineraryId, second.ItineraryId);
    }

    [Fact]
    public async Task StopsAreReorderedAndTheNewOrderIsSaved()
    {
        using var host = Host(new FakeSearch { IsConfigured = false });
        var (client, tripId) = await SignInAndCreateTrip(host);

        var result = await Generate(client, tripId);

        // AI order was 0 km, 3 km, 0.75 km; nearest-next swaps the last two.
        var longitudes = result.Days[0].Stops.Select(s => s.DisplayLongitude!.Value).ToArray();
        Assert.Equal([2.30, 2.31, 2.34], longitudes.Select(l => Math.Round(l, 2)));
        Assert.Equal([1, 2, 3], result.Days[0].Stops.Select(s => s.SortOrder));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.ItineraryStops.Where(s => s.ItineraryDay.ItineraryId == result.ItineraryId && s.ItineraryDay.DayNumber == 1)
            .OrderBy(s => s.SortOrder).Select(s => s.DisplayLongitude).ToListAsync();
        Assert.Equal(longitudes, saved.Select(l => l!.Value));
    }

    [Fact]
    public async Task SlowOrFailingLookupsFallBackWithoutFailingTheTrip()
    {
        var search = new FakeSearch
        {
            Find = async (query, token) =>
            {
                if (query.Text.StartsWith("Louvre")) throw new HttpRequestException("private failure");
                if (query.Text.StartsWith("Montmartre")) await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Match("g-ok-" + query.Text.Split(',')[0], "Matched");
            }
        };
        using var host = Host(search, new() { ["GooglePlaces:LookupBudgetSeconds"] = "1" });
        var (client, tripId) = await SignInAndCreateTrip(host);

        var result = await Generate(client, tripId);

        Assert.Equal(TripStatusDto.Ready, result.Status);
        var names = result.Days.SelectMany(d => d.Stops).Select(s => s.DisplayName).ToList();
        Assert.Contains("Louvre Museum", names);
        Assert.Contains("Montmartre", names);
        Assert.Contains("Matched", names);
    }

    [Fact]
    public async Task FarAwayMatchesAreIgnored()
    {
        // A "Louvre" in Abu Dhabi is not the Paris stop the AI meant.
        var search = new FakeSearch { Find = (_, _) => Task.FromResult<PlaceMatch?>(Match("g-far", "Louvre Abu Dhabi", 24.53, 54.39)) };
        using var host = Host(search);
        var (client, tripId) = await SignInAndCreateTrip(host);

        var result = await Generate(client, tripId);

        Assert.DoesNotContain(result.Days.SelectMany(d => d.Stops), s => s.DisplayName == "Louvre Abu Dhabi");
    }

    [Fact]
    public async Task HealthEndpointReportsHealthyWithoutSignIn()
    {
        using var response = await factory.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
