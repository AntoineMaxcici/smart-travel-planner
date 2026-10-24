namespace TravelPlanner.Api.Data.Entities;

public class Itinerary
{
    public int Id { get; set; }

    public int TripId { get; set; }
    public Trip Trip { get; set; } = null!;

    public int? GenerationRunId { get; set; }
    public GenerationRun? GenerationRun { get; set; }

    public int RevisionNumber { get; set; }
    public ItineraryStatus Status { get; set; } = ItineraryStatus.Draft;
    public bool IsCurrent { get; set; }
    public string? Summary { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<ItineraryDay> Days { get; set; } = new();
}