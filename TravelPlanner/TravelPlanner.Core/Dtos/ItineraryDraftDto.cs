using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TravelPlanner.Core.Dtos;

[JsonConverter(typeof(JsonStringEnumConverter<ItineraryTimeSlot>))]
public enum ItineraryTimeSlot { Morning, Afternoon, Evening }

// These are generated suggestions, without database IDs or unverified Google Place IDs.
public sealed record ItineraryDraftDto
{
    public required IReadOnlyList<ItineraryDayDto> Days { get; init; }
}

public sealed record ItineraryDayDto
{
    [Range(1, TripInput.MaximumDays)]
    public required int DayNumber { get; init; }
    public required DateOnly ItineraryDate { get; init; }
    [StringLength(200)]
    public required string? Theme { get; init; }
    [StringLength(2000)]
    public required string? Notes { get; init; }
    public required IReadOnlyList<ItineraryStopDto> Stops { get; init; }
}

public sealed record ItineraryStopDto
{
    [Range(1, 6)]
    public required int SortOrder { get; init; }
    [EnumDataType(typeof(ItineraryTimeSlot))]
    public required ItineraryTimeSlot TimeSlot { get; init; }
    public required TimeOnly? StartTimeLocal { get; init; }
    public required TimeOnly? EndTimeLocal { get; init; }
    [Required, StringLength(200)]
    public required string DisplayName { get; init; }
    [StringLength(100)]
    public required string? Category { get; init; }
    [StringLength(1000)]
    public required string? ShortDescription { get; init; }
    [StringLength(300)]
    public required string? DisplayAddress { get; init; }
    [Range(-90, 90)]
    public required double? DisplayLatitude { get; init; }
    [Range(-180, 180)]
    public required double? DisplayLongitude { get; init; }
    [Range(typeof(decimal), "0", "99999999.99")]
    public required decimal? EstimatedCost { get; init; }
    [RegularExpression("^[A-Z]{3}$")]
    public required string? CurrencyCode { get; init; }
    [Range(1, 1440)]
    public required int? EstimatedVisitMinutes { get; init; }
    [Range(0, 1440)]
    public required int? TravelMinutesFromPrevious { get; init; }
}
