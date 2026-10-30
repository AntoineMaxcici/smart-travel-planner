using Microsoft.Extensions.DependencyInjection;
using TravelPlanner.Core.Services;
using TravelPlanner.Core.ViewModels;
using TravelPlanner.Maui.Views;

namespace TravelPlanner.Maui;

public partial class App : Application
{
    private readonly ISessionService session;
    private readonly IServiceProvider services;
    private Window? window;
    private bool starting = true;

    public App(ISessionService session, IServiceProvider services)
    {
        InitializeComponent();
        this.session = session;
        this.services = services;
        session.SessionChanged += OnSessionChanged;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        window = new Window(new ContentPage
        {
            Content = new VerticalStackLayout
            {
                VerticalOptions = LayoutOptions.Center, Spacing = 16,
                Children =
                {
                    new ActivityIndicator { IsRunning = true, HorizontalOptions = LayoutOptions.Center },
                    new Label { Text = "Checking your session…", HorizontalTextAlignment = TextAlignment.Center }
                }
            }
        });
        window.Created += async (_, _) => await RestoreSessionAsync();
        window.Resumed += async (_, _) => await CheckExpiryAsync();
        return window;
    }

    private async Task RestoreSessionAsync()
    {
        string? error = null;
        try { await session.InitializeAsync(); }
        catch (AuthException ex) { error = ex.Message; }
        catch (HttpRequestException) { error = "Cannot reach TravelPlanner to check your session. Please try signing in."; }
        catch (OperationCanceledException) { error = "Checking your session timed out. Please try signing in."; }
        finally { starting = false; }
        await MainThread.InvokeOnMainThreadAsync(() => ShowSessionPage(error));
    }

    private async Task CheckExpiryAsync()
    {
        try { await session.GetAccessTokenAsync(); }
        catch (AuthException ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() => ShowSessionPage(ex.Message));
        }
    }

    private void OnSessionChanged(object? sender, EventArgs args)
    {
        if (!starting) MainThread.BeginInvokeOnMainThread(() => ShowSessionPage());
    }

    private void ShowSessionPage(string? error = null)
    {
        if (window is null) return;
        if (session.IsAuthenticated)
        {
            window.Page = services.GetRequiredService<AppShell>();
        }
        else
        {
            // Replacing the root removes the authenticated back stack after logout/expiry.
            ShowLoginPage(error);
        }
    }

    private void ShowLoginPage(string? error = null)
    {
        if (window is null) return;
        var login = services.GetRequiredService<LoginPage>();
        var viewModel = (LoginViewModel)login.BindingContext;
        viewModel.ErrorMessage = error;
        viewModel.RegisterRequested += (_, _) => ShowRegisterPage();
        viewModel.ConfirmEmailRequested += (_, email) => ShowConfirmEmailPage(email,
            "Your email isn't confirmed yet. We've sent you a new code.");
        window.Page = login;
    }

    private void ShowRegisterPage()
    {
        if (window is null) return;
        var register = services.GetRequiredService<RegisterPage>();
        var viewModel = (RegisterViewModel)register.BindingContext;
        viewModel.LoginRequested += (_, _) => ShowLoginPage();
        viewModel.ConfirmationRequired += (_, email) => ShowConfirmEmailPage(email);
        window.Page = register;
    }

    private void ShowConfirmEmailPage(string email, string? info = null)
    {
        if (window is null) return;
        var confirm = services.GetRequiredService<ConfirmEmailPage>();
        var viewModel = (ConfirmEmailViewModel)confirm.BindingContext;
        viewModel.Email = email;
        viewModel.InfoMessage = info;
        viewModel.LoginRequested += (_, _) => ShowLoginPage();
        // A correct code signs the user in; OnSessionChanged then shows the AppShell.
        window.Page = confirm;
    }
}
