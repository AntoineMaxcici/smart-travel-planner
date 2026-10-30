namespace TravelPlanner.Api.Data.Entities;

public class UserSavedPlace
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public int PlaceId { get; set; }
    public Place Place { get; set; } = null!;

    public string? Notes { get; set; }
    public DateTime SavedAtUtc { get; set; } = DateTime.UtcNow;
}