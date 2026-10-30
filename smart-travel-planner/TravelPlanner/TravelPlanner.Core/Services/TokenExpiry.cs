using System.Text.Json;

namespace TravelPlanner.Core.Services;

public static class TokenExpiry
{
    // Only a local expiry check. The API validates signature, issuer, audience and lifetime.
    public static DateTimeOffset? Read(string? token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length > 16384) return null;
            var parts = token.Split('.');
            if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace)) return null;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
            using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
            return json.RootElement.TryGetProperty("exp", out var expiry) && expiry.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
