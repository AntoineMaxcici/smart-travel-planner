using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TravelPlanner.Api.Data;
using TravelPlanner.Api.Data.Entities;
using TravelPlanner.Api.Trips;
using TravelPlanner.Core.Dtos;
using TravelPlanner.Core.Services;

namespace TravelPlanner.Api.Itineraries;

public sealed record TripGenerationResult(int StatusCode, object Body);

public sealed class TripGenerationService(AppDbContext database, IItineraryGenerator generator,
    IPlacesEnricher enricher, TimeProvider clock, IOptions<GeminiOptions> settings,
    ILogger<TripGenerationService> logger)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public async Task<TripGenerationResult> GenerateAsync(int id, string userId, CancellationToken cancellationToken)
    {
        Trip trip;
        GenerationRun run;
        TripInput input;
        // The row lock serializes claims and edits. Commit Pending before external work starts.
        await using (var claim = await database.Database.BeginTransactionAsync(cancellationToken))
        {
            var found = await database.Trips.FromSqlInterpolated(
                $"SELECT * FROM \"Trips\" WHERE \"Id\" = {id} AND \"UserId\" = {userId} FOR UPDATE")
                .Include(t => t.TripInterests).ThenInclude(ti => ti.Interest).SingleOrDefaultAsync(cancellationToken);
            if (found is null) return new(404, new ApiError("Trip not found."));
            trip = found;
            if (trip.Status == TripStatus.Generating)
                return new(409, new ApiError("This trip is already generating. Please wait for it to finish."));
            input = trip.ToDetailDto().ToTripInput();
            try { ItineraryValidation.ValidateInput(input); }
            catch (ValidationException)
            {
                return new(400, new ApiError("Check the trip dates and details before generating. The trip must start today or later."));
            }
            trip.Status = TripStatus.Generating;
            trip.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
            run = new GenerationRun
            {
                Trip = trip, Provider = "Gemini", Model = settings.Value.Model, PromptVersion = "v1",
                Status = GenerationStatus.Pending, AttemptedAtUtc = clock.GetUtcNow().UtcDateTime
            };
            database.GenerationRuns.Add(run);
            await database.SaveChangesAsync(cancellationToken);
            await claim.CommitAsync(cancellationToken);
        }

        var elapsed = Stopwatch.StartNew();
        using var timer = new CancellationTokenSource(Timeout, clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(timer.Token, cancellationToken);
        try
        {
            IReadOnlyList<ItineraryDayDto> days = [];
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    days = await generator.GenerateAsync(input, deadline.Token).WaitAsync(deadline.Token);
                    ItineraryValidation.ValidateDays(input, days);
                    break;
                }
                catch (Exception error) when (attempt == 0 && IsBadJson(error) && !deadline.IsCancellationRequested)
                {
                    // Exactly one retry for malformed JSON/schema or invalid generated content.
                }
            }
            deadline.Token.ThrowIfCancellationRequested();
            // Publish the new revision atomically. Rollback preserves the previous current revision
            // if saving/enrichment fails; the Pending run remains available for failure recording.
            await using var publish = await database.Database.BeginTransactionAsync(deadline.Token);
            var revision = (await database.Itineraries.Where(i => i.TripId == id)
                .MaxAsync(i => (int?)i.RevisionNumber, deadline.Token) ?? 0) + 1;
            await database.Itineraries.Where(i => i.TripId == id && i.IsCurrent)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.IsCurrent, false), deadline.Token);
            var itinerary = new Itinerary
            {
                Trip = trip, GenerationRun = run, RevisionNumber = revision, IsCurrent = true,
                Status = ItineraryStatus.Saved, CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
                UpdatedAtUtc = clock.GetUtcNow().UtcDateTime,
                Days = days.Select(day => new ItineraryDay
                {
                    DayNumber = day.DayNumber, ItineraryDate = day.ItineraryDate, Theme = day.Theme, Notes = day.Notes,
                    Stops = day.Stops.Select(stop => new ItineraryStop
                    {
                        SortOrder = stop.SortOrder, TimeSlot = Enum.Parse<TimeSlot>(stop.TimeSlot.ToString()),
                        StartTimeLocal = stop.StartTimeLocal, EndTimeLocal = stop.EndTimeLocal,
                        DisplayName = stop.DisplayName, Category = stop.Category, ShortDescription = stop.ShortDescription,
                        DisplayAddress = stop.DisplayAddress, DisplayLatitude = stop.DisplayLatitude,
                        DisplayLongitude = stop.DisplayLongitude, EstimatedCost = stop.EstimatedCost,
                        CurrencyCode = stop.CurrencyCode, EstimatedVisitMinutes = stop.EstimatedVisitMinutes,
                        TravelMinutesFromPrevious = stop.TravelMinutesFromPrevious
                    }).ToList()
                }).ToList()
            };
            database.Itineraries.Add(itinerary);
            await database.SaveChangesAsync(deadline.Token);
            await enricher.EnrichAsync(itinerary, deadline.Token);
            deadline.Token.ThrowIfCancellationRequested();
            trip.Status = TripStatus.Ready;
            trip.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
            Complete(run, GenerationStatus.Succeeded, elapsed);
            await database.SaveChangesAsync(deadline.Token);
            await publish.CommitAsync(deadline.Token);
            return new(200, new TripGenerationDto(run.Id, itinerary.Id, revision, TripStatusDto.Ready,
                itinerary.Days.Select(d => new ItineraryDayDto
                {
                    DayNumber = d.DayNumber, ItineraryDate = d.ItineraryDate, Theme = d.Theme, Notes = d.Notes,
                    Stops = d.Stops.Select(s => new ItineraryStopDto
                    {
                        SortOrder = s.SortOrder, TimeSlot = Enum.Parse<ItineraryTimeSlot>(s.TimeSlot.ToString()),
                        StartTimeLocal = s.StartTimeLocal, EndTimeLocal = s.EndTimeLocal, DisplayName = s.DisplayName,
                        Category = s.Category, ShortDescription = s.ShortDescription, DisplayAddress = s.DisplayAddress,
                        DisplayLatitude = s.DisplayLatitude, DisplayLongitude = s.DisplayLongitude,
                        EstimatedCost = s.EstimatedCost, CurrencyCode = s.CurrencyCode,
                        EstimatedVisitMinutes = s.EstimatedVisitMinutes, TravelMinutesFromPrevious = s.TravelMinutesFromPrevious
                    }).ToArray()
                }).ToArray()));
        }
        catch (Exception error)
        {
            var cancelled = cancellationToken.IsCancellationRequested;
            var failure = cancelled ? GenerationFailure.Unavailable
                : timer.IsCancellationRequested || error is OperationCanceledException ? GenerationFailure.Timeout
                : IsBadJson(error) ? GenerationFailure.InvalidResponse
                : error is ItineraryGenerationException known ? known.Failure : GenerationFailure.Unavailable;
            var message = cancelled ? "Generation was cancelled. Please try again."
                : new ItineraryGenerationException(failure).Message;
            // Do not log provider payloads, input or exception text.
            logger.LogWarning("Generation run {RunId} failed ({Code}).", run.Id, failure);
            database.ChangeTracker.Clear();
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var savedRun = await database.GenerationRuns.Include(r => r.Trip).SingleAsync(r => r.Id == run.Id, cleanup.Token);
            savedRun.Trip.Status = TripStatus.Failed;
            savedRun.Trip.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
            Complete(savedRun, GenerationStatus.Failed, elapsed);
            savedRun.ErrorCode = cancelled ? "Cancelled" : failure.ToString();
            savedRun.ErrorMessage = message;
            await database.SaveChangesAsync(cleanup.Token);
            if (cancelled) throw;
            var status = failure switch
            {
                GenerationFailure.Timeout => 504, GenerationFailure.InvalidResponse or GenerationFailure.Blocked => 502,
                GenerationFailure.RateLimited => 429, _ => 503
            };
            return new(status, new TripGenerationErrorDto(run.Id, failure.ToString(), message));
        }
    }

    private static bool IsBadJson(Exception error) => error is JsonException
        || error is ItineraryGenerationException { Failure: GenerationFailure.InvalidResponse };

    private void Complete(GenerationRun run, GenerationStatus status, Stopwatch elapsed)
    {
        run.Status = status;
        run.CompletedAtUtc = clock.GetUtcNow().UtcDateTime;
        run.DurationMilliseconds = (int)Math.Min(elapsed.ElapsedMilliseconds, int.MaxValue);
    }
}
