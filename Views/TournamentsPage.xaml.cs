using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Views;

public partial class TournamentsPage : ContentPage
{
    private readonly TournamentsViewModel _vm;
    private readonly IServiceProvider _services;
    private readonly TournamentSession _session;

    /// <summary>
    /// Set whenever the trash icon's TapGestureRecognizer fires. The card-level
    /// tap handler ignores any tap that arrives within a short window afterwards,
    /// because Android bubbles the gesture from the inner Border up to the outer
    /// card even when the inner has its own recognizer.
    /// </summary>
    private DateTime _lastDeleteTapUtc = DateTime.MinValue;
    private static readonly TimeSpan SuppressOpenAfterDelete = TimeSpan.FromMilliseconds(350);

    public TournamentsPage(TournamentsViewModel vm, IServiceProvider services, TournamentSession session)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _services = services;
        _session = session;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _session.Close(); // returning here means we're not inside a tournament anymore
        await Task.Yield();
        await _vm.LoadAsync(showSpinner: false);
    }

    private async void OnRefreshTapped(object? sender, TappedEventArgs e)
        => await _vm.RefreshAsync();

    private async void OnNewTournamentClicked(object? sender, EventArgs e)
    {
        var page = _services.GetRequiredService<TournamentEditorPage>();
        page.PrepareForNew();
        await Navigation.PushAsync(page);
    }

    private async void OnTournamentTapped(object? sender, TappedEventArgs e)
    {
        // Swallow the bubble that follows a trash-icon tap on Android.
        if (DateTime.UtcNow - _lastDeleteTapUtc < SuppressOpenAfterDelete) return;

        if (sender is not BindableObject bo || bo.BindingContext is not TournamentRow row) return;

        // We have to refetch the tournament so the access page sees the latest password/roster.
        var sheets = _services.GetRequiredService<IGoogleSheetsService>();
        var full = await sheets.GetTournamentAsync(row.Id);
        if (full is null)
        {
            await DisplayAlert("Not found", "This tournament could not be loaded.", "OK");
            return;
        }

        var access = _services.GetRequiredService<TournamentAccessPage>();
        var role = await access.ShowAsync(Navigation, full);
        if (role is null) return; // user backed out; access page already popped by the system

        // Access page is still on top. Atomically replace it with the hub so this
        // TournamentsPage never re-appears in between — which would fire OnAppearing
        // and call _session.Close(), wiping the session we just opened.
        var hub = _services.GetRequiredService<TournamentHubPage>();
        Navigation.InsertPageBefore(hub, access);
        await Navigation.PopAsync();
    }

    private async void OnDeleteTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not BindableObject bo || bo.BindingContext is not TournamentRow row) return;

        // Block the card-level open handler from running for the same gesture.
        _lastDeleteTapUtc = DateTime.UtcNow;

        var confirm = await DisplayAlert(
            "Delete tournament",
            $"Delete \"{row.Name}\" and ALL its data (roster, pools, matches, standings)?\n\n" +
            "This cannot be undone.",
            "Continue", "Cancel");
        if (!confirm) return;

        var password = await DisplayPromptAsync(
            "Organiser password",
            "Enter the organiser password to confirm deletion.",
            accept: "Delete",
            cancel: "Cancel",
            placeholder: "Password",
            maxLength: 100,
            keyboard: Keyboard.Default);
        if (password is null) return; // user cancelled

        var (outcome, error) = await _vm.DeleteWithPasswordAsync(row.Id, password);
        switch (outcome)
        {
            case TournamentsViewModel.DeleteOutcome.Deleted:
                await DisplayAlert("Deleted", $"\"{row.Name}\" was deleted.", "OK");
                break;
            case TournamentsViewModel.DeleteOutcome.WrongPassword:
                await DisplayAlert("Wrong password", "The organiser password did not match. Nothing was deleted.", "OK");
                break;
            case TournamentsViewModel.DeleteOutcome.NotFound:
                await DisplayAlert("Not found", "This tournament no longer exists.", "OK");
                await _vm.RefreshAsync();
                break;
            case TournamentsViewModel.DeleteOutcome.Error:
                await DisplayAlert("Delete failed", error ?? "Unknown error.", "OK");
                break;
        }
    }
}