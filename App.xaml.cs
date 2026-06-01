using JustAnotherHemaClub.Views;

namespace JustAnotherHemaClub;

public partial class App : Application
{
    public App(IServiceProvider services)
    {
        InitializeComponent();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ShowError(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
            ShowError(e.Exception);

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