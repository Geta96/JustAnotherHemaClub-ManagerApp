using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace JustAnotherHemaClub.Views;

public partial class ProfilePage : ContentPage
{
    private readonly ProfileViewModel _vm;
    private readonly IServiceProvider _services;

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
    }

    private async void OnOpenGdprTapped(object? sender, TappedEventArgs e)
        => await Navigation.PushAsync(_services.GetRequiredService<GdprPage>());

    private async void OnOpenLiabilityTapped(object? sender, TappedEventArgs e)
        => await Navigation.PushAsync(_services.GetRequiredService<LiabilityPage>());

    // TEMP: one-time date migration trigger. DELETE this handler (and the button
    // in ProfilePage.xaml, plus GoogleSheetsService.Migration.cs) after running once.
    private async void OnMigrateDatesClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlert(
            "Fix stored dates",
            "This rewrites every stored date across all sheets to a safe format. " +
            "It is safe to run once. Continue?",
            "Run", "Cancel");
        if (!confirm) return;

        MigrateDatesButton.IsEnabled = false;
        MigrateStatusLabel.Text = "Migrating… please keep the app open.";

        try
        {
            var sheets = _services.GetRequiredService<GoogleSheetsService>();
            var summary = await sheets.MigrateAllDatesToIsoAsync();

            var report = string.Join("\n", summary.Select(kv => $"{kv.Key}: {kv.Value} cell(s)"));
            MigrateStatusLabel.Text = "Done. You can remove this button now.";
            await DisplayAlert("Migration complete", report, "OK");
        }
        catch (Exception ex)
        {
            MigrateStatusLabel.Text = "Migration failed.";
            await DisplayAlert("Migration failed", ex.Message, "OK");
        }
        finally
        {
            MigrateDatesButton.IsEnabled = true;
        }
    }
}