using TravelPlanner.Api.Data.Entities;

namespace TravelPlanner.Api.Itineraries;

// Receives a saved aggregate (including stop IDs) in the workflow's transaction.
// Implementations may update its places/display fields, but must honor cancellation.
public interface IPlacesEnricher
{
    Task EnrichAsync(Itinerary itinerary, CancellationToken cancellationToken = default);
}

// Replace this registration when Antoine's Places integration is merged.
public sealed class NoOpPlacesEnricher : IPlacesEnricher
{
    public Task EnrichAsync(Itinerary itinerary, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
