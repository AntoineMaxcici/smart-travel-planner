using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TravelPlanner.Api.Data;
using TravelPlanner.Core.Dtos;

namespace TravelPlanner.Api.Tests;

[Collection(StartupFailureCollection.Name)]
public sealed class PasswordResetEndpointTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const string OldPassword = "Sign-up!Passw0rd";
    private const string NewPassword = "Brand-new!Passw0rd";
    private const string InvalidCode = "That code is invalid or has expired. Request a new code and try again.";
    private static string NewEmail() => $"reset-{Guid.NewGuid():N}@example.com";
    private static string OtherCode(string code) => ((int.Parse(code) + 1) % 1_000_000).ToString("D6");

    private async Task RegisterAsync(string email, bool confirm = true)
    {
        using var register = await factory.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        { Email = email, Password = OldPassword });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        if (!confirm) return;
        using var confirmed = await factory.Client.PostAsJsonAsync("/api/auth/confirm-email", new ConfirmEmailRequest
        { Email = email, Code = factory.Emails.LatestCodeFor(email) });
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
    }

    private async Task<string> ForgotAsync(string email)
    {
        using var response = await factory.Client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest { Email = email });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return factory.Emails.LatestCodeFor(email);
    }

    private Task<HttpResponseMessage> ResetAsync(string email, string code, string password = NewPassword) =>
        factory.Client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest
        { Email = email, Code = code, NewPassword = password });

    private Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        factory.Client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = password });

    private async Task<ApplicationUser> FindAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await database.Users.AsNoTracking().SingleAsync(user => user.NormalizedEmail == email.ToUpperInvariant());
    }

    [Fact]
    public async Task ForgotPasswordEmailsAResetCode()
    {
        var email = NewEmail();
        await RegisterAsync(email);
        var code = await ForgotAsync($"  {email.ToUpperInvariant()} ");
        var message = factory.Emails.SentTo(email).Last();
        Assert.Contains("password reset", message.Subject);
        Assert.Contains(code, message.TextBody);
        Assert.Contains(code, message.HtmlBody);
    }

    [Fact]
    public async Task UnknownEmailGetsTheSameAnswerAndNoEmail()
    {
        var email = NewEmail();
        using var response = await factory.Client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest { Email = email });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(factory.Emails.SentTo(email));
        using var reset = await ResetAsync(email, "123456");
        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
        Assert.Equal(InvalidCode, (await reset.Content.ReadFromJsonAsync<ApiError>())!.Message);
    }

    [Fact]
    public async Task ValidCodeChangesThePasswordSignsInAndCannotBeReused()
    {
        var email = NewEmail();
        await RegisterAsync(email);
        var code = await ForgotAsync(email);
        using var reset = await ResetAsync(email, code);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        var tokens = (await reset.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.False(string.IsNullOrEmpty(tokens.AccessToken));
        Assert.Equal(email, tokens.User.Email);

        using (var oldLogin = await LoginAsync(email, OldPassword)) Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        using (var newLogin = await LoginAsync(email, NewPassword)) Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
        using var again = await ResetAsync(email, code, "Another!Passw0rd");
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task ResetAlsoConfirmsAnUnconfirmedAccount()
    {
        var email = NewEmail();
        await RegisterAsync(email, confirm: false);
        using var reset = await ResetAsync(email, await ForgotAsync(email));
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.True((await FindAsync(email)).EmailConfirmed);
        using var login = await LoginAsync(email, NewPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task WrongCodeIsRejectedAndKeepsTheOldPassword()
    {
        var email = NewEmail();
        await RegisterAsync(email);
        var code = await ForgotAsync(email);
        using var reset = await ResetAsync(email, OtherCode(code));
        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
        Assert.Equal(InvalidCode, (await reset.Content.ReadFromJsonAsync<ApiError>())!.Message);
        Assert.DoesNotContain("accessToken", await reset.Content.ReadAsStringAsync());
        using var login = await LoginAsync(email, OldPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task WeakPasswordIsRejectedWithIdentityMessageAndCodeStillWorks()
    {
        var email = NewEmail();
        await RegisterAsync(email);
        var code = await ForgotAsync(email);
        using var weak = await ResetAsync(email, code, "alllowercase");
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Contains("Passwords must", (await weak.Content.ReadFromJsonAsync<ApiError>())!.Message);
        using var reset = await ResetAsync(email, code);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
    }

    [Fact]
    public async Task RepeatedWrongCodesLockTheAccount()
    {
        var email = NewEmail();
        await RegisterAsync(email);
        var code = await ForgotAsync(email);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var wrong = await ResetAsync(email, OtherCode(code));
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        }
        using var locked = await ResetAsync(email, code);
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.NotNull((await FindAsync(email)).LockoutEnd);
    }

    [Theory]
    [InlineData("not-an-email", "123456", NewPassword)]
    [InlineData("someone@example.com", "12345", NewPassword)]
    [InlineData("someone@example.com", "abcdef", NewPassword)]
    [InlineData("someone@example.com", "123456", "short")]
    public async Task InvalidResetRequestsAre400(string email, string code, string password)
    {
        using var response = await ResetAsync(email, code, password);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
