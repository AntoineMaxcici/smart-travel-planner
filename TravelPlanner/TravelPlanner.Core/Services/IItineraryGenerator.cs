using TravelPlanner.Core.Dtos;

namespace TravelPlanner.Core.Services;

public interface IItineraryGenerator
{
    Task<IReadOnlyList<ItineraryDayDto>> GenerateAsync(TripInput trip, CancellationToken cancellationToken = default);
}
