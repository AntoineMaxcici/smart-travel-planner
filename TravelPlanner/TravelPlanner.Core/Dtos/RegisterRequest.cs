using System.ComponentModel.DataAnnotations;

namespace TravelPlanner.Core.Dtos;

public sealed record RegisterRequest
{
    public const int MinimumPasswordLength = 8;

    [Required, EmailAddress, StringLength(254)]
    public string Email { get; init; } = "";
    [Required, StringLength(1024, MinimumLength = MinimumPasswordLength)]
    public string Password { get; init; } = "";
    [StringLength(100)]
    public string? DisplayName { get; init; }
}
