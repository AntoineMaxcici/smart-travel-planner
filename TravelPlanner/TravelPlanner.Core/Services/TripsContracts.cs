using TravelPlanner.Core.Dtos;

namespace TravelPlanner.Core.Services;

/// <summary>The trip endpoints of the API (/api/trips and /api/interests). Every call needs a signed-in user.</summary>
public interface ITripsApi
{
    Task<PagedResult<TripSummaryDto>> GetTripsAsync(int page = 1, int pageSize = TripsApi.DefaultPageSize,
        CancellationToken cancellationToken = default);
    Task<TripDetailDto> GetTripAsync(int id, CancellationToken cancellationToken = default);
    Task<TripDetailDto> CreateTripAsync(TripRequest request, CancellationToken cancellationToken = default);
    Task<TripDetailDto> SaveTripAsync(int id, TripRequest request, CancellationToken cancellationToken = default);
    Task DeleteTripAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InterestDto>> GetInterestsAsync(CancellationToken cancellationToken = default);
}

/// <summary>A trip request failed. The message is safe to show to the user.</summary>
public sealed class TripsApiException(string message) : Exception(message);
