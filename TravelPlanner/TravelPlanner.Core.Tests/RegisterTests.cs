using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using TravelPlanner.Core.Dtos;
using TravelPlanner.Core.Services;
using TravelPlanner.Core.ViewModels;

namespace TravelPlanner.Core.Tests;

public sealed class RegisterViewModelTests
{
    private static RegisterViewModel Valid(StubAuthApi api) => new(api)
    {
        DisplayName = " Traveler ", Email = " new@example.com ", Password = "Str0ng!Pass", ConfirmPassword = "Str0ng!Pass"
    };

    [Fact]
    public void RegisterIsDisabledUntilRequiredFieldsAreFilled()
    {
        var vm = new RegisterViewModel(new StubAuthApi());
        Assert.False(vm.RegisterCommand.CanExecute(null));
        vm.Email = "new@example.com";
        vm.Password = "Str0ng!Pass";
        Assert.False(vm.RegisterCommand.CanExecute(null));
        vm.ConfirmPassword = "Str0ng!Pass";
        Assert.True(vm.RegisterCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("not-an-email", "Str0ng!Pass", "Str0ng!Pass")]
    [InlineData("new@example.com", "short", "short")]
    [InlineData("new@example.com", "Str0ng!Pass", "Different!1")]
    public async Task InvalidInputShowsErrorWithoutCallingApi(string email, string password, string confirm)
    {
        var api = new StubAuthApi();
        var vm = new RegisterViewModel(api) { Email = email, Password = password, ConfirmPassword = confirm };
        await vm.RegisterCommand.ExecuteAsync(null);
        Assert.True(vm.HasError);
        Assert.Equal(0, api.RegisterCalls);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task MismatchedPasswordsExplainTheProblem()
    {
        var vm = new RegisterViewModel(new StubAuthApi()) { Email = "new@example.com", Password = "Str0ng!Pass", ConfirmPassword = "Str0ng!Pazz" };
        await vm.RegisterCommand.ExecuteAsync(null);
        Assert.Equal("Passwords do not match.", vm.ErrorMessage);
    }

    [Fact]
    public async Task SuccessfulRegistrationTrimsFieldsClearsPasswordsAndAsksForCode()
    {
        var api = new StubAuthApi();
        var vm = Valid(api);
        string? confirmEmail = null;
        vm.ConfirmationRequired += (_, email) => confirmEmail = email;
        await vm.RegisterCommand.ExecuteAsync(null);
        Assert.Equal("new@example.com", api.RegisterRequest!.Email);
        Assert.Equal("Traveler", api.RegisterRequest.DisplayName);
        Assert.Equal("Str0ng!Pass", api.RegisterRequest.Password);
        Assert.Equal("", vm.Password);
        Assert.Equal("", vm.ConfirmPassword);
        Assert.Equal("new@example.com", confirmEmail);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task BlankDisplayNameIsSentAsNull()
    {
        var api = new StubAuthApi();
        var vm = Valid(api);
        vm.DisplayName = "   ";
        await vm.RegisterCommand.ExecuteAsync(null);
        Assert.Null(api.RegisterRequest!.DisplayName);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("network")]
    [InlineData("timeout")]
    public async Task ErrorsAreVisibleAndAllowRetry(string failure)
    {
        var api = new StubAuthApi
        {
            RegisterError = failure switch
            {
                "duplicate" => new AuthException("An account with this email already exists."),
                "network" => new HttpRequestException(),
                _ => new TaskCanceledException()
            }
        };
        var vm = Valid(api);
        var confirmations = 0;
        vm.ConfirmationRequired += (_, _) => confirmations++;
        await vm.RegisterCommand.ExecuteAsync(null);
        Assert.True(vm.HasError);
        Assert.False(vm.IsBusy);
        Assert.Equal(0, confirmations);
        Assert.Equal("Str0ng!Pass", vm.Password);
        Assert.True(vm.RegisterCommand.CanExecute(null));
        api.RegisterError = null;
        await vm.RegisterCommand.ExecuteAsync(null);
        Assert.False(vm.HasError);
        Assert.Equal(1, confirmations);
    }

    [Fact]
    public async Task LoadingStateBlocksDuplicateSubmissions()
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new StubAuthApi { OnRegister = () => pending.Task };
        var vm = Valid(api);
        var register = vm.RegisterCommand.ExecuteAsync(null);
        Assert.True(vm.IsBusy);
        Assert.False(vm.RegisterCommand.CanExecute(null));
        await vm.RegisterCommand.ExecuteAsync(null);
        Assert.Equal(1, api.RegisterCalls);
        pending.SetResult();
        await register;
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public void NavigationCommandsRaiseEvents()
    {
        var register = new RegisterViewModel(new StubAuthApi());
        var login = new LoginViewModel(new StubSession());
        var toLogin = 0; var toRegister = 0;
        register.LoginRequested += (_, _) => toLogin++;
        login.RegisterRequested += (_, _) => toRegister++;
        register.GoToLoginCommand.Execute(null);
        login.GoToRegisterCommand.Execute(null);
        Assert.Equal(1, toLogin);
        Assert.Equal(1, toRegister);
    }

    [Fact]
    public async Task LoginWithUnconfirmedEmailOpensConfirmationInsteadOfShowingError()
    {
        var session = new StubSession { OnSignIn = () => Task.FromException(new EmailNotConfirmedException("Confirm your email.")) };
        var vm = new LoginViewModel(session) { Email = " new@example.com ", Password = "Str0ng!Pass" };
        string? confirmEmail = null;
        vm.ConfirmEmailRequested += (_, email) => confirmEmail = email;
        await vm.LoginCommand.ExecuteAsync(null);
        Assert.Equal("new@example.com", confirmEmail);
        Assert.False(vm.HasError);
        Assert.Equal("", vm.Password);
        Assert.False(session.IsAuthenticated);
    }
}

public sealed class ConfirmEmailViewModelTests
{
    private static (ConfirmEmailViewModel vm, StubSession session, StubAuthApi api) Create(string code = "123456")
    {
        var session = new StubSession();
        var api = new StubAuthApi();
        return (new ConfirmEmailViewModel(session, api) { Email = "new@example.com", Code = code }, session, api);
    }

    [Fact]
    public void ConfirmIsDisabledUntilACodeIsEntered()
    {
        var (vm, _, _) = Create("");
        Assert.False(vm.ConfirmCommand.CanExecute(null));
        vm.Code = "1";
        Assert.True(vm.ConfirmCommand.CanExecute(null));
        Assert.Contains("new@example.com", vm.Instructions);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    [InlineData("١٢٣٤٥٦")]
    public async Task MalformedCodesAreRejectedLocally(string code)
    {
        var (vm, session, _) = Create(code);
        await vm.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal("Enter the 6-digit code from the email.", vm.ErrorMessage);
        Assert.Equal(0, session.ConfirmCalls);
    }

    [Theory]
    [InlineData("123456")]
    [InlineData(" 123 456 ")]
    [InlineData("123-456")]
    public async Task CorrectCodeSignsInAndAcceptsSpacesAndDashes(string code)
    {
        var (vm, session, _) = Create(code);
        await vm.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal(new ConfirmEmailRequest { Email = "new@example.com", Code = "123456" }, session.ConfirmRequest);
        Assert.True(session.IsAuthenticated);
        Assert.False(vm.HasError);
        Assert.Equal("", vm.Code);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("network")]
    [InlineData("timeout")]
    public async Task ConfirmErrorsAreVisibleAndAllowRetry(string failure)
    {
        Exception error = failure switch
        {
            "invalid" => new AuthException("That code is invalid or has expired."),
            "network" => new HttpRequestException(),
            _ => new TaskCanceledException()
        };
        var (vm, session, _) = Create();
        session.OnConfirm = () => Task.FromException(error);
        await vm.ConfirmCommand.ExecuteAsync(null);
        Assert.True(vm.HasError);
        Assert.False(vm.IsBusy);
        Assert.False(session.IsAuthenticated);
        Assert.Equal("123456", vm.Code);
        session.OnConfirm = () => Task.CompletedTask;
        await vm.ConfirmCommand.ExecuteAsync(null);
        Assert.False(vm.HasError);
        Assert.True(session.IsAuthenticated);
    }

    [Fact]
    public async Task ResendSendsToTheSameEmailAndConfirmsOnScreen()
    {
        var (vm, _, api) = Create();
        vm.ErrorMessage = "old error";
        await vm.ResendCommand.ExecuteAsync(null);
        Assert.Equal("new@example.com", api.ResendRequest!.Email);
        Assert.True(vm.HasInfo);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task ResendFailureShowsError()
    {
        var (vm, _, api) = Create();
        api.ResendError = new AuthException("Too many attempts. Please wait a few minutes and try again.");
        await vm.ResendCommand.ExecuteAsync(null);
        Assert.Equal("Too many attempts. Please wait a few minutes and try again.", vm.ErrorMessage);
        Assert.False(vm.HasInfo);
        Assert.True(vm.ResendCommand.CanExecute(null));
    }

    [Fact]
    public async Task BusyStateBlocksBothCommands()
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (vm, session, api) = Create();
        session.OnConfirm = () => pending.Task;
        var confirm = vm.ConfirmCommand.ExecuteAsync(null);
        Assert.False(vm.ConfirmCommand.CanExecute(null));
        Assert.False(vm.ResendCommand.CanExecute(null));
        await vm.ResendCommand.ExecuteAsync(null);
        Assert.Equal(0, api.ResendCalls);
        pending.SetResult();
        await confirm;
        Assert.True(vm.ResendCommand.CanExecute(null));
    }

    [Fact]
    public void BackToLoginRaisesEvent()
    {
        var (vm, _, _) = Create();
        var raised = 0;
        vm.LoginRequested += (_, _) => raised++;
        vm.GoToLoginCommand.Execute(null);
        Assert.Equal(1, raised);
    }
}

public sealed class RegisterApiTests
{
    private static readonly RegisterRequest Request = new() { Email = "new@example.com", Password = "Str0ng!Pass", DisplayName = "New" };
    private static HttpClient Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) =>
        new(new StubHttpHandler(send)) { BaseAddress = new Uri("https://api.example/") };
    private static HttpClient Returning(int status, string body = "") =>
        Client(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }));

    [Fact]
    public async Task PostsRegistrationAndReadsResponse()
    {
        using var client = Client(async request =>
        {
            Assert.Equal("/api/auth/register", request.RequestUri!.AbsolutePath);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Null(request.Headers.Authorization);
            Assert.Equal(Request, await request.Content!.ReadFromJsonAsync<RegisterRequest>());
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(new RegisterResponse("new@example.com")) };
        });
        Assert.Equal(new RegisterResponse("new@example.com"), await new AuthApi(client).RegisterAsync(Request));
    }

