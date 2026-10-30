namespace TravelPlanner.Api.Data.Entities;

public class Interest
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public List<TripInterest> TripInterests { get; set; } = new();
}