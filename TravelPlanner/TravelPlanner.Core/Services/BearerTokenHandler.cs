using System.Net;
using System.Net.Http.Headers;

namespace TravelPlanner.Core.Services;

public sealed class BearerTokenHandler(ISessionService session, Uri apiBaseAddress) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is null || !apiBaseAddress.IsBaseOf(request.RequestUri))
            throw new InvalidOperationException("The authenticated client can only call the configured API.");
        var token = await session.GetAccessTokenAsync();
        request.Headers.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized && token is not null)
        {
            try { await session.InvalidateAsync(token); }
            catch { response.Dispose(); throw; }
        }
        return response;
    }
}
