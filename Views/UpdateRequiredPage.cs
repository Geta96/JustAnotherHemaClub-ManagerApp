namespace JustAnotherHemaClub.Views;

/// <summary>
/// Full-screen gate shown when the installed app version is older than the
/// <c>MinSupportedAppVersion</c> value published on the Google Sheet "Config"
/// tab. It cannot be dismissed (there is no back navigation) — the only action
/// is a button that opens the app's Google Play listing so the user can update.
/// Built entirely in code so it needs no XAML registration.
/// </summary>
public sealed class UpdateRequiredPage : ContentPage
{
    private const string PlayStoreUrl =
        "https://play.google.com/store/apps/details?id=com.jahc.manager";

    public UpdateRequiredPage(int installedVersion, int requiredVersion)
    {
        Title = "Update required";

        // No flyout / back arrow — this is a hard gate.
        Shell.SetNavBarIsVisible(this, false);
        NavigationPage.SetHasBackButton(this, false);

        BackgroundColor = GetColor("Wine", Colors.Maroon);
        Padding = new Thickness(28);

        var heading = new Label
        {
            Text = "Please update the app",
            FontSize = 24,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            TextColor = GetColor("Cream", Colors.Beige)
        };

        var body = new Label
        {
            Text = "You're using an older version of JAHC Manager that is no longer " +
                   "supported. Update to the latest version from Google Play to keep " +
                   "using the app.",
            FontSize = 16,
            HorizontalTextAlignment = TextAlignment.Center,
            TextColor = GetColor("Cream", Colors.Beige)
        };

        var versionInfo = new Label
        {
            Text = $"Installed version: {installedVersion}\nRequired version: {requiredVersion}",
            FontSize = 13,
            Opacity = 0.7,
            HorizontalTextAlignment = TextAlignment.Center,
            TextColor = GetColor("Cream", Colors.Beige)
        };

        var updateButton = new Button
        {
            Text = "Update on Google Play",
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = GetColor("Green", Colors.Green),
            TextColor = GetColor("Cream", Colors.White),
            CornerRadius = 10,
            HeightRequest = 50
        };
        updateButton.Clicked += OnUpdateClicked;

        Content = new VerticalStackLayout
        {
            Spacing = 20,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Image
                {
                    Source = "monochrome_logo.jpg",
                    HeightRequest = 160,
                    Aspect = Aspect.AspectFit,
                    HorizontalOptions = LayoutOptions.Center
                },
                heading,
                body,
                versionInfo,
                updateButton
            }
        };
    }

    private async void OnUpdateClicked(object? sender, EventArgs e)
    {
        try
        {
            await Launcher.Default.OpenAsync(PlayStoreUrl);
        }
        catch
        {
            await DisplayAlert("Couldn't open Google Play",
                "Please open the Google Play Store and update JAHC Manager manually.", "OK");
        }
    }

    private static Color GetColor(string key, Color fallback) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color c
            ? c
            : fallback;
}
