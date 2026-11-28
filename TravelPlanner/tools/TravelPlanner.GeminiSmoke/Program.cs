using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TravelPlanner.Api.Itineraries;
using TravelPlanner.Core.Dtos;
using TravelPlanner.Core.Services;

var date = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1));
TripInput Trip(string city, string country, string[] interests) => new()
{
    DestinationName = city, DestinationCountryCode = country,
    StartDate = date, EndDate = date.AddDays(1), BudgetLevel = TripBudgetLevel.Medium,
    Interests = interests, Notes = "First visit; include a local food stop and time for lunch."
};
var trips = new[]
{
    Trip("Paris", "FR", ["art", "history", "local food"]),
    Trip("Tokyo", "JP", ["temples", "gardens", "local food"]),
    Trip("Beirut", "LB", ["history", "coastal walks", "Lebanese food"]),
    Trip("Rome", "IT", ["ancient history", "architecture", "Italian food"]),
    Trip("Lisbon", "PT", ["historic neighborhoods", "viewpoints", "Portuguese food"])
};
if (args is ["--schema", var schemaPath])
{
    await File.WriteAllTextAsync(schemaPath, ItineraryPrompt.CreateSchema(trips[0]).ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("Wrote the DTO-derived Gemini response schema.");
    return 0;
}
if (args is not ["--live", _])
{
    Console.WriteLine("Usage: dotnet run --project tools/TravelPlanner.GeminiSmoke -- --live <report.json>");
    Console.WriteLine("Calls Gemini once for each of Paris, Tokyo, Beirut, Rome and Lisbon using the API's Gemini:ApiKey user secret.");
    Console.WriteLine("Exit codes: 0 = all valid and within 8 seconds, 2 = unverified output, 3 = valid but over target.");
    Console.WriteLine("Use --schema <schema.json> to export the response schema without making network requests.");
    return 0;
}

var configuration = new ConfigurationBuilder()
    .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true)
    .AddUserSecrets<TravelPlanner.Api.Program>(optional: true)
    .AddEnvironmentVariables().Build();
var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(configuration);
services.AddLogging(logging => logging.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
services.AddGeminiItineraryGeneration();
using var provider = services.BuildServiceProvider();
var generator = provider.GetRequiredService<IItineraryGenerator>();
var results = new List<object>();
var passed = 0;
const int targetMs = 8000;
var withinTarget = 0;
foreach (var trip in trips)
{
    var watch = Stopwatch.StartNew();
    try
    {
        var days = await generator.GenerateAsync(trip);
        ItineraryValidation.ValidateDays(trip, days);
        watch.Stop();
        var metTarget = watch.ElapsedMilliseconds <= targetMs;
        if (metTarget) withinTarget++;
        results.Add(new { city = trip.DestinationName, status = "passed", durationMs = watch.ElapsedMilliseconds, metTarget, input = trip, days });
        Console.WriteLine($"PASS {trip.DestinationName}: {days.Count} days, {days.Sum(day => day.Stops.Count)} stops; {watch.Elapsed.TotalSeconds:F2}s; 8s target: {(metTarget ? "PASS" : "MISS")}.");
        passed++;
    }
    catch (ItineraryGenerationException error)
    {
        watch.Stop();
        results.Add(new { city = trip.DestinationName, status = "not_verified", durationMs = watch.ElapsedMilliseconds, metTarget = false, failure = error.Failure.ToString(), error = error.Message });
        Console.WriteLine($"NOT VERIFIED {trip.DestinationName}: {watch.Elapsed.TotalSeconds:F2}s; {error.Message}");
        if (error.Failure == GenerationFailure.NotConfigured) break;
    }
}
var report = new
{
    testedAtUtc = DateTimeOffset.UtcNow, model = configuration["Gemini:Model"] ?? new GeminiOptions().Model,
    promptVersion = ItineraryPrompt.Version, targetMs, withinTarget, passedCities = passed, expectedCities = trips.Length,
    timingScope = "One direct Gemini request plus parsing and domain validation; excludes Places, database, API retry and app/network overhead. No warmup or automatic retries.",
    note = "AI suggestions: real-world opening hours, reservations, prices, coordinates and travel times require independent checking.",
    results
};
await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions(ItineraryPrompt.JsonOptions) { WriteIndented = true }));
return passed != trips.Length ? 2 : withinTarget != trips.Length ? 3 : 0;
