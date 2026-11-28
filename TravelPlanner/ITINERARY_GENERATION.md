# Gemini itinerary generation

The API provides `POST /api/itineraries/generate`, protected by bearer authentication and the `ItineraryGeneration` rate-limit policy. It returns a preview containing `days`, each with its ordered `stops`. It does not save trips or itineraries or enrich places with Google Places data.

Generation permits 10 requests per signed-in user per fixed one-hour window by default. Configure `RateLimiting:ItineraryGeneration:PermitLimit` and `WindowSeconds` in `appsettings.json` or environment configuration. The partition key uses the JWT `sub` user ID, so users on the same IP have separate quotas and signing in again does not reset a quota. Authentication and authorization run before the limiter: anonymous calls return 401 without consuming generation quota. The auth endpoints retain their separate per-IP policies.

Requests over the limit return 429 with a positive `Retry-After` header in seconds and never invoke the generator. Failed or invalid authenticated generation attempts also count toward this request limit. Limits are held in memory per API process; deployments with multiple instances need a shared limiter to enforce one aggregate user quota.

## Local key setup

1. Open [Google AI Studio API keys](https://aistudio.google.com/apikey) and sign in to your Google account. Create a Gemini key in a project using the **Free** tier. Do not enable billing to run these checks. Google may require its terms or project verification first.
2. From the `TravelPlanner` folder, run `powershell -File .\tools\Set-GeminiKey.ps1` and paste the key into the hidden prompt. This saves **Gemini:ApiKey** to the API's existing .NET user-secrets store. It merges with the database connection and JWT secrets; it does not replace them. Do not paste the key into source files or chat.
3. Start the API in Development: `dotnet run --project TravelPlanner.Api`.

The key is server-side only. Production should use an environment secret named `Gemini__ApiKey`; .NET user-secrets are for local development. No key is committed or sent to MAUI. Missing keys return a helpful 503 for generation while authentication remains available.

Non-secret settings in `TravelPlanner.Api/appsettings.json` are `Gemini:Model`, `TimeoutSeconds` and `MaxOutputTokens`. The default is `gemini-3.5-flash-lite`, which Google's pricing documentation lists with free-tier text input/output as checked on November 15, 2026. Availability and quotas depend on the Google project. The code makes no automatic retries or paid-model fallbacks.

## Request

Sign in first and use the returned access token:

```http
POST /api/itineraries/generate
Authorization: Bearer <your access token>
Content-Type: application/json

{
  "destinationName": "Paris",
  "destinationCountryCode": "FR",
  "startDate": "2027-05-10",
  "endDate": "2027-05-11",
  "budgetLevel": "Medium",
  "interests": ["art", "history", "local food"],
  "notes": "First visit; leave time for lunch."
}
```

Trips may span 1–14 inclusive calendar days and have up to 10 interests. The start date must be today or later, using the current UTC date. `budgetLevel` is Low, Medium or High. Country codes, when present, are two uppercase letters. The API validates dates, text lengths, interests and budget before invoking the generator.

`IItineraryGenerator.GenerateAsync(TripInput, CancellationToken)` is the single-method boundary in Core. `GeminiItineraryGenerator` implements it on the API side through an injected HttpClient. Requests go to Google's fixed HTTPS `generateContent` endpoint, with the key in the redacted `x-goog-api-key` header. Redirects are disabled.

The generation configuration requests `application/json` and a `responseJsonSchema` exported from the actual `ItineraryDraftDto`, `ItineraryDayDto` and `ItineraryStopDto` serialization contracts, including field constraints. The prompt is versioned `itinerary-v2`. It separates system instructions from serialized trip data and asks for 3–6 real stops per day, geographic grouping, travel time, budget and interest matching, and nulls for uncertain details. Day and stop counts appear in descriptions rather than nested schema array bounds: real Gemini requests reject the bounded schema with `400 INVALID_ARGUMENT` because those bounds expand the output grammar. The API still enforces exact day counts and 3–6 stops per day after parsing.

Responses must be complete (`finishReason: STOP`), fit within 1 MiB, deserialize into the DTOs without unknown or missing properties, and pass validation of exact dates/day count, stop order, time slots, non-overlapping times, coordinate pairs and ranges, costs/currencies and durations. For stops with a start time, the API derives the Morning/Afternoon/Evening display label from that time; real responses sometimes label 11:30 as Afternoon or 17:00 as Evening. This deterministic correction leaves the generated schedule unchanged. All other invalid values are rejected. Opening hours, attraction existence, reservations, costs and real-world travel estimates still need independent checking; JSON validation cannot prove those facts.

Invalid input returns 400; anonymous calls return 401; provider quota returns 429; blocked/incomplete/invalid output returns 502; missing configuration or provider connection errors return 503; deadlines return 504. Caller cancellation propagates. The deadline includes reading the response body. Provider error bodies, prompts, keys and raw user input are not included in application error messages.

Domain validation runs once in the controller after the generator returns, protecting the endpoint even when a different generator or fake is injected. Direct callers of `IItineraryGenerator`, including the live-check tool, validate the candidate days before using them.

## Saved trip generation

`POST /api/trips/{id}/generate` has no request body. It uses the saved trip and its interest names, requires the owner's bearer token, and shares the `ItineraryGeneration` user quota with the preview endpoint. Missing trips and other users' trips both return 404. Past/invalid trip details return 400 before a run is created.

The endpoint commits a `Pending` GenerationRun and sets the trip to `Generating` before calling Gemini. Concurrent generation, editing, or deletion of that trip returns 409. A successful request returns 200 with `generationRunId`, `itineraryId`, `revisionNumber`, `status: "Ready"`, and `days` with their stops. This is a synchronous request; it returns when generation finishes rather than starting a background job.

The workflow has a shared **20-second deadline** for generation, its retry, and enrichment/persistence. Malformed JSON, schema failures, or invalid generated days/stops get exactly one retry within the remaining time. Configuration, quota, blocked-content, availability and timeout failures are not retried. The preview endpoint's existing timeout remains unchanged.

Each successful run saves the next revision (maximum existing revision + 1), its days/stops and `IsCurrent = true`. Previous revisions remain stored with `IsCurrent = false`. Saving, enrichment and publishing the revision are one transaction, so a failed attempt keeps the previous current revision intact. Runs finish as `Succeeded` with the trip `Ready`, or `Failed` with the trip `Failed`, completion time, duration, error code and safe message. Request cancellation also records failure using a separate short cleanup deadline. This is request-scoped execution; recovery after process termination is not a durable job/recovery system.

Failures return `{ "generationRunId": 123, "code": "Timeout", "message": "Itinerary generation took too long. Please try again." }`. The app can show `message`. HTTP statuses are 504 for timeout, 502 for invalid/blocked output, 429 for provider quota, and 503 for unavailable/unconfigured services. Provider payloads and exception details are not returned or logged.

`IPlacesEnricher.EnrichAsync(Itinerary, CancellationToken)` receives the saved aggregate, including stop IDs, inside the publication transaction. The workflow saves its modifications before marking success. The registered implementation is `PlacesEnricher` (Google Places lookups and stop ordering, see [PLACES_ENRICHMENT.md](PLACES_ENRICHMENT.md)); `NoOpPlacesEnricher` remains for tests. Enrichers must honor cancellation and should avoid irreversible external writes because database rollback cannot undo those.

Verified on November 22, 2026 with PostgreSQL and fake generation/enrichment: 103 Core and 198 API tests passed (301 total). Coverage includes saved revisions, Pending visibility, ownership, shared rate limits, retry exhaustion/recovery, the shared timeout during both generation and enrichment, cancellation cleanup, concurrency, past dates and rollback after enrichment failure. These checks do not use Gemini quota.

## Automated and live verification

Verified on November 17, 2026: 90 shared-code tests and 148 API tests passed (238 total), including per-user throttling, anonymous requests, quota isolation across users and refreshed tokens, invalid limit settings, and past/today/future trip dates. API Debug/Release and Windows MAUI Debug builds completed without warnings or errors; the Windows build used cached restored dependencies.

The real prompt was verified on November 15, 2026: Paris (2 days, 8 stops), Tokyo (2 days, 7 stops) and Beirut (2 days, 7 stops) passed with the new TravelPlanner project's free-tier key. The review changes were tested with fake generators and consumed no Gemini quota. Other native platform builds were not exercised.

```powershell
dotnet test TravelPlanner.Core.Tests
dotnet test TravelPlanner.Api.Tests
dotnet build TravelPlanner.Api
dotnet build TravelPlanner.Maui -f net9.0-windows10.0.19041.0 -p:MauiTargetFrameworks=net9.0-windows10.0.19041.0
```

API integration tests need the same PostgreSQL setup as the authentication tests. They create and clean up isolated test databases. Itinerary endpoint tests replace `IItineraryGenerator` with a fake. Gemini adapter tests inject a fake HTTP handler, including Paris, Tokyo and Beirut examples, so normal tests need no Gemini key and consume no Gemini quota.

To test the real prompt with the saved key, run from `TravelPlanner`:

```powershell
dotnet run --project tools/TravelPlanner.GeminiSmoke -- --live gemini-three-cities.json
```

This makes one request each for Paris, Tokyo and Beirut using two-day trips next month, validates every returned itinerary and writes the inputs and outputs to a JSON report. All three must succeed for exit code 0. It returns exit code 2 when a city cannot be verified. A missing key stops immediately without making any requests. A local generated report is ignored by Git. Review the actual suggestions in the report in addition to the structural checks.

Export the exact schema without contacting Google:

```powershell
dotnet run --project tools/TravelPlanner.GeminiSmoke -- --schema gemini-response-schema.json
```

Google references: [API keys](https://ai.google.dev/gemini-api/docs/api-key), [structured output](https://ai.google.dev/gemini-api/docs/structured-output), [generateContent REST reference](https://ai.google.dev/api/generate-content), [model details](https://ai.google.dev/gemini-api/docs/models/gemini-3.5-flash-lite), [pricing](https://ai.google.dev/gemini-api/docs/pricing#gemini-3.5-flash-lite).
