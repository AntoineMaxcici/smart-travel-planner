namespace TravelPlanner.Core.Dtos;

public sealed record UserProfile(string Id, string Email, string? DisplayName);
public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAtUtc, UserProfile User);
public sealed record ApiError(string Message);
