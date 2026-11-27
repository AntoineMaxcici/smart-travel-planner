using System.ComponentModel.DataAnnotations;

namespace TravelPlanner.Core.Dtos;

/// <summary>Body of POST /api/auth/forgot-password: emails a 6-digit reset code if the account exists.</summary>
public sealed record ForgotPasswordRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; init; } = "";
}

/// <summary>Body of POST /api/auth/reset-password: sets a new password with the emailed code and signs the user in.</summary>
public sealed record ResetPasswordRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; init; } = "";
    [Required, RegularExpression(@"^\d{6}$")]
    public string Code { get; init; } = "";
    [Required, StringLength(1024, MinimumLength = RegisterRequest.MinimumPasswordLength)]
    public string NewPassword { get; init; } = "";
}
