namespace TravelPlanner.Api.Data.Entities;

public class GenerationRun
{
    public int Id { get; set; }

    public int TripId { get; set; }
    public Trip Trip { get; set; } = null!;

    public GenerationStatus Status { get; set; } = GenerationStatus.Pending;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string? PromptVersion { get; set; }
    public DateTime AttemptedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public int? DurationMilliseconds { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? RequestPayload { get; set; }
    public string? ResponsePayload { get; set; }

    public Itinerary? Itinerary { get; set; }
}