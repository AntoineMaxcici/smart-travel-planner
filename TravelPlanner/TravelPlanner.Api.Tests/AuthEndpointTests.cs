using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using TravelPlanner.Api.Data;
using TravelPlanner.Core.Dtos;

namespace TravelPlanner.Api.Tests;

public sealed class AuthEndpointTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    [Fact]
    public async Task ValidLoginIssuesJwtAndAllowsProtectedMe()
    {
        var before = DateTimeOffset.UtcNow;
        using var login = await factory.Client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        { Email = " TRAVELER@example.com ", Password = AuthApiFactory.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.False(login.Headers.Contains("Set-Cookie"));
        var response = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.Equal(factory.UserId, response.User.Id);
        Assert.InRange(response.ExpiresAtUtc, before.AddMinutes(59), before.AddMinutes(61));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(response.AccessToken);
        Assert.Equal(factory.UserId, jwt.Subject);
        Assert.Equal(SecurityAlgorithms.HmacSha256, jwt.Header.Alg);
        Assert.Equal("TravelPlanner.Api.Tests", jwt.Issuer);
        Assert.Contains("TravelPlanner.Client.Tests", jwt.Audiences);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", response.AccessToken);
        using var me = await factory.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var profile = await me.Content.ReadFromJsonAsync<UserProfile>();
        Assert.Equal(factory.UserId, profile!.Id);
        Assert.Equal(AuthApiFactory.Email, profile.Email);
        var json = await me.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("traveler@example.com", "wrong-password")]
    [InlineData("unknown@example.com", "wrong-password")]
    [InlineData("locked@example.com", AuthApiFactory.Password)]
    [InlineData("twofactor@example.com", AuthApiFactory.Password)]
    public async Task InvalidCredentialsAndRestrictedAccountsNeverGetToken(string email, string password)
    {
        using var response = await factory.Client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("Invalid email or password.", error!.Message);
        Assert.DoesNotContain("accessToken", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("not-an-email", "password")]
    [InlineData("", "password")]
    [InlineData("traveler@example.com", "")]
    public async Task InvalidRequestReturns400(string email, string password)
    {
        using var response = await factory.Client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task IdentityLocksAccountAfterRepeatedFailedPasswords()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var rejected = await factory.Client.PostAsJsonAsync("/api/auth/login", new LoginRequest
            { Email = "bruteforce@example.com", Password = "incorrect" });
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        }
        using var response = await factory.Client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        { Email = "bruteforce@example.com", Password = AuthApiFactory.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousMeIs401WithBearerChallenge()
    {
        using var response = await factory.Client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, value => value.Scheme == "Bearer");
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("expired")]
    [InlineData("wrong-key")]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-audience")]
    [InlineData("future")]
    [InlineData("missing-user")]
    [InlineData("missing-exp")]
    [InlineData("wrong-algorithm")]
    [InlineData("unsigned")]
    public async Task MeRejectsUntrustedExpiredAndUnusableTokens(string kind)
    {
        var now = DateTime.UtcNow;
        var token = kind == "malformed" ? "not-a-jwt" : CreateToken(
            kind == "expired" ? now.AddMinutes(-10) : now.AddMinutes(-1),
            kind == "expired" ? now.AddSeconds(-1) : now.AddMinutes(10), kind);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await factory.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Jwt:SigningKey", "")]
    [InlineData("Jwt:SigningKey", "short-key")]
    [InlineData("Jwt:ExpiryMinutes", "0")]
    [InlineData("Jwt:ExpiryMinutes", "1441")]
    [InlineData("Jwt:Issuer", "")]
    [InlineData("Jwt:Audience", "")]
    public void InvalidJwtConfigurationFailsAtStartup(string key, string value)
    {
        using var invalid = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })));
        Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() => invalid.CreateClient());
    }

    private string CreateToken(DateTime notBefore, DateTime expires, string kind)
    {
        var token = new JwtSecurityToken(
            kind == "wrong-issuer" ? "wrong" : "TravelPlanner.Api.Tests",
            kind == "wrong-audience" ? "wrong" : "TravelPlanner.Client.Tests",
            [new Claim("sub", kind == "missing-user" ? "deleted-user" : factory.UserId)],
            kind == "future" ? DateTime.UtcNow.AddMinutes(5) : notBefore, kind == "missing-exp" ? null : expires,
            kind == "unsigned" ? null : new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                kind == "wrong-key" ? new string('x', 64) : AuthApiFactory.SigningKey)),
                kind == "wrong-algorithm" ? SecurityAlgorithms.HmacSha384 : SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
