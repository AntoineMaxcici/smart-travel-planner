using System.Net;
using Microsoft.AspNetCore.Identity;
using TravelPlanner.Api.Data;

namespace TravelPlanner.Api.Email;

/// <summary>Generates Identity's 6-digit email confirmation code and emails it to the user.</summary>
public sealed class EmailConfirmationService(UserManager<ApplicationUser> users, IEmailSender sender,
    ILogger<EmailConfirmationService> logger)
{
    /// <summary>Sends a new code. Returns false (and logs) if the email could not be sent, so callers never fail on SMTP errors.</summary>
    public async Task<bool> SendCodeAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(user.Email)) return false;
        var code = await users.GenerateEmailConfirmationTokenAsync(user);
        var name = string.IsNullOrWhiteSpace(user.DisplayName) ? "there" : user.DisplayName;
        var text = $"""
            Hi {name},

            Your TravelPlanner confirmation code is: {code}

            Enter it in the app to finish creating your account. The code expires in about 5 minutes; you can ask the app for a new one.
            If you didn't sign up for TravelPlanner, you can ignore this email.
            """;
        var html = $"""
            <p>Hi {WebUtility.HtmlEncode(name)},</p>
            <p>Your TravelPlanner confirmation code is:</p>
            <p style="font-size:28px;font-weight:bold;letter-spacing:6px">{code}</p>
            <p>Enter it in the app to finish creating your account. The code expires in about 5 minutes; you can ask the app for a new one.</p>
            <p style="color:#666">If you didn't sign up for TravelPlanner, you can ignore this email.</p>
            """;
        try
        {
            await sender.SendAsync(new EmailMessage(user.Email, $"Your TravelPlanner code: {code}", text, html), cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not send the confirmation email for user {UserId}.", user.Id);
            return false;
        }
    }
}
