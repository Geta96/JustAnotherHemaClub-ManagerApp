using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

/// <summary>
/// Owns the four-tab tournament hub (Pools / Pool Standings / Elimination / Final Standings)
/// and the End/Reopen/Withdraw tournament actions in the header.
/// </summary>
public partial class TournamentHubViewModel : ObservableObject
{
    public const string TabPools          = "pools";
    public const string TabElimination    = "elim";
    public const string TabPoolStandings  = "poolst";
    public const string TabFinalStandings = "finalst";

    private readonly TournamentSession _session;
    private readonly IGoogleSheetsService _sheets;

    public PoolsTabViewModel PoolsVm { get; }
    public PoolStandingsTabViewModel PoolStandingsVm { get; }
    public ElimTabViewModel ElimVm { get; }
    public FinalStandingsTabViewModel FinalStandingsVm { get; }

    public IReadOnlyList<TournamentHubTab> Tabs { get; }

    [ObservableProperty] private int selectedTabIndex;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string errorMessage = "";

    /// <summary>
    /// Set by the page so the VM can ask for a fencer to withdraw.
    /// Returns the picked fencer, or null if the user cancelled.
    /// (Plain property, not an event — there's only ever one subscriber.)
    /// </summary>
    public Func<IReadOnlyList<TournamentFencer>, Task<TournamentFencer?>>? PickFencerToWithdrawAsync { get; set; }

