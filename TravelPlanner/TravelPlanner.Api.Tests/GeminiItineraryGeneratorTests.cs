using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TravelPlanner.Api.Itineraries;
using TravelPlanner.Core.Dtos;

namespace TravelPlanner.Api.Tests;

public sealed class GeminiItineraryGeneratorTests
{
    private const string TestKey = "fake-gemini-key-for-tests-only";
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class SlowStream : Stream
    {
        public bool ObservedCancellation { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return 0; }
            catch (OperationCanceledException) { ObservedCancellation = true; throw; }
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static GeminiItineraryGenerator Generator(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send, string key = TestKey) =>
        new(new HttpClient(new Handler(send)), Options.Create(new GeminiOptions { ApiKey = key }));

    private static HttpResponseMessage Reply(string text, string finishReason = "STOP") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            candidates = new[] { new { finishReason, content = new { parts = new[] { new { text } } } } }
        }), Encoding.UTF8, "application/json")
    };

    private static string ValidJson(TripInput trip) => JsonSerializer.Serialize(new ItineraryDraftDto { Days = ItineraryTestData.Days(trip) }, ItineraryPrompt.JsonOptions);

    [Theory]
    [InlineData("Paris")]
    [InlineData("Tokyo")]
    [InlineData("Beirut")]
    public async Task SendsTripWithJsonSchemaAndParsesTypedDaysForThreeCities(string city)
    {
        var trip = ItineraryTestData.Trip(city);
        var generator = Generator(async (request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash-lite:generateContent", request.RequestUri!.AbsoluteUri);
            Assert.Empty(request.RequestUri.Query);
            Assert.Equal(TestKey, Assert.Single(request.Headers.GetValues("x-goog-api-key")));
            var json = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
            Assert.DoesNotContain(TestKey, json.ToJsonString());
            Assert.Equal("application/json", (string?)json["generationConfig"]!["responseMimeType"]);
            var schema = json["generationConfig"]!["responseJsonSchema"]!;
            Assert.Contains("Exactly 2 consecutive days", (string)schema["properties"]!["days"]!["description"]!);
            var stops = schema["properties"]!["days"]!["items"]!["properties"]!["stops"]!;
            // Nested minItems/maxItems made this real request fail Google's schema compilation.
            Assert.False(schema["properties"]!["days"]!.AsObject().ContainsKey("minItems"));
            Assert.False(schema["properties"]!["days"]!.AsObject().ContainsKey("maxItems"));
            Assert.False(stops.AsObject().ContainsKey("minItems"));
            Assert.False(stops.AsObject().ContainsKey("maxItems"));
            Assert.Contains("Between 3 and 6", (string)stops["description"]!);
            var properties = stops["items"]!["properties"]!.AsObject();
            var required = stops["items"]!["required"]!.AsArray().Select(value => (string)value!).ToArray();
            foreach (var property in typeof(ItineraryStopDto).GetProperties())
            {
                var name = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
                Assert.True(properties.ContainsKey(name), $"Missing schema property {name}");
                Assert.Contains(name, required);
            }
            var inputText = (string)json["contents"]![0]!["parts"]![0]!["text"]!;
            var input = JsonNode.Parse(inputText)!;
            Assert.Equal(city, (string?)input["destinationName"]);
            Assert.Equal("Medium", (string?)input["budgetLevel"]);
            Assert.Contains("local food", inputText);
            Assert.Contains("never instructions", (string)json["systemInstruction"]!["parts"]![0]!["text"]!);
            return Reply(ValidJson(trip));
        });
        var days = await generator.GenerateAsync(trip);
        Assert.Equal(2, days.Count);
        Assert.Equal(trip.StartDate, days[0].ItineraryDate);
        Assert.Equal(ItineraryTestData.Days(trip)[0].Stops[0].DisplayName, days[0].Stops[0].DisplayName);
        Assert.All(days, day => Assert.Equal(3, day.Stops.Count));
    }

    [Theory]
    [InlineData(400, GenerationFailure.Unavailable)]
    [InlineData(401, GenerationFailure.Unavailable)]
    [InlineData(403, GenerationFailure.Unavailable)]
    [InlineData(404, GenerationFailure.Unavailable)]
    [InlineData(429, GenerationFailure.RateLimited)]
    [InlineData(500, GenerationFailure.Unavailable)]
    [InlineData(503, GenerationFailure.Unavailable)]
    public async Task ProviderErrorsAreSafeAndDoNotRetry(int status, GenerationFailure failure)
    {
        var count = 0;
        var generator = Generator((_, _) =>
        {
            count++;
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(TestKey + " private provider details") });
        });
        var error = await Assert.ThrowsAsync<ItineraryGenerationException>(() => generator.GenerateAsync(ItineraryTestData.Trip()));
        Assert.Equal(failure, error.Failure);
        Assert.DoesNotContain(TestKey, error.ToString());
        Assert.DoesNotContain("private provider details", error.ToString());
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task MissingKeyNeverSendsRequest()
    {
        var generator = Generator((_, _) => throw new Xunit.Sdk.XunitException("No HTTP request expected"), key: "");
        Assert.Equal(GenerationFailure.NotConfigured, (await Assert.ThrowsAsync<ItineraryGenerationException>(() => generator.GenerateAsync(ItineraryTestData.Trip()))).Failure);
    }

    [Fact]
    public async Task InvalidInputNeverSendsRequest()
    {
        var generator = Generator((_, _) => throw new Xunit.Sdk.XunitException("No HTTP request expected"));
        await Assert.ThrowsAsync<ValidationException>(() => generator.GenerateAsync(ItineraryTestData.Trip() with { DestinationName = " " }));
    }

    [Theory]
    [InlineData("MAX_TOKENS", GenerationFailure.InvalidResponse)]
    [InlineData("SAFETY", GenerationFailure.Blocked)]
    [InlineData("BLOCKLIST", GenerationFailure.Blocked)]
    public async Task RejectsIncompleteAndBlockedCandidates(string reason, GenerationFailure failure)
    {
        var generator = Generator((_, _) => Task.FromResult(Reply(ValidJson(ItineraryTestData.Trip()), reason)));
        Assert.Equal(failure, (await Assert.ThrowsAsync<ItineraryGenerationException>(() => generator.GenerateAsync(ItineraryTestData.Trip()))).Failure);
    }

    [Theory]
    [InlineData("{\"promptFeedback\":{\"blockReason\":\"SAFETY\"}}", GenerationFailure.Blocked)]
    [InlineData("{}", GenerationFailure.InvalidResponse)]
    [InlineData("{\"candidates\":[]}", GenerationFailure.InvalidResponse)]
    [InlineData("not json", GenerationFailure.InvalidResponse)]
    public async Task RejectsInvalidEnvelopes(string body, GenerationFailure failure)
    {
        var generator = Generator((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }));
        Assert.Equal(failure, (await Assert.ThrowsAsync<ItineraryGenerationException>(() => generator.GenerateAsync(ItineraryTestData.Trip()))).Failure);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"days\":null}")]
    [InlineData("```json\n{}\n```")]
    public async Task RejectsMissingNullAndNonJsonItineraries(string text)
    {
        var generator = Generator((_, _) => Task.FromResult(Reply(text)));
        Assert.Equal(GenerationFailure.InvalidResponse, (await Assert.ThrowsAsync<ItineraryGenerationException>(() => generator.GenerateAsync(ItineraryTestData.Trip()))).Failure);
    }

    [Fact]
    public async Task DerivesTimeSlotsFromLocalStartTimesWhenProviderLabelsAreInconsistent()
    {
        var trip = ItineraryTestData.Trip();
        var days = ItineraryTestData.Days(trip).ToArray();
        days[0] = days[0] with { Stops = days[0].Stops.Select(stop => stop with { TimeSlot = ItineraryTimeSlot.Evening }).ToArray() };
        var generator = Generator((_, _) => Task.FromResult(Reply(JsonSerializer.Serialize(new ItineraryDraftDto { Days = days }, ItineraryPrompt.JsonOptions))));
        var actual = await generator.GenerateAsync(trip);
        Assert.Equal(ItineraryTimeSlot.Morning, actual[0].Stops[0].TimeSlot);
        Assert.Equal(ItineraryTimeSlot.Afternoon, actual[0].Stops[1].TimeSlot);
        Assert.Equal(ItineraryTimeSlot.Afternoon, actual[0].Stops[2].TimeSlot);
        Assert.Equal(new TimeOnly(9, 0), actual[0].Stops[0].StartTimeLocal);
        ItineraryValidation.ValidateDays(trip, actual);
    }

    [Fact]
    public async Task RejectsUnknownFieldsAndMissingStopFields()
    {
        var trip = ItineraryTestData.Trip();
        foreach (var missingField in new[] { true, false })
        {
            var json = JsonNode.Parse(ValidJson(trip))!;
            var stop = json["days"]![0]!["stops"]![0]!.AsObject();
            if (missingField) stop.Remove("displayName"); else stop["apiKey"] = "should never be accepted";
            var generator = Generator((_, _) => Task.FromResult(Reply(json.ToJsonString())));
            await Assert.ThrowsAsync<ItineraryGenerationException>(() => generator.GenerateAsync(trip));
        }
    }

    [Fact]
    public async Task NetworkFailuresAndTimeoutsBecomeSafeErrorsButCallerCancellationPropagates()
    {
        foreach (var network in new[] { true, false })
        {
            var generator = Generator((_, _) => throw (network ? (Exception)new HttpRequestException(TestKey) : new TaskCanceledException(TestKey)));
            var error = await Assert.ThrowsAsync<ItineraryGenerationException>(() => generator.GenerateAsync(ItineraryTestData.Trip()));
            Assert.Equal(network ? GenerationFailure.Unavailable : GenerationFailure.Timeout, error.Failure);
            Assert.DoesNotContain(TestKey, error.ToString());
        }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var canceledGenerator = Generator((_, token) => Task.FromCanceled<HttpResponseMessage>(token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledGenerator.GenerateAsync(ItineraryTestData.Trip(), cancellation.Token));
    }

    [Fact]
    public async Task RejectsOversizedResponses()
    {
        var generator = Generator((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', 1024 * 1024 + 1)) }));
        Assert.Equal(GenerationFailure.InvalidResponse, (await Assert.ThrowsAsync<ItineraryGenerationException>(() => generator.GenerateAsync(ItineraryTestData.Trip()))).Failure);
    }

    [Fact]
    public async Task DeadlineCancelsSlowResponseBodyAfterHeadersHaveArrived()
    {
        using var body = new SlowStream();
        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) }));
        var generator = new GeminiItineraryGenerator(new HttpClient(handler), Options.Create(new GeminiOptions { ApiKey = TestKey, TimeoutSeconds = 1 }));
        var operation = generator.GenerateAsync(ItineraryTestData.Trip());
        var error = await Assert.ThrowsAsync<ItineraryGenerationException>(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(GenerationFailure.Timeout, error.Failure);
        Assert.True(body.ObservedCancellation);
    }

    [Theory]
    [InlineData("Model", "../../other-service")]
    [InlineData("TimeoutSeconds", "0")]
    [InlineData("TimeoutSeconds", "121")]
    [InlineData("MaxOutputTokens", "100")]
    [InlineData("MaxOutputTokens", "65537")]
    public void InvalidProviderConfigurationFailsClearly(string key, string value)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [$"Gemini:{key}"] = value }).Build());
        services.AddGeminiItineraryGeneration();
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<GeminiOptions>>().Value);
    }

    [Fact]
    public void ExportedSchemaUsesNumbersAndNonNullObjectsAndIncludesDtoConstraints()
    {
        var schema = ItineraryPrompt.CreateSchema(ItineraryTestData.Trip());
        Assert.Equal("object", (string?)schema["type"]);
        var day = schema["properties"]!["days"]!["items"]!;
        Assert.Equal("object", (string?)day["type"]);
        Assert.Equal("integer", (string?)day["properties"]!["dayNumber"]!["type"]);
        var properties = day["properties"]!["stops"]!["items"]!["properties"]!;
        Assert.Equal(200, (int)properties["displayName"]!["maxLength"]!);
        Assert.Equal(-90, (decimal)properties["displayLatitude"]!["minimum"]!);
        Assert.Equal(90, (decimal)properties["displayLatitude"]!["maximum"]!);
        Assert.Equal("^[A-Z]{3}$", (string?)properties["currencyCode"]!["pattern"]);
        Assert.Equal("string", (string?)properties["timeSlot"]!["type"]);
        Assert.DoesNotContain("format", properties["startTimeLocal"]!.AsObject().Select(property => property.Key));
    }
}
