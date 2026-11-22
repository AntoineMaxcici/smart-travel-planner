namespace TravelPlanner.Core.Dtos;

public sealed record TripGenerationDto(int GenerationRunId, int ItineraryId, int RevisionNumber,
    TripStatusDto Status, IReadOnlyList<ItineraryDayDto> Days);

public sealed record TripGenerationErrorDto(int GenerationRunId, string Code, string Message);
