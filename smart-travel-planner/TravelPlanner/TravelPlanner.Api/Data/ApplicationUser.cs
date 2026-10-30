using Microsoft.AspNetCore.Identity;

namespace TravelPlanner.Api.Data;

public class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}