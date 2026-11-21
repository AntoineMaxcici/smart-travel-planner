using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using TravelPlanner.Core.Dtos;
using TravelPlanner.Core.Services;
using TravelPlanner.Core.ViewModels;

namespace TravelPlanner.Core.Tests;

public sealed class ForgotPasswordViewModelTests
{
    private readonly StubAuthApi api = new();
    private readonly StubSession session = new();

    private ForgotPasswordViewModel Create(string email = "maya@example.com") => new(api, session) { Email = email };

    private async Task<ForgotPasswordViewModel> AtCodeStepAsync()
    {
        var vm = Create();
        await vm.SendCodeCommand.ExecuteAsync(null);
        vm.Code = "123456";
        vm.NewPassword = "N3w!Password";
        vm.ConfirmPassword = "N3w!Password";
        return vm;
    }

    [Fact]
    public void StartsOnTheEmailStep()
    {
        var vm = Create("");
        Assert.True(vm.IsEmailStep);
        Assert.False(vm.IsCodeStep);
        Assert.Equal("Forgot your password?", vm.Title);
        Assert.False(vm.SendCodeCommand.CanExecute(null));
        vm.Email = "maya@example.com";
        Assert.True(vm.SendCodeCommand.CanExecute(null));
    }

    [Fact]
    public async Task InvalidEmailShowsErrorWithoutCallingApi()
    {
        var vm = Create("not-an-email");
        await vm.SendCodeCommand.ExecuteAsync(null);
        Assert.Equal("Enter a valid email address.", vm.ErrorMessage);
        Assert.Equal(0, api.ForgotCalls);
        Assert.True(vm.IsEmailStep);
    }

    [Fact]
    public async Task SendingTheCodeTrimsTheEmailAndMovesToTheCodeStep()
    {
        var vm = Create("  maya@example.com ");
        await vm.SendCodeCommand.ExecuteAsync(null);
        Assert.Equal("maya@example.com", api.ForgotRequest!.Email);
        Assert.True(vm.IsCodeStep);
        Assert.Equal("Choose a new password", vm.Title);
        Assert.Contains("maya@example.com", vm.Instructions);
        Assert.True(vm.HasInfo);
        Assert.False(vm.HasError);
    }

    [Theory]
    [InlineData("rate")]
    [InlineData("network")]
    [InlineData("timeout")]
    public async Task SendErrorsAreVisibleAndStayOnTheEmailStep(string failure)
    {
        api.ForgotError = failure switch
        {
            "rate" => new AuthException("Too many attempts. Please wait a few minutes and try again."),
            "network" => new HttpRequestException(),
            _ => new TaskCanceledException()
        };
        var vm = Create();
        await vm.SendCodeCommand.ExecuteAsync(null);
        Assert.True(vm.HasError);
        Assert.True(vm.IsEmailStep);
        Assert.False(vm.IsBusy);
        Assert.True(vm.SendCodeCommand.CanExecute(null));
    }

