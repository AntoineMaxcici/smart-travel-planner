using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelPlanner.Api.Data;
using TravelPlanner.Core.Dtos;

namespace TravelPlanner.Api.Controllers;

[ApiController, Authorize, Route("api/interests")]
public sealed class InterestsController(AppDbContext database) : ControllerBase
{
    // The choices for the interests step of the trip form. Send the IDs back in TripRequest.InterestIds.
    [HttpGet]
    public async Task<IReadOnlyList<InterestDto>> List(CancellationToken cancellationToken) =>
        await database.Interests.AsNoTracking()
            .OrderBy(i => i.Id)
            .Select(i => new InterestDto(i.Id, i.Name, i.Description))
            .ToListAsync(cancellationToken);
}
