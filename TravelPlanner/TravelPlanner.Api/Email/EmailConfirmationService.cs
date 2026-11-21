using System.Net;
using Microsoft.AspNetCore.Identity;
using TravelPlanner.Api.Data;

namespace TravelPlanner.Api.Email;

/// <summary>Generates Identity's 6-digit codes (email confirmation and password reset) and emails them to the user.</summary>
public sealed class EmailConfirmationService(UserManager<ApplicationUser> users, IEmailSender sender,
    ILogger<EmailConfirmationService> logger)
{
    /// <summary>Sends a new confirmation code. Returns false (and logs) if the email could not be sent, so callers never fail on SMTP errors.</summary>
    public async Task<bool> SendCodeAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(user.Email)) return false;
        var code = await users.GenerateEmailConfirmationTokenAsync(user);
        return await SendAsync(user, code, $"Your TravelPlanner code: {code}", "confirmation",
            "Your TravelPlanner confirmation code is:",
            "Enter it in the app to finish creating your account.",
            "If you didn't sign up for TravelPlanner, you can ignore this email.", cancellationToken);
    }

    /// <summary>Sends a password reset code. Returns false (and logs) if the email could not be sent.</summary>
    public async Task<bool> SendPasswordResetCodeAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(user.Email)) return false;
        var code = await users.GeneratePasswordResetTokenAsync(user);
        return await SendAsync(user, code, $"Your TravelPlanner password reset code: {code}", "password reset",
            "Your TravelPlanner password reset code is:",
            "Enter it in the app with your new password.",
            "If you didn't ask to reset your password, you can ignore this email; your password stays the same.", cancellationToken);
    }

    private async Task<bool> SendAsync(ApplicationUser user, string code, string subject, string kind, string intro,
        string action, string ignore, CancellationToken cancellationToken)
    {
        var name = string.IsNullOrWhiteSpace(user.DisplayName) ? "there" : user.DisplayName;
        var text = $"""
            Hi {name},

            {intro} {code}

            {action} The code expires in about 5 minutes; you can ask the app for a new one.
            {ignore}
            """;
        var html = $"""
            <p>Hi {WebUtility.HtmlEncode(name)},</p>
            <p>{intro}</p>
            <p style="font-size:28px;font-weight:bold;letter-spacing:6px">{code}</p>
            <p>{action} The code expires in about 5 minutes; you can ask the app for a new one.</p>
            <p style="color:#666">{ignore}</p>
            """;
        try
        {
            await sender.SendAsync(new EmailMessage(user.Email!, subject, text, html), cancellationToken);
            return true;
        }
        catch (EmailDeliveryException ex)
        {
            // The reason is written for people, so log it without a stack trace.
            logger.LogError("Could not send the {Kind} email for user {UserId}: {Reason}", kind, user.Id, ex.Message);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not send the {Kind} email for user {UserId}.", kind, user.Id);
            return false;
        }
    }
}