    [Fact]
    public async Task ResetIsDisabledUntilCodeAndBothPasswordsAreEntered()
    {
        var vm = Create();
        await vm.SendCodeCommand.ExecuteAsync(null);
        Assert.False(vm.ResetCommand.CanExecute(null));
        vm.Code = "123456";
        vm.NewPassword = "N3w!Password";
        Assert.False(vm.ResetCommand.CanExecute(null));
        vm.ConfirmPassword = "N3w!Password";
        Assert.True(vm.ResetCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("12345", "N3w!Password", "N3w!Password", "Enter the 6-digit code from the email.")]
    [InlineData("12a456", "N3w!Password", "N3w!Password", "Enter the 6-digit code from the email.")]
    [InlineData("123456", "short", "short", "Password must be at least 8 characters.")]
    [InlineData("123456", "N3w!Password", "Other!Passw0rd", "Passwords do not match.")]
    public async Task InvalidResetInputIsRejectedLocally(string code, string password, string confirm, string expected)
    {
        var vm = await AtCodeStepAsync();
        vm.Code = code;
        vm.NewPassword = password;
        vm.ConfirmPassword = confirm;
        await vm.ResetCommand.ExecuteAsync(null);
        Assert.Equal(expected, vm.ErrorMessage);
        Assert.Equal(0, session.ResetCalls);
    }

    [Theory]
    [InlineData("123456")]
    [InlineData(" 123 456 ")]
    [InlineData("123-456")]
    public async Task SuccessfulResetSignsInAndClearsTheSecrets(string code)
    {
        var vm = await AtCodeStepAsync();
        vm.Code = code;
        await vm.ResetCommand.ExecuteAsync(null);
        Assert.Equal(new ResetPasswordRequest { Email = "maya@example.com", Code = "123456", NewPassword = "N3w!Password" },
            session.ResetRequest);
        Assert.True(session.IsAuthenticated);
        Assert.Equal("", vm.Code);
        Assert.Equal("", vm.NewPassword);
        Assert.Equal("", vm.ConfirmPassword);
        Assert.False(vm.HasError);
    }

    [Theory]
    [InlineData("code")]
    [InlineData("network")]
    [InlineData("timeout")]
    public async Task ResetErrorsAreVisibleKeepTheInputAndAllowRetry(string failure)
    {
        Exception error = failure switch
        {
            "code" => new AuthException("That code is invalid or has expired. Request a new code and try again."),
            "network" => new HttpRequestException(),
            _ => new TaskCanceledException()
        };
        var vm = await AtCodeStepAsync();
        session.OnReset = () => Task.FromException(error);
        await vm.ResetCommand.ExecuteAsync(null);
        Assert.True(vm.HasError);
        Assert.False(session.IsAuthenticated);
        Assert.Equal("123456", vm.Code);
        Assert.True(vm.ResetCommand.CanExecute(null));
        session.OnReset = () => Task.CompletedTask;
        await vm.ResetCommand.ExecuteAsync(null);
        Assert.True(session.IsAuthenticated);
    }

    [Fact]
    public async Task ResendSendsAnotherCodeToTheSameEmail()
    {
        var vm = await AtCodeStepAsync();
        await vm.ResendCommand.ExecuteAsync(null);
        Assert.Equal(2, api.ForgotCalls);
        Assert.Contains("new code", vm.InfoMessage);
        Assert.True(vm.IsCodeStep);
    }

    [Fact]
    public async Task ChangeEmailGoesBackToTheFirstStep()
    {
        var vm = await AtCodeStepAsync();
        vm.ChangeEmailCommand.Execute(null);
        Assert.True(vm.IsEmailStep);
        Assert.False(vm.HasInfo);
    }

    [Fact]
    public async Task BusyStateBlocksDuplicateSubmissions()
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        api.OnForgot = () => pending.Task;
        var vm = Create();
        var send = vm.SendCodeCommand.ExecuteAsync(null);
        Assert.True(vm.IsBusy);
        Assert.False(vm.SendCodeCommand.CanExecute(null));
        await vm.SendCodeCommand.ExecuteAsync(null);
        Assert.Equal(1, api.ForgotCalls);
        pending.SetResult();
        await send;
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public void BackToLoginRaisesEvent()
    {
        var vm = Create();
        var raised = 0;
        vm.LoginRequested += (_, _) => raised++;
        vm.GoToLoginCommand.Execute(null);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void LoginPageForgotPasswordPassesTheTypedEmail()
    {
        var login = new LoginViewModel(new StubSession()) { Email = " maya@example.com " };
        string? email = null;
        login.ForgotPasswordRequested += (_, value) => email = value;
        login.GoToForgotPasswordCommand.Execute(null);
        Assert.Equal("maya@example.com", email);
    }
}

public sealed class PasswordResetApiTests
{
    private static HttpClient Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) =>
        new(new StubHttpHandler(send)) { BaseAddress = new Uri("https://api.example/") };
    private static HttpClient Returning(int status, string body = "") =>
        Client(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }));
    private static readonly ResetPasswordRequest Reset = new() { Email = "maya@example.com", Code = "123456", NewPassword = "N3w!Password" };

    [Fact]
    public async Task ForgotPostsTheEmail()
    {
        using var client = Client(async request =>
        {
            Assert.Equal("/api/auth/forgot-password", request.RequestUri!.AbsolutePath);
            Assert.Equal("maya@example.com", (await request.Content!.ReadFromJsonAsync<ForgotPasswordRequest>())!.Email);
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        await new AuthApi(client).ForgotPasswordAsync(new ForgotPasswordRequest { Email = "maya@example.com" });
    }

    [Theory]
    [InlineData(429, "Too many attempts. Please wait a few minutes and try again.")]
    [InlineData(400, "Enter a valid email address.")]
    [InlineData(500, "A reset code couldn't be sent. Please try again shortly.")]
    public async Task ForgotErrorsBecomeReadableMessages(int status, string expected)
    {
        using var client = Returning(status);
        var error = await Assert.ThrowsAsync<AuthException>(() =>
            new AuthApi(client).ForgotPasswordAsync(new ForgotPasswordRequest { Email = "maya@example.com" }));
        Assert.Equal(expected, error.Message);
    }

    [Fact]
    public async Task ResetPostsCodeAndPasswordAndReadsToken()
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(1);
        var expected = new LoginResponse(TestTokens.Create(expiry), expiry, TestTokens.User);
        using var client = Client(async request =>
        {
            Assert.Equal("/api/auth/reset-password", request.RequestUri!.AbsolutePath);
            Assert.Equal(Reset, await request.Content!.ReadFromJsonAsync<ResetPasswordRequest>());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(expected) };
        });
        Assert.Equal(expected, await new AuthApi(client).ResetPasswordAsync(Reset));
    }

    [Theory]
    [InlineData(400, "{\"message\":\"Passwords must have at least one digit ('0'-'9').\"}", "Passwords must have at least one digit ('0'-'9').")]
    [InlineData(400, "", "That code is invalid or has expired. Request a new code and try again.")]
    [InlineData(429, "", "Too many attempts. Please wait a few minutes and try again.")]
    [InlineData(500, "", "Password reset is unavailable. Please try again shortly.")]
    public async Task ResetErrorsBecomeReadableMessages(int status, string body, string expected)
    {
        using var client = Returning(status, body);
        var error = await Assert.ThrowsAsync<AuthException>(() => new AuthApi(client).ResetPasswordAsync(Reset));
        Assert.Equal(expected, error.Message);
    }

    [Fact]
    public async Task SessionResetSignsInAndSavesTheToken()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero));
        var expiry = clock.GetUtcNow().AddHours(1);
        var store = new MemoryTokenStore();
        var stub = new StubAuthApi { Response = new LoginResponse(TestTokens.Create(expiry), expiry, TestTokens.User) };
        using var session = new SessionService(stub, store, clock);
        await session.ResetPasswordAsync(Reset);
        Assert.True(session.IsAuthenticated);
        Assert.Equal(stub.Response.AccessToken, store.Token);
        Assert.Equal(Reset, stub.ResetRequest);
    }
}
