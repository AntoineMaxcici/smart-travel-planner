using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using TravelPlanner.Api.Data;
using TravelPlanner.Api.Data.Entities;
using TravelPlanner.Api.Itineraries;
using TravelPlanner.Core.Dtos;
using TravelPlanner.Core.Services;

namespace TravelPlanner.Api.Tests;

public sealed class TripGenerationTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private sealed class Generator : IItineraryGenerator
    {
        public int Calls;
        public Func<TripInput, CancellationToken, Task<IReadOnlyList<ItineraryDayDto>>> Work =
            (input, _) => Task.FromResult(ItineraryTestData.Days(input));
        public Task<IReadOnlyList<ItineraryDayDto>> GenerateAsync(TripInput input, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            return Work(input, cancellationToken);
        }
    }

    private sealed class Enricher : IPlacesEnricher
    {
        public bool Fail;
        public int Calls;
        public Func<CancellationToken, Task>? Work;
        public Task EnrichAsync(Itinerary itinerary, CancellationToken cancellationToken = default)
        {
            Calls++;
            Assert.True(itinerary.Id > 0);
            Assert.All(itinerary.Days.SelectMany(d => d.Stops), s => Assert.True(s.Id > 0));
            if (Fail) throw new InvalidOperationException("Private provider failure");
            itinerary.Days[0].Stops[0].DisplayAddress = "Enriched address";
            return Work?.Invoke(cancellationToken) ?? Task.CompletedTask;
        }
    }

    private WebApplicationFactory<Program> Host(Generator generator, Enricher? enricher = null,
        FakeTimeProvider? clock = null, int limit = 1000) => factory.WithWebHostBuilder(builder =>
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:ItineraryGeneration:PermitLimit"] = limit.ToString()
        }));
        builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IItineraryGenerator>(generator));
            if (enricher is not null) services.Replace(ServiceDescriptor.Singleton<IPlacesEnricher>(enricher));
            if (clock is not null) services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
        });
    });

    private static async Task<HttpClient> SignIn(WebApplicationFactory<Program> host, string email = AuthApiFactory.Email)
    {
        var client = host.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = AuthApiFactory.Password });
        response.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await response.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        return client;
    }

    private static TripRequest Request()
    {
        var input = ItineraryTestData.Trip();
        return new TripRequest
        {
            DestinationName = input.DestinationName, DestinationCountryCode = input.DestinationCountryCode,
            StartDate = input.StartDate, EndDate = input.EndDate, BudgetLevel = input.BudgetLevel, InterestIds = [1, 3]
        };
    }

    private static async Task<int> Create(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/trips", Request());
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TripDetailDto>())!.Id;
    }

    private async Task CheckFailed(int id, string code, int itineraries = 0)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var trip = await db.Trips.Include(t => t.GenerationRuns).Include(t => t.Itineraries).SingleAsync(t => t.Id == id);
        Assert.Equal(TripStatus.Failed, trip.Status);
        var run = trip.GenerationRuns.OrderByDescending(r => r.Id).First();
        Assert.Equal(GenerationStatus.Failed, run.Status);
        Assert.Equal(code, run.ErrorCode);
        Assert.NotNull(run.CompletedAtUtc);
        Assert.NotNull(run.DurationMilliseconds);
        Assert.Equal(itineraries, trip.Itineraries.Count);
    }

    [Fact]
    public async Task SavesRevisionsAndCallsEnricherAfterSavingStops()
    {
        var generator = new Generator();
        var enricher = new Enricher();
        using var host = Host(generator, enricher);
        using var client = await SignIn(host);
        var id = await Create(client);
        for (var revision = 1; revision <= 2; revision++)
        {
            using var response = await client.PostAsync($"/api/trips/{id}/generate", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = (await response.Content.ReadFromJsonAsync<TripGenerationDto>())!;
            Assert.Equal(revision, result.RevisionNumber);
            Assert.Equal(TripStatusDto.Ready, result.Status);
            Assert.Equal("Enriched address", result.Days[0].Stops[0].DisplayAddress);
        }
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var trip = await db.Trips.Include(t => t.GenerationRuns).Include(t => t.Itineraries)
            .ThenInclude(i => i.Days).ThenInclude(d => d.Stops).SingleAsync(t => t.Id == id);
        Assert.Equal(TripStatus.Ready, trip.Status);
        Assert.All(trip.GenerationRuns, r => { Assert.Equal(GenerationStatus.Succeeded, r.Status); Assert.NotNull(r.CompletedAtUtc); Assert.Null(r.ErrorCode); });
        Assert.Equal(2, Assert.Single(trip.Itineraries, i => i.IsCurrent).RevisionNumber);
        Assert.Equal(2, trip.Itineraries.Count);
        Assert.All(trip.Itineraries, i => { Assert.Equal(2, i.Days.Count); Assert.All(i.Days, d => Assert.Equal(3, d.Stops.Count)); });
        Assert.Equal(2, enricher.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BadJsonOrContentIsRetriedOnce(bool alwaysBad)
    {
        var generator = new Generator();
        generator.Work = (input, _) => generator.Calls == 1 || alwaysBad
            ? throw new JsonException("Private malformed payload") : Task.FromResult(ItineraryTestData.Days(input));
        using var host = Host(generator);
        using var client = await SignIn(host);
        var id = await Create(client);
        using var response = await client.PostAsync($"/api/trips/{id}/generate", null);
        Assert.Equal(alwaysBad ? HttpStatusCode.BadGateway : HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, generator.Calls);
        if (alwaysBad)
        {
            var error = (await response.Content.ReadFromJsonAsync<TripGenerationErrorDto>())!;
            Assert.Equal("InvalidResponse", error.Code);
            Assert.DoesNotContain("Private", error.Message);
            await CheckFailed(id, "InvalidResponse");
        }
    }

    [Fact]
    public async Task InvalidDaysAreRetriedThenRejectedWithoutPersistence()
    {
        var generator = new Generator { Work = (_, _) => Task.FromResult<IReadOnlyList<ItineraryDayDto>>([]) };
        using var host = Host(generator);
        using var client = await SignIn(host);
        var id = await Create(client);
        using var response = await client.PostAsync($"/api/trips/{id}/generate", null);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(2, generator.Calls);
        await CheckFailed(id, "InvalidResponse");
    }

    [Theory]
    [InlineData(GenerationFailure.Unavailable, 503)]
    [InlineData(GenerationFailure.RateLimited, 429)]
    [InlineData(GenerationFailure.Blocked, 502)]
    [InlineData(GenerationFailure.NotConfigured, 503)]
    public async Task ProviderFailuresAreNotRetried(GenerationFailure failure, int status)
    {
        var generator = new Generator { Work = (_, _) => throw new ItineraryGenerationException(failure) };
        using var host = Host(generator);
        using var client = await SignIn(host);
        var id = await Create(client);
        using var response = await client.PostAsync($"/api/trips/{id}/generate", null);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(1, generator.Calls);
        await CheckFailed(id, failure.ToString());
    }

    [Fact]
    public async Task TimeoutCoversBothAttemptsAndPendingStateIsVisible()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var generator = new Generator();
        generator.Work = async (input, token) =>
        {
            if (generator.Calls == 1) { clock.Advance(TimeSpan.FromSeconds(15)); throw new JsonException(); }
            entered.SetResult();
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
            return ItineraryTestData.Days(input);
        };
        using var host = Host(generator, clock: clock);
        using var client = await SignIn(host);
        var id = await Create(client);
        var pending = client.PostAsync($"/api/trips/{id}/generate", null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(TripStatus.Generating, (await db.Trips.FindAsync(id))!.Status);
            Assert.Equal(GenerationStatus.Pending, (await db.GenerationRuns.SingleAsync(r => r.TripId == id)).Status);
        }
        using var duplicate = await client.PostAsync($"/api/trips/{id}/generate", null);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var edit = await client.PutAsJsonAsync($"/api/trips/{id}", Request());
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        using var delete = await client.DeleteAsync($"/api/trips/{id}");
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        clock.Advance(TimeSpan.FromSeconds(5));
        using var response = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Equal(2, generator.Calls);
        await CheckFailed(id, "Timeout");
    }

    [Fact]
    public async Task EnrichmentFailureRollsBackNewRevisionAndKeepsPreviousCurrent()
    {
        var generator = new Generator();
        var enricher = new Enricher();
        using var host = Host(generator, enricher);
        using var client = await SignIn(host);
        var id = await Create(client);
        using var first = await client.PostAsync($"/api/trips/{id}/generate", null);
        first.EnsureSuccessStatusCode();
        enricher.Fail = true;
        using var failed = await client.PostAsync($"/api/trips/{id}/generate", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.DoesNotContain("Private", await failed.Content.ReadAsStringAsync());
        await CheckFailed(id, "Unavailable", 1);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True((await db.Itineraries.SingleAsync(i => i.TripId == id)).IsCurrent);
        enricher.Fail = false;
        using var retry = await client.PostAsync($"/api/trips/{id}/generate", null);
        retry.EnsureSuccessStatusCode();
        Assert.Equal(2, (await retry.Content.ReadFromJsonAsync<TripGenerationDto>())!.RevisionNumber);
    }

    [Fact]
    public async Task OwnershipAnonymousMissingAndQuotaAreEnforced()
    {
        var generator = new Generator();
        using var host = Host(generator, limit: 3);
        using var client = await SignIn(host);
        var id = await Create(client);
        using var anonymous = host.CreateClient();
        using var unauthorized = await anonymous.PostAsync($"/api/trips/{id}/generate", null);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        using var other = await SignIn(host, "bruteforce@example.com");
        using var forbidden = await other.PostAsync($"/api/trips/{id}/generate", null);
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
        using var missing = await client.PostAsync("/api/trips/2147483647/generate", null);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        // Preview and persisted generation share the same user quota.
        using var preview = await client.PostAsJsonAsync("/api/itineraries/generate", ItineraryTestData.Trip());
        preview.EnsureSuccessStatusCode();
        using var success = await client.PostAsync($"/api/trips/{id}/generate", null);
        success.EnsureSuccessStatusCode();
        using var limited = await client.PostAsync($"/api/trips/{id}/generate", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        Assert.Equal(2, generator.Calls);
    }

    [Fact]
    public async Task EnrichmentSharesTheTwentySecondDeadlineAndRollsBack()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var generator = new Generator();
        generator.Work = (input, _) =>
        {
            clock.Advance(TimeSpan.FromSeconds(12));
            return Task.FromResult(ItineraryTestData.Days(input));
        };
        var enricher = new Enricher
        {
            Work = async token => { entered.SetResult(); await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token); }
        };
        using var host = Host(generator, enricher, clock);
        using var client = await SignIn(host);
        var id = await Create(client);
        var pending = client.PostAsync($"/api/trips/{id}/generate", null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        clock.Advance(TimeSpan.FromSeconds(8));
        using var response = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Equal(1, generator.Calls);
        await CheckFailed(id, "Timeout");
    }

    [Fact]
    public async Task RequestCancellationStillRecordsFailure()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var generator = new Generator
        {
            Work = async (input, token) =>
            {
                entered.SetResult();
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
                return ItineraryTestData.Days(input);
            }
        };
        using var host = Host(generator);
        using var client = await SignIn(host);
        var id = await Create(client);
        // Invoke the scoped workflow directly so the test waits for cancellation cleanup,
        // rather than merely observing that HttpClient stopped waiting for a response.
        using var scope = host.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TripGenerationService>();
        using var cancellation = new CancellationTokenSource();
        var pending = service.GenerateAsync(id, factory.UserId, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await CheckFailed(id, "Cancelled");
    }

    [Fact]
    public async Task PastTripCannotStartAGenerationRun()
    {
        var generator = new Generator();
        using var host = Host(generator);
        using var client = await SignIn(host);
        var id = await Create(client);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var trip = (await db.Trips.FindAsync(id))!;
            trip.StartDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
            trip.EndDate = trip.StartDate.AddDays(1);
            await db.SaveChangesAsync();
        }
        using var response = await client.PostAsync($"/api/trips/{id}/generate", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, generator.Calls);
        using var check = factory.Services.CreateScope();
        Assert.False(await check.ServiceProvider.GetRequiredService<AppDbContext>().GenerationRuns.AnyAsync(r => r.TripId == id));
    }
}
