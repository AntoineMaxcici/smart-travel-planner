namespace TravelPlanner.Api.Data.Entities;

public class Place
{
    public int Id { get; set; }
    public string? GooglePlaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? FormattedAddress { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? PrimaryCategory { get; set; }
    public string? GoogleMapsUri { get; set; }
    public string? OpeningHours { get; set; }
    public DateTime? LastEnrichedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<ItineraryStop> Stops { get; set; } = new();
    public List<UserSavedPlace> SavedBy { get; set; } = new();
}