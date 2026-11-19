using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelPlanner.Api.Data;
using TravelPlanner.Api.Data.Entities;
using TravelPlanner.Api.Trips;
using TravelPlanner.Core.Dtos;

namespace TravelPlanner.Api.Controllers;

// Every query is filtered by the signed-in user's ID. Another user's trip returns 404, not 403,
// so the API never reveals which trip IDs exist.
[ApiController, Authorize, Route("api/trips")]
public sealed class TripsController(AppDbContext database, TimeProvider clock) : ControllerBase
{
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 50;

    private string UserId => User.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? throw new InvalidOperationException("The access token has no subject claim.");

    private IQueryable<Trip> MyTrips => database.Trips.Where(t => t.UserId == UserId);

    [HttpGet]
    public async Task<ActionResult<PagedResult<TripSummaryDto>>> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = DefaultPageSize, CancellationToken cancellationToken = default)
    {
        if (page < 1) ModelState.AddModelError(nameof(page), "Page must be 1 or more.");
        if (pageSize is < 1 or > MaximumPageSize)
            ModelState.AddModelError(nameof(pageSize), $"Page size must be between 1 and {MaximumPageSize}.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var trips = MyTrips.AsNoTracking();
        var total = await trips.CountAsync(cancellationToken);
        var items = await trips
            .OrderByDescending(t => t.StartDate).ThenByDescending(t => t.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(t => new TripSummaryDto
            {
                Id = t.Id,
                DestinationName = t.DestinationName,
                DestinationCountryCode = t.DestinationCountryCode,
                StartDate = t.StartDate,
                EndDate = t.EndDate,
                BudgetLevel = t.BudgetLevel.ToDto(),
                Status = t.Status.ToDto(),
                Interests = t.TripInterests.OrderBy(ti => ti.InterestId).Select(ti => ti.Interest.Name).ToList(),
                UpdatedAtUtc = t.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken);
        return new PagedResult<TripSummaryDto> { Items = items, Page = page, PageSize = pageSize, TotalCount = total };
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TripDetailDto>> Get(int id, CancellationToken cancellationToken)
    {
        var trip = await LoadAsync(id, tracked: false, cancellationToken);
        return trip is null ? TripNotFound() : trip.ToDetailDto();
    }

    [HttpPost]
    public async Task<ActionResult<TripDetailDto>> Create(TripRequest request, CancellationToken cancellationToken)
    {
        if (TripRules.StartsInThePast(request.StartDate, clock))
            ModelState.AddModelError(nameof(TripRequest.StartDate), TripRules.PastStartMessage);
        var interests = await FindInterestsAsync(request.InterestIds, cancellationToken);
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var now = UtcNow();
        var trip = new Trip { UserId = UserId, Status = TripStatus.Draft, CreatedAtUtc = now, UpdatedAtUtc = now };
        trip.Apply(request);
        trip.TripInterests = interests.Select(i => new TripInterest { Interest = i }).ToList();
        database.Trips.Add(trip);
        await database.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = trip.Id }, trip.ToDetailDto());
    }

    // Saves changes to a trip. The whole trip is sent, including the full list of interests.
    [HttpPut("{id:int}")]
    public async Task<ActionResult<TripDetailDto>> Save(int id, TripRequest request, CancellationToken cancellationToken)
    {
        var trip = await LoadAsync(id, tracked: true, cancellationToken);
        if (trip is null) return TripNotFound();
        // A trip that has already started can still be edited, as long as its start date doesn't move into the past.
        if (request.StartDate != trip.StartDate && TripRules.StartsInThePast(request.StartDate, clock))
            ModelState.AddModelError(nameof(TripRequest.StartDate), TripRules.PastStartMessage);
        var interests = await FindInterestsAsync(request.InterestIds, cancellationToken);
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        trip.Apply(request);
        var wanted = interests.Select(i => i.Id).ToHashSet();
        trip.TripInterests.RemoveAll(ti => !wanted.Contains(ti.InterestId));
        foreach (var interest in interests.Where(i => trip.TripInterests.All(ti => ti.InterestId != i.Id)))
            trip.TripInterests.Add(new TripInterest { Interest = interest });
        trip.UpdatedAtUtc = UtcNow();
        await database.SaveChangesAsync(cancellationToken);
        return trip.ToDetailDto();
    }

    // Deletes the trip with its interests, generation runs and itineraries (cascade).
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var deleted = await MyTrips.Where(t => t.Id == id).ExecuteDeleteAsync(cancellationToken);
        return deleted == 0 ? TripNotFound() : NoContent();
    }

    private Task<Trip?> LoadAsync(int id, bool tracked, CancellationToken cancellationToken)
    {
        var query = MyTrips.Include(t => t.TripInterests).ThenInclude(ti => ti.Interest).Where(t => t.Id == id);
        return (tracked ? query : query.AsNoTracking()).SingleOrDefaultAsync(cancellationToken);
    }

    // Adds a model error for any ID that isn't a known interest.
    private async Task<List<Interest>> FindInterestsAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken)
    {
        var interests = await database.Interests.Where(i => ids.Contains(i.Id)).ToListAsync(cancellationToken);
        var unknown = ids.Except(interests.Select(i => i.Id)).ToArray();
        if (unknown.Length > 0)
            ModelState.AddModelError(nameof(TripRequest.InterestIds), $"Unknown interest IDs: {string.Join(", ", unknown)}.");
        return interests.OrderBy(i => i.Id).ToList();
    }

    // PostgreSQL stores microseconds, so round here to return exactly what was saved.
    private DateTime UtcNow()
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return now.AddTicks(-(now.Ticks % 10));
    }

    private NotFoundObjectResult TripNotFound() => NotFound(new ApiError("Trip not found."));
}
