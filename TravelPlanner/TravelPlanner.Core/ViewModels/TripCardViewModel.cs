using System.Globalization;
using TravelPlanner.Core.Dtos;

namespace TravelPlanner.Core.ViewModels;

/// <summary>One trip in the My Trips list, with the text the card shows.</summary>
public sealed class TripCardViewModel(TripSummaryDto trip, DateOnly today)
{
    public TripSummaryDto Trip { get; } = trip;
    public int Id => Trip.Id;
    public string Destination => Trip.DestinationName;
    public string Initial => string.IsNullOrWhiteSpace(Trip.DestinationName) ? "?" : Trip.DestinationName.Trim()[..1].ToUpperInvariant();
    public string? CountryCode => Trip.DestinationCountryCode;
    public bool HasCountry => !string.IsNullOrEmpty(Trip.DestinationCountryCode);
    /// <summary>0–3: which of the four card colour gradients to use, so neighbouring trips look different.</summary>
    public int Palette => Math.Abs(Trip.Id % 4);
    /// <summary>Optional card photo: Resources/Images/trip_photo_1.jpg to trip_photo_4.jpg in the MAUI project. Missing files show the gradient.</summary>
    public string PhotoFile => $"trip_photo_{Palette + 1}.jpg";
    public int Days => Trip.EndDate.DayNumber - Trip.StartDate.DayNumber + 1;
    public string DurationText => Days == 1 ? "1 day" : $"{Days} days · {Days - 1} {(Days - 1 == 1 ? "night" : "nights")}";

    public string DateRangeText
    {
        get
        {
            var culture = CultureInfo.CurrentCulture;
            if (Trip.StartDate == Trip.EndDate) return Trip.StartDate.ToString("d MMM yyyy", culture);
            return Trip.StartDate.Year == Trip.EndDate.Year
                ? $"{Trip.StartDate.ToString("d MMM", culture)} – {Trip.EndDate.ToString("d MMM yyyy", culture)}"
                : $"{Trip.StartDate.ToString("d MMM yyyy", culture)} – {Trip.EndDate.ToString("d MMM yyyy", culture)}";
        }
    }

    public string BudgetText => Trip.BudgetLevel switch
    {
        TripBudgetLevel.Low => "$ Budget",
        TripBudgetLevel.High => "$$$ Premium",
        _ => "$$ Comfort"
    };

    public string StatusText => Trip.Status switch
    {
        TripStatusDto.Generating => "Planning…",
        TripStatusDto.Ready => "Itinerary ready",
        TripStatusDto.Failed => "Needs attention",
        _ => "Draft"
    };

    public bool IsPast => Trip.EndDate < today;
    public bool IsUpcoming => !IsPast;

    /// <summary>"In 12 days", "Starts tomorrow", "Happening now" or "Completed".</summary>
    public string CountdownText
    {
        get
        {
            if (IsPast) return "Completed";
            if (Trip.StartDate <= today) return "Happening now";
            var days = Trip.StartDate.DayNumber - today.DayNumber;
            return days == 1 ? "Starts tomorrow" : $"In {days} days";
        }
    }

    public string InterestsText => Trip.Interests.Count == 0 ? "No interests yet" : string.Join(" · ", Trip.Interests);
}
