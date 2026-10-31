using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TravelPlanner.Api.Data;
using TravelPlanner.Core.Dtos;

namespace TravelPlanner.Api.Tests;

public sealed class RegisterRateLimitTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const string StrongPassword = "Sign-up!Passw0rd";

    private WebApplicationFactory<Program> WithLimit(string permitLimit, string windowSeconds = "3600", string policy = "Register") =>
        WithSettings(new()
        {
            [$"RateLimiting:{policy}:PermitLimit"] = permitLimit,
            [$"RateLimiting:{policy}:WindowSeconds"] = windowSeconds
        });

    private WebApplicationFactory<Program> WithSettings(Dictionary<string, string?> settings, string? environment = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            if (environment is not null) builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
        });

    [Fact]
    public async Task RegistrationsOverTheLimitGet429AndCreateNoAccount()
    {
        using var limited = WithLimit("2");
        using var client = limited.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var allowed = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
            { Email = $"limit-{Guid.NewGuid():N}@example.com", Password = StrongPassword });
            Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        }

        var blockedEmail = $"limit-{Guid.NewGuid():N}@example.com";
        using var blocked = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest { Email = blockedEmail, Password = StrongPassword });
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal("Too many attempts. Please wait a few minutes and try again.", (await blocked.Content.ReadFromJsonAsync<ApiError>())!.Message);

        using var scope = limited.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Null(await users.FindByEmailAsync(blockedEmail));
    }

    [Fact]
    public async Task InvalidAttemptsAlsoCountTowardsTheLimit()
    {
        using var limited = WithLimit("1");
        using var client = limited.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var rejected = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest { Email = "traveler@example.com", Password = StrongPassword });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        using var blocked = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        { Email = $"limit-{Guid.NewGuid():N}@example.com", Password = StrongPassword });
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    [Fact]
    public async Task LoginIsNotAffectedByTheSignUpLimit()
    {
        using var limited = WithLimit("1");
        using var client = limited.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var first = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        { Email = $"limit-{Guid.NewGuid():N}@example.com", Password = StrongPassword });
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = AuthApiFactory.Email, Password = AuthApiFactory.Password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }
    }

    [Fact]
    public async Task ConfirmAndResendShareTheirOwnLimitSeparateFromSignUp()
    {
        using var limited = WithLimit("2", policy: "EmailConfirmation");
        using var client = limited.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var confirm = await client.PostAsJsonAsync("/api/auth/confirm-email", new ConfirmEmailRequest { Email = "nobody@example.com", Code = "123456" });
        Assert.Equal(HttpStatusCode.BadRequest, confirm.StatusCode);
        using var resend = await client.PostAsJsonAsync("/api/auth/resend-confirmation", new ResendConfirmationRequest { Email = "nobody@example.com" });
        Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);
        using var blocked = await client.PostAsJsonAsync("/api/auth/confirm-email", new ConfirmEmailRequest { Email = "nobody@example.com", Code = "123456" });
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        using var register = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        { Email = $"limit-{Guid.NewGuid():N}@example.com", Password = StrongPassword });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
    }

    [Theory]
    [InlineData("Register", "0", "60")]
    [InlineData("Register", "5", "0")]
    [InlineData("Register", "5", "86401")]
    [InlineData("EmailConfirmation", "0", "60")]
    [InlineData("EmailConfirmation", "5", "86401")]
    public void InvalidRateLimitConfigurationFailsAtStartup(string policy, string permitLimit, string windowSeconds)
    {
        using var invalid = WithLimit(permitLimit, windowSeconds, policy);
        Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() => invalid.CreateClient());
    }

    [Theory]
    [InlineData("Development", "smtp.gmail.com", "", "app-password")]
    [InlineData("Development", "smtp.gmail.com", "someone@gmail.com", "")]
    [InlineData("Production", "", "", "")]
    public void IncompleteEmailConfigurationFailsAtStartup(string environment, string host, string username, string password)
    {
        using var invalid = WithSettings(new()
        {
            ["Email:Smtp:Host"] = host,
            ["Email:Smtp:Username"] = username,
            ["Email:Smtp:Password"] = password
        }, environment);
        Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() => invalid.CreateClient());
    }
}
