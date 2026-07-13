using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;
using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Views;

public partial class TournamentHubPage : ContentPage
{
    private readonly TournamentHubViewModel _vm;
    private readonly TournamentSession _session;
    private readonly IServiceProvider _services;

    public TournamentHubPage(TournamentHubViewModel vm,
                             TournamentSession session,
                             IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _session = session;
        _services = services;

        _vm.PoolsVm.MatchSelected += OnPoolMatchSelected;
        _vm.ElimVm.MatchSelected  += OnElimMatchSelected;
        _vm.PickFencerToWithdrawAsync = PickFencerAsync;
    }

    private bool _returningFromMatch;

    /// <summary>
    /// When true, OnAppearing will switch to the Elim tab after loading.
    /// Set by TournamentEditorPage after a successful Reset Elimination.
    /// </summary>
    public bool NavigateToElimTabOnAppear { get; set; }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_session.Current is null) { await Navigation.PopAsync(); return; }

        if (_returningFromMatch)
        {
            _returningFromMatch = false;
            await _vm.ReloadMatchesOnlyAsync();
        }
        else
        {
            await _vm.LoadAsync();
        }

        if (NavigateToElimTabOnAppear)
        {
            NavigateToElimTabOnAppear = false;
            _vm.SelectedTabIndex = 2; // Elim tab
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.PoolsVm.StopPolling();
    }

    private async void OnManageRosterClicked(object? sender, EventArgs e)
    {
        // Hard gate: only organisers may open the roster editor, regardless of
        // any button-visibility binding state.
        if (_session.Current is null || !_session.IsOrganiser) return;

        var page = _services.GetRequiredService<TournamentEditorPage>();
        // Push first so the navigation animation is not blocked by the network call.
        await Navigation.PushAsync(page);
        await page.PrepareForExistingAsync(_session.Current.Id);
    }

    private async void OnPoolMatchSelected(PoolMatchRowVm row)
    {
        if (row is null) return;
        await PushMatchAsync(row.Match);
    }

    private async void OnElimMatchSelected(Match match)
    {
        if (match is null) return;
        await PushMatchAsync(match);
    }

    private async Task PushMatchAsync(Match match)
    {
        var page = _services.GetRequiredService<MatchPage>();
        _returningFromMatch = true;
        // Assign the match id BEFORE pushing: PushAsync triggers OnAppearing,
        // which loads the match from _matchId. PrepareForMatchAsync only stores
        // the id (no I/O), so there's nothing to defer past the animation.
        await page.PrepareForMatchAsync(match.Id);
        await Navigation.PushAsync(page);
    }

    private async Task<TournamentFencer?> PickFencerAsync(IReadOnlyList<TournamentFencer> candidates)
    {
        if (candidates is null || candidates.Count == 0) return null;

        var labels = candidates.Select(c => c.Name).ToArray();
        var choice = await DisplayActionSheet(
            "Withdraw which fencer?",
            "Cancel",
            null,
            labels);
        if (string.IsNullOrEmpty(choice) || choice == "Cancel") return null;

        var picked = candidates.FirstOrDefault(c => c.Name == choice);
        if (picked is null) return null;

        var confirm = await DisplayAlert(
            "Withdraw fencer",
            $"Withdraw {picked.Name}?\n\n" +
            "All of their remaining matches will end as 0–0 walkovers and their opponents will win automatically. " +
            "Already-finished matches keep their scores.",
            "Withdraw", "Cancel");
        return confirm ? picked : null;
    }
}