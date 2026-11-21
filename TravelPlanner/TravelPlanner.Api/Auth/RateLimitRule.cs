namespace TravelPlanner.Api.Auth;

/// <summary>A fixed-window request limit, partitioned by IP or signed-in user according to the policy. Bound as named options.</summary>
public sealed class RateLimitRule
{
    public int PermitLimit { get; set; } = 5;
    public int WindowSeconds { get; set; } = 900;
}

public static class RateLimitPolicies
{
    /// <summary>POST /api/auth/register. Configured under RateLimiting:Register.</summary>
    public const string Register = "Register";
    /// <summary>POST /api/auth/confirm-email and /api/auth/resend-confirmation. Configured under RateLimiting:EmailConfirmation.</summary>
    public const string EmailConfirmation = "EmailConfirmation";
    /// <summary>POST /api/itineraries/generate. Counted per signed-in user. Configured under RateLimiting:ItineraryGeneration.</summary>
    public const string ItineraryGeneration = "ItineraryGeneration";
    /// <summary>POST /api/auth/forgot-password and /api/auth/reset-password. Configured under RateLimiting:PasswordReset.</summary>
    public const string PasswordReset = "PasswordReset";
    public static readonly string[] All = [Register, EmailConfirmation, ItineraryGeneration, PasswordReset];
}
