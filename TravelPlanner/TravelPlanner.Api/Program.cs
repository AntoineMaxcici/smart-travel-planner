using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Globalization;
using System.Text;
using System.Threading.RateLimiting;
using TravelPlanner.Api.Auth;
using TravelPlanner.Api.Data;
using TravelPlanner.Api.Email;
using TravelPlanner.Api.Itineraries;
using TravelPlanner.Core.Dtos;

namespace TravelPlanner.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddControllers();
        builder.Services.AddOpenApi();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddDbContext<AppDbContext>((services, options) => options.UseNpgsql(
            services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.")));
        builder.Services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.User.RequireUniqueEmail = true;
            // Keep in sync with RegisterRequest.MinimumPasswordLength. Identity's default
            // digit/upper/lower/symbol requirements stay enabled.
            options.Password.RequiredLength = TravelPlanner.Core.Dtos.RegisterRequest.MinimumPasswordLength;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            // 6-digit codes the user types into the app (valid for roughly 6-9 minutes), instead of links.
            options.Tokens.EmailConfirmationTokenProvider = TokenOptions.DefaultEmailProvider;
            options.Tokens.PasswordResetTokenProvider = TokenOptions.DefaultEmailProvider;
        }).AddEntityFrameworkStores<AppDbContext>().AddSignInManager()
            .AddTokenProvider<EmailTokenProvider<ApplicationUser>>(TokenOptions.DefaultEmailProvider);

        // Email: Gmail (or any SMTP server) when Email:Smtp:Host is set; otherwise, in Development only,
        // emails are written to the API console so the app can be run without an email account.
        builder.Services.AddOptions<SmtpOptions>()
            .BindConfiguration(SmtpOptions.SectionName)
            .Validate<IHostEnvironment>((smtp, environment) => !string.IsNullOrWhiteSpace(smtp.Host) || environment.IsDevelopment(),
                "Email:Smtp:Host must be configured outside Development.")
            .Validate(smtp => string.IsNullOrWhiteSpace(smtp.Host) || smtp.Port is >= 1 and <= 65535,
                "Email:Smtp:Port must be between 1 and 65535.")
            .Validate(smtp => string.IsNullOrWhiteSpace(smtp.Host) || !string.IsNullOrWhiteSpace(smtp.Username),
                "Email:Smtp:Username is required (for Gmail, the Gmail address).")
            .Validate(smtp => string.IsNullOrWhiteSpace(smtp.Host) || !string.IsNullOrWhiteSpace(smtp.Password),
                "Email:Smtp:Password is required (for Gmail, a Google app password).")
            .Validate(smtp => smtp.TimeoutSeconds is >= 1 and <= 60, "Email:Smtp:TimeoutSeconds must be between 1 and 60.")
            .ValidateOnStart();
        builder.Services.AddSingleton<IEmailSender>(services =>
            string.IsNullOrWhiteSpace(services.GetRequiredService<IOptions<SmtpOptions>>().Value.Host)
                ? ActivatorUtilities.CreateInstance<ConsoleEmailSender>(services)
                : ActivatorUtilities.CreateInstance<SmtpEmailSender>(services));
        builder.Services.AddScoped<EmailConfirmationService>();

        builder.Services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .Validate(options => !string.IsNullOrWhiteSpace(options.Issuer), "Jwt:Issuer is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Audience), "Jwt:Audience is required.")
            .Validate(options => Encoding.UTF8.GetByteCount(options.SigningKey) >= 32,
                "Jwt:SigningKey must contain at least 32 UTF-8 bytes. Store it in user secrets or environment configuration.")
            .Validate(options => options.ExpiryMinutes is >= 1 and <= 1440, "Jwt:ExpiryMinutes must be between 1 and 1440.")
            .ValidateOnStart();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((options, settings) =>
            {
                var jwt = settings.Value;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateIssuer = true, ValidIssuer = jwt.Issuer,
                    ValidateAudience = true, ValidAudience = jwt.Audience,
                    ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
                    ClockSkew = TimeSpan.Zero, NameClaimType = "sub"
                };
            });
        builder.Services.AddAuthorization();
        foreach (var policy in RateLimitPolicies.All)
        {
            builder.Services.AddOptions<RateLimitRule>(policy)
                .BindConfiguration($"RateLimiting:{policy}")
                .Validate(rule => rule.PermitLimit >= 1, $"RateLimiting:{policy}:PermitLimit must be at least 1.")
                .Validate(rule => rule.WindowSeconds is >= 1 and <= 86400, $"RateLimiting:{policy}:WindowSeconds must be between 1 and 86400.")
                .ValidateOnStart();
        }
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // Generation is per user; anonymous auth endpoints remain per IP.
            // Behind a reverse proxy, configure forwarded headers for the real client IP.
            foreach (var policy in RateLimitPolicies.All)
            {
                options.AddPolicy(policy, context =>
                {
                    var rule = context.RequestServices.GetRequiredService<IOptionsMonitor<RateLimitRule>>().Get(policy);
                    var partitionKey = policy == RateLimitPolicies.ItineraryGeneration
                        && context.User.FindFirstValue(JwtRegisteredClaimNames.Sub) is { Length: > 0 } userId
                            ? $"user:{userId}"
                            : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    return RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey,
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = rule.PermitLimit,
                            Window = TimeSpan.FromSeconds(rule.WindowSeconds),
                            QueueLimit = 0
                        });
                });
            }
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                var message = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName
                    == RateLimitPolicies.ItineraryGeneration
                        ? "You've reached the itinerary generation limit. Please try again later."
                        : "Too many attempts. Please wait a few minutes and try again.";
                await context.HttpContext.Response.WriteAsJsonAsync(new ApiError(message), cancellationToken);
            };
        });
        builder.Services.AddScoped<JwtTokenService>();
        builder.Services.AddGeminiItineraryGeneration();
        builder.Services.AddScoped<IPlacesEnricher, NoOpPlacesEnricher>();
        builder.Services.AddScoped<TripGenerationService>();
        var app = builder.Build();
        LogEmailMode(app);
        if (app.Environment.IsDevelopment()) app.MapOpenApi();
        else app.UseHttpsRedirection();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapControllers();
        app.Run();
    }

    // Says at startup where confirmation codes will go, so a missing email is easy to explain.
    private static void LogEmailMode(WebApplication app)
    {
        SmtpOptions smtp;
        try { smtp = app.Services.GetRequiredService<IOptions<SmtpOptions>>().Value; }
        catch (OptionsValidationException) { return; } // Startup validation reports the problem.
        if (string.IsNullOrWhiteSpace(smtp.Host))
            app.Logger.LogWarning("Email:Smtp:Host is not set: confirmation codes are printed in this window instead of being emailed.");
        else
            app.Logger.LogInformation("Confirmation codes are emailed through {Host}:{Port} from {From}.", smtp.Host, smtp.Port,
                string.IsNullOrWhiteSpace(smtp.FromAddress) ? smtp.Username : smtp.FromAddress);
    }
}
