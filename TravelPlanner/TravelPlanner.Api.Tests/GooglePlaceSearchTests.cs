using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TravelPlanner.Api.Places;

namespace TravelPlanner.Api.Tests;

public sealed class GooglePlaceSearchTests
{
    private const string TestKey = "fake-places-key-for-tests-only";

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return await send(request);
        }
    }

    private static (GooglePlaceSearch Search, Handler Handler) Create(Func<HttpRequestMessage, Task<HttpResponseMessage>> send, string key = TestKey)
    {
        var handler = new Handler(send);
        return (new GooglePlaceSearch(new HttpClient(handler), Options.Create(new GooglePlacesOptions { ApiKey = key })), handler);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private const string Louvre = """
        {"places":[{"id":"ChIJLouvre","displayName":{"text":"Louvre Museum","languageCode":"en"},
          "formattedAddress":"75001 Paris, France","location":{"latitude":48.8606,"longitude":2.3376},
          "primaryTypeDisplayName":{"text":"Art museum"},"googleMapsUri":"https://maps.google.com/?cid=1",
          "regularOpeningHours":{"weekdayDescriptions":["Monday: 9:00 AM – 6:00 PM","Tuesday: Closed"]}}]}
        """;

    [Fact]
    public async Task SendsATextSearchWithKeyFieldMaskAndLocationBias()
    {
        var (search, handler) = Create(_ => Task.FromResult(Json(Louvre)));

        var match = await search.FindAsync(new PlaceQuery("Louvre, Paris", "FR", 48.86, 2.33));

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(GooglePlaceSearch.Endpoint, request.RequestUri!.ToString());
        Assert.Equal(TestKey, request.Headers.GetValues("X-Goog-Api-Key").Single());
        Assert.Equal(GooglePlaceSearch.FieldMask, request.Headers.GetValues("X-Goog-FieldMask").Single());
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Louvre, Paris", json.RootElement.GetProperty("textQuery").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal("FR", json.RootElement.GetProperty("regionCode").GetString());
        Assert.Equal(48.86, json.RootElement.GetProperty("locationBias").GetProperty("circle").GetProperty("center").GetProperty("latitude").GetDouble());

        Assert.NotNull(match);
        Assert.Equal("ChIJLouvre", match.GooglePlaceId);
        Assert.Equal("Louvre Museum", match.Name);
        Assert.Equal("75001 Paris, France", match.FormattedAddress);
        Assert.Equal(48.8606, match.Latitude);
        Assert.Equal("Art museum", match.PrimaryCategory);
        Assert.Equal(["Monday: 9:00 AM – 6:00 PM", "Tuesday: Closed"], match.OpeningHours!);
    }

    [Fact]
    public async Task OmitsLocationBiasAndRegionWhenUnknown()
    {
        var (search, handler) = Create(_ => Task.FromResult(Json(Louvre)));
        await search.FindAsync(new PlaceQuery("Louvre, Paris", null, null, null));
        using var json = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.False(json.RootElement.TryGetProperty("locationBias", out _));
        Assert.False(json.RootElement.TryGetProperty("regionCode", out _));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"places":[]}""")]
    [InlineData("""{"places":[{"id":"x","displayName":{"text":"No location"}}]}""")]
    [InlineData("""{"places":[{"id":"x","location":{"latitude":123,"longitude":2}}]}""")]
    public async Task ReturnsNullForEmptyOrUnusableResults(string body)
    {
        var (search, _) = Create(_ => Task.FromResult(Json(body)));
        Assert.Null(await search.FindAsync(new PlaceQuery("Nowhere, Paris", "FR", null, null)));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ReturnsNullForProviderErrors(HttpStatusCode status)
    {
        var (search, _) = Create(_ => Task.FromResult(Json("""{"error":{"message":"private"}}""", status)));
        Assert.Null(await search.FindAsync(new PlaceQuery("Louvre, Paris", "FR", null, null)));
    }

    [Fact]
    public async Task DoesNotCallGoogleWithoutAKey()
    {
        var (search, handler) = Create(_ => Task.FromResult(Json(Louvre)), key: "");
        Assert.False(search.IsConfigured);
        Assert.Null(await search.FindAsync(new PlaceQuery("Louvre, Paris", "FR", null, null)));
        Assert.Empty(handler.Requests);
    }
}
