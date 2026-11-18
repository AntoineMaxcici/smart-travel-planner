using System.ComponentModel.DataAnnotations;

namespace TravelPlanner.Core.Dtos;

// The flat stop shape the AI returns, before Google Places adds the address and coordinates
// and stop ordering assigns SortOrder and times. It becomes an ItineraryStopDto after that.
public sealed record SuggestedStopDto
{
    [Range(1, TripInput.MaximumDays)]
    public required int DayNumber { get; init; }
    [EnumDataType(typeof(ItineraryTimeSlot))]
    public required ItineraryTimeSlot TimeSlot { get; init; }
    [Required, StringLength(200)]
    public required string Name { get; init; }
    [StringLength(100)]
    public string? Category { get; init; }
    [StringLength(1000)]
    public string? Description { get; init; }
    [Range(typeof(decimal), "0", "99999999.99")]
    public decimal? EstimatedCost { get; init; }
    [Range(1, 1440)]
    public int? EstimatedVisitMinutes { get; init; }
}
