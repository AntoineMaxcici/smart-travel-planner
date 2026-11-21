using System.Globalization;
using TravelPlanner.Core.Dtos;
using TravelPlanner.Core.Services;
using TravelPlanner.Core.ViewModels;

namespace TravelPlanner.Core.Tests;

public sealed class MyTripsViewModelTests
{
    private readonly StubTripsApi api = new();
    private readonly StubSession session = new() { User = TestTokens.User, Token = "token" };
    private readonly StubNavigation navigation = new();
    private readonly StubDialogs dialogs = new();

    private MyTripsViewModel Create() => new(api, session, navigation, dialogs, TripTestData.Clock());

    [Fact]
    public async Task LoadShowsTripsNewestStartDateFirst()
    {
        api.Seed("Lisbon", 10);
        api.Seed("Kyoto", 40);
        api.Seed("Rome", 2);
        var vm = Create();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "Kyoto", "Lisbon", "Rome" }, vm.Trips.Select(t => t.Destination).ToArray());
        Assert.Equal("3 trips", vm.TripCountText);
        Assert.Equal(3, vm.UpcomingCount);
        Assert.False(vm.ShowEmptyState);
        Assert.False(vm.HasError);
        Assert.False(vm.IsBusy);
        Assert.Equal((1, MyTripsViewModel.PageSize), api.PagesRequested.Single());
    }

    [Fact]
    public async Task ReloadReplacesTheListInsteadOfAppending()
    {
        api.Seed("Lisbon", 10);
        var vm = Create();
        await vm.LoadCommand.ExecuteAsync(null);
        api.Seed("Kyoto", 40);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "Kyoto", "Lisbon" }, vm.Trips.Select(t => t.Destination).ToArray());
    }

    [Fact]
    public async Task NoTripsShowsEmptyStateOnlyAfterLoading()
    {
        var vm = Create();
        Assert.False(vm.ShowEmptyState);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.ShowEmptyState);
        Assert.Empty(vm.Trips);
        Assert.Equal("0 trips", vm.TripCountText);
    }

    [Fact]
    public async Task LoadingStateBlocksDuplicateLoadsAndResetsOnCompletion()
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        api.OnCall = () => pending.Task;
        var vm = Create();
        var load = vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.IsBusy);
        Assert.True(vm.ShowInitialLoading);
        Assert.False(vm.LoadCommand.CanExecute(null));
        Assert.False(vm.NewTripCommand.CanExecute(null));
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(1, api.ListCalls);
        pending.SetResult();
        await load;
        Assert.False(vm.IsBusy);
        Assert.False(vm.ShowInitialLoading);
    }

    [Fact]
    public async Task PullToRefreshFlagIsClearedAfterLoading()
    {
        api.Seed("Lisbon", 10);
        var vm = Create();
        vm.IsRefreshing = true;
        Assert.False(vm.ShowInitialLoading);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.False(vm.IsRefreshing);
    }

    [Theory]
    [InlineData("api")]
    [InlineData("network")]
    [InlineData("timeout")]
    public async Task LoadErrorsAreVisibleAndAllowRetry(string failure)
    {
        api.Seed("Lisbon", 10);
        api.Error = failure switch
        {
            "api" => new TripsApiException("Your trips couldn't be loaded. Please try again."),
            "network" => new HttpRequestException(),
            _ => new TaskCanceledException()
        };
        var vm = Create();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.HasError);
        Assert.False(vm.ShowEmptyState);
        Assert.False(vm.IsBusy);
        Assert.True(vm.LoadCommand.CanExecute(null));
        api.Error = null;
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.False(vm.HasError);
        Assert.Single(vm.Trips);
    }

    [Fact]
    public async Task ExpiredSessionShowsNoErrorBecauseTheAppReturnsToSignIn()
    {
        api.Error = new UnauthorizedSessionException();
        var vm = Create();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.False(vm.HasError);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task LoadMoreAppendsTheNextPage()
    {
        for (var i = 0; i < MyTripsViewModel.PageSize + 5; i++) api.Seed($"City {i:00}", 1 + i);
        var vm = Create();
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(MyTripsViewModel.PageSize, vm.Trips.Count);
        Assert.True(vm.HasMore);
        Assert.True(vm.LoadMoreCommand.CanExecute(null));
        await vm.LoadMoreCommand.ExecuteAsync(null);
        Assert.Equal(MyTripsViewModel.PageSize + 5, vm.Trips.Count);
        Assert.Equal(vm.Trips.Count, vm.Trips.Select(t => t.Id).Distinct().Count());
        Assert.False(vm.HasMore);
        Assert.False(vm.LoadMoreCommand.CanExecute(null));
        Assert.Equal(2, api.PagesRequested[1].Page);
    }

    [Fact]
    public async Task LoadMoreDoesNothingWhenEverythingIsShown()
    {
        api.Seed("Lisbon", 10);
        var vm = Create();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.LoadMoreCommand.ExecuteAsync(null);
        Assert.Equal(1, api.ListCalls);
    }

    [Fact]
    public async Task NewTripOpensTheNewTripPage()
    {
        var vm = Create();
        await vm.NewTripCommand.ExecuteAsync(null);
        Assert.Equal(1, navigation.NewTripCalls);
    }

    [Fact]
    public async Task DeleteAsksForConfirmationThenRemovesTheTrip()
    {
        api.Seed("Lisbon", 10);
        api.Seed("Kyoto", 40);
        var vm = Create();
        await vm.LoadCommand.ExecuteAsync(null);
        var kyoto = vm.Trips.Single(t => t.Destination == "Kyoto");
        await vm.DeleteTripCommand.ExecuteAsync(kyoto);
        var dialog = Assert.Single(dialogs.Shown);
        Assert.Equal("Delete trip?", dialog.Title);
        Assert.Contains("Kyoto", dialog.Message);
        Assert.Equal("Delete", dialog.Accept);
        Assert.Equal(new[] { "Lisbon" }, vm.Trips.Select(t => t.Destination).ToArray());
        Assert.Equal("1 trip", vm.TripCountText);
        Assert.Equal(1, (await api.Inner.GetTripsAsync()).TotalCount);
    }

    [Fact]
    public async Task DecliningTheConfirmationKeepsTheTripAndCallsNothing()
    {
        api.Seed("Lisbon", 10);
        dialogs.Answer = false;
        var vm = Create();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.DeleteTripCommand.ExecuteAsync(vm.Trips[0]);
        Assert.Single(dialogs.Shown);
        Assert.Equal(0, api.DeleteCalls);
        Assert.Single(vm.Trips);
    }

    [Fact]
    public async Task DeleteFailureShowsErrorAndKeepsTheTrip()
    {
        api.Seed("Lisbon", 10);
        var vm = Create();
        await vm.LoadCommand.ExecuteAsync(null);
        api.Error = new TripsApiException("Trip not found.");
        await vm.DeleteTripCommand.ExecuteAsync(vm.Trips[0]);
        Assert.Equal("Trip not found.", vm.ErrorMessage);
        Assert.Single(vm.Trips);
        Assert.False(vm.IsBusy);
        Assert.True(vm.DeleteTripCommand.CanExecute(vm.Trips[0]));
    }

    [Fact]
    public async Task DeleteWithNoTripDoesNothing()
    {
        var vm = Create();
        await vm.DeleteTripCommand.ExecuteAsync(null);
        Assert.Empty(dialogs.Shown);
        Assert.Equal(0, api.DeleteCalls);
    }

    [Fact]
    public void GreetingUsesTheFirstNameOrFallsBack()
    {
        session.User = new UserProfile("u", "maya@example.com", "Maya Haddad");
        Assert.Equal("Hi, Maya", Create().Greeting);
        session.User = new UserProfile("u", "maya@example.com", null);
        Assert.Equal("Hi, traveler", Create().Greeting);
    }

    [Fact]
    public async Task SignOutClearsSessionAndRaisesNavigationEvent()
    {
        var events = 0;
        session.SessionChanged += (_, _) => events++;
        var vm = Create();
        await vm.SignOutCommand.ExecuteAsync(null);
        Assert.Equal(1, session.SignOutCalls);
        Assert.False(session.IsAuthenticated);
        Assert.Null(session.Token);
        Assert.Equal(1, events);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task SignOutStorageFailureShowsErrorAndAllowsRetry()
    {
        session.OnSignOut = () => Task.FromException(new AuthException("Storage unavailable."));
        var vm = Create();
        await vm.SignOutCommand.ExecuteAsync(null);
        Assert.True(vm.HasError);
        Assert.True(session.IsAuthenticated);
        Assert.True(vm.SignOutCommand.CanExecute(null));
    }
}

public sealed class TripCardViewModelTests
{
    private static TripCardViewModel Card(int startInDays, int days, TripBudgetLevel budget = TripBudgetLevel.Medium,
        TripStatusDto status = TripStatusDto.Draft, params string[] interests) =>
        new(TripTestData.Summary(7, "lisbon", startInDays, days, status, budget, interests), TripTestData.Today);

    [Theory]
    [InlineData(10, 3, "In 10 days")]
    [InlineData(1, 3, "Starts tomorrow")]
    [InlineData(0, 3, "Happening now")]
    [InlineData(-1, 3, "Happening now")]
    [InlineData(-5, 3, "Completed")]
    public void CountdownDescribesWhenTheTripHappens(int startInDays, int days, string expected)
    {
        var card = Card(startInDays, days);
        Assert.Equal(expected, card.CountdownText);
        Assert.Equal(expected == "Completed", card.IsPast);
    }

    [Theory]
    [InlineData(1, "1 day")]
    [InlineData(2, "2 days · 1 night")]
    [InlineData(5, "5 days · 4 nights")]
    public void DurationCountsDaysAndNights(int days, string expected) => Assert.Equal(expected, Card(3, days).DurationText);

    [Fact]
    public void DateRangeIsCompactWithinOneYear()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal("1 Dec – 3 Dec 2026", Card(10, 3).DateRangeText);
            Assert.Equal("29 Dec 2026 – 2 Jan 2027", Card(38, 5).DateRangeText);
            Assert.Equal("1 Dec 2026", Card(10, 1).DateRangeText);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void LabelsDescribeBudgetStatusAndInterests()
    {
        var card = Card(3, 2, TripBudgetLevel.High, TripStatusDto.Ready, "Food", "Art");
        Assert.Equal("$$$ Premium", card.BudgetText);
        Assert.Equal("Itinerary ready", card.StatusText);
        Assert.Equal("Food · Art", card.InterestsText);
        Assert.Equal("L", card.Initial);
        Assert.Equal("No interests yet", Card(3, 2).InterestsText);
        Assert.InRange(card.Palette, 0, 3);
    }
}
