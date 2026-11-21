using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TravelPlanner.Api.Email;

// Sends one test email with the API's own email settings and prints exactly what happened.
if (args is not [var to] || !to.Contains('@'))
{
    Console.WriteLine("Usage: dotnet run --project tools/TravelPlanner.EmailSmoke -- you@example.com");
    Console.WriteLine("Sends one test email using the API's Email:Smtp user secrets (set them with tools/Set-GmailEmail.ps1).");
    return 0;
}

var configuration = new ConfigurationBuilder()
    .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true)
    .AddUserSecrets<TravelPlanner.Api.Program>(optional: true)
    .AddEnvironmentVariables().Build();
var smtp = configuration.GetSection(SmtpOptions.SectionName).Get<SmtpOptions>() ?? new SmtpOptions();

if (string.IsNullOrWhiteSpace(smtp.Host))
{
    Console.WriteLine("NOT CONFIGURED: Email:Smtp:Host is empty, so the API prints codes in its window instead of emailing them.");
    Console.WriteLine("Run: powershell -ExecutionPolicy Bypass -File .\\tools\\Set-GmailEmail.ps1   then restart the API.");
    return 2;
}
var from = string.IsNullOrWhiteSpace(smtp.FromAddress) ? smtp.Username : smtp.FromAddress;
Console.WriteLine($"Settings: {smtp.Host}:{smtp.Port}, TLS {(smtp.EnableSsl ? "on" : "off")}, sign in as {smtp.Username}, " +
                  $"send from {from}, password {(string.IsNullOrEmpty(smtp.Password) ? "MISSING" : $"set ({smtp.Password.Length} characters)")}, " +
                  $"time limit {smtp.TimeoutSeconds} s.");
if (smtp.Host.Contains("gmail", StringComparison.OrdinalIgnoreCase) && smtp.Password.Length != 16)
    Console.WriteLine("WARNING: Google app passwords have 16 characters. A normal Gmail password will be rejected.");

Console.WriteLine($"Sending a test email to {to}...");
using var loggers = LoggerFactory.Create(logging => logging.AddSimpleConsole().SetMinimumLevel(LogLevel.Information));
var sender = new SmtpEmailSender(Options.Create(smtp), loggers.CreateLogger<SmtpEmailSender>());
try
{
    await sender.SendAsync(new EmailMessage(to, "TravelPlanner test email",
        "This is a test from tools/TravelPlanner.EmailSmoke. If you can read it, email delivery works.",
        "<p>This is a test from <b>tools/TravelPlanner.EmailSmoke</b>. If you can read it, email delivery works.</p>"));
    Console.WriteLine($"SENT: {smtp.Host} accepted the email. It should arrive at {to} within a minute (check Spam too)," +
                      $" and appear in the Sent folder of {from}.");
    return 0;
}
catch (EmailDeliveryException error)
{
    Console.WriteLine($"FAILED: {error.Message}");
    return 1;
}
