# Authentication

The API checks email/password through Identity's `SignInManager.CheckPasswordSignInAsync` with lockout enabled (five failed attempts, five minutes). It returns a signed HS256 JWT and a public user profile. Unknown users, bad passwords, locked accounts, and accounts that require two-factor authentication receive the same 401 message. Two-factor login is a separate future feature. New accounts are created with `POST /api/auth/register` (see below), which stores the user in the PostgreSQL `AspNetUsers` table through Identity and emails a 6-digit confirmation code. The user is signed in once they enter the code. Login refuses accounts whose email is not confirmed.

## Configuration and running

The database connection and a cryptographically generated JWT signing key are saved in the API's .NET user secrets on this computer. Never commit those secrets or copy the signing key into the MAUI app. On another development machine configure:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=travelplanner;Username=postgres;Password=YOUR_PASSWORD" --project TravelPlanner.Api
# Generate a private signing key; this command does not print it.
$key = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(64))
dotnet user-secrets set "Jwt:SigningKey" $key --project TravelPlanner.Api
dotnet run --project TravelPlanner.Api --launch-profile http
```

### Sending confirmation emails with Gmail

1. Turn on 2-Step Verification for the Gmail account (Google Account → Security).
2. Create an app password: Google Account → Security → 2-Step Verification → App passwords. Copy the 16-character password. Your normal Gmail password will not work.
3. Store the settings in user secrets:

```powershell
dotnet user-secrets set "Email:Smtp:Host" "smtp.gmail.com" --project TravelPlanner.Api
dotnet user-secrets set "Email:Smtp:Username" "your.address@gmail.com" --project TravelPlanner.Api
dotnet user-secrets set "Email:Smtp:Password" "your16charapppassword" --project TravelPlanner.Api
```

Port 587 with STARTTLS and the sender name "TravelPlanner" come from `appsettings.json`. Emails are sent from `Email:Smtp:Username` unless `Email:Smtp:FromAddress` is set; Gmail only allows the account's own address or a verified alias. Gmail limits personal accounts to about 500 emails a day.

Without `Email:Smtp:Host`, the API runs in Development without sending email: each email, including its code, is written to the API console as a warning. Outside Development a missing host, username or password stops the API at startup. Deployment supplies them as `Email__Smtp__Host`, `Email__Smtp__Username` and `Email__Smtp__Password`. If sending fails, registration still succeeds and the error is logged; the user can request a new code.

`appsettings.json` configures issuer `TravelPlanner.Api`, audience `TravelPlanner.Maui`, and expiry of 60 minutes. Startup rejects missing/short signing keys, empty issuer/audience, and expiry outside 1–1440 minutes. Deployment must supply `ConnectionStrings__DefaultConnection` and `Jwt__SigningKey` through the hosting environment and use HTTPS.

Run the MAUI Windows project from Visual Studio, with the API running. Debug builds use `http://localhost:5082/` (Android emulator: `http://10.0.2.2:5082/`). The HTTP profile supports local development without bypassing certificate checks. Android permits local cleartext traffic only in Debug. Release builds require HTTPS. `TRAVELPLANNER_API_URL` overrides the default address; use the deployed API's HTTPS address for physical devices and deployments.

iOS simulator builds include the Keychain entitlement required by MAUI SecureStorage. Device builds do not include that simulator entitlement. If the app bundle identifier changes, update the simulator Keychain access group to match. Apple targets require a Mac and its platform SDKs to build and verify.

## Endpoints

`POST /api/auth/login`:

```json
{ "email": "existing-user@example.com", "password": "their-password" }
```

Success returns `{ "accessToken": "...", "expiresAtUtc": "...", "user": { "id": "...", "email": "...", "displayName": "..." } }`. Invalid input is 400, invalid credentials are 401. Login never issues a cookie.

`POST /api/auth/register`:

```json
{ "email": "new-user@example.com", "password": "Str0ng!Password", "displayName": "Optional name" }
```

Success is 201 `{ "email": "..." }` with no token; a 6-digit code is emailed to the user. The email is trimmed and used as both the Identity email and user name; `displayName` is optional (max 100 characters, blank is stored as null). Passwords are hashed by Identity and must be at least 8 characters with an uppercase letter, a lowercase letter, a digit and a symbol. Responses: 400 for an invalid email, a missing/short password or a password that fails the policy (body `{ "message": "..." }` describing the rule for policy failures); 409 `{ "message": "An account with this email already exists." }` when the email is taken, compared case-insensitively. One account per email is guaranteed by PostgreSQL's unique `UserNameIndex`, so simultaneous sign-ups with the same email produce one 201 and 409s for the rest. No schema migration is required: `DisplayName`, `CreatedAtUtc` and `EmailConfirmed` already exist on `AspNetUsers`.

