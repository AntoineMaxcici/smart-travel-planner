using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TravelPlanner.Core.Dtos;

namespace TravelPlanner.Core.Services;

public sealed class AuthApi(HttpClient client) : IAuthApi
{
    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await client.PostAsJsonAsync("api/auth/login", request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized) throw new AuthException("Invalid email or password.");
            if (response.StatusCode == HttpStatusCode.BadRequest) throw new AuthException("Enter a valid email address and password.");
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new EmailNotConfirmedException(await ReadErrorAsync(response,
                    "Confirm your email address to sign in. We've sent a new code to your inbox.", cancellationToken));
            if (!response.IsSuccessStatusCode) throw new AuthException("Sign-in is unavailable. Please try again shortly.");
            return await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken)
                ?? throw new AuthException("The server returned an invalid sign-in response.");
        }
        catch (JsonException) { throw new AuthException("The server returned an invalid sign-in response."); }
    }

    public async Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await client.PostAsJsonAsync("api/auth/register", request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new AuthException(await ReadErrorAsync(response, "An account with this email already exists.", cancellationToken));
        if (response.StatusCode == HttpStatusCode.BadRequest)
            throw new AuthException(await ReadErrorAsync(response, "Check your details and try again.", cancellationToken));
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new AuthException("Too many sign-up attempts. Please wait a few minutes and try again.");
        if (!response.IsSuccessStatusCode) throw new AuthException("Registration is unavailable. Please try again shortly.");
        try
        {
            return await response.Content.ReadFromJsonAsync<RegisterResponse>(cancellationToken)
                ?? throw new AuthException("The server returned an invalid registration response.");
        }
        catch (JsonException) { throw new AuthException("The server returned an invalid registration response."); }
    }

    public async Task<LoginResponse> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await client.PostAsJsonAsync("api/auth/confirm-email", request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.BadRequest)
            throw new AuthException(await ReadErrorAsync(response, "That code is invalid or has expired. Request a new code and try again.", cancellationToken));
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new AuthException("Too many attempts. Please wait a few minutes and try again.");
        if (!response.IsSuccessStatusCode) throw new AuthException("Confirmation is unavailable. Please try again shortly.");
        try
        {
            return await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken)
                ?? throw new AuthException("The server returned an invalid confirmation response.");
        }
        catch (JsonException) { throw new AuthException("The server returned an invalid confirmation response."); }
    }

    public async Task ResendConfirmationAsync(ResendConfirmationRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await client.PostAsJsonAsync("api/auth/resend-confirmation", request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new AuthException("Too many attempts. Please wait a few minutes and try again.");
        if (!response.IsSuccessStatusCode) throw new AuthException("A new code couldn't be sent. Please try again shortly.");
    }

    // Reads the API's { "message": "..." } body; falls back when the body is missing or a different shape.
    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, string fallback, CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(cancellationToken);
            return string.IsNullOrWhiteSpace(error?.Message) ? fallback : error.Message;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException) { return fallback; }
    }

    public async Task<UserProfile> GetMeAsync(string token, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new UnauthorizedSessionException();
        if (!response.IsSuccessStatusCode) throw new AuthException("Your session could not be checked. Please try again.");
        try
        {
            return await response.Content.ReadFromJsonAsync<UserProfile>(cancellationToken)
                ?? throw new AuthException("The server returned an invalid profile.");
        }
        catch (JsonException) { throw new AuthException("The server returned an invalid profile."); }
    }
}
