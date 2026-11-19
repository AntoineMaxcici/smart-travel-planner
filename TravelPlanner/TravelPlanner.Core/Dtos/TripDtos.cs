using System.Text.Json.Serialization;

namespace TravelPlanner.Core.Dtos;

// Same values as the database's TripStatus, sent as text ("Draft", "Ready", ...).
[JsonConverter(typeof(JsonStringEnumConverter<TripStatusDto>))]
public enum TripStatusDto { Draft, Generating, Ready, Failed }

public sealed record InterestDto(int Id, string Name, string? Description);

// One row of GET /api/trips.
public sealed record TripSummaryDto
{
    public required int Id { get; init; }
    public required string DestinationName { get; init; }
    public required string? DestinationCountryCode { get; init; }
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }
    public required TripBudgetLevel BudgetLevel { get; init; }
    public required TripStatusDto Status { get; init; }
    public required IReadOnlyList<string> Interests { get; init; }
    public required DateTime UpdatedAtUtc { get; init; }
}

// GET /api/trips/{id}, and the response of create and save.
public sealed record TripDetailDto
{
    public required int Id { get; init; }
    public required string DestinationName { get; init; }
    public required string? DestinationCountryCode { get; init; }
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }
    public required TripBudgetLevel BudgetLevel { get; init; }
    public required TripStatusDto Status { get; init; }
    public required IReadOnlyList<InterestDto> Interests { get; init; }
    public required string? Notes { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public required DateTime UpdatedAtUtc { get; init; }

    // The generator's input for this trip, using the interest names.
    public TripInput ToTripInput() => new()
    {
        DestinationName = DestinationName,
        DestinationCountryCode = DestinationCountryCode,
        StartDate = StartDate,
        EndDate = EndDate,
        BudgetLevel = BudgetLevel,
        Interests = Interests.Select(interest => interest.Name).ToArray(),
        Notes = Notes
    };
}

public sealed record PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int TotalCount { get; init; }
    public int TotalPages => PageSize == 0 ? 0 : (TotalCount + PageSize - 1) / PageSize;
}
