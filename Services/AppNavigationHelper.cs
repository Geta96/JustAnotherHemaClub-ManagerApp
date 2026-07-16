namespace JustAnotherHemaClub.Services;

/// <summary>
/// Central helper to read/set the application's root page without touching the
/// deprecated <c>Application.MainPage</c> property (obsolete in .NET MAUI 9).
/// Uses the single-window <c>Windows[0].Page</c> pattern recommended by MAUI.
/// </summary>
public static class AppNavigationHelper
{
    /// <summary>
    /// Gets the current root page of the primary window, or <c>null</c> if none.
    /// </summary>
    public static Page? RootPage
    {
        get
        {
            var app = Application.Current;
            if (app is null || app.Windows.Count == 0)
                return null;

            return app.Windows[0].Page;
        }
    }

    /// <summary>
    /// Sets the root page of the primary window. If no window exists yet, the
    /// call is ignored (the window is created via <c>CreateWindow</c> at startup).
    /// </summary>
    public static void SetRootPage(Page page)
    {
        var app = Application.Current;
        if (app is null)
            return;

        if (app.Windows.Count > 0)
            app.Windows[0].Page = page;
    }
}
