using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.Views;

namespace JustAnotherHemaClub;

public partial class App : Application
{
    private readonly IServiceProvider _services;
    private Page _rootPage;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
        UserAppTheme = AppTheme.Light;   // force light theme app-wide

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ShowError(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
            ShowError(e.Exception);

        // Warm the Google Sheets client at launch (service-account auth needs no
        // user login), so the OAuth handshake + HttpClient setup is already done
        // by the time the user signs in. Fire-and-forget; failures are ignored
        // and the first real read will simply build the client itself.
        _ = Task.Run(async () =>
        {
            try
            {
                await services.GetRequiredService<GoogleSheetsService>().InitializeAsync();
            }
            catch { /* best-effort launch warm-up */ }
        });

        try
        {
            _rootPage = new NavigationPage(services.GetRequiredService<LoginPage>());
        }
        catch (Exception ex)
        {
            _rootPage = BuildErrorPage(ex);
        }
    }

    protected override Window CreateWindow(IActivationState? activationState)
        => new Window(_rootPage);

    private void ShowError(Exception? ex)
    {
        var page = BuildErrorPage(ex);
        _rootPage = page;
        AppNavigationHelper.SetRootPage(page);
    }

    private static Page BuildErrorPage(Exception? ex)
    {
        var text = ex?.ToString() ?? "Unknown startup error.";
        return new ContentPage
        {
            BackgroundColor = Colors.White,
            Content = new ScrollView
            {
                Content = new Label
                {
                    Text = text,
                    TextColor = Colors.Black,
                    Padding = 16,
                    FontSize = 12
                }
            }
        };
    }
}