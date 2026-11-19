using System.ComponentModel.DataAnnotations;

namespace TravelPlanner.Core.Dtos;

// Date rules shared by TripInput (generation) and TripRequest (saving trips), so both reject the same inputs.
public static class TripRules
{
    public const string PastStartMessage = "The trip cannot start in the past.";

    public static TimeProvider Clock(ValidationContext context) =>
        context.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;

    public static bool StartsInThePast(DateOnly startDate, TimeProvider clock) =>
        startDate < DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    // Pass a clock to also reject start dates before today (UTC); pass null to only check the date range.
    public static IEnumerable<ValidationResult> ValidateDates(DateOnly startDate, DateOnly endDate, TimeProvider? clock)
    {
        if (startDate == default)
            yield return new("A start date is required.", [nameof(TripInput.StartDate)]);
        else if (clock is not null && StartsInThePast(startDate, clock))
            yield return new(PastStartMessage, [nameof(TripInput.StartDate)]);
        if (endDate == default || endDate < startDate)
            yield return new("End date must be on or after the start date.", [nameof(TripInput.EndDate)]);
        else if (endDate.DayNumber - startDate.DayNumber >= TripInput.MaximumDays)
            yield return new($"A trip can contain at most {TripInput.MaximumDays} days.", [nameof(TripInput.EndDate)]);
    }
}
