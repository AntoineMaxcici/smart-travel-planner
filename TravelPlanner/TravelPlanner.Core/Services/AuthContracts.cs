using TravelPlanner.Core.Dtos;

namespace TravelPlanner.Core.Services;

public interface ITokenStore
{
    Task<string?> ReadAsync();
    Task WriteAsync(string token);
    Task RemoveAsync();
}

public interface IAuthApi
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<UserProfile> GetMeAsync(string token, CancellationToken cancellationToken = default);
}

public interface ISessionService
{
    event EventHandler? SessionChanged;
    UserProfile? User { get; }
    bool IsAuthenticated { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task SignInAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task SignOutAsync();
    Task<string?> GetAccessTokenAsync();
    Task InvalidateAsync(string rejectedToken);
}

public sealed class AuthException(string message) : Exception(message);
public sealed class UnauthorizedSessionException : Exception;
