using TravelPlanner.Api.Itineraries;
using TravelPlanner.Core.Dtos;

namespace TravelPlanner.Api.Tests;

public sealed class ItineraryValidationTests
{
    public static IEnumerable<object[]> InvalidDrafts()
    {
        var trip = ItineraryTestData.Trip();
        var day = ItineraryTestData.Days(trip)[0];
        var stop = day.Stops[0];
        yield return [new ItineraryDraftDto { Days = [] }];
        yield return [new ItineraryDraftDto { Days = [day with { DayNumber = 2 }, ItineraryTestData.Days(trip)[1]] }];
        yield return [new ItineraryDraftDto { Days = [day with { ItineraryDate = trip.EndDate }, ItineraryTestData.Days(trip)[1]] }];
        foreach (var broken in new[]
        {
            stop with { DisplayName = " " }, stop with { SortOrder = 3 },
            stop with { DisplayLatitude = 91, DisplayLongitude = 0 }, stop with { DisplayLatitude = 10 },
            stop with { EstimatedCost = -1 }, stop with { EstimatedCost = 10, CurrencyCode = null },
            stop with { CurrencyCode = "EURO" }, stop with { EndTimeLocal = new TimeOnly(8, 0) },
            stop with { EndTimeLocal = null }, stop with { EstimatedVisitMinutes = 0 },
            stop with { TravelMinutesFromPrevious = -1 }, stop with { TravelMinutesFromPrevious = 10 },
            stop with { TimeSlot = (ItineraryTimeSlot)99 }, stop with { DisplayName = new string('x', 201) }
        }) yield return [new ItineraryDraftDto { Days = [day with { Stops = [broken, day.Stops[1], day.Stops[2]] }, ItineraryTestData.Days(trip)[1]] }];
        yield return [new ItineraryDraftDto { Days = [day with { Stops = [stop] }, ItineraryTestData.Days(trip)[1]] }];
        yield return [new ItineraryDraftDto { Days = [day with { Stops = [stop, day.Stops[1] with { StartTimeLocal = new TimeOnly(9, 30) }, day.Stops[2]] }, ItineraryTestData.Days(trip)[1]] }];
    }

    [Theory, MemberData(nameof(InvalidDrafts))]
    public void RejectsSemanticallyInvalidDaysAndStops(ItineraryDraftDto draft)
    {
        var error = Assert.Throws<ItineraryGenerationException>(() => ItineraryValidation.ValidateDays(ItineraryTestData.Trip(), draft.Days));
        Assert.Equal(GenerationFailure.InvalidResponse, error.Failure);
    }
}
