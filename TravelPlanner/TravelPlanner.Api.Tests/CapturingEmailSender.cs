using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using TravelPlanner.Api.Email;

namespace TravelPlanner.Api.Tests;

/// <summary>Replaces SMTP in integration tests and keeps every email so tests can read the confirmation code.</summary>
public sealed partial class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> sent = new();
    public IReadOnlyList<EmailMessage> SentTo(string email) =>
        sent.Where(message => string.Equals(message.To, email, StringComparison.OrdinalIgnoreCase)).ToList();

    public string LatestCodeFor(string email)
    {
        var message = SentTo(email).LastOrDefault() ?? throw new InvalidOperationException($"No email was sent to {email}.");
        return SixDigits().Match(message.TextBody).Value;
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        sent.Enqueue(message);
        return Task.CompletedTask;
    }

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex SixDigits();
}
