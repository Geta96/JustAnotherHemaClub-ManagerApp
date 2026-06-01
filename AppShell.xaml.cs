using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.Views;

namespace JustAnotherHemaClub;

public partial class AppShell : Shell
{
    private readonly AuthService _auth;
    private readonly IServiceProvider _services;

    public AppShell(AuthService auth, IServiceProvider services)
    {
        InitializeComponent();
        _auth = auth;
        _services = services;

        UserLabel.Text = _auth.IsGuest
            ? "Signed in as Guest"
            : $"Signed in as {_auth.CurrentFencer?.Name ?? _auth.CurrentFencer?.Username ?? "user"}";

        // Instructor-only flyout entries
        Shell.SetFlyoutItemIsVisible(StatisticsFlyout, _auth.IsLoggedInInstructor);
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        _auth.Logout();

        var login = _services.GetRequiredService<LoginPage>();
        Application.Current!.MainPage = new NavigationPage(login);
        await Task.CompletedTask;
    }
}