    [Theory]
    [InlineData(409, "{\"message\":\"An account with this email already exists.\"}", "An account with this email already exists.")]
    [InlineData(400, "{\"message\":\"Passwords must have at least one digit ('0'-'9').\"}", "Passwords must have at least one digit ('0'-'9').")]
    [InlineData(400, "{\"title\":\"One or more validation errors occurred.\"}", "Check your details and try again.")]
    [InlineData(400, "not json", "Check your details and try again.")]
    [InlineData(429, "", "Too many sign-up attempts. Please wait a few minutes and try again.")]
    [InlineData(500, "", "Registration is unavailable. Please try again shortly.")]
    public async Task TranslatesRegisterErrorsIntoReadableMessages(int status, string body, string expected)
    {
        using var client = Returning(status, body);
        var error = await Assert.ThrowsAsync<AuthException>(() => new AuthApi(client).RegisterAsync(Request));
        Assert.Equal(expected, error.Message);
    }

    [Fact]
    public async Task LoginWithUnconfirmedEmailThrowsDedicatedException()
    {
        using var client = Returning(403, "{\"message\":\"Confirm your email address to sign in.\"}");
        var error = await Assert.ThrowsAsync<EmailNotConfirmedException>(() =>
            new AuthApi(client).LoginAsync(new LoginRequest { Email = "new@example.com", Password = "Str0ng!Pass" }));
        Assert.Equal("Confirm your email address to sign in.", error.Message);
    }

