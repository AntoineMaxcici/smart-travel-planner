# Authentication

The API checks email/password through Identity's `SignInManager.CheckPasswordSignInAsync` with lockout enabled (five failed attempts, five minutes). It returns a signed HS256 JWT and a public user profile. Unknown users, bad passwords, locked accounts, and accounts that require two-factor authentication receive the same 401 message. Two-factor login and account registration are separate future features; login uses existing Identity accounts.

## Configuration and running

The database connection and a cryptographically generated JWT signing key are saved in the API's .NET user secrets on this computer. Never commit those secrets or copy the signing key into the MAUI app. On another development machine configure:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=travelplanner;Username=postgres;Password=YOUR_PASSWORD" --project TravelPlanner.Api
# Generate a private signing key; this command does not print it.
$key = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(64))
dotnet user-secrets set "Jwt:SigningKey" $key --project TravelPlanner.Api
dotnet run --project TravelPlanner.Api --launch-profile http
```

`appsettings.json` configures issuer `TravelPlanner.Api`, audience `TravelPlanner.Maui`, and expiry of 60 minutes. Startup rejects missing/short signing keys, empty issuer/audience, and expiry outside 1–1440 minutes. Deployment must supply `ConnectionStrings__DefaultConnection` and `Jwt__SigningKey` through the hosting environment and use HTTPS.

Run the MAUI Windows project from Visual Studio, with the API running. Debug builds use `http://localhost:5082/` (Android emulator: `http://10.0.2.2:5082/`). The HTTP profile supports local development without bypassing certificate checks. Android permits local cleartext traffic only in Debug. Release builds require HTTPS. `TRAVELPLANNER_API_URL` overrides the default address; use the deployed API's HTTPS address for physical devices and deployments.

iOS simulator builds include the Keychain entitlement required by MAUI SecureStorage. Device builds do not include that simulator entitlement. If the app bundle identifier changes, update the simulator Keychain access group to match. Apple targets require a Mac and its platform SDKs to build and verify.

## Endpoints

`POST /api/auth/login`:

```json
{ "email": "existing-user@example.com", "password": "their-password" }
```

Success returns `{ "accessToken": "...", "expiresAtUtc": "...", "user": { "id": "...", "email": "...", "displayName": "..." } }`. Invalid input is 400, invalid credentials are 401. Login never issues a cookie.

`GET /api/auth/me` requires `Authorization: Bearer <accessToken>`. It returns only the current user's public profile. Missing, expired, incorrectly signed, incorrectly issued/audienced tokens and tokens belonging to deleted users return 401. Signature algorithm is restricted to HS256; lifetime validation has zero clock skew.

## Client behavior

`LoginViewModel`, `HomeViewModel`, `SessionService`, and the bearer handler live in Core so they can be tested without starting a MAUI device. The MAUI `SecureTokenStore` wraps `SecureStorage.Default` and handles storage failures. Passwords are never saved. The shared authenticated `HttpClient` adds the current bearer token to API requests and refuses requests to other API origins. It does not follow redirects.

At startup the app shows a progress indicator, discards malformed/expired saved tokens, and validates a remaining token with `/me` before showing Home. Network failure preserves the saved token but keeps protected pages closed. Local expiry is a convenience check; only the API validates token trust. A timer, app resume check, and 401 handler expire the session while the app is open. Sign-out removes the saved token, clears session memory, and replaces the root page with Login, removing the authenticated back stack. Logout is local; an already issued JWT remains valid on the server until expiry (no refresh/revocation endpoint is introduced).

## Verification

```powershell
dotnet test TravelPlanner.Core.Tests
dotnet test TravelPlanner.Api.Tests
dotnet build TravelPlanner.Maui -f net9.0-windows10.0.19041.0 -p:MauiTargetFrameworks=net9.0-windows10.0.19041.0
```

Integration tests use the development PostgreSQL credentials from user secrets or `TRAVELPLANNER_TEST_DATABASE`, create a uniquely named `travelplanner_auth_tests_<guid>` database, apply the real migrations, seed test-only Identity users, and drop only that isolated test database on completion. They never seed accounts into `travelplanner`. The PostgreSQL role needs permission to create/drop its test database.

Unit tests cover input validation, loading/retry behavior, error states, session restoration, expiry boundaries/timers, token persistence, storage failure, sign-out, bearer headers, rejected tokens, and late responses from previous sessions. Integration tests run the real Identity password checks, JWT middleware, `[Authorize]`, and PostgreSQL schema, including invalid credentials, lockout, invalid configuration, and untrusted tokens.