    public bool IsPoolsTab          => SelectedTabIndex == 0;
    public bool IsPoolStandingsTab  => SelectedTabIndex == 1;
    public bool IsElimTab           => SelectedTabIndex == 2;
    public bool IsFinalStandingsTab => SelectedTabIndex == 3;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsPoolsTab));
        OnPropertyChanged(nameof(IsPoolStandingsTab));
        OnPropertyChanged(nameof(IsElimTab));
        OnPropertyChanged(nameof(IsFinalStandingsTab));
    }

    [RelayCommand] private void ShowPoolsTab()          => SelectedTabIndex = 0;
    [RelayCommand] private void ShowPoolStandingsTab()  => SelectedTabIndex = 1;
    [RelayCommand] private void ShowElimTab()           => SelectedTabIndex = 2;
    [RelayCommand] private void ShowFinalStandingsTab() => SelectedTabIndex = 3;

    public Tournament? Tournament => _session.Current;
    public TournamentRole Role => _session.Role;
    public bool CanEdit => _session.CanEdit;
    public string TournamentName => Tournament?.Name ?? "(no tournament)";

    public string StateText => Tournament?.State switch
    {
        TournamentState.Setup                 => "Setup",
        TournamentState.PoolsInProgress       => "Pools in progress",
        TournamentState.PoolsClosed           => "Pools closed",
        TournamentState.EliminationInProgress => "Elimination in progress",
        TournamentState.Finished              => "Finished",
        _                                     => ""
    };

    public string RoleText => Role switch
    {
        TournamentRole.Organiser => "Organiser",
        TournamentRole.Fencer    => "Viewer",
        _                        => "Viewer"
    };

    public bool ShowSetupHint => Tournament?.State == TournamentState.Setup;
    public bool CanManageRoster => CanEdit;

    /// <summary>Manual End button: organiser, bracket complete, not yet finished.</summary>
    public bool CanEndTournament =>
        _session.IsOrganiser &&
        Tournament is not null &&
        Tournament.State != TournamentState.Finished &&
        Tournament.Bracket is not null &&
        TournamentEngine.IsBracketComplete(Tournament.Bracket);

    /// <summary>Reopen button: organiser and the tournament is already finished.</summary>
    public bool CanReopenTournament =>
        _session.IsOrganiser &&
        Tournament?.State == TournamentState.Finished;

    /// <summary>Live-withdraw button: organiser, tournament is running (not Setup, not Finished).</summary>
    public bool CanWithdrawFencer =>
        _session.IsOrganiser &&
        Tournament is not null &&
        Tournament.State is not TournamentState.Setup and not TournamentState.Finished &&
        Tournament.Fencers.Any(f => !f.IsWithdrawn);

    public TournamentHubViewModel(TournamentSession session,
                                  IGoogleSheetsService sheets,
                                  PoolsTabViewModel poolsVm,
                                  PoolStandingsTabViewModel poolStandingsVm,
                                  ElimTabViewModel elimVm,
                                  FinalStandingsTabViewModel finalStandingsVm)
    {
        _session          = session;
        _sheets           = sheets;
        PoolsVm           = poolsVm;
        PoolStandingsVm   = poolStandingsVm;
        ElimVm            = elimVm;
        FinalStandingsVm  = finalStandingsVm;

        Tabs = new[]
        {
            new TournamentHubTab(TabPools,          this),
            new TournamentHubTab(TabPoolStandings,  this),
            new TournamentHubTab(TabElimination,    this),
            new TournamentHubTab(TabFinalStandings, this),
        };
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            PoolsVm.AttachTo(_session);
            await PoolsVm.LoadAsync();

            PoolStandingsVm.AttachTo(_session);
            PoolStandingsVm.Recompute();

            ElimVm.AttachTo(_session);
            ElimVm.Recompute();

            FinalStandingsVm.AttachTo(_session);
            FinalStandingsVm.Recompute();

            RaiseHeaderPropertiesChanged();
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task EndTournamentAsync()
    {
        if (!CanEndTournament || _session.Current is null) return;
        var t = _session.Current;

        IsLoading = true;
        ErrorMessage = "";
        try
        {
            var order = TournamentEngine.ComputeFinalStandings(t);
            await _sheets.SaveFinalStandingsAsync(t.Id, order);
            t.FinalStandingFencerIds = order;

            t.State = TournamentState.Finished;
            await _sheets.UpsertTournamentHeaderAsync(t);

            FinalStandingsVm.Recompute();
            RaiseHeaderPropertiesChanged();
        }
        catch (Exception ex) { ErrorMessage = $"End failed: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task ReopenTournamentAsync()
    {
        if (!CanReopenTournament || _session.Current is null) return;
        var t = _session.Current;

        IsLoading = true;
        ErrorMessage = "";
        try
        {
            t.State = t.Bracket is not null
                ? TournamentState.EliminationInProgress
                : TournamentState.PoolsClosed;
            await _sheets.UpsertTournamentHeaderAsync(t);

            RaiseHeaderPropertiesChanged();
        }
        catch (Exception ex) { ErrorMessage = $"Reopen failed: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    /// <summary>
    /// Prompts the page for a fencer, then walks-over every unfinished match they're
    /// in (0–0, opponent wins). Refreshes every tab so the cascade is visible.
    /// </summary>
    [RelayCommand]
    private async Task WithdrawFencerPromptAsync()
    {
        if (!CanWithdrawFencer || PickFencerToWithdrawAsync is null || _session.Current is null) return;

        var candidates = _session.Current.Fencers
            .Where(f => !f.IsWithdrawn)
            .OrderBy(f => f.Name)
            .ToList();
        if (candidates.Count == 0) return;

        var picked = await PickFencerToWithdrawAsync.Invoke(candidates);
        if (picked is null) return;

        await WithdrawFencerAsync(picked);
    }

    /// <summary>Performs the actual withdraw + persist + refresh.</summary>
    public async Task WithdrawFencerAsync(TournamentFencer fencer)
    {
        var t = _session.Current;
        if (t is null || fencer is null) return;
        if (t.State is TournamentState.Setup or TournamentState.Finished) return;

        IsLoading = true;
        ErrorMessage = "";
        try
        {
            fencer.IsWithdrawn = true;
            await _sheets.UpsertTournamentFencerAsync(t.Id, fencer);

            var cascade = TournamentEngine.ApplyWithdrawalCascade(t, fencer.Id);
            foreach (var m in cascade.ChangedPoolMatches)
                await _sheets.UpsertMatchAsync(t.Id, m);
            foreach (var m in cascade.ChangedBracketMatches)
                await _sheets.UpsertMatchAsync(t.Id, m);

            // Refresh every read-model so the new walkovers and propagation are visible.
            PoolsVm.RefreshAfterExternalChange();
            PoolStandingsVm.Recompute();
            ElimVm.Recompute();
            FinalStandingsVm.Recompute();
            RaiseHeaderPropertiesChanged();
        }
        catch (Exception ex) { ErrorMessage = $"Withdraw failed: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    public void RaiseHeaderPropertiesChanged()
    {
        OnPropertyChanged(nameof(Tournament));
        OnPropertyChanged(nameof(TournamentName));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(RoleText));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanManageRoster));
        OnPropertyChanged(nameof(ShowSetupHint));
        OnPropertyChanged(nameof(CanEndTournament));
        OnPropertyChanged(nameof(CanReopenTournament));
        OnPropertyChanged(nameof(CanWithdrawFencer));
    }
}

/// <summary>Carousel item; <see cref="Vm"/> exposes the shared hub VM so child bindings can reach it.</summary>
public sealed class TournamentHubTab
{
    public string Key { get; }
    public TournamentHubViewModel Vm { get; }
    public TournamentHubTab(string key, TournamentHubViewModel vm) { Key = key; Vm = vm; }
}