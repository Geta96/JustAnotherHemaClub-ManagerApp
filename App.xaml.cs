using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.Views;

namespace JustAnotherHemaClub;

public partial class App : Application
{
    public App(IServiceProvider services)
    {
        InitializeComponent();
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
            MainPage = new NavigationPage(services.GetRequiredService<LoginPage>());
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void ShowError(Exception? ex)
    {
        var text = ex?.ToString() ?? "Unknown startup error.";
        MainPage = new ContentPage
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