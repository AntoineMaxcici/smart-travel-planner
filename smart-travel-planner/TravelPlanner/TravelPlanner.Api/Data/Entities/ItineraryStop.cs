namespace TravelPlanner.Api.Data.Entities;

public class ItineraryStop
{
    public int Id { get; set; }

    public int ItineraryDayId { get; set; }
    public ItineraryDay ItineraryDay { get; set; } = null!;

    public int? PlaceId { get; set; }
    public Place? Place { get; set; }

    public int SortOrder { get; set; }
    public TimeSlot TimeSlot { get; set; }
    public TimeOnly? StartTimeLocal { get; set; }
    public TimeOnly? EndTimeLocal { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? ShortDescription { get; set; }
    public string? DisplayAddress { get; set; }
    public double? DisplayLatitude { get; set; }
    public double? DisplayLongitude { get; set; }
    public decimal? EstimatedCost { get; set; }
    public string? CurrencyCode { get; set; }
    public int? EstimatedVisitMinutes { get; set; }
    public int? TravelMinutesFromPrevious { get; set; }
}