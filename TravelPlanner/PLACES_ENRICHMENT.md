# Places enrichment and stop ordering (PB-5, PB-6)

After `POST /api/trips/{id}/generate` saves a new itinerary revision, `PlacesEnricher` runs inside the same
transaction (see [ITINERARY_GENERATION.md](ITINERARY_GENERATION.md)). It does two things:

1. **Real places (PB-5).** Each stop is looked up with Google **Places API (New) Text Search** using
   `"<stop name>, <destination>"`, the trip's country code as `regionCode`, and the AI's coordinates as a 10 km
   location bias. The field mask asks only for id, name, address, location, category, Maps link and weekly opening
   hours. A match is saved in `Places` (one row per Google place id, inserted or updated with a single
   `INSERT ... ON CONFLICT`), linked through `ItineraryStops.PlaceId`, and its name, address and coordinates are
   copied onto the stop.
2. **Sensible order (PB-6).** `StopOrdering` re-orders each day: the first stop stays first, then the nearest
   remaining stop comes next. Stops only move inside their morning/afternoon/evening slot, a slot only changes when
   the route gets at least 200 m shorter, and times are re-laid so stops still don't overlap. Travel minutes for
   stops that now follow a different stop are re-estimated (walk up to 1.5 km, otherwise a ride with 10 minutes of
   waiting). If any stop of a day has no coordinates, or the new times would leave a slot, that day keeps the AI's
   order.

## Fallbacks (FR-4)

A stop keeps the AI's text and an empty `PlaceId` when Google finds nothing, returns an error (quota, bad key,
outage), times out, or returns a place more than 30 km from where the AI put the stop. All lookups of one itinerary
share an 8-second budget, so the trip's 20-second generation deadline isn't used up by Places. Without an API key
no requests are made and stops are only re-ordered. Provider payloads and exception details are never logged.

## Setup

1. In Google Cloud Console, create a project, enable **Places API (New)**, and create an API key restricted to
   that API. A billing account is required; the monthly free usage covers development.
2. Store the key in user-secrets (never in `appsettings.json`):

   ```
   dotnet user-secrets set "GooglePlaces:ApiKey" "<your key>" --project TravelPlanner.Api
   ```

   When deployed, set `GooglePlaces__ApiKey` in the App Service configuration instead.

Optional settings under `GooglePlaces` in `appsettings.json`: `RequestTimeoutSeconds` (5),
`LookupBudgetSeconds` (8), `MaxConcurrentLookups` (4), `MaxDistanceFromSuggestionKm` (30).

## Health check

`GET /health` (no sign-in) returns `200 Healthy` when the API can reach its database and `503 Unhealthy`
otherwise. Point Azure App Service's health check or an uptime monitor at it.

## Tests

`StopOrderingTests` (ordering rules), `GooglePlaceSearchTests` (request shape and parsing with a fake HTTP handler)
and `PlacesEnrichmentTests` (the generate endpoint against PostgreSQL with a fake AI and fake Places search,
including fallbacks, place reuse across revisions and the health endpoint). None of them call Google.
