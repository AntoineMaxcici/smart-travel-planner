using TravelPlanner.Core.Dtos;
using TravelPlanner.Core.Services;
using TravelPlanner.Core.ViewModels;

namespace TravelPlanner.Core.Tests;

public sealed class NewTripViewModelTests
{
    private readonly StubTripsApi api = new();
    private readonly StubNavigation navigation = new();

    private NewTripViewModel Create() => new(api, navigation, TripTestData.Clock());

    private static DateTime Day(int daysFromToday) => TripTestData.Today.AddDays(daysFromToday).ToDateTime(TimeOnly.MinValue);

    private async Task<NewTripViewModel> ReadyAsync(string destination = "Lisbon", params string[] interests)
    {
        var vm = Create();
        await vm.LoadInterestsCommand.ExecuteAsync(null);
        vm.Destination = destination;
        foreach (var name in interests.Length == 0 ? ["Food"] : interests)
            vm.ToggleInterestCommand.Execute(vm.Interests.Single(chip => chip.Name == name));
        return vm;
    }

    [Fact]
    public void StartsWithAValidFutureRangeAndMediumBudget()
    {
        var vm = Create();
        Assert.Equal(Day(7), vm.StartDate);
        Assert.Equal(Day(10), vm.EndDate);
        Assert.Equal(Day(0), vm.MinimumDate);
        Assert.Equal("4 days", vm.DurationText);
        Assert.Equal(TripBudgetLevel.Medium, vm.SelectedBudget!.Level);
        Assert.Equal(3, vm.Budgets.Count);
    }

    [Fact]
    public async Task InterestsLoadOnceAsChips()
    {
        var vm = Create();
        await vm.LoadInterestsCommand.ExecuteAsync(null);
        await vm.LoadInterestsCommand.ExecuteAsync(null);
        Assert.Equal(1, api.InterestCalls);
        Assert.Equal(FakeTripsApi.SampleInterests.Select(i => i.Name), vm.Interests.Select(chip => chip.Name));
        Assert.All(vm.Interests, chip => Assert.False(chip.IsSelected));
        Assert.False(vm.ShowInterestsLoading);
        Assert.Equal("Pick at least one", vm.SelectedInterestsText);
    }

    [Theory]
    [InlineData("api")]
    [InlineData("network")]
    [InlineData("timeout")]
    public async Task InterestLoadErrorsAreVisibleAndAllowRetry(string failure)
    {
        api.Error = failure switch
        {
            "api" => new TripsApiException("Interests couldn't be loaded. Please try again."),
            "network" => new HttpRequestException(),
            _ => new TaskCanceledException()
        };
        var vm = Create();
        await vm.LoadInterestsCommand.ExecuteAsync(null);
        Assert.True(vm.HasInterestsError);
        Assert.Empty(vm.Interests);
        Assert.False(vm.SaveCommand.CanExecute(null));
        Assert.True(vm.LoadInterestsCommand.CanExecute(null));
        api.Error = null;
        await vm.LoadInterestsCommand.ExecuteAsync(null);
        Assert.False(vm.HasInterestsError);
        Assert.NotEmpty(vm.Interests);
    }

