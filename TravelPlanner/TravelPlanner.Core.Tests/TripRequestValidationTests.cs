using System.ComponentModel.DataAnnotations;
using TravelPlanner.Core.Dtos;

namespace TravelPlanner.Core.Tests;

public sealed class TripRequestValidationTests
{
    private static readonly DateOnly Start = new(2030, 6, 1);

    private static TripRequest Valid() => new()
    {
        DestinationName = "Rome", StartDate = Start, EndDate = Start.AddDays(3),
        BudgetLevel = TripBudgetLevel.Low, InterestIds = [2, 3]
    };

    private static List<ValidationResult> Validate(TripRequest trip)
    {
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(trip, new ValidationContext(trip), errors, true);
        return errors;
    }

    [Fact]
    public void ValidRequestHasNoErrors() => Assert.Empty(Validate(Valid()));

    [Fact]
    public void EndBeforeStartIsRejected() =>
        Assert.Contains(Validate(Valid() with { EndDate = Start.AddDays(-1) }), e => e.MemberNames.Contains(nameof(TripRequest.EndDate)));

    [Fact]
    public void SameDayTripIsAllowed() => Assert.Empty(Validate(Valid() with { EndDate = Start }));

    [Fact]
    public void FourteenDaysIsTheMaximum()
    {
        Assert.Empty(Validate(Valid() with { EndDate = Start.AddDays(TripInput.MaximumDays - 1) }));
        Assert.NotEmpty(Validate(Valid() with { EndDate = Start.AddDays(TripInput.MaximumDays) }));
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 1, 1 })]
    [InlineData(new[] { 0 })]
    [InlineData(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 })]
    public void BadInterestListsAreRejected(int[] ids) =>
        Assert.Contains(Validate(Valid() with { InterestIds = ids }), e => e.MemberNames.Contains(nameof(TripRequest.InterestIds)));

    [Fact]
    public void UnknownBudgetIsRejected() =>
        Assert.Contains(Validate(Valid() with { BudgetLevel = (TripBudgetLevel)7 }), e => e.MemberNames.Contains(nameof(TripRequest.BudgetLevel)));

    [Fact]
    public void BlankDestinationIsRejected() =>
        Assert.Contains(Validate(Valid() with { DestinationName = "   " }), e => e.MemberNames.Contains(nameof(TripRequest.DestinationName)));

    [Fact]
    public void PastStartIsLeftToTheApi() =>
        Assert.Empty(Validate(Valid() with { StartDate = new DateOnly(2001, 1, 1), EndDate = new DateOnly(2001, 1, 2) }));

    [Fact]
    public void DetailConvertsToGeneratorInputWithInterestNames()
    {
        var detail = new TripDetailDto
        {
            Id = 4, DestinationName = "Rome", DestinationCountryCode = "IT", StartDate = Start, EndDate = Start.AddDays(1),
            BudgetLevel = TripBudgetLevel.High, Status = TripStatusDto.Draft, Notes = "Gelato",
            Interests = [new(1, "Food", null), new(3, "History", null)],
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        };
        var input = detail.ToTripInput();
        Assert.Equal(["Food", "History"], input.Interests);
        Assert.Equal(TripBudgetLevel.High, input.BudgetLevel);
        Assert.Equal("IT", input.DestinationCountryCode);
        Assert.Equal("Gelato", input.Notes);
    }

    [Fact]
    public void SuggestedStopValidatesItsRanges()
    {
        var stop = new SuggestedStopDto { DayNumber = 1, TimeSlot = ItineraryTimeSlot.Morning, Name = "Colosseum", EstimatedCost = 18m, EstimatedVisitMinutes = 120 };
        var errors = new List<ValidationResult>();
        Assert.True(Validator.TryValidateObject(stop, new ValidationContext(stop), errors, true));
        var bad = stop with { DayNumber = 0, EstimatedCost = -1m, EstimatedVisitMinutes = 0, Name = "" };
        Assert.False(Validator.TryValidateObject(bad, new ValidationContext(bad), errors, true));
        Assert.Equal(4, errors.Count);
    }
}