`POST /api/auth/confirm-email` with `{ "email": "...", "code": "123456" }` confirms the address and returns the same body as login, so the user is signed in. Codes come from Identity's email token provider: they are tied to the account and its security stamp, and are valid for roughly 6–9 minutes. Requesting another code within the same 3-minute window returns the same code. A wrong code, an unknown account and an already-confirmed account all return 400 with the same message. Wrong codes count towards Identity lockout: after 5 the account is locked for 5 minutes (429), even for the correct code. That lockout also blocks password login during that time. A malformed code (not exactly 6 digits) is 400.

`POST /api/auth/resend-confirmation` with `{ "email": "..." }` always returns 202 with the same message, and sends a new code only when the account exists and is unconfirmed, so it can't be used to discover registered emails.

Login with the correct password for an unconfirmed account returns 403 `{ "message": "Confirm your email address to sign in. ..." }` and emails a new code. The wrong password still gets the normal 401. Accounts created before email confirmation was added have `EmailConfirmed = false` and confirm the same way on their next login.

Rate limits are per client IP address with a fixed window, configured in `appsettings.json` under `RateLimiting`: `Register` (default 5 requests per 15 minutes) for sign-up, and `EmailConfirmation` (default 10 per 15 minutes, shared by confirm and resend). Every request counts, including rejected ones. Requests over a limit get 429 with a `Retry-After` header (seconds) and `{ "message": "Too many attempts. Please wait a few minutes and try again." }`. Login has no IP limit; it keeps its per-account lockout. Startup rejects a limit below 1 or a window outside 1–86400 seconds. If the API is deployed behind a reverse proxy, enable forwarded headers so the limits apply to the real client IP rather than the proxy's.

`GET /api/auth/me` requires `Authorization: Bearer <accessToken>`. It returns only the current user's public profile. Missing, expired, incorrectly signed, incorrectly issued/audienced tokens and tokens belonging to deleted users return 401. Signature algorithm is restricted to HS256; lifetime validation has zero clock skew.

## Client behavior

`LoginViewModel`, `HomeViewModel`, `SessionService`, and the bearer handler live in Core so they can be tested without starting a MAUI device. The MAUI `SecureTokenStore` wraps `SecureStorage.Default` and handles storage failures. Passwords are never saved. The Login page links to a Register page (`RegisterViewModel`), which checks the email, minimum length and password confirmation locally and shows the server's password-policy message when one is returned. After registering, or when login reports an unconfirmed email, the app opens the Confirm Email page (`ConfirmEmailViewModel`). It accepts the 6-digit code (spaces and dashes are ignored), can request a new code, and confirms through the same `SessionService` flow as login to save the token and open Home. The shared authenticated `HttpClient` adds the current bearer token to API requests and refuses requests to other API origins. It does not follow redirects.

At startup the app shows a progress indicator, discards malformed/expired saved tokens, and validates a remaining token with `/me` before showing Home. Network failure preserves the saved token but keeps protected pages closed. Local expiry is a convenience check; only the API validates token trust. A timer, app resume check, and 401 handler expire the session while the app is open. Sign-out removes the saved token, clears session memory, and replaces the root page with Login, removing the authenticated back stack. Logout is local; an already issued JWT remains valid on the server until expiry (no refresh/revocation endpoint is introduced).

## Verification

```powershell
dotnet test TravelPlanner.Core.Tests
dotnet test TravelPlanner.Api.Tests
dotnet build TravelPlanner.Maui -f net9.0-windows10.0.19041.0 -p:MauiTargetFrameworks=net9.0-windows10.0.19041.0
```

Integration tests use the development PostgreSQL credentials from user secrets or `TRAVELPLANNER_TEST_DATABASE`, create a uniquely named `travelplanner_auth_tests_<guid>` database, apply the real migrations, seed test-only Identity users, and drop only that isolated test database on completion. They never seed accounts into `travelplanner`. The PostgreSQL role needs permission to create/drop its test database.

Unit tests cover input validation, loading/retry behavior, error states, session restoration, expiry boundaries/timers, token persistence, storage failure, sign-out, bearer headers, rejected tokens, and late responses from previous sessions. Integration tests run the real Identity password checks, JWT middleware, `[Authorize]`, and PostgreSQL schema, including invalid credentials, lockout, invalid configuration, and untrusted tokens.
