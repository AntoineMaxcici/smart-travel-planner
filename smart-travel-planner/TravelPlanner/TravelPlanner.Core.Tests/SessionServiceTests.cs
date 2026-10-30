using Microsoft.Extensions.Time.Testing;
using TravelPlanner.Core.Dtos;
using TravelPlanner.Core.Services;

namespace TravelPlanner.Core.Tests;

public sealed class SessionServiceTests
{
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
    private readonly MemoryTokenStore store = new();
    private readonly StubAuthApi api = new();
    private LoginRequest Request => new() { Email = TestTokens.User.Email, Password = "password" };
    private SessionService Create()
    {
        var expiry = clock.GetUtcNow().AddHours(1);
        api.Response = new LoginResponse(TestTokens.Create(expiry), expiry, TestTokens.User);
        return new SessionService(api, store, clock);
    }

    [Fact]
    public async Task LoginSavesTokenBeforePublishingAuthenticatedSession()
    {
        using var session = Create();
        session.SessionChanged += (_, _) => Assert.Equal(api.Response.AccessToken, store.Token);
        await session.SignInAsync(Request);
        Assert.True(session.IsAuthenticated);
        Assert.Equal(TestTokens.User, session.User);
        Assert.Equal(store.Token, await session.GetAccessTokenAsync());
    }

    [Fact]
    public async Task FreshInstallStaysSignedOutWithoutNetworkCall()
    {
        using var session = Create();
        await session.InitializeAsync();
        Assert.False(session.IsAuthenticated);
        Assert.Equal(0, api.ProfileCalls);
    }

    [Fact]
    public async Task ValidSavedTokenIsVerifiedWithMeAndRestored()
    {
        using var session = Create();
        store.Token = api.Response.AccessToken;
        await session.InitializeAsync();
        Assert.True(session.IsAuthenticated);
        Assert.Equal(1, api.ProfileCalls);
        Assert.Equal(TestTokens.User, session.User);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("boundary")]
    [InlineData("malformed")]
    [InlineData("missing-exp")]
    [InlineData("invalid-exp")]
    public async Task ExpiredOrInvalidSavedTokenIsClearedWithoutCallingApi(string kind)
    {
        using var session = Create();
        store.Token = kind switch
        {
            "expired" => TestTokens.Create(clock.GetUtcNow().AddSeconds(-1)),
            "boundary" => TestTokens.Create(clock.GetUtcNow()),
            "missing-exp" => "header.e30.signature",
            "invalid-exp" => "header.eyJleHAiOiJ0b21vcnJvdyJ9.signature",
            _ => "broken"
        };
        await session.InitializeAsync();
        Assert.False(session.IsAuthenticated);
        Assert.Null(store.Token);
        Assert.Equal(0, api.ProfileCalls);
    }

    [Fact]
    public async Task ServerRejectedSavedTokenIsCleared()
    {
        using var session = Create();
        store.Token = api.Response.AccessToken;
        api.ProfileError = new UnauthorizedSessionException();
        await session.InitializeAsync();
        Assert.False(session.IsAuthenticated);
        Assert.Null(store.Token);
    }

    [Fact]
    public async Task OfflineRestorePreservesSavedTokenButDoesNotOpenProtectedPages()
    {
        using var session = Create();
        store.Token = api.Response.AccessToken;
        api.ProfileError = new HttpRequestException();
        await Assert.ThrowsAsync<HttpRequestException>(() => session.InitializeAsync());
        Assert.False(session.IsAuthenticated);
        Assert.Equal(api.Response.AccessToken, store.Token);
    }

    [Fact]
    public async Task SignOutRemovesTokenAndNotifiesNavigation()
    {
        using var session = Create();
        await session.SignInAsync(Request);
        var events = 0;
        session.SessionChanged += (_, _) => events++;
        await session.SignOutAsync();
        Assert.Null(store.Token);
        Assert.Null(session.User);
        Assert.Null(await session.GetAccessTokenAsync());
        Assert.Equal(1, events);
    }

    [Fact]
    public async Task ExpiryTimerClearsSessionWhileAppIsOpen()
    {
        using var session = Create();
        await session.SignInAsync(Request);
        var expired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.SessionChanged += (_, _) => { if (!session.IsAuthenticated) expired.TrySetResult(); };
        clock.Advance(TimeSpan.FromHours(1));
        await expired.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Null(store.Token);
        Assert.Null(await session.GetAccessTokenAsync());
    }

    [Fact]
    public async Task Late401FromPreviousTokenDoesNotClearNewLogin()
    {
        using var session = Create();
        await session.SignInAsync(Request);
        var old = api.Response.AccessToken;
        var expiry = clock.GetUtcNow().AddHours(2);
        api.Response = new LoginResponse(TestTokens.Create(expiry, "new"), expiry, TestTokens.User);
        await session.SignInAsync(Request);
        await session.InvalidateAsync(old);
        Assert.True(session.IsAuthenticated);
        Assert.Equal(api.Response.AccessToken, await session.GetAccessTokenAsync());
    }

    [Theory]
    [InlineData("api")]
    [InlineData("storage")]
    [InlineData("invalid-token")]
    public async Task FailedLoginNeverCreatesAuthenticatedSession(string failure)
    {
        using var session = Create();
        if (failure == "api") api.LoginError = new AuthException("Invalid credentials.");
        if (failure == "storage") store.WriteError = new AuthException("Storage unavailable.");
        if (failure == "invalid-token") api.Response = api.Response with { AccessToken = "invalid" };
        await Assert.ThrowsAsync<AuthException>(() => session.SignInAsync(Request));
        Assert.False(session.IsAuthenticated);
        Assert.Null(store.Token);
    }

    [Fact]
    public async Task ExpiryStillClearsMemoryWhenStorageFails()
    {
        using var session = Create();
        await session.SignInAsync(Request);
        store.RemoveError = new AuthException("Storage unavailable.");
        await Assert.ThrowsAsync<AuthException>(() => session.InvalidateAsync(api.Response.AccessToken));
        Assert.False(session.IsAuthenticated);
        Assert.Null(session.User);
    }

    [Fact]
    public async Task TokenExpiringDuringServerVerificationIsNotRestored()
    {
        using var session = Create();
        store.Token = api.Response.AccessToken;
        api.OnGetProfile = () => clock.Advance(TimeSpan.FromHours(1));
        await session.InitializeAsync();
        Assert.False(session.IsAuthenticated);
        Assert.Null(store.Token);
    }

    [Fact]
    public async Task TokenExpiringDuringStorageWriteIsDiscarded()
    {
        using var session = Create();
        store.OnWrite = () => clock.Advance(TimeSpan.FromHours(1));
        await Assert.ThrowsAsync<AuthException>(() => session.SignInAsync(Request));
        Assert.False(session.IsAuthenticated);
        Assert.Null(store.Token);
    }

    [Fact]
    public async Task UnreadableSecureStorageDoesNotOpenProtectedPages()
    {
        using var session = Create();
        store.ReadError = new AuthException("Your saved session could not be read.");
        await Assert.ThrowsAsync<AuthException>(() => session.InitializeAsync());
        Assert.False(session.IsAuthenticated);
        Assert.Equal(0, api.ProfileCalls);
    }
}
