using TravelPlanner.Core.Services;
using TravelPlanner.Core.ViewModels;

namespace TravelPlanner.Core.Tests;

public sealed class LoginViewModelTests
{
    [Fact]
    public void EmptyCredentialsDisableLogin()
    {
        var vm = new LoginViewModel(new StubSession());
        Assert.False(vm.LoginCommand.CanExecute(null));
        vm.Email = "traveler@example.com";
        Assert.False(vm.LoginCommand.CanExecute(null));
        vm.Password = " password with spaces ";
        Assert.True(vm.LoginCommand.CanExecute(null));
    }

    [Fact]
    public async Task InvalidEmailShowsValidationWithoutCallingApi()
    {
        var session = new StubSession();
        var vm = new LoginViewModel(session) { Email = "not-an-email", Password = "password" };
        await vm.LoginCommand.ExecuteAsync(null);
        Assert.True(vm.HasError);
        Assert.Equal(0, session.SignInCalls);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task SuccessfulLoginTrimsEmailPreservesPasswordAndClearsItAfterwards()
    {
        var session = new StubSession();
        var vm = new LoginViewModel(session) { Email = " traveler@example.com ", Password = " password with spaces " };
        await vm.LoginCommand.ExecuteAsync(null);
        Assert.Equal("traveler@example.com", session.Request!.Email);
        Assert.Equal(" password with spaces ", session.Request.Password);
        Assert.Equal("", vm.Password);
        Assert.True(session.IsAuthenticated);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task LoadingStateDisablesDuplicateSubmissionsAndResetsOnCompletion()
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new StubSession { OnSignIn = () => pending.Task };
        var vm = new LoginViewModel(session) { Email = "traveler@example.com", Password = "password" };
        var login = vm.LoginCommand.ExecuteAsync(null);
        Assert.True(vm.IsBusy);
        Assert.False(vm.IsNotBusy);
        Assert.False(vm.LoginCommand.CanExecute(null));
        await vm.LoginCommand.ExecuteAsync(null);
        Assert.Equal(1, session.SignInCalls);
        pending.SetResult();
        await login;
        Assert.False(vm.IsBusy);
    }

    [Theory]
    [InlineData("credentials")]
    [InlineData("network")]
    [InlineData("timeout")]
    [InlineData("storage")]
    public async Task ErrorsAreVisibleAndAllowRetry(string failure)
    {
        Exception error = failure switch
        {
            "credentials" => new AuthException("Invalid email or password."),
            "network" => new HttpRequestException(),
            "timeout" => new TaskCanceledException(),
            _ => new AuthException("Your session could not be saved securely.")
        };
        var session = new StubSession { OnSignIn = () => Task.FromException(error) };
        var vm = new LoginViewModel(session) { Email = "traveler@example.com", Password = "password" };
        await vm.LoginCommand.ExecuteAsync(null);
        Assert.True(vm.HasError);
        Assert.False(vm.IsBusy);
        Assert.True(vm.LoginCommand.CanExecute(null));
        session.OnSignIn = () => Task.CompletedTask;
        await vm.LoginCommand.ExecuteAsync(null);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task HomeSignOutClearsSessionAndRaisesNavigationEvent()
    {
        var session = new StubSession { User = TestTokens.User, Token = "token" };
        var events = 0;
        session.SessionChanged += (_, _) => events++;
        var vm = new HomeViewModel(session);
        await vm.SignOutCommand.ExecuteAsync(null);
        Assert.Equal(1, session.SignOutCalls);
        Assert.False(session.IsAuthenticated);
        Assert.Null(session.Token);
        Assert.Equal(1, events);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task SignOutStorageFailureShowsErrorAndAllowsRetry()
    {
        var session = new StubSession { User = TestTokens.User, OnSignOut = () => Task.FromException(new AuthException("Storage unavailable.")) };
        var vm = new HomeViewModel(session);
        await vm.SignOutCommand.ExecuteAsync(null);
        Assert.True(vm.HasError);
        Assert.True(session.IsAuthenticated);
        Assert.True(vm.SignOutCommand.CanExecute(null));
    }
}
