using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TravelPlanner.Api.Data;
using TravelPlanner.Core.Dtos;

namespace TravelPlanner.Api.Tests;

public sealed class RegisterEndpointTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const string StrongPassword = "Sign-up!Passw0rd";
    private const string InvalidCode = "That code is invalid or has expired. Request a new code and try again.";
    private static string NewEmail() => $"new-{Guid.NewGuid():N}@example.com";

    private async Task<string> RegisterAsync(string email, string? displayName = null)
    {
        using var response = await factory.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        { Email = email, Password = StrongPassword, DisplayName = displayName });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return factory.Emails.LatestCodeFor(email);
    }

    private Task<HttpResponseMessage> ConfirmAsync(string email, string code) =>
        factory.Client.PostAsJsonAsync("/api/auth/confirm-email", new ConfirmEmailRequest { Email = email, Code = code });

    private Task<HttpResponseMessage> LoginAsync(string email) =>
        factory.Client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = StrongPassword });

    private static string OtherCode(string code) => ((int.Parse(code) + 1) % 1_000_000).ToString("D6");

    private async Task<ApplicationUser> FindAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await database.Users.AsNoTracking().SingleAsync(user => user.NormalizedEmail == email.ToUpperInvariant());
    }

    [Fact]
    public async Task RegistrationStoresUnconfirmedUserAndEmailsCodeWithoutIssuingToken()
    {
        var email = NewEmail();
        using var register = await factory.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        { Email = $"  {email}  ", Password = StrongPassword, DisplayName = "  New <Traveler>  " });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        Assert.False(register.Headers.Contains("Set-Cookie"));
        var json = await register.Content.ReadAsStringAsync();
        Assert.DoesNotContain("accessToken", json);
        Assert.DoesNotContain(StrongPassword, json);
        Assert.Equal(email, (await register.Content.ReadFromJsonAsync<RegisterResponse>())!.Email);

        var saved = await FindAsync(email);
        Assert.False(saved.EmailConfirmed);
        Assert.Equal("New <Traveler>", saved.DisplayName);
        Assert.False(string.IsNullOrEmpty(saved.PasswordHash));
        Assert.NotEqual(StrongPassword, saved.PasswordHash);
        Assert.InRange(saved.CreatedAtUtc, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(1));

        var message = Assert.Single(factory.Emails.SentTo(email));
        var code = factory.Emails.LatestCodeFor(email);
        Assert.Matches(@"^\d{6}$", code);
        Assert.Contains(code, message.HtmlBody);
        Assert.Contains("New &lt;Traveler&gt;", message.HtmlBody);
        Assert.DoesNotContain("<Traveler>", message.HtmlBody);
    }

    [Fact]
    public async Task CorrectCodeConfirmsEmailSignsInAndEnablesLogin()
    {
        var email = NewEmail();
        var code = await RegisterAsync(email, "New Traveler");

        using var confirm = await ConfirmAsync(email, code);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var response = (await confirm.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.Equal("New Traveler", response.User.DisplayName);
        Assert.True((await FindAsync(email)).EmailConfirmed);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", response.AccessToken);
        using var me = await factory.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(response.User.Id, (await me.Content.ReadFromJsonAsync<UserProfile>())!.Id);

        using var login = await LoginAsync(email);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task UnconfirmedLoginIsRefusedWith403AndSendsNewCode()
    {
        var email = NewEmail();
        await RegisterAsync(email);
        using var login = await LoginAsync(email);
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        Assert.DoesNotContain("accessToken", await login.Content.ReadAsStringAsync());
        Assert.Contains("Confirm your email", (await login.Content.ReadFromJsonAsync<ApiError>())!.Message);
        Assert.Equal(2, factory.Emails.SentTo(email).Count);

        using var confirm = await ConfirmAsync(email, factory.Emails.LatestCodeFor(email));
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
    }

    [Fact]
    public async Task UnconfirmedLoginWithWrongPasswordGets401AndNoEmail()
    {
        var email = NewEmail();
        await RegisterAsync(email);
        using var login = await factory.Client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = "Wrong!Passw0rd" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Single(factory.Emails.SentTo(email));
    }

    [Fact]
    public async Task WrongCodeIsRejectedAndDoesNotConfirm()
    {
        var email = NewEmail();
        var code = await RegisterAsync(email);
        using var confirm = await ConfirmAsync(email, OtherCode(code));
        Assert.Equal(HttpStatusCode.BadRequest, confirm.StatusCode);
        Assert.Equal(InvalidCode, (await confirm.Content.ReadFromJsonAsync<ApiError>())!.Message);
        Assert.DoesNotContain("accessToken", await confirm.Content.ReadAsStringAsync());
        Assert.False((await FindAsync(email)).EmailConfirmed);
    }

    [Fact]
    public async Task FiveWrongCodesLockConfirmationEvenForTheRightCode()
    {
        var email = NewEmail();
        var code = await RegisterAsync(email);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var wrong = await ConfirmAsync(email, OtherCode(code));
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        }
        using var locked = await ConfirmAsync(email, code);
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.False((await FindAsync(email)).EmailConfirmed);
    }

    [Fact]
    public async Task CodeCannotBeUsedForAnotherAccount()
    {
        var first = NewEmail();
        var second = NewEmail();
        var firstCode = await RegisterAsync(first);
        await RegisterAsync(second);
        using var confirm = await ConfirmAsync(second, firstCode);
        Assert.Equal(HttpStatusCode.BadRequest, confirm.StatusCode);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("confirmed")]
    public async Task UnknownOrAlreadyConfirmedAccountsGetTheSameAnswerAsAWrongCode(string kind)
    {
        var email = NewEmail();
        var code = "123456";
        if (kind == "confirmed")
        {
            code = await RegisterAsync(email);
            using var first = await ConfirmAsync(email, code);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }
        using var response = await ConfirmAsync(email, code);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(InvalidCode, (await response.Content.ReadFromJsonAsync<ApiError>())!.Message);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("abcdef")]
    [InlineData("")]
    public async Task MalformedCodeIs400(string code)
    {
        using var response = await ConfirmAsync(AuthApiFactory.Email, code);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResendSendsANewWorkingCodeOnlyToUnconfirmedAccounts()
    {
        var unconfirmed = NewEmail();
        await RegisterAsync(unconfirmed);
        var unknown = NewEmail();
        foreach (var email in new[] { unconfirmed, unknown, AuthApiFactory.Email })
        {
            using var resend = await factory.Client.PostAsJsonAsync("/api/auth/resend-confirmation", new ResendConfirmationRequest { Email = email });
            Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);
        }
        Assert.Equal(2, factory.Emails.SentTo(unconfirmed).Count);
        Assert.Empty(factory.Emails.SentTo(unknown));
        Assert.Empty(factory.Emails.SentTo(AuthApiFactory.Email));
        using var confirm = await ConfirmAsync(unconfirmed, factory.Emails.LatestCodeFor(unconfirmed));
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
    }

    [Fact]
    public async Task BlankDisplayNameIsStoredAsNull()
    {
        var email = NewEmail();
        await RegisterAsync(email, "   ");
        Assert.Null((await FindAsync(email)).DisplayName);
    }

    [Theory]
    [InlineData("traveler@example.com")]
    [InlineData("TRAVELER@EXAMPLE.COM")]
    public async Task ExistingEmailIsRejectedWith409(string email)
    {
        using var response = await factory.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        { Email = email, Password = StrongPassword });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("An account with this email already exists.", (await response.Content.ReadFromJsonAsync<ApiError>())!.Message);
        Assert.Empty(factory.Emails.SentTo(email));
    }

    [Fact]
    public async Task ConcurrentRegistrationsForSameEmailCreateOnlyOneAccount()
    {
        var email = NewEmail();
        var attempts = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            factory.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest { Email = email, Password = StrongPassword })));
        try
        {
            Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.Created);
            Assert.All(attempts.Where(response => response.StatusCode != HttpStatusCode.Created),
                response => Assert.Equal(HttpStatusCode.Conflict, response.StatusCode));
        }
        finally { foreach (var response in attempts) response.Dispose(); }
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Equal(1, await users.Users.CountAsync(user => user.NormalizedEmail == email.ToUpperInvariant()));
        Assert.Single(factory.Emails.SentTo(email));
    }

    [Theory]
    [InlineData("alllowercase1!")]
    [InlineData("NoDigitsHere!")]
    [InlineData("NoSymbols123")]
    [InlineData("Sh0rt!")]
    public async Task WeakPasswordIsRejectedAndNothingIsSent(string password)
    {
        var email = NewEmail();
        using var response = await factory.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Null(await users.FindByEmailAsync(email));
        Assert.Empty(factory.Emails.SentTo(email));
    }

    [Fact]
    public async Task PasswordPolicyFailureExplainsTheRule()
    {
        using var response = await factory.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        { Email = NewEmail(), Password = "NoDigitsHere!" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("digit", (await response.Content.ReadFromJsonAsync<ApiError>())!.Message);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    public async Task InvalidEmailIsRejected(string email)
    {
        using var response = await factory.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest { Email = email, Password = StrongPassword });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OverlongDisplayNameIsRejected()
    {
        using var response = await factory.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        { Email = NewEmail(), Password = StrongPassword, DisplayName = new string('a', 101) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
