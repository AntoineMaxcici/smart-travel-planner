using System.Diagnostics;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.Extensions.Options;

namespace TravelPlanner.Api.Email;

/// <summary>Sends email through an SMTP server such as Gmail (smtp.gmail.com:587 with STARTTLS).</summary>
public sealed class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var from = string.IsNullOrWhiteSpace(settings.FromAddress) ? settings.Username : settings.FromAddress;
        using var mail = new MailMessage(new MailAddress(from, settings.FromName), new MailAddress(message.To))
        {
            Subject = message.Subject,
            Body = message.TextBody
        };
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.HtmlBody, null, MediaTypeNames.Text.Html));
        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(settings.Username, settings.Password),
            // Only applies to the synchronous Send; the async send is limited by the token below.
            Timeout = settings.TimeoutSeconds * 1000
        };

        // SendMailAsync has no time limit of its own: if a firewall silently drops the connection it would wait
        // forever, leaving the request hanging with nothing logged. Give up after TimeoutSeconds instead.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await client.SendMailAsync(mail, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new EmailDeliveryException(NoAnswer(settings));
        }
        catch (Exception ex) when (ex is SmtpException or SocketException or AuthenticationException or IOException)
        {
            throw new EmailDeliveryException(Explain(ex, settings), ex);
        }
        logger.LogInformation("Email sent to {To} through {Host}:{Port} in {Milliseconds} ms.",
            Mask(message.To), settings.Host, settings.Port, stopwatch.ElapsedMilliseconds);
    }

    // Turns the SMTP client's errors into a sentence that says what to check.
    public static string Explain(Exception ex, SmtpOptions settings)
    {
        var details = string.Join(" ", Chain(ex).Select(e => e.Message)).Trim();
        if (Chain(ex).Any(e => e is TimeoutException) || details.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            return NoAnswer(settings);
        if (details.Contains("5.7.8", StringComparison.Ordinal) || details.Contains("5.7.0", StringComparison.Ordinal)
            || details.Contains("Username and Password not accepted", StringComparison.OrdinalIgnoreCase)
            || details.Contains("Authentication Required", StringComparison.OrdinalIgnoreCase))
            return $"{settings.Host} rejected the sign-in for {settings.Username}. For Gmail, Email:Smtp:Password must be a " +
                   $"16-character Google app password (not the normal password) and 2-Step Verification must be on. Server said: {details}";
        if (Chain(ex).Any(e => e is AuthenticationException))
            return $"The secure (TLS) connection to {settings.Host} failed. Antivirus \"email scanning\" or \"SSL scanning\" " +
                   $"often causes this. Details: {details}";
        if (Chain(ex).Any(e => e is SocketException))
            return $"Could not connect to {settings.Host}:{settings.Port}. Check the internet connection and that no firewall " +
                   $"or antivirus blocks this port. Details: {details}";
        return $"{settings.Host} refused the email. Details: {details}";
    }

    private static string NoAnswer(SmtpOptions settings) =>
        $"No answer from {settings.Host}:{settings.Port} within {settings.TimeoutSeconds} seconds. " +
        "A firewall, antivirus or network may be blocking outgoing email on this port.";

    private static IEnumerable<Exception> Chain(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException) yield return current;
    }

    // "maya@example.com" -> "m***@example.com", so logs show which inbox without the full address.
    public static string Mask(string email)
    {
        var at = email.IndexOf('@');
        return at <= 1 ? "***" + email[Math.Max(at, 0)..] : email[0] + "***" + email[at..];
    }
}

/// <summary>An email could not be delivered to the SMTP server. The message says why, in plain words.</summary>
public sealed class EmailDeliveryException(string message, Exception? innerException = null) : Exception(message, innerException);
