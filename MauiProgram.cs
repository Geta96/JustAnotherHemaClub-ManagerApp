using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.ViewModels;
using JustAnotherHemaClub.Views;
using Microsoft.Extensions.Logging;

namespace JustAnotherHemaClub;

public static class MauiProgram
{
    private const string SpreadsheetId = "1KYpk1ElUTYGoFLlcJcJovYXXuvJzmkQ8tkzvh9y4EHU";

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
        builder.Services.AddSingleton(new GoogleSheetsService(SpreadsheetId));
        builder.Services.AddSingleton<CachedGoogleSheetsService>(sp =>
            new CachedGoogleSheetsService(sp.GetRequiredService<GoogleSheetsService>()));
        builder.Services.AddSingleton<IGoogleSheetsService>(sp => sp.GetRequiredService<CachedGoogleSheetsService>());
        builder.Services.AddSingleton<ICacheControl>(sp => sp.GetRequiredService<CachedGoogleSheetsService>());

        // Recurring training materialization (runs after login warm-up)
        builder.Services.AddSingleton<RecurringTrainingMaterializer>();

        // Auth + proxy
        builder.Services.AddSingleton<AuthService>();
        builder.Services.AddSingleton<IBiometricService, BiometricService>();

        // ViewModels
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<RegisterViewModel>();
        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<TrainingsViewModel>();
        builder.Services.AddTransient<FinanceViewModel>();
        builder.Services.AddTransient<PricesViewModel>();
        builder.Services.AddTransient<StatisticsViewModel>();
        builder.Services.AddTransient<FencersViewModel>();
        builder.Services.AddTransient<ProfileViewModel>();
        builder.Services.AddTransient<IndividualLessonsViewModel>();
        builder.Services.AddTransient<WeeklyViewModel>();
        builder.Services.AddTransient<TrainingsHubViewModel>();

        // Tournament view-models
        builder.Services.AddTransient<TournamentsViewModel>();
        builder.Services.AddTransient<TournamentEditorVm>();
        builder.Services.AddTransient<TournamentAccessVm>();
        builder.Services.AddTransient<TournamentHubViewModel>();
        builder.Services.AddTransient<PoolsTabViewModel>();
        builder.Services.AddTransient<PoolStandingsTabViewModel>();
        builder.Services.AddTransient<ElimTabViewModel>();
        builder.Services.AddTransient<FinalStandingsTabViewModel>();
        builder.Services.AddTransient<MatchViewModel>();

        // Pages
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<RegisterPage>();
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<NewTrainingPage>();
        builder.Services.AddTransient<FinancePage>();
        builder.Services.AddTransient<StatisticsPage>();
        builder.Services.AddTransient<FencersPage>();
        builder.Services.AddTransient<ProfilePage>();
        builder.Services.AddTransient<GdprPage>();
        builder.Services.AddTransient<LiabilityPage>();
        builder.Services.AddTransient<TrainingsHubPage>();

        // Tournament pages
        builder.Services.AddTransient<TournamentsPage>();
        builder.Services.AddTransient<TournamentEditorPage>();
        builder.Services.AddTransient<TournamentAccessPage>();
        builder.Services.AddTransient<TournamentHubPage>();
        builder.Services.AddTransient<MatchPage>();

        // Shell
        builder.Services.AddTransient<AppShell>();

        // Tournament services
        builder.Services.AddSingleton<TournamentAutoSaveService>();
        builder.Services.AddSingleton<TournamentSession>();
        builder.Services.AddSingleton<TournamentRefreshService>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}