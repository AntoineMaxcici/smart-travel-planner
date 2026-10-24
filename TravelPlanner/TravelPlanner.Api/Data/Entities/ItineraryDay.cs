namespace TravelPlanner.Api.Data.Entities;

public class ItineraryDay
{
    public int Id { get; set; }

    public int ItineraryId { get; set; }
    public Itinerary Itinerary { get; set; } = null!;

    public int DayNumber { get; set; }
    public DateOnly ItineraryDate { get; set; }
    public string? Theme { get; set; }
    public string? Notes { get; set; }
    public List<ItineraryStop> Stops { get; set; } = new();
}