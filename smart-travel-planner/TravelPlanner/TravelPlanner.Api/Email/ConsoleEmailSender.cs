namespace TravelPlanner.Api.Email;

/// <summary>Development only: writes emails to the API log instead of sending them, so no SMTP account is needed.</summary>
public sealed class ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogWarning("Email not sent (Email:Smtp:Host is not configured). To: {To}. Subject: {Subject}.{NewLine}{Body}",
            message.To, message.Subject, Environment.NewLine, message.TextBody);
        return Task.CompletedTask;
    }
}
