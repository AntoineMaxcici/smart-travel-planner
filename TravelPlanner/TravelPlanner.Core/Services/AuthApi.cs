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
            if (!response.IsSuccessStatusCode) throw new AuthException("Sign-in is unavailable. Please try again shortly.");
            return await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken)
                ?? throw new AuthException("The server returned an invalid sign-in response.");
        }
        catch (JsonException) { throw new AuthException("The server returned an invalid sign-in response."); }
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
