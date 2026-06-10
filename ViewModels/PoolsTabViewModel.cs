using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class PoolsTabViewModel : ObservableObject, IDisposable
{
    private readonly IGoogleSheetsService _sheets;
    private readonly TournamentRefreshService _refresh;
    private readonly TournamentAutoSaveService _autoSave;
    private TournamentSession? _session;

    public ObservableCollection<PoolRowVm> Pools { get; } = new();

    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string errorMessage = "";

    public bool CanEdit => _session?.CanEdit ?? false;
    public bool HasNoPools => Pools.Count == 0;

    /// <summary>Raised when a row is tapped so the page can push the MatchPage.</summary>
    public event Action<PoolMatchRowVm>? MatchSelected;

    public PoolsTabViewModel(IGoogleSheetsService sheets,
                             TournamentRefreshService refresh,
                             TournamentAutoSaveService autoSave)
    {
        _sheets = sheets;
        _refresh = refresh;
        _autoSave = autoSave;
        _refresh.MatchUpdated += OnRemoteMatchUpdated;
    }

    public void AttachTo(TournamentSession session)
    {
        _session = session;
        OnPropertyChanged(nameof(CanEdit));
    }

    public async Task LoadAsync()
    {
        if (_session?.Current is null) return;
        IsLoading = true;
        ErrorMessage = "";
        try
        {
            // Always re-fetch the aggregate so we see other judges' state on entry.
            var fresh = await _sheets.GetTournamentAsync(_session.Current.Id);
            if (fresh is null) { ErrorMessage = "Tournament not found."; return; }

            // Swap in the fresh data — keep the same Tournament reference for session continuity.
            var t = _session.Current;
            t.Pools = fresh.Pools;
            t.Bracket = fresh.Bracket;
            t.FinalStandingFencerIds = fresh.FinalStandingFencerIds;
            t.Fencers = fresh.Fencers;
            t.State = fresh.State;
            t.Version = fresh.Version;

            BuildRows();

            // Begin polling. Prime so the first tick doesn't fire false-positive events.
            var allMatches = t.Pools.SelectMany(p => p.Matches).ToList();
            _refresh.Start(t.Id);
            _refresh.Prime(allMatches);
        }
        catch (Exception ex) { ErrorMessage = $"Load failed: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    public void StopPolling() => _refresh.Stop();

    private void BuildRows()
    {
        // Remember which pools the user had expanded so we can restore the
        // state after rebuilding (e.g. when returning from the Match page).
        var previouslyExpanded = Pools
            .Where(p => p.IsExpanded)
            .Select(p => p.Pool.Id)
            .ToHashSet();

        Pools.Clear();
        if (_session?.Current is null) return;

        var nameById       = _session.Current.Fencers.ToDictionary(f => f.Id, f => f.Name);
        var canEdit        = _session.CanEdit;
        var bracketStarted = _session.Current.Bracket is not null;
        foreach (var pool in _session.Current.Pools
                                .Where(p => p.FencerIds.Count > 0)
                                .OrderBy(p => p.Index))
        {
            // Roster shown at the top of the pool card, in seeded order.
            var fencerNames = pool.FencerIds
                .Select(id => nameById.TryGetValue(id, out var n) ? n : "?")
                .ToList();

            var row = new PoolRowVm(pool, fencerNames, canEdit, bracketStarted);
            for (int i = 0; i < pool.Matches.Count; i++)
            {
                var m = pool.Matches[i];
                row.Matches.Add(new PoolMatchRowVm(
                    m, i,
                    nameById.TryGetValue(m.LeftFencerId,  out var l) ? l : "?",
                    nameById.TryGetValue(m.RightFencerId, out var r) ? r : "?"));
            }
            row.RaiseProgressChanged();

            // Restore the previous expanded state for this pool, if any.
            if (previouslyExpanded.Contains(pool.Id))
                row.IsExpanded = true;

            Pools.Add(row);
        }
        OnPropertyChanged(nameof(HasNoPools));
    }

    private void OnRemoteMatchUpdated(Match remote)
    {
        // Polling timer is on a background thread; marshal to UI.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var pool in Pools)
            {
                var row = pool.Matches.FirstOrDefault(r => r.Match.Id == remote.Id);
                if (row is null) continue;

                row.Patch(remote);

                // Also patch the underlying Match in the session aggregate, so views built later are fresh.
                var poolMatches = _session?.Current?.Pools.FirstOrDefault(p => p.Id == pool.Pool.Id)?.Matches;
                if (poolMatches is not null)
                {
                    var idx = poolMatches.FindIndex(m => m.Id == remote.Id);
                    if (idx >= 0) poolMatches[idx] = remote;
                }

                pool.RaiseProgressChanged();
                return;
            }
        });
    }

    [RelayCommand]
    private void OpenMatch(PoolMatchRowVm row)
    {
        if (row is null) return;
        // Fencers entered for spectating only — they must not navigate into the match screen.
        if (_session is not null && !_session.CanOpenMatches) return;
        // Organisers and other viewers may still open a match (read-only); the MatchPage decides what to enable.
        MatchSelected?.Invoke(row);
    }

    [RelayCommand]
    private void ToggleExpanded(PoolRowVm pool)
    {
        if (pool is not null) pool.IsExpanded = !pool.IsExpanded;
    }

    [RelayCommand]
    private async Task TogglePoolClosedAsync(PoolRowVm pool)
    {
        if (pool is null || _session?.Current is null || !CanEdit) return;
        if (!pool.CanClose && !pool.CanReopen) return;

        // Defensive: once the bracket exists, reopening is forbidden even if
        // a stale UI state somehow let the button through.
        if (pool.Pool.IsClosed && _session.Current.Bracket is not null) return;

        var t = _session.Current;
        var target = t.Pools.FirstOrDefault(p => p.Id == pool.Pool.Id);
        if (target is null) return;

        var wasClosed = target.IsClosed;
        target.IsClosed = !wasClosed;

        try
        {
            await _sheets.UpsertPoolAsync(t.Id, target);
            pool.RaiseProgressChanged();
        }
        catch (ConcurrencyConflictException)
        {
            // Someone else changed the pool meanwhile — refetch and retry once.
            var fresh = (await _sheets.GetTournamentAsync(t.Id))?
                .Pools.FirstOrDefault(p => p.Id == pool.Pool.Id);
            if (fresh is not null)
            {
                fresh.IsClosed = !wasClosed;
                await _sheets.UpsertPoolAsync(t.Id, fresh);
                target.IsClosed = fresh.IsClosed;
                target.Version  = fresh.Version;
                pool.RaiseProgressChanged();
            }
        }
        catch (Exception ex) { ErrorMessage = $"Update failed: {ex.Message}"; }
    }

    /// <summary>
    /// Rebuild the pool/match rows from the in-memory session without re-fetching
    /// from the backend. Used by the hub after it mutated matches itself
    /// (e.g. the withdraw-cascade walkovers).
    /// </summary>
    public void RefreshAfterExternalChange()
    {
        BuildRows();
    }

    public void Dispose() => _refresh.MatchUpdated -= OnRemoteMatchUpdated;
}