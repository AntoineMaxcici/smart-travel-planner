using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TravelPlanner.Core.Dtos;
using TravelPlanner.Core.Services;

namespace TravelPlanner.Core.ViewModels;

/// <summary>The page users see after signing in: their trips, newest start date first.</summary>
public partial class MyTripsViewModel(ITripsApi api, ISessionService session, INavigationService navigation,
    IDialogService dialogs, TimeProvider clock) : ObservableObject
{
    public const int PageSize = 20;
    private int loadedPages;
    private int totalCount;

    public ObservableCollection<TripCardViewModel> Trips { get; } = [];

    public string Greeting => $"Hi, {FirstName(session.User?.DisplayName) ?? "traveler"}";
    public string? Email => session.User?.Email;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsNotBusy), nameof(ShowEmptyState), nameof(ShowInitialLoading)),
     NotifyCanExecuteChangedFor(nameof(LoadCommand), nameof(LoadMoreCommand), nameof(NewTripCommand),
        nameof(DeleteTripCommand), nameof(SignOutCommand))]
    private bool isBusy;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowInitialLoading))]
    private bool isRefreshing;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError), nameof(ShowEmptyState))]
    private string? errorMessage;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool hasLoaded;

    public bool IsNotBusy => !IsBusy;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasMore => Trips.Count < totalCount;
    /// <summary>The full-page spinner, shown only while the list is still empty (pull-to-refresh has its own).</summary>
    public bool ShowInitialLoading => IsBusy && !IsRefreshing && Trips.Count == 0;
    public bool ShowEmptyState => HasLoaded && !IsBusy && !HasError && Trips.Count == 0;
    public string TripCountText => totalCount == 1 ? "1 trip" : $"{totalCount} trips";
    public int UpcomingCount => Trips.Count(trip => trip.IsUpcoming);

    private bool CanRun() => !IsBusy;
    private bool CanLoadMore() => !IsBusy && HasMore;

    /// <summary>Loads (or reloads) the first page. The page calls this each time it appears.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) { IsRefreshing = false; return; }
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var page = await api.GetTripsAsync(1, PageSize, cancellationToken);
            Trips.Clear();
            Append(page.Items);
            loadedPages = 1;
            totalCount = page.TotalCount;
            HasLoaded = true;
        }
        catch (UnauthorizedSessionException) { /* The session ended; the app is returning to sign-in. */ }
        catch (Exception ex) when (IsExpected(ex)) { ErrorMessage = Describe(ex, "Your trips couldn't be loaded."); }
        finally
        {
            IsBusy = false;
            IsRefreshing = false;
            NotifyListChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private async Task LoadMoreAsync(CancellationToken cancellationToken)
    {
        if (IsBusy || !HasMore) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var page = await api.GetTripsAsync(loadedPages + 1, PageSize, cancellationToken);
            // Skip trips already shown, in case one was added since the first page loaded.
            Append(page.Items.Where(item => Trips.All(shown => shown.Id != item.Id)));
            loadedPages = page.Page;
            totalCount = page.TotalCount;
        }
        catch (UnauthorizedSessionException) { /* The session ended; the app is returning to sign-in. */ }
        catch (Exception ex) when (IsExpected(ex)) { ErrorMessage = Describe(ex, "More trips couldn't be loaded."); }
        finally
        {
            IsBusy = false;
            NotifyListChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task NewTripAsync()
    {
        if (IsBusy) return;
        await navigation.GoToNewTripAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task DeleteTripAsync(TripCardViewModel? trip)
    {
        if (IsBusy || trip is null) return;
        var confirmed = await dialogs.ConfirmAsync("Delete trip?",
            $"{trip.Destination} ({trip.DateRangeText}) and its itinerary will be deleted. This can't be undone.",
            "Delete", "Keep trip");
        if (!confirmed) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await api.DeleteTripAsync(trip.Id);
            Trips.Remove(trip);
            totalCount = Math.Max(0, totalCount - 1);
        }
        catch (UnauthorizedSessionException) { /* The session ended; the app is returning to sign-in. */ }
        catch (Exception ex) when (IsExpected(ex)) { ErrorMessage = Describe(ex, "This trip couldn't be deleted."); }
        finally
        {
            IsBusy = false;
            NotifyListChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task SignOutAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        try { await session.SignOutAsync(); }
        catch (AuthException ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    private void Append(IEnumerable<TripSummaryDto> items)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        foreach (var item in items) Trips.Add(new TripCardViewModel(item, today));
    }

    private void NotifyListChanged()
    {
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowInitialLoading));
        OnPropertyChanged(nameof(TripCountText));
        OnPropertyChanged(nameof(UpcomingCount));
        LoadMoreCommand.NotifyCanExecuteChanged();
    }

    private static bool IsExpected(Exception ex) =>
        ex is TripsApiException or AuthException or HttpRequestException or OperationCanceledException;

    private static string Describe(Exception ex, string action) => ex switch
    {
        HttpRequestException => "Cannot reach TravelPlanner. Check your connection and try again.",
        OperationCanceledException => $"{action} The request timed out. Please try again.",
        _ => ex.Message
    };

    private static string? FirstName(string? displayName) =>
        string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim().Split(' ', 2)[0];
}
