using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TravelPlanner.Core.Dtos;

[JsonConverter(typeof(JsonStringEnumConverter<TripBudgetLevel>))]
public enum TripBudgetLevel { Low, Medium, High }

public sealed record TripInput : IValidatableObject
{
    public const int MaximumDays = 14;

    [Required, StringLength(200)]
    public string DestinationName { get; init; } = "";

    [RegularExpression("^[A-Z]{2}$")]
    public string? DestinationCountryCode { get; init; }

    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }

    [EnumDataType(typeof(TripBudgetLevel))]
    public TripBudgetLevel BudgetLevel { get; init; } = TripBudgetLevel.Medium;

    [Required, MaxLength(10)]
    public IReadOnlyList<string> Interests { get; init; } = [];

    [StringLength(2000)]
    public string? Notes { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var error in TripRules.ValidateDates(StartDate, EndDate, TripRules.Clock(validationContext)))
            yield return error;
        if (Interests is not null && Interests.Any(interest => string.IsNullOrWhiteSpace(interest) || interest.Length > 100))
            yield return new("Each interest must contain between 1 and 100 characters.", [nameof(Interests)]);
    }
}
