using System.ComponentModel.DataAnnotations;

namespace TravelPlanner.Core.Dtos;

public sealed record LoginRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; init; } = "";
    [Required, StringLength(1024)]
    public string Password { get; init; } = "";
}
