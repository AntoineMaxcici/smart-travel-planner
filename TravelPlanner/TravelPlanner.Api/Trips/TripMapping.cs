using TravelPlanner.Api.Data.Entities;
using TravelPlanner.Core.Dtos;

namespace TravelPlanner.Api.Trips;

// Converts between the database entities and the shared DTOs in TravelPlanner.Core.
// The enums have the same names on both sides, so the JSON text ("Low", "Ready", ...) matches the database.
public static class TripMapping
{
    public static BudgetLevel ToEntity(this TripBudgetLevel budget) => budget switch
    {
        TripBudgetLevel.Low => BudgetLevel.Low,
        TripBudgetLevel.Medium => BudgetLevel.Medium,
        TripBudgetLevel.High => BudgetLevel.High,
        _ => throw new ArgumentOutOfRangeException(nameof(budget), budget, null)
    };

    public static TripBudgetLevel ToDto(this BudgetLevel budget) => budget switch
    {
        BudgetLevel.Low => TripBudgetLevel.Low,
        BudgetLevel.Medium => TripBudgetLevel.Medium,
        BudgetLevel.High => TripBudgetLevel.High,
        _ => throw new ArgumentOutOfRangeException(nameof(budget), budget, null)
    };

    public static TripStatusDto ToDto(this TripStatus status) => status switch
    {
        TripStatus.Draft => TripStatusDto.Draft,
        TripStatus.Generating => TripStatusDto.Generating,
        TripStatus.Ready => TripStatusDto.Ready,
        TripStatus.Failed => TripStatusDto.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    public static InterestDto ToDto(this Interest interest) => new(interest.Id, interest.Name, interest.Description);

    // Expects TripInterests and their Interest to be loaded.
    public static TripDetailDto ToDetailDto(this Trip trip) => new()
    {
        Id = trip.Id,
        DestinationName = trip.DestinationName,
        DestinationCountryCode = trip.DestinationCountryCode,
        StartDate = trip.StartDate,
        EndDate = trip.EndDate,
        BudgetLevel = trip.BudgetLevel.ToDto(),
        Status = trip.Status.ToDto(),
        Interests = trip.TripInterests.Select(ti => ti.Interest).OrderBy(i => i.Id).Select(ToDto).ToArray(),
        Notes = trip.Notes,
        CreatedAtUtc = trip.CreatedAtUtc,
        UpdatedAtUtc = trip.UpdatedAtUtc
    };

    public static void Apply(this Trip trip, TripRequest request)
    {
        trip.DestinationName = request.DestinationName.Trim();
        trip.DestinationCountryCode = request.DestinationCountryCode;
        trip.StartDate = request.StartDate;
        trip.EndDate = request.EndDate;
        trip.BudgetLevel = request.BudgetLevel.ToEntity();
        trip.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
    }
}
