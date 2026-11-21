using System.ComponentModel.DataAnnotations;
using TravelPlanner.Core.Dtos;

namespace TravelPlanner.Core.Services;

/// <summary>
/// An in-memory ITripsApi with sample trips, for running the app without the API and for tests.
/// It follows the real API's rules: newest start date first, paging, validation, and 404 for unknown trips.
/// Changes last until the app closes.
/// </summary>
public sealed class FakeTripsApi : ITripsApi
{
    // The same interests the database seeds (migration SeedInterests).
    public static readonly IReadOnlyList<InterestDto> SampleInterests =
    [
        new(1, "Food", "Local dishes, markets, cafes and restaurants."),
        new(2, "Museums", "Art, science and history museums."),
        new(3, "History", "Monuments, old towns and historic sites."),
        new(4, "Nature", "Parks, gardens, hikes and viewpoints."),
        new(5, "Nightlife", "Bars, live music and evening entertainment."),
        new(6, "Shopping", "Shopping streets, boutiques and souvenirs."),
        new(7, "Beaches", "Beaches, coastline and waterfront walks."),
        new(8, "Art", "Galleries, street art and architecture.")
    ];

    private readonly Lock gate = new();
    private readonly List<TripDetailDto> trips = [];
    private readonly TimeProvider clock;
    private readonly TimeSpan delay;
    private int nextId = 1;

    /// <param name="delay">Optional pause before each call, to show loading states while running the app.</param>
    public FakeTripsApi(TimeProvider? clock = null, TimeSpan delay = default, bool includeSampleTrips = true)
    {
        this.clock = clock ?? TimeProvider.System;
        this.delay = delay;
        if (!includeSampleTrips) return;
        var today = Today;
        Add("Lisbon", "PT", today.AddDays(12), today.AddDays(16), TripBudgetLevel.Medium, TripStatusDto.Ready, [1, 3, 7],
            "Ride tram 28 and try pastéis de nata in Belém.");
        Add("Kyoto", "JP", today.AddDays(45), today.AddDays(52), TripBudgetLevel.High, TripStatusDto.Draft, [3, 4, 8], null);
        Add("Marrakesh", "MA", today.AddDays(80), today.AddDays(84), TripBudgetLevel.Low, TripStatusDto.Draft, [1, 6], null);
        Add("Reykjavík", "IS", today.AddDays(-20), today.AddDays(-15), TripBudgetLevel.High, TripStatusDto.Ready, [4], "Northern lights tour.");
    }

    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public async Task<PagedResult<TripSummaryDto>> GetTripsAsync(int page = 1, int pageSize = TripsApi.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, TripsApi.MaximumPageSize);
        await PauseAsync(cancellationToken);
        lock (gate)
        {
            var ordered = trips.OrderByDescending(t => t.StartDate).ThenByDescending(t => t.Id).ToList();
            return new PagedResult<TripSummaryDto>
            {
                Items = ordered.Skip((page - 1) * pageSize).Take(pageSize).Select(ToSummary).ToList(),
                Page = page,
                PageSize = pageSize,
                TotalCount = ordered.Count
            };
        }
    }

    public async Task<TripDetailDto> GetTripAsync(int id, CancellationToken cancellationToken = default)
    {
        await PauseAsync(cancellationToken);
        lock (gate) return Find(id);
    }

    public async Task<TripDetailDto> CreateTripAsync(TripRequest request, CancellationToken cancellationToken = default)
    {
        await PauseAsync(cancellationToken);
        Validate(request, checkPastStart: true);
        lock (gate)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var trip = Build(nextId++, request, TripStatusDto.Draft, now, now);
            trips.Add(trip);
            return trip;
        }
    }

    public async Task<TripDetailDto> SaveTripAsync(int id, TripRequest request, CancellationToken cancellationToken = default)
    {
        await PauseAsync(cancellationToken);
        lock (gate)
        {
            var existing = Find(id);
            Validate(request, checkPastStart: request.StartDate != existing.StartDate);
            var saved = Build(id, request, existing.Status, existing.CreatedAtUtc, clock.GetUtcNow().UtcDateTime);
            trips[trips.IndexOf(existing)] = saved;
            return saved;
        }
    }

    public async Task DeleteTripAsync(int id, CancellationToken cancellationToken = default)
    {
        await PauseAsync(cancellationToken);
        lock (gate) trips.Remove(Find(id));
    }

    public async Task<IReadOnlyList<InterestDto>> GetInterestsAsync(CancellationToken cancellationToken = default)
    {
        await PauseAsync(cancellationToken);
        return SampleInterests;
    }

    private void Add(string destination, string country, DateOnly start, DateOnly end, TripBudgetLevel budget,
        TripStatusDto status, int[] interestIds, string? notes)
    {
        var request = new TripRequest
        {
            DestinationName = destination, DestinationCountryCode = country, StartDate = start, EndDate = end,
            BudgetLevel = budget, InterestIds = interestIds, Notes = notes
        };
        var now = clock.GetUtcNow().UtcDateTime;
        trips.Add(Build(nextId++, request, status, now, now));
    }

    private TripDetailDto Find(int id) =>
        trips.FirstOrDefault(t => t.Id == id) ?? throw new TripsApiException("Trip not found.");

    private void Validate(TripRequest request, bool checkPastStart)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        var messages = results.Select(r => r.ErrorMessage).OfType<string>().ToList();
        if (checkPastStart && request.StartDate != default && TripRules.StartsInThePast(request.StartDate, clock))
            messages.Add(TripRules.PastStartMessage);
        var unknown = (request.InterestIds ?? []).Where(id => SampleInterests.All(i => i.Id != id)).ToArray();
        if (unknown.Length > 0) messages.Add($"Unknown interest IDs: {string.Join(", ", unknown)}.");
        if (messages.Count > 0) throw new TripsApiException(string.Join(" ", messages.Distinct()));
    }

    private static TripDetailDto Build(int id, TripRequest request, TripStatusDto status, DateTime createdAtUtc, DateTime updatedAtUtc) => new()
    {
        Id = id,
        DestinationName = request.DestinationName.Trim(),
        DestinationCountryCode = request.DestinationCountryCode,
        StartDate = request.StartDate,
        EndDate = request.EndDate,
        BudgetLevel = request.BudgetLevel,
        Status = status,
        Interests = SampleInterests.Where(i => request.InterestIds.Contains(i.Id)).ToList(),
        Notes = request.Notes,
        CreatedAtUtc = createdAtUtc,
        UpdatedAtUtc = updatedAtUtc
    };

    private static TripSummaryDto ToSummary(TripDetailDto trip) => new()
    {
        Id = trip.Id,
        DestinationName = trip.DestinationName,
        DestinationCountryCode = trip.DestinationCountryCode,
        StartDate = trip.StartDate,
        EndDate = trip.EndDate,
        BudgetLevel = trip.BudgetLevel,
        Status = trip.Status,
        Interests = trip.Interests.Select(i => i.Name).ToList(),
        UpdatedAtUtc = trip.UpdatedAtUtc
    };

    private Task PauseAsync(CancellationToken cancellationToken) =>
        delay > TimeSpan.Zero ? Task.Delay(delay, clock, cancellationToken) : Task.CompletedTask;
}