    [Fact]
    public async Task ConfirmPostsCodeAndReadsToken()
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(1);
        var expected = new LoginResponse(TestTokens.Create(expiry), expiry, TestTokens.User);
        var request = new ConfirmEmailRequest { Email = "new@example.com", Code = "123456" };
        using var client = Client(async message =>
        {
            Assert.Equal("/api/auth/confirm-email", message.RequestUri!.AbsolutePath);
            Assert.Equal(request, await message.Content!.ReadFromJsonAsync<ConfirmEmailRequest>());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(expected) };
        });
        Assert.Equal(expected, await new AuthApi(client).ConfirmEmailAsync(request));
    }

    [Theory]
    [InlineData(400, "{\"message\":\"That code is invalid or has expired. Request a new code and try again.\"}", "That code is invalid or has expired. Request a new code and try again.")]
    [InlineData(400, "", "That code is invalid or has expired. Request a new code and try again.")]
    [InlineData(429, "", "Too many attempts. Please wait a few minutes and try again.")]
    [InlineData(500, "", "Confirmation is unavailable. Please try again shortly.")]
    public async Task TranslatesConfirmErrors(int status, string body, string expected)
    {
        using var client = Returning(status, body);
        var error = await Assert.ThrowsAsync<AuthException>(() =>
            new AuthApi(client).ConfirmEmailAsync(new ConfirmEmailRequest { Email = "new@example.com", Code = "123456" }));
        Assert.Equal(expected, error.Message);
    }

    [Fact]
    public async Task ResendPostsEmailAndAcceptsAccepted()
    {
        using var client = Client(async message =>
        {
            Assert.Equal("/api/auth/resend-confirmation", message.RequestUri!.AbsolutePath);
            Assert.Equal("new@example.com", (await message.Content!.ReadFromJsonAsync<ResendConfirmationRequest>())!.Email);
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        await new AuthApi(client).ResendConfirmationAsync(new ResendConfirmationRequest { Email = "new@example.com" });
    }

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    public async Task ResendErrorsThrowAuthException(int status)
    {
        using var client = Returning(status);
        await Assert.ThrowsAsync<AuthException>(() =>
            new AuthApi(client).ResendConfirmationAsync(new ResendConfirmationRequest { Email = "new@example.com" }));
    }
}

public sealed class ConfirmEmailSessionTests
{
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
    private readonly MemoryTokenStore store = new();
    private readonly StubAuthApi api = new();
    private static readonly ConfirmEmailRequest Request = new() { Email = "new@example.com", Code = "123456" };

    private SessionService Create()
    {
        var expiry = clock.GetUtcNow().AddHours(1);
        api.Response = new LoginResponse(TestTokens.Create(expiry), expiry, TestTokens.User);
        return new SessionService(api, store, clock);
    }

    [Fact]
    public async Task ConfirmingSignsInAndSavesToken()
    {
        using var session = Create();
        var events = 0;
        session.SessionChanged += (_, _) => events++;
        await session.ConfirmEmailAsync(Request);
        Assert.Equal(Request, api.ConfirmRequest);
        Assert.True(session.IsAuthenticated);
        Assert.Equal(api.Response.AccessToken, store.Token);
        Assert.Equal(1, events);
    }

    [Fact]
    public async Task WrongCodeLeavesUserSignedOut()
    {
        using var session = Create();
        api.ConfirmError = new AuthException("That code is invalid or has expired.");
        await Assert.ThrowsAsync<AuthException>(() => session.ConfirmEmailAsync(Request));
        Assert.False(session.IsAuthenticated);
        Assert.Null(store.Token);
    }

    [Fact]
    public async Task UnconfirmedLoginLeavesUserSignedOut()
    {
        using var session = Create();
        api.LoginError = new EmailNotConfirmedException("Confirm your email.");
        await Assert.ThrowsAsync<EmailNotConfirmedException>(() =>
            session.SignInAsync(new LoginRequest { Email = "new@example.com", Password = "Str0ng!Pass" }));
        Assert.False(session.IsAuthenticated);
        Assert.Null(store.Token);
    }
}
