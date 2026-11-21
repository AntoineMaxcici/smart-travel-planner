using System.Net;
using System.Net.Http.Json;
using System.Text;
using TravelPlanner.Core.Dtos;
using TravelPlanner.Core.Services;

namespace TravelPlanner.Core.Tests;

public sealed class TripsApiTests
{
    private static readonly Uri BaseAddress = new("https://api.example/");

    private static HttpClient Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) =>
        new(new StubHttpHandler(send)) { BaseAddress = BaseAddress };

    private static HttpClient Returning(HttpStatusCode status, string body = "", string mediaType = "application/json") =>
        Client(_ => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) }));

    private static TripDetailDto Detail(int id = 5) => new()
    {
        Id = id,
        DestinationName = "Lisbon",
        DestinationCountryCode = "PT",
        StartDate = new DateOnly(2026, 12, 1),
        EndDate = new DateOnly(2026, 12, 4),
        BudgetLevel = TripBudgetLevel.Low,
        Status = TripStatusDto.Draft,
        Interests = [new InterestDto(1, "Food", null)],
        Notes = null,
        CreatedAtUtc = new DateTime(2026, 11, 21, 9, 0, 0, DateTimeKind.Utc),
        UpdatedAtUtc = new DateTime(2026, 11, 21, 9, 0, 0, DateTimeKind.Utc)
    };

    private static readonly TripRequest Request = new()
    {
        DestinationName = "Lisbon",
        StartDate = new DateOnly(2026, 12, 1),
        EndDate = new DateOnly(2026, 12, 4),
        BudgetLevel = TripBudgetLevel.Low,
        InterestIds = [1]
    };

    [Fact]
    public async Task GetTripsRequestsThePageAndReadsIt()
    {
        var page = new PagedResult<TripSummaryDto>
        {
            Items = [TripTestData.Summary(3, "Kyoto", 10, interests: ["Art"])], Page = 2, PageSize = 10, TotalCount = 11
        };
        using var client = Client(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/trips?page=2&pageSize=10", request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(page) });
        });
        var result = await new TripsApi(client).GetTripsAsync(2, 10);
        Assert.Equal(11, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal("Kyoto", Assert.Single(result.Items).DestinationName);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 51)]
    public async Task GetTripsRejectsPagesTheApiWouldRefuse(int page, int pageSize)
    {
        using var client = Returning(HttpStatusCode.OK);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new TripsApi(client).GetTripsAsync(page, pageSize));
    }

    [Fact]
    public async Task CreatePostsTheTripAndReadsTheCreatedTrip()
    {
        using var client = Client(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/trips", request.RequestUri!.AbsolutePath);
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("\"budgetLevel\":\"Low\"", body);
            Assert.Contains("\"startDate\":\"2026-12-01\"", body);
            Assert.Contains("\"interestIds\":[1]", body);
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(Detail()) };
        });
        var created = await new TripsApi(client).CreateTripAsync(Request);
        Assert.Equal(5, created.Id);
        Assert.Equal("Food", Assert.Single(created.Interests).Name);
    }

    [Fact]
    public async Task SavePutsTheTripToItsAddress()
    {
        using var client = Client(request =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("/api/trips/5", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Detail()) });
        });
        Assert.Equal(5, (await new TripsApi(client).SaveTripAsync(5, Request)).Id);
    }

    [Fact]
    public async Task DeleteSendsDeleteAndAcceptsNoContent()
    {
        HttpRequestMessage? sent = null;
        using var client = Client(request =>
        {
            sent = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });
        await new TripsApi(client).DeleteTripAsync(5);
        Assert.Equal(HttpMethod.Delete, sent!.Method);
        Assert.Equal("/api/trips/5", sent.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetInterestsReadsTheList()
    {
        using var client = Client(request =>
        {
            Assert.Equal("/api/interests", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(FakeTripsApi.SampleInterests) });
        });
        var interests = await new TripsApi(client).GetInterestsAsync();
        Assert.Equal(8, interests.Count);
        Assert.Equal("Food", interests[0].Name);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest,
        "{\"title\":\"One or more validation errors occurred.\",\"errors\":{\"StartDate\":[\"The trip cannot start in the past.\"],\"InterestIds\":[\"Unknown interest IDs: 99.\"]}}",
        "The trip cannot start in the past. Unknown interest IDs: 99.")]
    [InlineData(HttpStatusCode.BadRequest, "{\"title\":\"One or more validation errors occurred.\"}", "Check the trip details and try again.")]
    [InlineData(HttpStatusCode.BadRequest, "not json", "Check the trip details and try again.")]
    [InlineData(HttpStatusCode.NotFound, "{\"message\":\"Trip not found.\"}", "Trip not found.")]
    [InlineData(HttpStatusCode.NotFound, "", "Trip not found.")]
    [InlineData(HttpStatusCode.TooManyRequests, "", "Too many requests. Please wait a moment and try again.")]
    [InlineData(HttpStatusCode.InternalServerError, "", "Your trip couldn't be created. Please try again.")]
    public async Task TranslatesErrorsIntoReadableMessages(HttpStatusCode status, string body, string expected)
    {
        using var client = Returning(status, body);
        var error = await Assert.ThrowsAsync<TripsApiException>(() => new TripsApi(client).CreateTripAsync(Request));
        Assert.Equal(expected, error.Message);
    }

    [Fact]
    public async Task UnauthorizedMeansTheSessionEnded()
    {
        using var client = Returning(HttpStatusCode.Unauthorized);
        await Assert.ThrowsAsync<UnauthorizedSessionException>(() => new TripsApi(client).GetTripsAsync());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    public async Task InvalidResponseBodiesBecomeReadableErrors(string body)
    {
        using var client = Returning(HttpStatusCode.OK, body);
        var error = await Assert.ThrowsAsync<TripsApiException>(() => new TripsApi(client).GetTripAsync(5));
        Assert.Equal("The server returned an invalid response.", error.Message);
    }

    [Fact]
    public async Task RequestsGoThroughTheBearerTokenHandler()
    {
        var session = new StubSession { User = TestTokens.User, Token = "access-token" };
        string? authorization = null;
        using var client = new HttpClient(new BearerTokenHandler(session, BaseAddress)
        {
            InnerHandler = new StubHttpHandler(request =>
            {
                authorization = request.Headers.Authorization?.ToString();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            })
        }) { BaseAddress = BaseAddress };
        await Assert.ThrowsAsync<UnauthorizedSessionException>(() => new TripsApi(client).GetInterestsAsync());
        Assert.Equal("Bearer access-token", authorization);
        // The handler ended the session, which sends the app back to sign-in.
        Assert.Equal("access-token", session.RejectedToken);
        Assert.Null(session.Token);
    }
}

