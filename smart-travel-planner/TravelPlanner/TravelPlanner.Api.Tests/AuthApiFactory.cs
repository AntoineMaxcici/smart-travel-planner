using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TravelPlanner.Api.Data;
using TravelPlanner.Api.Email;

namespace TravelPlanner.Api.Tests;

public sealed class AuthApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SigningKey = "IntegrationTestsOnly-0123456789-abcdefghijklmnopqrstuvwxyz-0123456789";
    public const string Password = "Test-only!Passw0rd";
    public const string Email = "traveler@example.com";
    public HttpClient Client { get; private set; } = null!;
    public string UserId { get; private set; } = "";
    public CapturingEmailSender Emails { get; } = new();
    private readonly string connectionString;
    private readonly string databaseName = "travelplanner_auth_tests_" + Guid.NewGuid().ToString("N");

    public AuthApiFactory()
    {
        var configuration = new ConfigurationBuilder().AddUserSecrets<Program>(optional: true).Build();
        var baseConnection = Environment.GetEnvironmentVariable("TRAVELPLANNER_TEST_DATABASE")
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Set TRAVELPLANNER_TEST_DATABASE or the API's DefaultConnection user secret to a PostgreSQL connection with database creation permission.");
        connectionString = new NpgsqlConnectionStringBuilder(baseConnection) { Database = databaseName }.ConnectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connectionString,
            ["Jwt:SigningKey"] = SigningKey,
            ["Jwt:Issuer"] = "TravelPlanner.Api.Tests",
            ["Jwt:Audience"] = "TravelPlanner.Client.Tests",
            ["Jwt:ExpiryMinutes"] = "60",
            // All test requests share one client address; RegisterRateLimitTests use a low limit explicitly.
            ["RateLimiting:Register:PermitLimit"] = "1000",
            ["RateLimiting:EmailConfirmation:PermitLimit"] = "1000",
            ["Logging:LogLevel:Default"] = "Warning"
        }));
        builder.ConfigureTestServices(services => services.AddSingleton<IEmailSender>(Emails));
    }

    public async Task InitializeAsync()
    {
        Client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var scope = Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await database.Database.MigrateAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var email in new[] { Email, "locked@example.com", "twofactor@example.com", "bruteforce@example.com" })
        {
            var user = new ApplicationUser { UserName = email, Email = email, DisplayName = "Test Traveler", EmailConfirmed = true };
            var result = await users.CreateAsync(user, Password);
            Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(error => error.Description)));
            if (email == Email) UserId = user.Id;
            if (email == "locked@example.com") await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(5));
            if (email == "twofactor@example.com") await users.SetTwoFactorEnabledAsync(user, true);
        }
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        try
        {
            using var scope = Services.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Never delete the development database, even if test configuration changes accidentally.
            Assert.StartsWith("travelplanner_auth_tests_", databaseName);
            Assert.Equal(databaseName, database.Database.GetDbConnection().Database);
            await database.Database.EnsureDeletedAsync();
        }
        finally { await base.DisposeAsync(); }
    }
}
