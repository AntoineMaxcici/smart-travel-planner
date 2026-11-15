using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TravelPlanner.Api.Itineraries;
using TravelPlanner.Core.Dtos;
using TravelPlanner.Core.Services;

namespace TravelPlanner.Api.Tests;

public sealed class ItineraryEndpointTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private async Task<HttpClient> AuthenticatedClient(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = AuthApiFactory.Email, Password = AuthApiFactory.Password });
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
}
