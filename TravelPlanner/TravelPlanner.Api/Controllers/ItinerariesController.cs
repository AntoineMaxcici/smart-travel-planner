using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TravelPlanner.Api.Itineraries;
using TravelPlanner.Core.Dtos;
using TravelPlanner.Core.Services;

namespace TravelPlanner.Api.Controllers;

[ApiController, Authorize, Route("api/itineraries")]
public sealed class ItinerariesController(IItineraryGenerator generator) : ControllerBase
{
    // Generates a preview. Saving trips and itineraries is a separate operation.
    [HttpPost("generate")]
    [RequestSizeLimit(16 * 1024)]
    public async Task<ActionResult<ItineraryDraftDto>> Generate(TripInput trip, CancellationToken cancellationToken)
    {
        try
        {
            var days = await generator.GenerateAsync(trip, cancellationToken);
            ItineraryValidation.ValidateDays(trip, days);
            return Ok(new ItineraryDraftDto { Days = days });
        }
        catch (ItineraryGenerationException error)
        {
            var status = error.Failure switch
            {
                GenerationFailure.RateLimited => StatusCodes.Status429TooManyRequests,
                GenerationFailure.Timeout => StatusCodes.Status504GatewayTimeout,
                GenerationFailure.InvalidResponse or GenerationFailure.Blocked => StatusCodes.Status502BadGateway,
                _ => StatusCodes.Status503ServiceUnavailable
            };
            return StatusCode(status, new ApiError(error.Message));
        }
    }
}
