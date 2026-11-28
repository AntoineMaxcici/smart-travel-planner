using TravelPlanner.Api.Data.Entities;
using TravelPlanner.Api.Places;

namespace TravelPlanner.Api.Tests;

public sealed class StopOrderingTests
{
    // Points along one street in Paris, about 0.75 km apart (0.01 degrees of longitude at this latitude).
    private const double Lat = 48.8566;
    private static double Lng(int step) => 2.30 + step * 0.01;

    private static ItineraryStop Stop(string name, int sortOrder, TimeSlot slot, double? lng,
        int? startHour = null, int travel = 15) => new()
    {
        DisplayName = name, SortOrder = sortOrder, TimeSlot = slot,
        DisplayLatitude = lng is null ? null : Lat, DisplayLongitude = lng,
        StartTimeLocal = startHour is int h ? new TimeOnly(h, 0) : null,
        EndTimeLocal = startHour is int e ? new TimeOnly(e + 1, 0) : null,
        EstimatedVisitMinutes = 60, TravelMinutesFromPrevious = sortOrder == 1 ? 0 : travel
    };

    private static ItineraryDay Day(params ItineraryStop[] stops) => new() { DayNumber = 1, Stops = stops.ToList() };
    private static string[] Names(ItineraryDay day) => day.Stops.Select(s => s.DisplayName).ToArray();

    [Fact]
    public void ZigZagWithinASlotIsReorderedNearestFirst()
    {
        // A(0) -> C(4) -> B(1) -> D(5) goes back and forth; nearest-next is A, B, C, D.
        var day = Day(
            Stop("A", 1, TimeSlot.Morning, Lng(0)), Stop("C", 2, TimeSlot.Morning, Lng(4)),
            Stop("B", 3, TimeSlot.Morning, Lng(1)), Stop("D", 4, TimeSlot.Morning, Lng(5)));

        Assert.True(StopOrdering.Optimize(day));

        Assert.Equal(["A", "B", "C", "D"], Names(day));
        Assert.Equal([1, 2, 3, 4], day.Stops.Select(s => s.SortOrder));
        Assert.Equal(0, day.Stops[0].TravelMinutesFromPrevious);
        Assert.All(day.Stops.Skip(1), s => Assert.True(s.TravelMinutesFromPrevious >= 5));
    }

    [Fact]
    public void StopsNeverMoveIntoAnotherTimeSlot()
    {
        // The evening stop is next to A, but it must stay in the evening.
        var day = Day(
            Stop("A", 1, TimeSlot.Morning, Lng(0)), Stop("Far", 2, TimeSlot.Afternoon, Lng(8)),
            Stop("Near A", 3, TimeSlot.Evening, Lng(0)));

        StopOrdering.Optimize(day);

        Assert.Equal(["A", "Far", "Near A"], Names(day));
        Assert.Equal([TimeSlot.Morning, TimeSlot.Afternoon, TimeSlot.Evening], day.Stops.Select(s => s.TimeSlot));
    }

    [Fact]
    public void TimesAreRelaidWithoutOverlapsAndKeepTheirSlot()
    {
        var day = Day(
            Stop("A", 1, TimeSlot.Morning, Lng(0), startHour: 8), Stop("C", 2, TimeSlot.Morning, Lng(4), startHour: 9),
            Stop("B", 3, TimeSlot.Morning, Lng(1), startHour: 10), Stop("Lunch", 4, TimeSlot.Afternoon, Lng(5), startHour: 13));

        Assert.True(StopOrdering.Optimize(day));

        Assert.Equal(["A", "B", "C", "Lunch"], Names(day));
        for (var i = 1; i < day.Stops.Count; i++)
        {
            var previous = day.Stops[i - 1];
            var stop = day.Stops[i];
            Assert.True(stop.StartTimeLocal!.Value.ToTimeSpan() >=
                previous.EndTimeLocal!.Value.ToTimeSpan() + TimeSpan.FromMinutes(stop.TravelMinutesFromPrevious ?? 0));
            Assert.Equal(TimeSpan.FromHours(1), stop.EndTimeLocal!.Value - stop.StartTimeLocal.Value);
        }
        Assert.Equal(new TimeOnly(8, 0), day.Stops[0].StartTimeLocal);
        Assert.True(day.Stops[2].StartTimeLocal!.Value.Hour < 12);
    }

    [Fact]
    public void KeepsTheAiOrderWhenTheNewOrderNoLongerFitsItsSlot()
    {
        // C is ~30 km away. Visiting B first is shorter, but the long ride to C would then start after noon.
        var day = Day(
            Stop("A", 1, TimeSlot.Morning, Lng(0), startHour: 9), Stop("C", 2, TimeSlot.Morning, Lng(40), startHour: 10),
            Stop("B", 3, TimeSlot.Morning, Lng(1), startHour: 11));

        Assert.False(StopOrdering.Optimize(day));
        Assert.Equal(["A", "C", "B"], Names(day));
    }

    [Fact]
    public void DaysWithMissingCoordinatesAreLeftAlone()
    {
        var day = Day(
            Stop("A", 1, TimeSlot.Morning, Lng(0)), Stop("C", 2, TimeSlot.Morning, null),
            Stop("B", 3, TimeSlot.Morning, Lng(1)));

        Assert.False(StopOrdering.Optimize(day));
        Assert.Equal(["A", "C", "B"], Names(day));
    }

    [Fact]
    public void AnAlreadyGoodOrderIsUnchanged()
    {
        var day = Day(
            Stop("A", 1, TimeSlot.Morning, Lng(0)), Stop("B", 2, TimeSlot.Morning, Lng(1)),
            Stop("C", 3, TimeSlot.Afternoon, Lng(2)));

        Assert.False(StopOrdering.Optimize(day));
        Assert.Equal([15, 15], day.Stops.Skip(1).Select(s => s.TravelMinutesFromPrevious!.Value));
    }

    [Theory]
    [InlineData(0.2, 5)]     // short walk
    [InlineData(1.0, 20)]   // 1.3 km on foot ~ 17 min
    [InlineData(10.0, 55)]  // 13 km ride ~ 43 min + 10 min wait
    public void TravelEstimateIsRoundedUpToFiveMinutes(double km, int expected)
    {
        var from = Stop("From", 1, TimeSlot.Morning, 2.30);
        var to = Stop("To", 2, TimeSlot.Morning, 2.30);
        to.DisplayLatitude = Lat + km / 111.2; // one degree of latitude is ~111.2 km

        Assert.Equal(expected, StopOrdering.EstimateTravelMinutes(from, to));
    }
}
