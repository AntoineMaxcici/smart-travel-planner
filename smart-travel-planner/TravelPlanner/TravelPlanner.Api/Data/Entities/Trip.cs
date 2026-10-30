namespace TravelPlanner.Api.Data.Entities;

public class Trip
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public string DestinationName { get; set; } = string.Empty;
    public string? DestinationCountryCode { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public BudgetLevel BudgetLevel { get; set; }
    public TripStatus Status { get; set; } = TripStatus.Draft;
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<TripInterest> TripInterests { get; set; } = new();

    public List<GenerationRun> GenerationRuns { get; set; } = new();
    public List<Itinerary> Itineraries { get; set; } = new();
}