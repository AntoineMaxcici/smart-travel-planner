namespace TravelPlanner.Api.Itineraries;

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gemini-3.5-flash-lite";
    public int TimeoutSeconds { get; set; } = 90;
    public int MaxOutputTokens { get; set; } = 32768;
}
