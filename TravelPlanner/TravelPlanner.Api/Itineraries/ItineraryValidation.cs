using System.ComponentModel.DataAnnotations;
using TravelPlanner.Core.Dtos;

namespace TravelPlanner.Api.Itineraries;

public static class ItineraryValidation
{
    public static IReadOnlyList<ItineraryDayDto> NormalizeTimeSlots(IReadOnlyList<ItineraryDayDto>? days)
    {
        if (days is null) Invalid();
        var result = new List<ItineraryDayDto>(days.Count);
        foreach (var day in days)
        {
            if (day is null || day.Stops is null) Invalid();
            var stops = new List<ItineraryStopDto>(day.Stops.Count);
            foreach (var stop in day.Stops)
            {
                if (stop is null) Invalid();
                // The local start time unambiguously determines this redundant display label.
                // Preserve unknown enum values so validation still rejects them.
                stops.Add(stop.StartTimeLocal is TimeOnly start && Enum.IsDefined(stop.TimeSlot)
                    ? stop with { TimeSlot = TimeSlotFor(start) } : stop);
            }
            result.Add(day with { Stops = stops });
        }
        return result;
    }

    public static void ValidateInput(TripInput trip)
    {
        ArgumentNullException.ThrowIfNull(trip);
        Validator.ValidateObject(trip, new ValidationContext(trip), validateAllProperties: true);
    }

    public static void ValidateDays(TripInput trip, IReadOnlyList<ItineraryDayDto>? days)
    {
        var count = trip.EndDate.DayNumber - trip.StartDate.DayNumber + 1;
        if (days is null || days.Count != count) Invalid();
        for (var index = 0; index < days!.Count; index++)
        {
            var day = days[index];
            if (day is null || !ValidObject(day) || day.DayNumber != index + 1 ||
                day.ItineraryDate != trip.StartDate.AddDays(index) || day.Stops is null || day.Stops.Count is < 3 or > 6)
                Invalid();
            TimeOnly? previousEnd = null;
            ItineraryTimeSlot previousSlot = ItineraryTimeSlot.Morning;
            for (var stopIndex = 0; stopIndex < day!.Stops.Count; stopIndex++)
            {
                var stop = day.Stops[stopIndex];
                if (stop is null || !ValidObject(stop) || stop.SortOrder != stopIndex + 1 || stop.TimeSlot < previousSlot ||
                    stop.DisplayLatitude.HasValue != stop.DisplayLongitude.HasValue ||
                    (stop.DisplayLatitude is double latitude && !double.IsFinite(latitude)) ||
                    (stop.DisplayLongitude is double longitude && !double.IsFinite(longitude)) ||
                    stop.StartTimeLocal.HasValue != stop.EndTimeLocal.HasValue ||
                    (stop.EstimatedCost.HasValue && string.IsNullOrWhiteSpace(stop.CurrencyCode)) ||
                    (stopIndex == 0 && stop.TravelMinutesFromPrevious is > 0))
                    Invalid();
                if (stop!.StartTimeLocal is TimeOnly start && stop.EndTimeLocal is TimeOnly end)
                {
                    var slot = TimeSlotFor(start);
                    if (end <= start || slot != stop.TimeSlot ||
                        (previousEnd is TimeOnly lastEnd && start.ToTimeSpan() < lastEnd.ToTimeSpan() + TimeSpan.FromMinutes(stop.TravelMinutesFromPrevious ?? 0)))
                        Invalid();
                    previousEnd = end;
                }
                previousSlot = stop.TimeSlot;
            }
        }
    }

    private static bool ValidObject(object value) => Validator.TryValidateObject(value, new ValidationContext(value), [], true);
    private static ItineraryTimeSlot TimeSlotFor(TimeOnly start) => start.Hour < 12 ? ItineraryTimeSlot.Morning
        : start.Hour < 18 ? ItineraryTimeSlot.Afternoon : ItineraryTimeSlot.Evening;
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Invalid() => throw new ItineraryGenerationException(GenerationFailure.InvalidResponse);
}
