using Microsoft.Extensions.Time.Testing;
using TravelPlanner.Core.Dtos;
using TravelPlanner.Core.Services;

namespace TravelPlanner.Core.Tests;

internal static class TripTestData
{
    // A fixed "today" so dates in the tests never depend on when they run.
    public static readonly DateTimeOffset Now = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);
    public static DateOnly Today => DateOnly.FromDateTime(Now.UtcDateTime);
    public static FakeTimeProvider Clock() => new(Now);

    public static TripSummaryDto Summary(int id, string destination, int startInDays, int days = 3,
        TripStatusDto status = TripStatusDto.Draft, TripBudgetLevel budget = TripBudgetLevel.Medium, params string[] interests) => new()
    {
        Id = id,
        DestinationName = destination,
        DestinationCountryCode = null,
        StartDate = Today.AddDays(startInDays),
        EndDate = Today.AddDays(startInDays + days - 1),
        BudgetLevel = budget,
        Status = status,
        Interests = interests,
        UpdatedAtUtc = Now.UtcDateTime
    };
}

/// <summary>Wraps FakeTripsApi so tests can count calls, hold a call open, or make it fail.</summary>
internal sealed class StubTripsApi(FakeTripsApi? inner = null) : ITripsApi
{
    public FakeTripsApi Inner { get; } = inner ?? new FakeTripsApi(TripTestData.Clock(), includeSampleTrips: false);
    public Func<Task> OnCall { get; set; } = () => Task.CompletedTask;
    public Exception? Error { get; set; }
    public int ListCalls { get; private set; }
    public int CreateCalls { get; private set; }
    public int DeleteCalls { get; private set; }
    public int InterestCalls { get; private set; }
    public List<(int Page, int PageSize)> PagesRequested { get; } = [];
    public TripRequest? LastCreate { get; private set; }

    public async Task<PagedResult<TripSummaryDto>> GetTripsAsync(int page = 1, int pageSize = TripsApi.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        ListCalls++; PagesRequested.Add((page, pageSize));
        await BeforeAsync();
        return await Inner.GetTripsAsync(page, pageSize, cancellationToken);
    }

    public async Task<TripDetailDto> GetTripAsync(int id, CancellationToken cancellationToken = default)
    {
        await BeforeAsync();
        return await Inner.GetTripAsync(id, cancellationToken);
    }

    public async Task<TripDetailDto> CreateTripAsync(TripRequest request, CancellationToken cancellationToken = default)
    {
        CreateCalls++; LastCreate = request;
        await BeforeAsync();
        return await Inner.CreateTripAsync(request, cancellationToken);
    }

    public async Task<TripDetailDto> SaveTripAsync(int id, TripRequest request, CancellationToken cancellationToken = default)
    {
        await BeforeAsync();
        return await Inner.SaveTripAsync(id, request, cancellationToken);
    }

    public async Task DeleteTripAsync(int id, CancellationToken cancellationToken = default)
    {
        DeleteCalls++;
        await BeforeAsync();
        await Inner.DeleteTripAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<InterestDto>> GetInterestsAsync(CancellationToken cancellationToken = default)
    {
        InterestCalls++;
        await BeforeAsync();
        return await Inner.GetInterestsAsync(cancellationToken);
    }

    // Adds a trip through the fake so it gets a real ID, starting the given number of days from TripTestData.Today.
    public TripDetailDto Seed(string destination, int startInDays, int days = 3, params int[] interestIds) =>
        Inner.CreateTripAsync(new TripRequest
        {
            DestinationName = destination,
            StartDate = TripTestData.Today.AddDays(startInDays),
            EndDate = TripTestData.Today.AddDays(startInDays + days - 1),
            InterestIds = interestIds.Length == 0 ? [1] : interestIds
        }).GetAwaiter().GetResult();

    private async Task BeforeAsync()
    {
        await OnCall();
        if (Error is not null) throw Error;
    }
}

internal sealed class StubNavigation : INavigationService
{
    public int NewTripCalls { get; private set; }
    public int BackCalls { get; private set; }
    public Task GoToNewTripAsync() { NewTripCalls++; return Task.CompletedTask; }
    public Task GoBackAsync() { BackCalls++; return Task.CompletedTask; }
}

internal sealed class StubDialogs : IDialogService
{
    public bool Answer { get; set; } = true;
    public List<(string Title, string Message, string Accept, string Cancel)> Shown { get; } = [];
    public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel)
    {
        Shown.Add((title, message, accept, cancel));
        return Task.FromResult(Answer);
    }
}
