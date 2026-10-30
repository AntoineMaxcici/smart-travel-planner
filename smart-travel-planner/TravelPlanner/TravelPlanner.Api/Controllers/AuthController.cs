using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TravelPlanner.Api.Auth;
using TravelPlanner.Api.Data;
using TravelPlanner.Api.Email;
using TravelPlanner.Core.Dtos;

namespace TravelPlanner.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signInManager, JwtTokenService tokens,
    EmailConfirmationService confirmation) : ControllerBase
{
    private const string DuplicateEmailMessage = "An account with this email already exists.";
    private const string InvalidCodeMessage = "That code is invalid or has expired. Request a new code and try again.";
    private const string TooManyAttemptsMessage = "Too many attempts. Please wait a few minutes and try again.";

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null) return Unauthorized(new ApiError("Invalid email or password."));
        // Applies Identity lockout and account restrictions without issuing a cookie.
        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded || user.TwoFactorEnabled)
            return Unauthorized(new ApiError("Invalid email or password."));
        if (!user.EmailConfirmed)
        {
            // Only reachable with the correct password, so this reveals nothing to someone guessing emails.
            await confirmation.SendCodeAsync(user, cancellationToken);
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiError("Confirm your email address to sign in. We've sent a new code to your inbox."));
        }
        return Ok(tokens.Create(user));
    }

    [AllowAnonymous]
    [HttpPost("register")]
    [EnableRateLimiting(RateLimitPolicies.Register)]
    public async Task<ActionResult<RegisterResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();
        // The email doubles as the Identity user name, so the unique NormalizedUserName index
        // in PostgreSQL guarantees one account per email even under concurrent requests.
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = displayName };
        IdentityResult result;
        try { result = await users.CreateAsync(user, request.Password); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Conflict(new ApiError(DuplicateEmailMessage));
        }
        if (!result.Succeeded)
        {
            if (result.Errors.Any(error => error.Code is "DuplicateEmail" or "DuplicateUserName"))
                return Conflict(new ApiError(DuplicateEmailMessage));
            if (result.Errors.Any(error => error.Code is "InvalidEmail" or "InvalidUserName"))
                return BadRequest(new ApiError("Enter a valid email address."));
            // Remaining errors are password policy failures; their descriptions are safe to show.
            return BadRequest(new ApiError(string.Join(" ", result.Errors.Select(error => error.Description))));
        }
        // If sending fails the account still exists; the user can request a new code from the app.
        await confirmation.SendCodeAsync(user, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new RegisterResponse(email));
    }

    [AllowAnonymous]
    [HttpPost("confirm-email")]
    [EnableRateLimiting(RateLimitPolicies.EmailConfirmation)]
    public async Task<ActionResult<LoginResponse>> ConfirmEmail(ConfirmEmailRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        // Unknown and already-confirmed accounts get the same answer as a wrong code.
        if (user is null || user.EmailConfirmed) return BadRequest(new ApiError(InvalidCodeMessage));
        // Wrong codes count towards Identity lockout (5 attempts, 5 minutes) to stop code guessing.
        if (await users.IsLockedOutAsync(user))
            return StatusCode(StatusCodes.Status429TooManyRequests, new ApiError(TooManyAttemptsMessage));
        var result = await users.ConfirmEmailAsync(user, request.Code);
        if (!result.Succeeded)
        {
            await users.AccessFailedAsync(user);
            return BadRequest(new ApiError(InvalidCodeMessage));
        }
        await users.ResetAccessFailedCountAsync(user);
        // The code proves the user owns the inbox, so they are signed in straight away.
        return Ok(tokens.Create(user));
    }

    [AllowAnonymous]
    [HttpPost("resend-confirmation")]
    [EnableRateLimiting(RateLimitPolicies.EmailConfirmation)]
    public async Task<IActionResult> ResendConfirmation(ResendConfirmationRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is { EmailConfirmed: false }) await confirmation.SendCodeAsync(user, cancellationToken);
        // Same response whether or not the account exists, so this can't be used to discover registered emails.
        return Accepted(new ApiError("If that account still needs confirming, a new code is on its way."));
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