    [Fact]
    public async Task SaveIsDisabledUntilThereIsADestinationAndInterestsToChooseFrom()
    {
        var vm = Create();
        vm.Destination = "Lisbon";
        Assert.False(vm.SaveCommand.CanExecute(null));
        await vm.LoadInterestsCommand.ExecuteAsync(null);
        Assert.True(vm.SaveCommand.CanExecute(null));
        vm.Destination = "   ";
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task TogglingChipsSelectsAndDeselectsThem()
    {
        var vm = Create();
        await vm.LoadInterestsCommand.ExecuteAsync(null);
        var food = vm.Interests[0];
        var art = vm.Interests[^1];
        vm.ToggleInterestCommand.Execute(food);
        vm.ToggleInterestCommand.Execute(art);
        Assert.True(food.IsSelected);
        Assert.Equal("2 selected", vm.SelectedInterestsText);
        vm.ToggleInterestCommand.Execute(food);
        Assert.False(food.IsSelected);
        Assert.Equal(1, vm.SelectedInterestCount);
        Assert.Equal("1 selected", vm.SelectedInterestsText);
    }

    [Fact]
    public async Task SuccessfulSaveSendsTheTripAndGoesBack()
    {
        var vm = await ReadyAsync("  Lisbon  ", "Food", "Beaches");
        vm.StartDate = Day(12);
        vm.EndDate = Day(16);
        vm.SelectedBudget = vm.Budgets.Single(b => b.Level == TripBudgetLevel.Low);
        await vm.SaveCommand.ExecuteAsync(null);
        var request = api.LastCreate!;
        Assert.Equal("Lisbon", request.DestinationName);
        Assert.Equal(TripTestData.Today.AddDays(12), request.StartDate);
        Assert.Equal(TripTestData.Today.AddDays(16), request.EndDate);
        Assert.Equal(TripBudgetLevel.Low, request.BudgetLevel);
        Assert.Equal(new[] { 1, 7 }, request.InterestIds);
        Assert.Equal(1, navigation.BackCalls);
        Assert.False(vm.HasError);
        Assert.False(vm.IsBusy);
        var saved = Assert.Single((await api.Inner.GetTripsAsync()).Items);
        Assert.Equal(new[] { "Food", "Beaches" }, saved.Interests);
    }

    [Fact]
    public async Task SaveWithoutInterestsShowsErrorWithoutCallingApi()
    {
        var vm = Create();
        await vm.LoadInterestsCommand.ExecuteAsync(null);
        vm.Destination = "Lisbon";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("Choose at least one interest.", vm.ErrorMessage);
        Assert.Equal(0, api.CreateCalls);
        Assert.Equal(0, navigation.BackCalls);
    }

    [Fact]
    public async Task StartDateInThePastIsRejectedLocally()
    {
        var vm = await ReadyAsync();
        vm.StartDate = Day(-1);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(TripRules.PastStartMessage, vm.ErrorMessage);
        Assert.Equal(0, api.CreateCalls);
    }

    [Fact]
    public void MovingTheStartDateKeepsTheRangeValid()
    {
        var vm = Create();
        vm.StartDate = Day(20);
        Assert.Equal(Day(20), vm.EndDate);
        vm.EndDate = Day(33);
        Assert.Equal("14 days", vm.DurationText);
        Assert.Equal(Day(33), vm.MaximumEndDate);
        vm.StartDate = Day(15);
        Assert.Equal(Day(28), vm.EndDate);
    }

    [Fact]
    public async Task TooLongTripIsRejectedLocally()
    {
        var vm = await ReadyAsync();
        vm.StartDate = Day(5);
        vm.EndDate = Day(30);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal($"A trip can contain at most {TripInput.MaximumDays} days.", vm.ErrorMessage);
        Assert.Equal(0, api.CreateCalls);
    }

    [Theory]
    [InlineData("api")]
    [InlineData("network")]
    [InlineData("timeout")]
    public async Task SaveErrorsAreVisibleStayOnThePageAndAllowRetry(string failure)
    {
        var vm = await ReadyAsync();
        api.Error = failure switch
        {
            "api" => new TripsApiException("Unknown interest IDs: 99."),
            "network" => new HttpRequestException(),
            _ => new TaskCanceledException()
        };
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.True(vm.HasError);
        Assert.Equal(0, navigation.BackCalls);
        Assert.False(vm.IsBusy);
        Assert.True(vm.SaveCommand.CanExecute(null));
        api.Error = null;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.False(vm.HasError);
        Assert.Equal(1, navigation.BackCalls);
    }

    [Fact]
    public async Task ExpiredSessionShowsNoError()
    {
        var vm = await ReadyAsync();
        api.Error = new UnauthorizedSessionException();
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.False(vm.HasError);
        Assert.Equal(0, navigation.BackCalls);
    }

    [Fact]
    public async Task LoadingStateBlocksDuplicateSubmissionsAndResetsOnCompletion()
    {
        var vm = await ReadyAsync();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        api.OnCall = () => pending.Task;
        var save = vm.SaveCommand.ExecuteAsync(null);
        Assert.True(vm.IsBusy);
        Assert.False(vm.IsNotBusy);
        Assert.False(vm.SaveCommand.CanExecute(null));
        Assert.False(vm.CancelCommand.CanExecute(null));
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(1, api.CreateCalls);
        pending.SetResult();
        await save;
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task CancelGoesBackWithoutSaving()
    {
        var vm = await ReadyAsync();
        await vm.CancelCommand.ExecuteAsync(null);
        Assert.Equal(1, navigation.BackCalls);
        Assert.Equal(0, api.CreateCalls);
    }
}
