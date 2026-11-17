using TravelPlanner.Core.Dtos;
using TravelPlanner.Core.Services;

namespace TravelPlanner.Api.Tests;

internal static class ItineraryTestData
{
    private static readonly DateOnly StartDate = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1);
    public static TripInput Trip(string city = "Paris") => new()
    {
        DestinationName = city,
        DestinationCountryCode = city switch { "Tokyo" => "JP", "Beirut" => "LB", _ => "FR" },
        StartDate = StartDate, EndDate = StartDate.AddDays(1),
        BudgetLevel = TripBudgetLevel.Medium, Interests = ["history", "local food"]
    };

    public static IReadOnlyList<ItineraryDayDto> Days(TripInput trip)
    {
        string[] names = trip.DestinationName switch
        {
            "Tokyo" => ["Senso-ji", "Ueno Park", "Akihabara", "Meiji Shrine", "Shibuya", "Shinjuku Gyoen"],
            "Beirut" => ["National Museum of Beirut", "Sursock Museum", "Gemmayzeh", "Pigeon Rocks", "Corniche", "Zaitunay Bay"],
            _ => ["Eiffel Tower", "Louvre Museum", "Montmartre", "Notre-Dame", "Luxembourg Gardens", "Seine riverbank"]
        };
        return Enumerable.Range(0, trip.EndDate.DayNumber - trip.StartDate.DayNumber + 1).Select(index => new ItineraryDayDto
        {
            DayNumber = index + 1, ItineraryDate = trip.StartDate.AddDays(index), Theme = "Culture and local food",
            Notes = "Check opening hours, reservations, prices and travel estimates.",
            Stops = Enumerable.Range(0, 3).Select(stop => new ItineraryStopDto
            {
                SortOrder = stop + 1, TimeSlot = stop == 0 ? ItineraryTimeSlot.Morning : ItineraryTimeSlot.Afternoon,
                StartTimeLocal = new TimeOnly(9 + stop * 3, 0), EndTimeLocal = new TimeOnly(10 + stop * 3, 0),
                DisplayName = names[(index * 3 + stop) % names.Length], Category = "culture",
                ShortDescription = "Explore the neighborhood and its history.", DisplayAddress = null,
                DisplayLatitude = null, DisplayLongitude = null, EstimatedCost = null, CurrencyCode = null,
                EstimatedVisitMinutes = 60, TravelMinutesFromPrevious = stop == 0 ? 0 : 20
            }).ToArray()
        }).ToArray();
    }
}

internal sealed class FakeItineraryGenerator : IItineraryGenerator
{
    public List<TripInput> Calls { get; } = [];
    public Func<TripInput, IReadOnlyList<ItineraryDayDto>> Generate { get; set; } = ItineraryTestData.Days;
    public CancellationToken LastCancellationToken { get; private set; }
    public Task<IReadOnlyList<ItineraryDayDto>> GenerateAsync(TripInput trip, CancellationToken cancellationToken = default)
    {
        Calls.Add(trip);
        LastCancellationToken = cancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Generate(trip));
    }
}
