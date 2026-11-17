namespace TravelPlanner.Api.Itineraries;

public enum GenerationFailure { NotConfigured, RateLimited, Unavailable, Timeout, Blocked, InvalidResponse }

public sealed class ItineraryGenerationException(GenerationFailure failure) : Exception(failure switch
{
    GenerationFailure.NotConfigured => "Itinerary generation is not configured. Please contact the administrator.",
    GenerationFailure.RateLimited => "The itinerary service has reached its request limit. Please try again later.",
    GenerationFailure.Timeout => "Itinerary generation took too long. Please try again.",
    GenerationFailure.Blocked => "An itinerary could not be generated for this trip. Please revise your trip details.",
    GenerationFailure.InvalidResponse => "The itinerary service returned an incomplete or invalid itinerary. Please try again.",
    _ => "The itinerary service is temporarily unavailable. Please try again later."
})
{
    public GenerationFailure Failure { get; } = failure;
}
