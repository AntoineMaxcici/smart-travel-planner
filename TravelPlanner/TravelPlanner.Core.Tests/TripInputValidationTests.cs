using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Time.Testing;
using TravelPlanner.Core.Dtos;

namespace TravelPlanner.Core.Tests;

public sealed class TripInputValidationTests
{
    private sealed class ClockServices(TimeProvider clock) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(TimeProvider) ? clock : null;
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void StartDateMustBeTodayOrLaterInUtc(int dayOffset, bool expected)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2027, 2, 3, 0, 30, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("TestWest", TimeSpan.FromHours(-8), "TestWest", "TestWest"));
        var start = new DateOnly(2027, 2, 3).AddDays(dayOffset);
        var trip = new TripInput { DestinationName = "Paris", StartDate = start, EndDate = start };
        var errors = new List<ValidationResult>();
        var valid = Validator.TryValidateObject(trip, new ValidationContext(trip, new ClockServices(clock), null), errors, true);
        Assert.Equal(expected, valid);
        if (!expected) Assert.Contains(errors, error => error.ErrorMessage == "The trip cannot start in the past." && error.MemberNames.Contains(nameof(TripInput.StartDate)));
    }
}
