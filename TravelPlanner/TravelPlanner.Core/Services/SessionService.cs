using TravelPlanner.Core.Dtos;

namespace TravelPlanner.Core.Services;

public sealed class SessionService(IAuthApi api, ITokenStore store, TimeProvider clock) : ISessionService, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private string? token;
    private ITimer? expiryTimer;
    public event EventHandler? SessionChanged;
    public UserProfile? User { get; private set; }
    public bool IsAuthenticated => User is not null && token is not null && TokenExpiry.Read(token) > clock.GetUtcNow();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var saved = await store.ReadAsync();
            if (string.IsNullOrEmpty(saved)) return;
            if (TokenExpiry.Read(saved) is not { } expiry || expiry <= clock.GetUtcNow())
            {
                await store.RemoveAsync();
                return;
            }
            try
            {
                var profile = await api.GetMeAsync(saved, cancellationToken);
                if (string.IsNullOrWhiteSpace(profile?.Id))
                    throw new AuthException("The server returned an invalid profile.");
                if (expiry <= clock.GetUtcNow()) { await store.RemoveAsync(); return; }
                SetSession(saved, profile, expiry);
            }
            catch (UnauthorizedSessionException) { await store.RemoveAsync(); }
        }
        finally { gate.Release(); }
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SignInAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var response = await api.LoginAsync(request, cancellationToken);
            if (TokenExpiry.Read(response.AccessToken) is not { } expiry || expiry <= clock.GetUtcNow()
                || string.IsNullOrWhiteSpace(response.User?.Id))
                throw new AuthException("The server returned an invalid sign-in response.");
            await store.WriteAsync(response.AccessToken);
            if (expiry <= clock.GetUtcNow())
            {
                await store.RemoveAsync();
                throw new AuthException("Your session expired while signing in. Please try again.");
            }
            SetSession(response.AccessToken, response.User, expiry);
        }
        finally { gate.Release(); }
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SignOutAsync()
    {
        await gate.WaitAsync();
        try { await store.RemoveAsync(); ClearSession(); }
        finally { gate.Release(); }
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<string?> GetAccessTokenAsync()
    {
        var current = token;
        if (current is null) return null;
        if (TokenExpiry.Read(current) > clock.GetUtcNow()) return current;
        await InvalidateAsync(current);
        return null;
    }

    public async Task InvalidateAsync(string rejectedToken)
    {
        var changed = false;
        await gate.WaitAsync();
        try
        {
            // A late 401 from an old request must not clear a more recent login.
            if (token != rejectedToken) return;
            try { await store.RemoveAsync(); }
            finally { ClearSession(); changed = true; }
        }
        finally
        {
            gate.Release();
            if (changed) SessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetSession(string accessToken, UserProfile profile, DateTimeOffset expiry)
    {
        expiryTimer?.Dispose();
        token = accessToken;
        User = profile;
        var due = expiry - clock.GetUtcNow();
        expiryTimer = clock.CreateTimer(state => { _ = ExpireAsync(accessToken); }, null,
            due > TimeSpan.Zero ? due : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }

    private async Task ExpireAsync(string expiredToken)
    {
        try { await InvalidateAsync(expiredToken); }
        catch (AuthException) { /* Memory is cleared even when encrypted storage is unavailable. */ }
    }

    private void ClearSession()
    {
        token = null;
        User = null;
        expiryTimer?.Dispose();
        expiryTimer = null;
    }

    public void Dispose() => expiryTimer?.Dispose();
}
