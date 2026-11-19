using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TravelPlanner.Api.Data;
using TravelPlanner.Core.Dtos;

namespace TravelPlanner.Api.Tests;

public sealed class TripEndpointTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const string OtherEmail = "other-traveler@example.com";
    private static readonly DateOnly Start = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1);

    private static TripRequest Paris() => new()
    {
        DestinationName = "Paris", DestinationCountryCode = "FR",
        StartDate = Start, EndDate = Start.AddDays(2),
        BudgetLevel = TripBudgetLevel.Medium, InterestIds = [1, 3], Notes = "First visit"
    };

    private async Task<HttpClient> SignIn(string email = AuthApiFactory.Email)
    {
        if (email != AuthApiFactory.Email)
        {
            using var scope = factory.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            if (await users.FindByEmailAsync(email) is null)
            {
                var created = await users.CreateAsync(new ApplicationUser { UserName = email, Email = email, DisplayName = "Other", EmailConfirmed = true }, AuthApiFactory.Password);
                Assert.True(created.Succeeded);
            }
        }
        var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = AuthApiFactory.Password });
        response.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await response.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        return client;
    }

    private static async Task<TripDetailDto> CreateTrip(HttpClient client, TripRequest? request = null)
    {
        using var response = await client.PostAsJsonAsync("/api/trips", request ?? Paris());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TripDetailDto>())!;
    }

    [Fact]
    public async Task InterestsAreSeeded()
    {
        using var client = await SignIn();
        var interests = await client.GetFromJsonAsync<List<InterestDto>>("/api/interests");
        Assert.Equal(["Food", "Museums", "History", "Nature", "Nightlife", "Shopping", "Beaches", "Art"], interests!.Select(i => i.Name));
        Assert.Equal(Enumerable.Range(1, 8), interests!.Select(i => i.Id));
        Assert.All(interests!, i => Assert.False(string.IsNullOrWhiteSpace(i.Description)));
    }

    [Theory]
    [InlineData("GET", "/api/interests")]
    [InlineData("GET", "/api/trips")]
    [InlineData("GET", "/api/trips/1")]
    [InlineData("POST", "/api/trips")]
    [InlineData("PUT", "/api/trips/1")]
    [InlineData("DELETE", "/api/trips/1")]
    public async Task AnonymousRequestsAreRejected(string method, string path)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT") request.Content = JsonContent.Create(Paris());
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateThenOpenReturnsTheSavedTrip()
    {
        using var client = await SignIn();
        using var response = await client.PostAsJsonAsync("/api/trips", Paris() with { DestinationName = "  Paris  " });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<TripDetailDto>())!;
        Assert.Equal($"/api/trips/{created.Id}", response.Headers.Location!.AbsolutePath);
        Assert.Equal("Paris", created.DestinationName);
        Assert.Equal(TripStatusDto.Draft, created.Status);
        Assert.Equal(["Food", "History"], created.Interests.Select(i => i.Name));

        var opened = await client.GetFromJsonAsync<TripDetailDto>($"/api/trips/{created.Id}");
        Assert.Equal(created.Id, opened!.Id);
        Assert.Equal(Start, opened.StartDate);
        Assert.Equal(Start.AddDays(2), opened.EndDate);
        Assert.Equal(TripBudgetLevel.Medium, opened.BudgetLevel);
        Assert.Equal("First visit", opened.Notes);
        Assert.Equal([1, 3], opened.Interests.Select(i => i.Id));

        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Trips.SingleAsync(t => t.Id == created.Id);
        Assert.Equal(factory.UserId, stored.UserId);
    }

    [Fact]
    public async Task BudgetAndStatusAreSentAsText()
    {
        using var client = await SignIn();
        var created = await CreateTrip(client, Paris() with { BudgetLevel = TripBudgetLevel.High });
        var json = await client.GetStringAsync($"/api/trips/{created.Id}");
        Assert.Contains("\"budgetLevel\":\"High\"", json);
        Assert.Contains("\"status\":\"Draft\"", json);
    }

    [Fact]
    public async Task SaveReplacesFieldsAndInterests()
    {
        using var client = await SignIn();
        var created = await CreateTrip(client);
        var changes = Paris() with
        {
            DestinationName = "Lyon", EndDate = Start.AddDays(4), BudgetLevel = TripBudgetLevel.Low,
            InterestIds = [3, 8], Notes = null
        };
        using var response = await client.PutAsJsonAsync($"/api/trips/{created.Id}", changes);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var saved = await client.GetFromJsonAsync<TripDetailDto>($"/api/trips/{created.Id}");
        Assert.Equal("Lyon", saved!.DestinationName);
        Assert.Equal(Start.AddDays(4), saved.EndDate);
        Assert.Equal(TripBudgetLevel.Low, saved.BudgetLevel);
        Assert.Equal(["History", "Art"], saved.Interests.Select(i => i.Name));
        Assert.Null(saved.Notes);
        Assert.True(saved.UpdatedAtUtc >= created.UpdatedAtUtc);
        Assert.Equal(created.CreatedAtUtc, saved.CreatedAtUtc);
    }

    [Fact]
    public async Task ATripThatAlreadyStartedCanStillBeSaved()
    {
        using var client = await SignIn();
        var created = await CreateTrip(client);
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await database.Trips.Where(t => t.Id == created.Id).ExecuteUpdateAsync(t => t.SetProperty(x => x.StartDate, yesterday));
        }
        using var unchangedStart = await client.PutAsJsonAsync($"/api/trips/{created.Id}", Paris() with { StartDate = yesterday, EndDate = yesterday.AddDays(3), Notes = "Day 2 notes" });
        Assert.Equal(HttpStatusCode.OK, unchangedStart.StatusCode);
        using var movedIntoPast = await client.PutAsJsonAsync($"/api/trips/{created.Id}", Paris() with { StartDate = yesterday.AddDays(-1), EndDate = yesterday.AddDays(3) });
        Assert.Equal(HttpStatusCode.BadRequest, movedIntoPast.StatusCode);
    }

    [Fact]
    public async Task DeleteRemovesTheTrip()
    {
        using var client = await SignIn();
        var created = await CreateTrip(client);
        using var deleted = await client.DeleteAsync($"/api/trips/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var opened = await client.GetAsync($"/api/trips/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, opened.StatusCode);
        using var deletedAgain = await client.DeleteAsync($"/api/trips/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deletedAgain.StatusCode);

        using var scope = factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().TripInterests.AnyAsync(ti => ti.TripId == created.Id));
    }

    [Fact]
    public async Task OtherUsersTripsAreInvisible()
    {
        using var owner = await SignIn();
        var trip = await CreateTrip(owner);
        using var other = await SignIn(OtherEmail);

        using (var open = await other.GetAsync($"/api/trips/{trip.Id}")) Assert.Equal(HttpStatusCode.NotFound, open.StatusCode);
        using (var save = await other.PutAsJsonAsync($"/api/trips/{trip.Id}", Paris() with { DestinationName = "Hijacked" }))
            Assert.Equal(HttpStatusCode.NotFound, save.StatusCode);
        using (var delete = await other.DeleteAsync($"/api/trips/{trip.Id}")) Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        var list = await other.GetFromJsonAsync<PagedResult<TripSummaryDto>>("/api/trips");
        Assert.DoesNotContain(list!.Items, t => t.Id == trip.Id);

        var unchanged = await owner.GetFromJsonAsync<TripDetailDto>($"/api/trips/{trip.Id}");
        Assert.Equal("Paris", unchanged!.DestinationName);
    }

    [Fact]
    public async Task UnknownTripReturns404()
    {
        using var client = await SignIn();
        using var response = await client.GetAsync("/api/trips/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Trip not found.", (await response.Content.ReadFromJsonAsync<ApiError>())!.Message);
    }

    [Fact]
    public async Task ListIsPagedAndOnlyShowsMyTrips()
    {
        const string email = "pager@example.com";
        using var client = await SignIn(email);
        for (var i = 0; i < 5; i++)
            await CreateTrip(client, Paris() with { DestinationName = $"City {i}", StartDate = Start.AddDays(i), EndDate = Start.AddDays(i) });
        using var other = await SignIn(OtherEmail);
        await CreateTrip(other);

        var first = await client.GetFromJsonAsync<PagedResult<TripSummaryDto>>("/api/trips?page=1&pageSize=2");
        Assert.Equal(5, first!.TotalCount);
        Assert.Equal(3, first.TotalPages);
        Assert.Equal(["City 4", "City 3"], first.Items.Select(t => t.DestinationName));
        Assert.Equal(["Food", "History"], first.Items[0].Interests);

        var last = await client.GetFromJsonAsync<PagedResult<TripSummaryDto>>("/api/trips?page=3&pageSize=2");
        Assert.Equal(["City 0"], last!.Items.Select(t => t.DestinationName));
        var beyond = await client.GetFromJsonAsync<PagedResult<TripSummaryDto>>("/api/trips?page=4&pageSize=2");
        Assert.Empty(beyond!.Items);

        var defaults = await client.GetFromJsonAsync<PagedResult<TripSummaryDto>>("/api/trips");
        Assert.Equal(1, defaults!.Page);
        Assert.Equal(20, defaults.PageSize);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?page=-1")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=51")]
    public async Task InvalidPagingReturns400(string query)
    {
        using var client = await SignIn();
        using var response = await client.GetAsync("/api/trips" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public static IEnumerable<object[]> InvalidTrips()
    {
        var trip = Paris();
        yield return ["DestinationName", trip with { DestinationName = " " }];
        yield return ["DestinationName", trip with { DestinationName = new string('x', 201) }];
        yield return ["DestinationCountryCode", trip with { DestinationCountryCode = "France" }];
        yield return ["StartDate", trip with { StartDate = default }];
        yield return ["StartDate", trip with { StartDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1), EndDate = DateOnly.FromDateTime(DateTime.UtcNow) }];
        yield return ["EndDate", trip with { EndDate = trip.StartDate.AddDays(-1) }];
        yield return ["EndDate", trip with { EndDate = trip.StartDate.AddDays(TripInput.MaximumDays) }];
        yield return ["InterestIds", trip with { InterestIds = [] }];
        yield return ["InterestIds", trip with { InterestIds = [1, 1] }];
        yield return ["InterestIds", trip with { InterestIds = [1, 9999] }];
        yield return ["InterestIds", trip with { InterestIds = [0] }];
        yield return ["InterestIds", trip with { InterestIds = Enumerable.Range(1, 11).ToArray() }];
        yield return ["BudgetLevel", trip with { BudgetLevel = (TripBudgetLevel)99 }];
        yield return ["Notes", trip with { Notes = new string('x', 2001) }];
    }

    [Theory, MemberData(nameof(InvalidTrips))]
    public async Task InvalidTripsReturn400AndAreNotSaved(string field, TripRequest trip)
    {
        using var client = await SignIn();
        var before = await CountMyTrips();
        using var response = await client.PostAsJsonAsync("/api/trips", trip);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;
        Assert.Contains(problem.Errors.Keys, key => key.Equals(field, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(before, await CountMyTrips());

        var existing = await CreateTrip(client);
        using var save = await client.PutAsJsonAsync($"/api/trips/{existing.Id}", trip);
        Assert.Equal(HttpStatusCode.BadRequest, save.StatusCode);
    }

    [Theory]
    [InlineData("{\"destinationName\":\"Paris\",\"startDate\":\"2099-01-01\",\"endDate\":\"2099-01-02\",\"budgetLevel\":\"Luxury\",\"interestIds\":[1]}")]
    [InlineData("{\"destinationName\":\"Paris\",\"startDate\":\"2099-01-01\",\"endDate\":\"2099-01-02\",\"budgetLevel\":\"Low\"}")]
    [InlineData("{\"destinationName\":\"Paris\",\"startDate\":\"2099-01-01\",\"endDate\":\"2099-01-02\",\"budgetLevel\":\"Low\",\"interestIds\":null}")]
    public async Task MalformedBodiesReturn400(string json)
    {
        using var client = await SignIn();
        using var response = await client.PostAsync("/api/trips", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<int> CountMyTrips()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Trips.CountAsync(t => t.UserId == factory.UserId);
    }
}
