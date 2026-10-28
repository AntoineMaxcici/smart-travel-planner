using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TravelPlanner.Api.Auth;
using TravelPlanner.Api.Data;
using TravelPlanner.Core.Dtos;

namespace TravelPlanner.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signInManager, JwtTokenService tokens) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null) return Unauthorized(new ApiError("Invalid email or password."));
        // Applies Identity lockout and account restrictions without issuing a cookie.
        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded || user.TwoFactorEnabled)
            return Unauthorized(new ApiError("Invalid email or password."));
        return Ok(tokens.Create(user));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserProfile>> Me()
    {
        var id = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var user = id is null ? null : await users.FindByIdAsync(id);
        return user is null ? Unauthorized() : Ok(new UserProfile(user.Id, user.Email ?? "", user.DisplayName));
    }
}