public sealed class FakeTripsApiTests
{
    private static FakeTripsApi Create(bool samples = true) => new(TripTestData.Clock(), includeSampleTrips: samples);

    [Fact]
    public async Task SampleTripsAreReturnedNewestStartDateFirst()
    {
        var page = await Create().GetTripsAsync();
        Assert.Equal(4, page.TotalCount);
        Assert.Equal(new[] { "Marrakesh", "Kyoto", "Lisbon", "Reykjavík" }, page.Items.Select(t => t.DestinationName).ToArray());
        Assert.All(page.Items, trip => Assert.NotEmpty(trip.Interests));
    }

    [Fact]
    public async Task PagesSplitTheTrips()
    {
        var fake = Create();
        var second = await fake.GetTripsAsync(2, 3);
        Assert.Equal("Reykjavík", Assert.Single(second.Items).DestinationName);
        Assert.Equal(2, second.TotalPages);
    }

    [Fact]
    public async Task CreateDeleteAndGetBehaveLikeTheApi()
    {
        var fake = Create(samples: false);
        var created = await fake.CreateTripAsync(new TripRequest
        {
            DestinationName = " Porto ", StartDate = TripTestData.Today.AddDays(3), EndDate = TripTestData.Today.AddDays(5),
            InterestIds = [2, 1]
        });
        Assert.Equal("Porto", created.DestinationName);
        Assert.Equal(TripStatusDto.Draft, created.Status);
        Assert.Equal(new[] { "Food", "Museums" }, created.Interests.Select(i => i.Name).ToArray());
        Assert.Equal(created, await fake.GetTripAsync(created.Id));
        await fake.DeleteTripAsync(created.Id);
        var error = await Assert.ThrowsAsync<TripsApiException>(() => fake.GetTripAsync(created.Id));
        Assert.Equal("Trip not found.", error.Message);
        Assert.Equal(0, (await fake.GetTripsAsync()).TotalCount);
    }

    [Theory]
    [InlineData(-1, 2, new[] { 1 }, "The trip cannot start in the past.")]
    [InlineData(3, 2, new int[0], "Choose at least one interest.")]
    [InlineData(3, 4, new[] { 99 }, "Unknown interest IDs: 99.")]
    public async Task CreateValidatesLikeTheApi(int startInDays, int endInDays, int[] interestIds, string expected)
    {
        var error = await Assert.ThrowsAsync<TripsApiException>(() => Create(samples: false).CreateTripAsync(new TripRequest
        {
            DestinationName = "Porto", StartDate = TripTestData.Today.AddDays(startInDays),
            EndDate = TripTestData.Today.AddDays(endInDays), InterestIds = interestIds
        }));
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public async Task InterestsMatchTheSeededDatabaseList()
    {
        var interests = await Create().GetInterestsAsync();
        Assert.Equal(Enumerable.Range(1, 8), interests.Select(i => i.Id));
    }
}
