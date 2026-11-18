using System.ComponentModel.DataAnnotations;

namespace TravelPlanner.Core.Dtos;

// Body of POST /api/trips (create) and PUT /api/trips/{id} (save). Same fields and rules as TripInput,
// except that interests are the IDs from GET /api/interests instead of free text.
public sealed record TripRequest : IValidatableObject
{
    public const int MaximumInterests = 10;

    [Required, StringLength(200)]
    public string DestinationName { get; init; } = "";

    [RegularExpression("^[A-Z]{2}$")]
    public string? DestinationCountryCode { get; init; }

    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }

    [EnumDataType(typeof(TripBudgetLevel))]
    public TripBudgetLevel BudgetLevel { get; init; } = TripBudgetLevel.Medium;

    [Required]
    public IReadOnlyList<int> InterestIds { get; init; } = [];

    [StringLength(2000)]
    public string? Notes { get; init; }

    // The "cannot start in the past" rule is applied by the API: always when creating, and when saving
    // only if the start date changes, so a trip that has already begun can still be edited.
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(DestinationName))
            yield return new("A destination is required.", [nameof(DestinationName)]);
        foreach (var error in TripRules.ValidateDates(StartDate, EndDate, clock: null))
            yield return error;
        if (InterestIds is null || InterestIds.Count == 0)
            yield return new("Choose at least one interest.", [nameof(InterestIds)]);
        else if (InterestIds.Count > MaximumInterests)
            yield return new($"Choose at most {MaximumInterests} interests.", [nameof(InterestIds)]);
        else if (InterestIds.Distinct().Count() != InterestIds.Count)
            yield return new("Each interest can only be chosen once.", [nameof(InterestIds)]);
        else if (InterestIds.Any(id => id <= 0))
            yield return new("Interest IDs must be positive.", [nameof(InterestIds)]);
    }
}
