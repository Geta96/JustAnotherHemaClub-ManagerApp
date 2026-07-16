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

        // Re-apply after the Shell handler has finished setting up its Android views
        Loaded += (_, _) => MainActivity.ApplyWineStatusBar();
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        _auth.Logout();

        var login = _services.GetRequiredService<LoginPage>();
        Services.AppNavigationHelper.SetRootPage(new NavigationPage(login));

        // Re-apply for the NavigationPage that just took over
        MainActivity.ApplyWineStatusBar();

        await Task.CompletedTask;
    }
}