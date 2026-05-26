using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.ViewModels;
using JustAnotherHemaClub.Views;
using Microsoft.Extensions.Logging;

namespace JustAnotherHemaClub;

public static class MauiProgram
{
    // TODO: replace with your spreadsheet ID
    private const string SpreadsheetId = "PUT_YOUR_SPREADSHEET_ID_HERE";

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        // Concrete backends
        builder.Services.AddSingleton(_ => new GoogleSheetsService(SpreadsheetId));
        builder.Services.AddSingleton<DemoGoogleSheetsService>();

        // Auth picks between them via the proxy below
        builder.Services.AddSingleton<AuthService>();

        // Proxy is what consumers see
        builder.Services.AddSingleton<IGoogleSheetsService, GoogleSheetsServiceProxy>();

        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<SessionsViewModel>();
        builder.Services.AddTransient<FinanceViewModel>();
        builder.Services.AddTransient<StatisticsViewModel>();

        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<SessionsPage>();
        builder.Services.AddTransient<FinancePage>();
        builder.Services.AddTransient<StatisticsPage>();

        // Shell host (resolved fresh on each login)
        builder.Services.AddTransient<AppShell>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}