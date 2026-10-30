using TravelPlanner.Core.Services;

namespace TravelPlanner.Maui.Services;

public sealed class SecureTokenStore : ITokenStore
{
    private const string TokenKey = "travelplanner.access_token";

    public async Task<string?> ReadAsync()
    {
        try { return await SecureStorage.Default.GetAsync(TokenKey); }
        catch (Exception)
        {
            // Restored Android backups can contain unreadable encryption keys.
            await RemoveAsync();
            throw new AuthException("Your saved session could not be read. Please sign in again.");
        }
    }

    public async Task WriteAsync(string token)
    {
        try { await SecureStorage.Default.SetAsync(TokenKey, token); }
        catch (Exception) { throw new AuthException("Your session could not be saved securely. Please try again."); }
    }

    public Task RemoveAsync()
    {
        try { SecureStorage.Default.Remove(TokenKey); return Task.CompletedTask; }
        catch (Exception) { throw new AuthException("Your saved session could not be cleared. Please try again."); }
    }
}
