using System.ComponentModel.DataAnnotations;

namespace TravelPlanner.Core.Dtos;

/// <summary>Returned by a successful registration. No token is issued until the email is confirmed.</summary>
public sealed record RegisterResponse(string Email);

public sealed record ConfirmEmailRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; init; } = "";
    [Required, RegularExpression(@"^\d{6}$")]
    public string Code { get; init; } = "";
}

public sealed record ResendConfirmationRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; init; } = "";
}
