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

        ApplyStatusBarInset();

        // Re-apply after the Shell handler has finished setting up its Android views
        Loaded += (_, _) => MainActivity.ApplyWineStatusBar();
    }

    /// <summary>
    /// Pads the flyout header below the device's real status bar so the crest
    /// is never clipped, then grows the header to fit the logo + user line.
    /// </summary>
    private void ApplyStatusBarInset()
    {
#if ANDROID
        var top = MainActivity.StatusBarHeightDip;
        // Small extra gap between the status bar and the crest for breathing room.
        var padTop = top + 12;
        FlyoutHeaderGrid.Padding = new Thickness(16, padTop, 16, 4);
        // 220 logo + ~24 label + top inset + tight bottom padding.
        FlyoutHeaderGrid.HeightRequest = 220 + 24 + padTop + 4;
#endif
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