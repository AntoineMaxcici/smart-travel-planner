namespace TravelPlanner.Api.Auth;

/// <summary>A fixed-window limit on how many requests one IP address can make. Bound as named options per policy.</summary>
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
    public static readonly string[] All = [Register, EmailConfirmation];
}
