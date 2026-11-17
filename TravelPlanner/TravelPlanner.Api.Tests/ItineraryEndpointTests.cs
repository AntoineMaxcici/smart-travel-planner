using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TravelPlanner.Api.Itineraries;
using TravelPlanner.Api.Data;
using TravelPlanner.Core.Dtos;
using TravelPlanner.Core.Services;

namespace TravelPlanner.Api.Tests;

public sealed class ItineraryEndpointTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private async Task<HttpClient> AuthenticatedClient(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host, string email = AuthApiFactory.Email)
    {
        var client = host.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = AuthApiFactory.Password });
        response.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await response.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        return client;
    }

    [Theory]
    [InlineData("Paris")]
    [InlineData("Tokyo")]
    [InlineData("Beirut")]
    public async Task GeneratesDaysWithInjectedFakeWithoutCallingGemini(string city)
    {
        var fake = new FakeItineraryGenerator();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<IItineraryGenerator>(fake))));
        using var client = await AuthenticatedClient(host);
        using var response = await client.PostAsJsonAsync("/api/itineraries/generate", ItineraryTestData.Trip(city));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var draft = (await response.Content.ReadFromJsonAsync<ItineraryDraftDto>())!;
        ItineraryValidation.ValidateDays(ItineraryTestData.Trip(city), draft.Days);
        var call = Assert.Single(fake.Calls);
        Assert.Equal(city, call.DestinationName);
        Assert.Equal(ItineraryTestData.Trip(city).Interests, call.Interests);
        Assert.True(fake.LastCancellationToken.CanBeCanceled);
    }

    [Fact]
    public async Task AnonymousRequestsNeverReachGenerator()
    {
        var fake = new FakeItineraryGenerator();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<IItineraryGenerator>(fake))));
        using var client = host.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/itineraries/generate", ItineraryTestData.Trip());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(fake.Calls);
    }

    public static IEnumerable<object[]> InvalidInputs()
    {
        var trip = ItineraryTestData.Trip();
        yield return [trip with { DestinationName = " " }];
        yield return [trip with { DestinationName = new string('x', 201) }];
        yield return [trip with { DestinationCountryCode = "France" }];
        yield return [trip with { StartDate = default }];
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        yield return [trip with { StartDate = yesterday, EndDate = yesterday.AddDays(1) }];
        yield return [trip with { EndDate = default }];
        yield return [trip with { EndDate = trip.StartDate.AddDays(-1) }];
        yield return [trip with { EndDate = trip.StartDate.AddDays(TripInput.MaximumDays) }];
        yield return [trip with { Interests = [" "] }];
        yield return [trip with { Interests = [new string('x', 101)] }];
        yield return [trip with { Interests = Enumerable.Repeat("history", 11).ToArray() }];
        yield return [trip with { BudgetLevel = (TripBudgetLevel)99 }];
        yield return [trip with { Notes = new string('x', 2001) }];
    }

    [Theory, MemberData(nameof(InvalidInputs))]
    public async Task InvalidTripsReturn400WithoutCallingGenerator(TripInput trip)
    {
        var fake = new FakeItineraryGenerator();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<IItineraryGenerator>(fake))));
        using var client = await AuthenticatedClient(host);
        using var response = await client.PostAsJsonAsync("/api/itineraries/generate", trip);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(fake.Calls);
    }

    [Theory]
    [InlineData(GenerationFailure.NotConfigured, 503)]
    [InlineData(GenerationFailure.Unavailable, 503)]
    [InlineData(GenerationFailure.RateLimited, 429)]
    [InlineData(GenerationFailure.Timeout, 504)]
    [InlineData(GenerationFailure.Blocked, 502)]
    [InlineData(GenerationFailure.InvalidResponse, 502)]
    public async Task GenerationFailuresReturnHelpfulErrors(GenerationFailure failure, int status)
    {
        var fake = new FakeItineraryGenerator { Generate = _ => throw new ItineraryGenerationException(failure) };
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<IItineraryGenerator>(fake))));
        using var client = await AuthenticatedClient(host);
        using var response = await client.PostAsJsonAsync("/api/itineraries/generate", ItineraryTestData.Trip());
        Assert.Equal(status, (int)response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace((await response.Content.ReadFromJsonAsync<ApiError>())!.Message));
    }

    [Fact]
    public async Task InvalidFakeOutputIsRejectedAtTheBoundary()
    {
        var fake = new FakeItineraryGenerator { Generate = _ => [] };
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<IItineraryGenerator>(fake))));
        using var client = await AuthenticatedClient(host);
        using var response = await client.PostAsJsonAsync("/api/itineraries/generate", ItineraryTestData.Trip());
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> WithGenerationLimit(FakeItineraryGenerator fake, string permitLimit) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:ItineraryGeneration:PermitLimit"] = permitLimit,
                ["RateLimiting:ItineraryGeneration:WindowSeconds"] = "3600"
            }));
            builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<IItineraryGenerator>(fake)));
        });

    [Fact]
    public async Task GenerationOverTheLimitGets429WithoutCallingGenerator()
    {
        var fake = new FakeItineraryGenerator();
        using var host = WithGenerationLimit(fake, "2");
        using var client = await AuthenticatedClient(host);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var allowed = await client.PostAsJsonAsync("/api/itineraries/generate", ItineraryTestData.Trip());
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }
        using var blocked = await client.PostAsJsonAsync("/api/itineraries/generate", ItineraryTestData.Trip());
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal("You've reached the itinerary generation limit. Please try again later.", (await blocked.Content.ReadFromJsonAsync<ApiError>())!.Message);
        Assert.Equal(2, fake.Calls.Count);
    }

    [Fact]
    public async Task AnonymousRequestsDoNotUseUpTheLimit()
    {
        var fake = new FakeItineraryGenerator();
        using var host = WithGenerationLimit(fake, "1");
        using (var anonymous = host.CreateClient())
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                using var rejected = await anonymous.PostAsJsonAsync("/api/itineraries/generate", ItineraryTestData.Trip());
                Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
            }
        }
        Assert.Empty(fake.Calls);
        using var client = await AuthenticatedClient(host);
        using var allowed = await client.PostAsJsonAsync("/api/itineraries/generate", ItineraryTestData.Trip());
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Single(fake.Calls);
    }

    [Fact]
    public async Task UsersOnTheSameIpHaveSeparateLimitsAndNewTokensDoNotResetTheirQuota()
    {
        var fake = new FakeItineraryGenerator();
        using var host = WithGenerationLimit(fake, "1");
        using var first = await AuthenticatedClient(host);
        using var allowedFirst = await first.PostAsJsonAsync("/api/itineraries/generate", ItineraryTestData.Trip());
        Assert.Equal(HttpStatusCode.OK, allowedFirst.StatusCode);

        using var newToken = await AuthenticatedClient(host);
        Assert.NotEqual(first.DefaultRequestHeaders.Authorization!.Parameter, newToken.DefaultRequestHeaders.Authorization!.Parameter);
        using var blockedFirst = await newToken.PostAsJsonAsync("/api/itineraries/generate", ItineraryTestData.Trip());
        Assert.Equal(HttpStatusCode.TooManyRequests, blockedFirst.StatusCode);

        var secondEmail = $"quota-{Guid.NewGuid():N}@example.com";
        using (var scope = host.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var created = await users.CreateAsync(new ApplicationUser { UserName = secondEmail, Email = secondEmail, EmailConfirmed = true }, AuthApiFactory.Password);
            Assert.True(created.Succeeded);
        }
        using var second = await AuthenticatedClient(host, secondEmail);
        using var allowedSecond = await second.PostAsJsonAsync("/api/itineraries/generate", ItineraryTestData.Trip());
        Assert.Equal(HttpStatusCode.OK, allowedSecond.StatusCode);
        using var blockedSecond = await second.PostAsJsonAsync("/api/itineraries/generate", ItineraryTestData.Trip());
        Assert.Equal(HttpStatusCode.TooManyRequests, blockedSecond.StatusCode);
        Assert.Equal(2, fake.Calls.Count);
    }

    [Fact]
    public async Task TripsStartingTodayAreAccepted()
    {
        var fake = new FakeItineraryGenerator();
        using var host = WithGenerationLimit(fake, "1");
        using var client = await AuthenticatedClient(host);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        using var response = await client.PostAsJsonAsync("/api/itineraries/generate", ItineraryTestData.Trip() with { StartDate = today, EndDate = today });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(fake.Calls);
    }
}
