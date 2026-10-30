using System.Net;
using System.Net.Http.Json;
using TravelPlanner.Core.Dtos;
using TravelPlanner.Core.Services;

namespace TravelPlanner.Core.Tests;

public sealed class HttpAuthTests
{
    [Fact]
    public async Task HandlerAddsBearerHeaderToApiRequests()
    {
        var session = new StubSession { Token = "saved-token" };
        using var handler = new BearerTokenHandler(session, new Uri("https://api.example/"))
        {
            InnerHandler = new StubHttpHandler(request =>
            {
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Assert.Equal("saved-token", request.Headers.Authorization?.Parameter);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            })
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        using var response = await client.GetAsync("api/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(401, true)]
    [InlineData(403, false)]
    [InlineData(500, false)]
    public async Task Only401InvalidatesSession(int status, bool invalidates)
    {
        var session = new StubSession { Token = "saved-token" };
        using var handler = new BearerTokenHandler(session, new Uri("https://api.example/"))
        { InnerHandler = StubHttpHandler.Returning((HttpStatusCode)status) };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        using var response = await client.GetAsync("api/auth/me");
        Assert.Equal(invalidates ? "saved-token" : null, session.RejectedToken);
    }

    [Fact]
    public async Task HandlerNeverSendsTokenToAnotherHost()
    {
        var sent = false;
        using var handler = new BearerTokenHandler(new StubSession { Token = "secret" }, new Uri("https://api.example/"))
        { InnerHandler = new StubHttpHandler(_ => { sent = true; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); }) };
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync("https://another.example/"));
        Assert.False(sent);
    }

    [Fact]
    public async Task AuthApiPostsCredentialsAndReadsToken()
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(1);
        var expected = new LoginResponse(TestTokens.Create(expiry), expiry, TestTokens.User);
        using var client = new HttpClient(new StubHttpHandler(async request =>
        {
            Assert.Equal("/api/auth/login", request.RequestUri!.AbsolutePath);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Null(request.Headers.Authorization);
            var body = await request.Content!.ReadFromJsonAsync<LoginRequest>();
            Assert.Equal("correct-password", body!.Password);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(expected) };
        })) { BaseAddress = new Uri("https://api.example/") };
        var response = await new AuthApi(client).LoginAsync(new LoginRequest { Email = TestTokens.User.Email, Password = "correct-password" });
        Assert.Equal(expected, response);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(500)]
    public async Task AuthApiTranslatesLoginErrors(int status)
    {
        using var client = new HttpClient(StubHttpHandler.Returning((HttpStatusCode)status)) { BaseAddress = new Uri("https://api.example/") };
        await Assert.ThrowsAsync<AuthException>(() => new AuthApi(client).LoginAsync(new LoginRequest()));
    }

    [Fact]
    public async Task RestoreRequestSendsSavedTokenAndReports401()
    {
        using var client = new HttpClient(new StubHttpHandler(request =>
        {
            Assert.Equal("saved-token", request.Headers.Authorization!.Parameter);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        })) { BaseAddress = new Uri("https://api.example/") };
        await Assert.ThrowsAsync<UnauthorizedSessionException>(() => new AuthApi(client).GetMeAsync("saved-token"));
    }
}
