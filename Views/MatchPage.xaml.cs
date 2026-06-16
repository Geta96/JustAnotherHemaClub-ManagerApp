using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Views;

public partial class MatchPage : ContentPage
{
    private readonly MatchViewModel _vm;
    private string? _matchId;

    public MatchPage(MatchViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;

        _vm.ConfirmTakeOverAsync = async otherId =>
            await DisplayAlert(
                "Match in use",
                $"This match is currently being refereed by {otherId}.\n\nTake over?",
                "Take over", "Cancel");

        _vm.LockTakenOver += async () =>
        {
            await DisplayAlert("Match taken over",
                "Another judge has taken over this match. Returning to the pool list.",
                "OK");
            await Navigation.PopAsync();
        };
    }

    public Task PrepareForMatchAsync(string matchId)
    {
        _matchId = matchId;
        return Task.CompletedTask;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_matchId is null) { await Navigation.PopAsync(); return; }
        await _vm.LoadAsync(_matchId);
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        await _vm.ReleaseLockAndDisposeAsync();
        _vm.Dispose();
    }

    private async void OnFinishClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlert(
            "Finish match",
            $"Declare a winner with score {_vm.LeftScore} – {_vm.RightScore}?",
            "Finish", "Cancel");
        if (!confirm) return;

        await _vm.FinishMatchCommand.ExecuteAsync(null);

        // Only navigate away on a confirmed save. If the flush failed, the VM
        // has rolled back its state and set ErrorMessage; staying on the page
        // lets the user retry instead of silently losing the score.
        if (string.IsNullOrEmpty(_vm.ErrorMessage) &&
            _vm.Match?.Status == JustAnotherHemaClub.Models.MatchStatus.Finished)
        {
            await Navigation.PopAsync();
        }
    }
}