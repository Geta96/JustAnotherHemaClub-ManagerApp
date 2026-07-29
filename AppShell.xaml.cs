using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.Views;
#if ANDROID
using Google.Android.Material.AppBar;
#endif

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
        Loaded += (_, _) =>
        {
            MainActivity.ApplyWineStatusBar();
#if ANDROID
            HookHamburger();
#endif
        };

#if ANDROID
        // The toolbar can be recreated on navigation, which drops our custom
        // navigation-click listener; re-attach after each navigation.
        Navigated += (_, _) => HookHamburger();
#endif
    }

    /// <summary>
    /// When the flyout (hamburger menu) is open, Back should close it instead of
    /// navigating or exiting the app. Only when the flyout is already closed do
    /// we fall through to Shell's default Back handling (page pop / app exit).
    /// </summary>
    protected override bool OnBackButtonPressed()
    {
        if (FlyoutIsPresented)
        {
            FlyoutIsPresented = false;
            return true; // handled — swallow the Back event
        }

        return base.OnBackButtonPressed();
    }

#if ANDROID
    /// <summary>
    /// Workaround for API 34/35: the Shell hamburger icon's click listener is not
    /// attached to the AndroidX DrawerLayout, so tapping it does nothing (only the
    /// edge-swipe opens the flyout). Re-attach a click handler on the toolbar's
    /// navigation icon that toggles the flyout. Posted to the UI queue so the
    /// toolbar is guaranteed to exist, and re-run after each navigation because
    /// MAUI can recreate the toolbar (dropping our listener).
    /// </summary>
    private void HookHamburger()
    {
        if (Handler?.PlatformView is not AndroidX.DrawerLayout.Widget.DrawerLayout drawerLayout)
            return;

        drawerLayout.Post(() =>
        {
            var toolbar = FindMaterialToolbar(drawerLayout);
            if (toolbar is null)
                return;

            toolbar.SetNavigationOnClickListener(new ShellNavClickListener(() =>
            {
                if (FlyoutBehavior != FlyoutBehavior.Disabled)
                    FlyoutIsPresented = !FlyoutIsPresented;
            }));
        });
    }

    private static MaterialToolbar? FindMaterialToolbar(Android.Views.ViewGroup group)
    {
        for (int i = 0; i < group.ChildCount; i++)
        {
            var child = group.GetChildAt(i);
            if (child is MaterialToolbar match) return match;
            if (child is Android.Views.ViewGroup nested)
            {
                var found = FindMaterialToolbar(nested);
                if (found is not null) return found;
            }
        }
        return null;
    }
#endif

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

#if ANDROID
internal sealed class ShellNavClickListener : Java.Lang.Object, Android.Views.View.IOnClickListener
{
    private readonly Action _onClick;
    public ShellNavClickListener(Action onClick) => _onClick = onClick;
    public void OnClick(Android.Views.View? v) => _onClick();
}
#endif