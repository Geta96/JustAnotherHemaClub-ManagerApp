using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace JustAnotherHemaClub.Views;

public partial class ProfilePage : ContentPage
{
    private readonly ProfileViewModel _vm;
    private readonly IServiceProvider _services;

    // TEMP: testing allowance for the one-time date migration.
    private const string MigrateRunCountKey = "maintenance.migrateDates.runCount";
    private const int MigrateRunLimit = 10;

    public ProfilePage(ProfileViewModel vm, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _services = services;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
        UpdateMigrateAvailability();
    }

    private async void OnOpenGdprTapped(object? sender, TappedEventArgs e)
        => await Navigation.PushAsync(_services.GetRequiredService<GdprPage>());

    private async void OnOpenLiabilityTapped(object? sender, TappedEventArgs e)
        => await Navigation.PushAsync(_services.GetRequiredService<LiabilityPage>());

    // TEMP: limited-use date migration trigger. DELETE this handler (and the button
    // in ProfilePage.xaml, plus GoogleSheetsService.Migration.cs) after testing.
    private async void OnMigrateDatesClicked(object? sender, EventArgs e)
    {
        var runsSoFar = Preferences.Get(MigrateRunCountKey, 0);
        if (runsSoFar >= MigrateRunLimit)
        {
            await DisplayAlert("Limit reached",
                $"This maintenance action has already been run {MigrateRunLimit} times.", "OK");
            UpdateMigrateAvailability();
            return;
        }

        var confirm = await DisplayAlert(
            "Fix stored dates",
            "This rewrites every stored date across all sheets to a safe format. " +
            $"Run {runsSoFar + 1} of {MigrateRunLimit}. Continue?",
            "Run", "Cancel");
        if (!confirm) return;

        MigrateDatesButton.IsEnabled = false;
        MigrateStatusLabel.Text = "Migrating… please keep the app open.";

        try
        {
            var sheets = _services.GetRequiredService<GoogleSheetsService>();
            var summary = await sheets.MigrateAllDatesToIsoAsync();

            // Only count successful runs against the allowance.
            runsSoFar = Preferences.Get(MigrateRunCountKey, 0) + 1;
            Preferences.Set(MigrateRunCountKey, runsSoFar);

            var report = string.Join("\n", summary.Select(kv => $"{kv.Key}: {kv.Value} cell(s)"));
            MigrateStatusLabel.Text = $"Done. Run {runsSoFar} of {MigrateRunLimit}.";
            await DisplayAlert("Migration complete", report, "OK");
        }
        catch (Exception ex)
        {
            MigrateStatusLabel.Text = "Migration failed.";
            await DisplayAlert("Migration failed", ex.Message, "OK");
        }
        finally
        {
            UpdateMigrateAvailability();
        }
    }

    // Enables the button only while runs remain, and reflects the count in the label.
    private void UpdateMigrateAvailability()
    {
        var runsSoFar = Preferences.Get(MigrateRunCountKey, 0);
        var remaining = MigrateRunLimit - runsSoFar;

        MigrateDatesButton.IsEnabled = remaining > 0;
        MigrateStatusLabel.Text = remaining > 0
            ? $"{remaining} of {MigrateRunLimit} runs remaining."
            : $"Limit reached ({MigrateRunLimit} runs used).";
    }
}