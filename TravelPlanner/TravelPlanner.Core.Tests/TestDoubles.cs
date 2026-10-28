using System.Net;
using System.Text;
using System.Text.Json;
using TravelPlanner.Core.Dtos;
using TravelPlanner.Core.Services;

namespace TravelPlanner.Core.Tests;

internal static class TestTokens
{
    public static readonly UserProfile User = new("user-1", "traveler@example.com", "Traveler");
    public static string Create(DateTimeOffset expiry, string id = "one")
    {
        static string Encode(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Encode("{\"alg\":\"HS256\"}")}.{Encode(JsonSerializer.Serialize(new { exp = expiry.ToUnixTimeSeconds(), jti = id }))}.signature";
    }
}

internal sealed class MemoryTokenStore : ITokenStore
{
    public string? Token { get; set; }
    public Exception? ReadError { get; set; }
    public Exception? WriteError { get; set; }
    public Exception? RemoveError { get; set; }
    public Action? OnWrite { get; set; }
    public Task<string?> ReadAsync() => ReadError is null ? Task.FromResult(Token) : Task.FromException<string?>(ReadError);
    public Task WriteAsync(string token)
    {
        if (WriteError is not null) return Task.FromException(WriteError);
        Token = token; OnWrite?.Invoke(); return Task.CompletedTask;
    }
    public Task RemoveAsync()
    {
        if (RemoveError is not null) return Task.FromException(RemoveError);
        Token = null; return Task.CompletedTask;
    }
}

internal sealed class StubAuthApi : IAuthApi
{
    public LoginResponse Response { get; set; } = null!;
    public Exception? LoginError { get; set; }
    public Exception? ProfileError { get; set; }
    public int ProfileCalls { get; private set; }
    public Action? OnGetProfile { get; set; }
    public Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
        LoginError is null ? Task.FromResult(Response) : Task.FromException<LoginResponse>(LoginError);
    public Task<UserProfile> GetMeAsync(string token, CancellationToken cancellationToken = default)
    {
        ProfileCalls++;
        OnGetProfile?.Invoke();
        return ProfileError is null ? Task.FromResult(TestTokens.User) : Task.FromException<UserProfile>(ProfileError);
    }
}

internal sealed class StubSession : ISessionService
{
    public event EventHandler? SessionChanged;
    public UserProfile? User { get; set; }
    public bool IsAuthenticated => User is not null;
    public LoginRequest? Request { get; private set; }
    public Func<Task> OnSignIn { get; set; } = () => Task.CompletedTask;
    public Func<Task> OnSignOut { get; set; } = () => Task.CompletedTask;
    public int SignInCalls { get; private set; }
    public int SignOutCalls { get; private set; }
    public string? Token { get; set; }
    public string? RejectedToken { get; private set; }
    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public async Task SignInAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        SignInCalls++; Request = request; await OnSignIn(); User = TestTokens.User; SessionChanged?.Invoke(this, EventArgs.Empty);
    }
    public async Task SignOutAsync()
    {
        SignOutCalls++; await OnSignOut(); User = null; Token = null; SessionChanged?.Invoke(this, EventArgs.Empty);
    }
    public Task<string?> GetAccessTokenAsync() => Task.FromResult(Token);
    public Task InvalidateAsync(string rejectedToken) { RejectedToken = rejectedToken; Token = null; return Task.CompletedTask; }
}

internal sealed class StubHttpHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    public static StubHttpHandler Returning(HttpStatusCode status) => new(_ => Task.FromResult(new HttpResponseMessage(status)));
}
