using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.ViewModels;
using JustAnotherHemaClub.Views;
using Microsoft.Extensions.Logging;

namespace JustAnotherHemaClub;

public static class MauiProgram
{
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

        // Backends
        builder.Services.AddSingleton(_ => new GoogleSheetsService(SpreadsheetId));
        builder.Services.AddSingleton<DemoGoogleSheetsService>();

        // Auth + proxy
        builder.Services.AddSingleton<AuthService>();
        builder.Services.AddSingleton<IGoogleSheetsService, GoogleSheetsServiceProxy>();
        builder.Services.AddSingleton<IProfileService, PreferencesProfileService>();

        // ViewModels
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<TrainingsViewModel>();
        builder.Services.AddTransient<FinanceViewModel>();
        builder.Services.AddTransient<StatisticsViewModel>();
        builder.Services.AddTransient<ProfileViewModel>();

        // Pages
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<TrainingsPage>();
        builder.Services.AddTransient<FinancePage>();
        builder.Services.AddTransient<StatisticsPage>();
        builder.Services.AddTransient<ProfilePage>();

        // Shell
        builder.Services.AddTransient<AppShell>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}