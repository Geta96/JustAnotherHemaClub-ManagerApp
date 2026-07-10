using Android.App;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Widget;
using AndroidX.Core.View;
using Plugin.Fingerprint;

namespace JustAnotherHemaClub;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        SetTheme(Resource.Style.Maui_MainTheme_NoActionBar);

        base.OnCreate(savedInstanceState);
        CrossFingerprint.SetCurrentActivityResolver(() => Platform.CurrentActivity!);

        ApplyGradientStatusBar();
    }

    /// <summary>
    /// Called from AppShell after Shell handlers finish setting up Android views.
    /// Delegates to <see cref="ApplyGradientStatusBar"/>.
    /// </summary>
    public static void ApplyWineStatusBar() => ApplyGradientStatusBar();

    /// <summary>
    /// Overlays a black→wine (top→bottom) gradient view on the DecorView so it
    /// sits precisely over the transparent status bar area.
    /// </summary>
    public static void ApplyGradientStatusBar()
    {
        if (Platform.CurrentActivity is not MainActivity activity) return;
        if (activity.Window is not { } window) return;

        // Keep icons white on the dark background.
        var controller = WindowCompat.GetInsetsController(window, window.DecorView);
        controller.AppearanceLightStatusBars = false;

        // Resolve the status-bar pixel height.
        int statusBarHeight = 0;
        var res = activity.Resources;
        if (res != null)
        {
            int id = res.GetIdentifier("status_bar_height", "dimen", "android");
            if (id > 0) statusBarHeight = res.GetDimensionPixelSize(id);
        }
        if (statusBarHeight <= 0)
            statusBarHeight = (int)(24 * (res?.DisplayMetrics?.Density ?? 1f));

        // Build a top→bottom gradient: black → wine (#5D1312).
        // Use Android.Graphics.Color explicitly to avoid ambiguity with Microsoft.Maui.Graphics.Color.
        var gradient = new GradientDrawable(
            GradientDrawable.Orientation.TopBottom,
            new[]
            {
                Android.Graphics.Color.Black.ToArgb(),
                Android.Graphics.Color.ParseColor("#5D1312").ToArgb()
            });

        // Add a full-width view that covers exactly the status bar area inside
        // the DecorView (which already spans the full screen, behind the bar).
        if (window.DecorView is Android.Views.ViewGroup decorView)
        {
            var overlay = new Android.Views.View(activity);
            overlay.Background = gradient;
            decorView.AddView(overlay, new FrameLayout.LayoutParams(
                Android.Views.ViewGroup.LayoutParams.MatchParent,
                statusBarHeight));
        }
    }
}