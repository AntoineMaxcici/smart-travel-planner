namespace TravelPlanner.Api.Email;

/// <summary>
/// SMTP settings (section Email:Smtp). For Gmail: Host smtp.gmail.com, Port 587, Username = the Gmail address,
/// Password = a Google app password. Leave Host empty in Development to print emails to the API console instead.
/// </summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Email:Smtp";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    /// <summary>Defaults to Username. Gmail only sends from the account's own address (or a verified alias).</summary>
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "TravelPlanner";
}
