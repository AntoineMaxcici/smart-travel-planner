using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TravelPlanner.Core.Dtos;
using TravelPlanner.Core.Services;

namespace TravelPlanner.Core.ViewModels;

/// <summary>A choice in the budget picker.</summary>
public sealed record BudgetOption(TripBudgetLevel Level, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One selectable interest chip on the New Trip page.</summary>
public partial class InterestChipViewModel(InterestDto interest) : ObservableObject
{
    public InterestDto Interest { get; } = interest;
    public int Id => Interest.Id;
    public string Name => Interest.Name;
    public string? Description => Interest.Description;
    [ObservableProperty]
    private bool isSelected;
}

public partial class NewTripViewModel : ObservableObject
{
    public static readonly IReadOnlyList<BudgetOption> BudgetOptions =
    [
        new(TripBudgetLevel.Low, "$ Budget: hostels, street food, free sights"),
        new(TripBudgetLevel.Medium, "$$ Comfort: mid-range hotels and restaurants"),
        new(TripBudgetLevel.High, "$$$ Premium: top hotels and experiences")
    ];

    private readonly ITripsApi api;
    private readonly INavigationService navigation;
    private readonly TimeProvider clock;

    public NewTripViewModel(ITripsApi api, INavigationService navigation, TimeProvider clock)
    {
        this.api = api;
        this.navigation = navigation;
        this.clock = clock;
        var today = Today.ToDateTime(TimeOnly.MinValue);
        startDate = today.AddDays(7);
        endDate = today.AddDays(10);
        selectedBudget = BudgetOptions[1];
    }

    public ObservableCollection<InterestChipViewModel> Interests { get; } = [];
    public IReadOnlyList<BudgetOption> Budgets => BudgetOptions;
    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    /// <summary>The earliest date the pickers allow: the API rejects trips that start in the past.</summary>
    public DateTime MinimumDate => Today.ToDateTime(TimeOnly.MinValue);
    public DateTime MaximumEndDate => StartDate.Date.AddDays(TripInput.MaximumDays - 1);

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string destination = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DurationText), nameof(MaximumEndDate))]
    private DateTime startDate;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DurationText))]
    private DateTime endDate;
    [ObservableProperty]
    private BudgetOption? selectedBudget;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsNotBusy)),
     NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(LoadInterestsCommand), nameof(CancelCommand))]
    private bool isBusy;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowInterestsLoading))]
    private bool isLoadingInterests;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasInterestsError))]
    private string? interestsError;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    public bool IsNotBusy => !IsBusy;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasInterestsError => !string.IsNullOrWhiteSpace(InterestsError);
    public bool ShowInterestsLoading => IsLoadingInterests && Interests.Count == 0;
    public int SelectedInterestCount => Interests.Count(chip => chip.IsSelected);
    public string SelectedInterestsText => SelectedInterestCount switch
    {
        0 => "Pick at least one",
        1 => "1 selected",
        var count => $"{count} selected"
    };

    public string DurationText
    {
        get
        {
            var days = (EndDate.Date - StartDate.Date).Days + 1;
            return days < 1 ? "End date is before the start date" : days == 1 ? "1 day" : $"{days} days";
        }
    }

    // Keeps the range valid while the user moves the start date.
    partial void OnStartDateChanged(DateTime value)
    {
        if (EndDate.Date < value.Date) EndDate = value.Date;
        else if ((EndDate.Date - value.Date).Days >= TripInput.MaximumDays) EndDate = value.Date.AddDays(TripInput.MaximumDays - 1);
    }

    private bool CanLoadInterests() => !IsBusy && !IsLoadingInterests;

    /// <summary>Loads the interest chips from /api/interests. The page calls this when it appears; it is also the retry button.</summary>
    [RelayCommand(CanExecute = nameof(CanLoadInterests))]
    private async Task LoadInterestsAsync(CancellationToken cancellationToken)
    {
        if (IsLoadingInterests || Interests.Count > 0) return;
        IsLoadingInterests = true;
        InterestsError = null;
        LoadInterestsCommand.NotifyCanExecuteChanged();
        try
        {
            foreach (var interest in await api.GetInterestsAsync(cancellationToken))
                Interests.Add(new InterestChipViewModel(interest));
            if (Interests.Count == 0) InterestsError = "No interests are available yet.";
        }
        catch (UnauthorizedSessionException) { /* The session ended; the app is returning to sign-in. */ }
        catch (TripsApiException ex) { InterestsError = ex.Message; }
        catch (HttpRequestException) { InterestsError = "Cannot reach TravelPlanner. Check your connection and try again."; }
        catch (OperationCanceledException) { InterestsError = "Loading interests timed out. Please try again."; }
        finally
        {
            IsLoadingInterests = false;
            LoadInterestsCommand.NotifyCanExecuteChanged();
            SaveCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(ShowInterestsLoading));
        }
    }

    [RelayCommand]
    private void ToggleInterest(InterestChipViewModel? chip)
    {
        if (chip is null || IsBusy) return;
        if (!chip.IsSelected && SelectedInterestCount >= TripRequest.MaximumInterests)
        {
            ErrorMessage = $"Choose at most {TripRequest.MaximumInterests} interests.";
            return;
        }
        chip.IsSelected = !chip.IsSelected;
        if (ErrorMessage?.StartsWith("Choose ", StringComparison.Ordinal) == true) ErrorMessage = null;
        OnPropertyChanged(nameof(SelectedInterestCount));
        OnPropertyChanged(nameof(SelectedInterestsText));
    }

    private bool CanSave() => !IsBusy && !string.IsNullOrWhiteSpace(Destination) && Interests.Count > 0;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return;
        ErrorMessage = null;
        var request = new TripRequest
        {
            DestinationName = Destination.Trim(),
            StartDate = DateOnly.FromDateTime(StartDate),
            EndDate = DateOnly.FromDateTime(EndDate),
            BudgetLevel = SelectedBudget?.Level ?? TripBudgetLevel.Medium,
            InterestIds = Interests.Where(chip => chip.IsSelected).Select(chip => chip.Id).ToArray()
        };
        var problem = Validate(request);
        if (problem is not null)
        {
            ErrorMessage = problem;
            return;
        }
        IsBusy = true;
        try
        {
            await api.CreateTripAsync(request, cancellationToken);
            // My Trips reloads when it appears again, so the new trip shows up there.
            await navigation.GoBackAsync();
        }
        catch (UnauthorizedSessionException) { /* The session ended; the app is returning to sign-in. */ }
        catch (TripsApiException ex) { ErrorMessage = ex.Message; }
        catch (HttpRequestException) { ErrorMessage = "Cannot reach TravelPlanner. Check your connection and try again."; }
        catch (OperationCanceledException) { ErrorMessage = "Saving timed out. Please try again."; }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private Task CancelAsync() => navigation.GoBackAsync();

    // The same rules the API applies, checked first so the user gets an answer without a round trip.
    private string? Validate(TripRequest request)
    {
        if (TripRules.StartsInThePast(request.StartDate, clock)) return TripRules.PastStartMessage;
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true)) return null;
        return results.Select(result => result.ErrorMessage).FirstOrDefault(message => !string.IsNullOrWhiteSpace(message))
            ?? "Check the trip details and try again.";
    }
}
