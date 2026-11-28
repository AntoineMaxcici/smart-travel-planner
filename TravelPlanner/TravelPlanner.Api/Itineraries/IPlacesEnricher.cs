using TravelPlanner.Api.Data.Entities;

namespace TravelPlanner.Api.Itineraries;

// Receives a saved aggregate (including stop IDs) in the workflow's transaction.
// Implementations may update its places/display fields, but must honor cancellation.
public interface IPlacesEnricher
{
    Task EnrichAsync(Itinerary itinerary, CancellationToken cancellationToken = default);
}

// Not registered by default any more (see Places/PlacesEnricher). Kept for tests that want no enrichment.
public sealed class NoOpPlacesEnricher : IPlacesEnricher
{
    public Task EnrichAsync(Itinerary itinerary, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
