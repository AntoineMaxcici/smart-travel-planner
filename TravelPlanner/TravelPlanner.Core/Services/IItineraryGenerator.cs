using TravelPlanner.Core.Dtos;

namespace TravelPlanner.Core.Services;

public interface IItineraryGenerator
{
    /// <summary>Produces candidate days. Callers validate domain constraints before using or returning them.</summary>
    Task<IReadOnlyList<ItineraryDayDto>> GenerateAsync(TripInput trip, CancellationToken cancellationToken = default);
}
