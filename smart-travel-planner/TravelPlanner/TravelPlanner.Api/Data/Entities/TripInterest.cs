namespace TravelPlanner.Api.Data.Entities;

public class TripInterest
{
    public int TripId { get; set; }
    public Trip Trip { get; set; } = null!;

    public int InterestId { get; set; }
    public Interest Interest { get; set; } = null!;
}