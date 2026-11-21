using System.Diagnostics;
using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TravelPlanner.Api.Email;

namespace TravelPlanner.Api.Tests;

public sealed class SmtpEmailSenderTests
{
    private static readonly EmailMessage Message = new("maya@example.com", "Test", "Text body", "<p>Html body</p>");

    private static SmtpEmailSender Sender(int port, int timeoutSeconds = 1) => new(Options.Create(new SmtpOptions
    {
        Host = "127.0.0.1", Port = port, EnableSsl = false, Username = "sender@example.com", Password = "abcdabcdabcdabcd",
        TimeoutSeconds = timeoutSeconds
    }), NullLogger<SmtpEmailSender>.Instance);

    [Fact]
    public async Task ServerThatNeverAnswersFailsAfterTheTimeLimitInsteadOfHanging()
    {
        // Accepts the connection but never sends the SMTP greeting, like a firewall that swallows the traffic.
        using var silent = new TcpListener(IPAddress.Loopback, 0);
        silent.Start();
        var port = ((IPEndPoint)silent.LocalEndpoint).Port;
        var accept = silent.AcceptTcpClientAsync();
        var watch = Stopwatch.StartNew();
        var error = await Assert.ThrowsAsync<EmailDeliveryException>(() => Sender(port).SendAsync(Message));
        Assert.InRange(watch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(10));
        Assert.Contains("within 1 seconds", error.Message);
        Assert.Contains("127.0.0.1:" + port, error.Message);
        (await accept).Dispose();
    }

    [Fact]
    public async Task CallerCancellationIsStillReportedAsCancellation()
    {
        using var silent = new TcpListener(IPAddress.Loopback, 0);
        silent.Start();
        var port = ((IPEndPoint)silent.LocalEndpoint).Port;
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Sender(port, timeoutSeconds: 30).SendAsync(Message, cancel.Token));
    }

    [Fact]
    public async Task ClosedPortExplainsTheConnectionProblem()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop(); // Nothing listens there any more.
        var error = await Assert.ThrowsAsync<EmailDeliveryException>(() => Sender(port, timeoutSeconds: 5).SendAsync(Message));
        Assert.Contains("Could not connect to 127.0.0.1:" + port, error.Message);
    }

    [Fact]
    public void RejectedGmailSignInPointsToTheAppPassword()
    {
        var gmail = new SmtpOptions { Host = "smtp.gmail.com", Username = "sender@gmail.com" };
        var reason = SmtpEmailSender.Explain(new SmtpException(SmtpStatusCode.MustIssueStartTlsFirst,
            "5.7.0 Authentication Required. Learn more at https://support.google.com/mail/?p=WantAuthError"), gmail);
        Assert.Contains("16-character Google app password", reason);
        reason = SmtpEmailSender.Explain(new SmtpException("5.7.8 Username and Password not accepted."), gmail);
        Assert.Contains("rejected the sign-in for sender@gmail.com", reason);
    }

    [Fact]
    public void TlsFailurePointsToAntivirusScanning()
    {
        var reason = SmtpEmailSender.Explain(new SmtpException("Failure sending mail.", new AuthenticationException("Remote certificate is invalid.")),
            new SmtpOptions { Host = "smtp.gmail.com" });
        Assert.Contains("secure (TLS) connection", reason);
    }

    [Theory]
    [InlineData("maya@example.com", "m***@example.com")]
    [InlineData("a@example.com", "***@example.com")]
    public void LogsMaskTheEmailAddress(string email, string expected) => Assert.Equal(expected, SmtpEmailSender.Mask(email));
}
