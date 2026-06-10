using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class TournamentEditorVm : ObservableObject
{
    public const int MaxFencers = 128;
    public const int MinFencersToStart = 4;

    private readonly IGoogleSheetsService _sheets;
    private readonly TournamentAutoSaveService _autoSave;
    private readonly ICacheControl _cache;

    private bool _isInitialSave;
    private bool _suppressAutoSave;

    [ObservableProperty] private Tournament? tournament;
    [ObservableProperty] private string name = "";
    [ObservableProperty] private string password = "";
    [ObservableProperty] private string newFencerName = "";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isStarting;
    [ObservableProperty] private string errorMessage = "";

    public ObservableCollection<TournamentFencerRow> Fencers { get; } = new();

    /// <summary>Editor-time pool allocation cards (Setup state only).</summary>
    public ObservableCollection<EditorPoolVm> DraftPools { get; } = new();

    public bool IsNew => _isInitialSave;
    public bool IsExisting => !_isInitialSave && Tournament is not null;
    public bool IsSetupState => Tournament?.State == TournamentState.Setup;
    public bool CanAddFencers => IsSetupState && Fencers.Count < MaxFencers;
    public bool CanRemoveFencers => IsSetupState;
    public bool CanStart => IsExisting && IsSetupState && ActiveFencerCount >= MinFencersToStart;
    public int ActiveFencerCount => Fencers.Count(f => !f.IsWithdrawn);
    public string FencerCountText => $"{Fencers.Count}/{MaxFencers} fencers ({ActiveFencerCount} active)";
    public string StartHintText => ActiveFencerCount < MinFencersToStart
        ? $"Add at least {MinFencersToStart} fencers to start ({ActiveFencerCount}/{MinFencersToStart})."
        : "Ready to start. Each non-empty pool must have 4–8 fencers; empty pools are dropped automatically.";

    public bool ShowPoolAllocation => IsExisting && IsSetupState && ActiveFencerCount > 0;
    public bool ShowLiveWithdrawHint => IsExisting && !IsSetupState;

    public TournamentEditorVm(IGoogleSheetsService sheets,
                              TournamentAutoSaveService autoSave,
                              ICacheControl cache)
    {
        _sheets = sheets;
        _autoSave = autoSave;
        _cache = cache;
    }

    // -------- Initialisation --------

    public void InitNew()
    {
        _suppressAutoSave = true;
        _isInitialSave = true;
        Tournament = new Tournament { State = TournamentState.Setup, CreatedAt = DateTime.UtcNow };
        Name = "";
        Password = "";
        NewFencerName = "";
        ErrorMessage = "";
        Fencers.Clear();
        DraftPools.Clear();
        NotifyStateChanged();
        _suppressAutoSave = false;
    }

    public async Task InitExistingAsync(string tournamentId)
    {
        _suppressAutoSave = true;
        IsLoading = true;
        try
        {
            _isInitialSave = false;
            Tournament = await _sheets.GetTournamentAsync(tournamentId);
            if (Tournament is null) { ErrorMessage = "Tournament not found."; return; }

            Name = Tournament.Name;
            Password = Tournament.PasswordPlain;
            NewFencerName = "";
            ErrorMessage = "";
            Fencers.Clear();
            foreach (var f in Tournament.Fencers.OrderBy(f => f.OrderIndex))
                Fencers.Add(new TournamentFencerRow(f));

            RebuildDraftPools();
            NotifyStateChanged();
        }
        finally
        {
            IsLoading = false;
            _suppressAutoSave = false;
        }
    }

    // -------- Auto-save for header changes (existing tournaments only) --------

    partial void OnNameChanged(string value)
    {
        if (_suppressAutoSave || _isInitialSave || Tournament is null) return;
        Tournament.Name = (value ?? "").Trim();
        _autoSave.ScheduleTournament(Tournament, latest => latest.Name = Tournament.Name);
    }

    partial void OnPasswordChanged(string value)
    {
        if (_suppressAutoSave || _isInitialSave || Tournament is null) return;
        Tournament.PasswordPlain = (value ?? "").Trim();
        _autoSave.ScheduleTournament(Tournament, latest => latest.PasswordPlain = Tournament.PasswordPlain);
    }

    // -------- Initial save (new tournaments) --------

    [RelayCommand]
    public async Task SaveNewAsync()
    {
        if (Tournament is null || !_isInitialSave) return;

        if (string.IsNullOrWhiteSpace(Name))     { ErrorMessage = "Name is required."; return; }
        if (string.IsNullOrWhiteSpace(Password)) { ErrorMessage = "Password is required."; return; }

        ErrorMessage = "";
        IsLoading = true;
        try
        {
            Tournament.Name = Name.Trim();
            Tournament.PasswordPlain = Password.Trim();

            // Header first so we have a row to attach fencers to.
            await _sheets.UpsertTournamentHeaderAsync(Tournament);

            // Persist any roster the user entered before saving.
            for (int i = 0; i < Fencers.Count; i++)
            {
                Fencers[i].Fencer.OrderIndex = i;
                await _sheets.UpsertTournamentFencerAsync(Tournament.Id, Fencers[i].Fencer);
            }

            _isInitialSave = false;
            _cache.InvalidateTournaments();
            RebuildDraftPools();
            NotifyStateChanged();
        }
        catch (Exception ex) { ErrorMessage = $"Save failed: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    // -------- Roster operations --------

    [RelayCommand]
    public async Task AddFencerAsync()
    {
        if (Tournament is null || !CanAddFencers) return;
        var n = (NewFencerName ?? "").Trim();
        if (n.Length == 0) return;

        var fencer = new TournamentFencer { Name = n, OrderIndex = Fencers.Count };
        Fencers.Add(new TournamentFencerRow(fencer));
        NewFencerName = "";

        if (!_isInitialSave)
        {
            try { await _sheets.UpsertTournamentFencerAsync(Tournament.Id, fencer); }
            catch (Exception ex) { ErrorMessage = $"Add failed: {ex.Message}"; }
        }

        // New fencer is unassigned in the draft pools until the organiser places them.
        RebuildDraftPools();
        NotifyStateChanged();
    }

    [RelayCommand]
    public async Task RemoveFencerAsync(TournamentFencerRow row)
    {
        if (Tournament is null || row is null || !CanRemoveFencers) return;

        Fencers.Remove(row);

        // Drop them from any draft pool they were placed in.
        foreach (var pool in Tournament.Pools)
            pool.FencerIds.Remove(row.Fencer.Id);

        if (!_isInitialSave)
        {
            try
            {
                await _sheets.DeleteTournamentFencerAsync(Tournament.Id, row.Fencer.Id);
                // Persist the new pool memberships (the fencer might have been in one).
                foreach (var pool in Tournament.Pools)
                    await _sheets.UpsertPoolAsync(Tournament.Id, pool);
            }
            catch (Exception ex) { ErrorMessage = $"Remove failed: {ex.Message}"; }
        }

        RebuildDraftPools();
        NotifyStateChanged();
    }

    /// <summary>
    /// Mark a fencer as withdrawn. Behaviour depends on tournament state:
    /// • Setup        — just flip the flag; the fencer can't be picked into pools.
    /// • Pools / Elim — flip the flag AND walk-over every unfinished match they're in
    ///                  (opponent wins 0–0). Already-finished matches are kept as-is.
    /// </summary>
    [RelayCommand]
    public async Task WithdrawFencerAsync(TournamentFencerRow row)
    {
        if (Tournament is null || row is null || _isInitialSave) return;

        bool willBeWithdrawn = !row.Fencer.IsWithdrawn;
        row.Fencer.IsWithdrawn = willBeWithdrawn;
        row.RaiseStatusChanged();

        try
        {
            await _sheets.UpsertTournamentFencerAsync(Tournament.Id, row.Fencer);

            // Only cascade walkovers when WITHDRAWING in an active tournament.
            // Reinstating doesn't resurrect already-walked-over matches.
            if (willBeWithdrawn && Tournament.State is not TournamentState.Setup and not TournamentState.Finished)
            {
                var cascade = TournamentEngine.ApplyWithdrawalCascade(Tournament, row.Fencer.Id);
                foreach (var m in cascade.ChangedPoolMatches)
                    await _sheets.UpsertMatchAsync(Tournament.Id, m);
                foreach (var m in cascade.ChangedBracketMatches)
                    await _sheets.UpsertMatchAsync(Tournament.Id, m);
            }

            // In Setup, the unassigned-fencer panel reflects withdrawal too.
            if (IsSetupState) RebuildDraftPools();
            NotifyStateChanged();
        }
        catch (Exception ex) { ErrorMessage = $"Update failed: {ex.Message}"; }
    }

    // -------- Pool allocation (Setup state only) --------

    /// <summary>Auto-distribute every active, unassigned fencer into draft pools.</summary>
    [RelayCommand]
    public async Task AutoDistributePoolsAsync()
    {
        if (Tournament is null || !IsSetupState) return;
        ErrorMessage = "";

        var active = Fencers.Where(f => !f.Fencer.IsWithdrawn).Select(f => f.Fencer).ToList();
        if (active.Count < MinFencersToStart)
        {
            ErrorMessage = $"Need at least {MinFencersToStart} active fencers to build pools.";
            return;
        }

        try
        {
            var draft = TournamentEngine.BuildDraftPools(active, new Random());
            await ReplacePoolsAsync(draft);
            RebuildDraftPools();
            NotifyStateChanged();
        }
        catch (Exception ex) { ErrorMessage = $"Auto-distribute failed: {ex.Message}"; }
    }

    /// <summary>Add one more empty pool to the draft.</summary>
    [RelayCommand]
    public async Task AddPoolAsync()
    {
        if (Tournament is null || !IsSetupState) return;
        ErrorMessage = "";

        var pools = Tournament.Pools.OrderBy(p => p.Index).ToList();
        pools.Add(new Pool { Index = pools.Count });

        try { await ReplacePoolsAsync(pools); }
        catch (Exception ex) { ErrorMessage = $"Add pool failed: {ex.Message}"; return; }

        RebuildDraftPools();
        NotifyStateChanged();
    }

    /// <summary>Remove the given pool; its fencers fall back to the unassigned list.</summary>
    [RelayCommand]
    public async Task RemovePoolAsync(EditorPoolVm poolVm)
    {
        if (Tournament is null || poolVm is null || !IsSetupState) return;
        ErrorMessage = "";

        var pools = Tournament.Pools
            .Where(p => p.Id != poolVm.Pool.Id)
            .OrderBy(p => p.Index)
            .ToList();

        // Re-index so they're contiguous 0..n-1.
        for (int i = 0; i < pools.Count; i++) pools[i].Index = i;

        try { await ReplacePoolsAsync(pools); }
        catch (Exception ex) { ErrorMessage = $"Remove pool failed: {ex.Message}"; return; }

        RebuildDraftPools();
        NotifyStateChanged();
    }

    /// <summary>Move a fencer to <paramref name="targetPoolId"/> (empty string = unassigned).</summary>
    public async Task MoveFencerToPoolAsync(string fencerId, string targetPoolId)
    {
        if (Tournament is null || !IsSetupState || string.IsNullOrEmpty(fencerId)) return;
        ErrorMessage = "";

        var affected = new List<Pool>();
        foreach (var pool in Tournament.Pools)
        {
            bool removed = pool.FencerIds.Remove(fencerId);
            if (removed) affected.Add(pool);
        }

        Pool? target = null;
        if (!string.IsNullOrEmpty(targetPoolId))
        {
            target = Tournament.Pools.FirstOrDefault(p => p.Id == targetPoolId);
            if (target is not null && !target.FencerIds.Contains(fencerId))
            {
                target.FencerIds.Add(fencerId);
                if (!affected.Contains(target)) affected.Add(target);
            }
        }

        try
        {
            foreach (var pool in affected)
                await _sheets.UpsertPoolAsync(Tournament.Id, pool);
        }
        catch (Exception ex) { ErrorMessage = $"Move failed: {ex.Message}"; }

        RebuildDraftPools();
        NotifyStateChanged();
    }

    /// <summary>
    /// Persist a brand-new pool set, deleting every existing pool first. Used by
    /// auto-distribute and add/remove pool. Matches are NOT generated here — that
    /// only happens on Start.
    /// </summary>
    private async Task ReplacePoolsAsync(IList<Pool> newPools)
    {
        if (Tournament is null) return;

        // Wipe existing pools server-side. There's no "delete pool" API, but
        // UpsertPoolAsync overwriting + clearing FencerIds isn't enough; deleting
        // the whole tournament's child rows would be too much. We compromise:
        //  - For pools that survive, upsert them.
        //  - For pools that disappear, clear their FencerIds and upsert so they
        //    stop hoarding fencers; the organiser can fully remove via "− Pool".
        var keptIds = new HashSet<string>(newPools.Select(p => p.Id), StringComparer.Ordinal);

        foreach (var old in Tournament.Pools)
        {
            if (!keptIds.Contains(old.Id))
            {
                old.FencerIds.Clear();
                await _sheets.UpsertPoolAsync(Tournament.Id, old);
            }
        }

        // Upsert the surviving / new pools.
        for (int i = 0; i < newPools.Count; i++)
        {
            newPools[i].Index = i;
            await _sheets.UpsertPoolAsync(Tournament.Id, newPools[i]);
        }

        // Refresh local list — combine new pools with the "stale" hidden ones
        // (now empty) so re-adding a fresh pool gets a fresh Id.
        var local = new List<Pool>(newPools);
        foreach (var old in Tournament.Pools)
            if (!keptIds.Contains(old.Id))
                local.Add(old);
        Tournament.Pools = local;
    }

    /// <summary>Rebuild the <see cref="DraftPools"/> + unassigned view from the model.</summary>
    private void RebuildDraftPools()
    {
        DraftPools.Clear();
        if (Tournament is null || !IsSetupState) { OnPropertyChanged(nameof(UnassignedFencers)); return; }

        var nameById = Tournament.Fencers.ToDictionary(f => f.Id, f => f.Name);

        // Only show pools that have non-empty content OR have never been emptied
        // (i.e. those the user is currently editing). The "hidden" empty pools
        // from ReplacePoolsAsync stay out of the UI but remain Ids we can reuse.
        var visiblePools = Tournament.Pools
            .OrderBy(p => p.Index)
            .Where(p => p.FencerIds.Count > 0 || ContainsId(Tournament.Pools, p.Id))
            .ToList();

        // Heuristic: a pool is "visible" if it's in the current top-level set
        // (i.e. its Index is contiguous from 0..n-1 in OrderBy). Otherwise it's
        // a leftover from ReplacePoolsAsync. We just take the first PoolCount
        // by ordering and trust the caller to maintain contiguous indexes.
        var contiguous = Tournament.Pools.OrderBy(p => p.Index).ToList();
        int validCount = 0;
        for (int i = 0; i < contiguous.Count; i++)
        {
            if (contiguous[i].Index != i) break;
            validCount++;
        }
        var shown = contiguous.Take(validCount).ToList();

        foreach (var pool in shown)
        {
            var rows = pool.FencerIds
                .Select(id => new EditorPoolFencerVm(id, nameById.TryGetValue(id, out var n) ? n : "?"))
                .ToList();
            DraftPools.Add(new EditorPoolVm(pool, rows));
        }

        OnPropertyChanged(nameof(UnassignedFencers));
        OnPropertyChanged(nameof(HasUnassignedFencers));
        OnPropertyChanged(nameof(ShowPoolAllocation));
    }

    private static bool ContainsId(IEnumerable<Pool> pools, string id) =>
        pools.Any(p => p.Id == id);

    /// <summary>Active fencers not currently placed in any draft pool.</summary>
    public IReadOnlyList<EditorPoolFencerVm> UnassignedFencers
    {
        get
        {
            if (Tournament is null) return Array.Empty<EditorPoolFencerVm>();
            var assigned = new HashSet<string>(
                Tournament.Pools.SelectMany(p => p.FencerIds),
                StringComparer.Ordinal);
            return Fencers
                .Where(f => !f.Fencer.IsWithdrawn && !assigned.Contains(f.Fencer.Id))
                .Select(f => new EditorPoolFencerVm(f.Fencer.Id, f.Fencer.Name))
                .ToList();
        }
    }

    public bool HasUnassignedFencers => UnassignedFencers.Count > 0;

    // -------- Lifecycle --------

    [RelayCommand]
    public async Task StartTournamentAsync()
    {
        if (Tournament is null || !IsExisting || !IsSetupState) return;

        if (ActiveFencerCount < MinFencersToStart)
        {
            ErrorMessage =
                $"Cannot start: need at least {MinFencersToStart} active fencers " +
                $"(currently {ActiveFencerCount}).";
            return;
        }

        ErrorMessage = "";
        IsStarting = true;
        try
        {
            const int minPoolSize = 4;
            const int maxPoolSize = 8;

            // Empty pools are allowed during editing but never make it into the
            // running tournament — drop them silently before any validation.
            var draftPools = Tournament.Pools
                .Where(p => p.FencerIds.Count > 0)
                .OrderBy(p => p.Index)
                .ToList();

            List<Pool> pools;
            if (draftPools.Count > 0)
            {
                // Strict size rule: every non-empty pool must have 4..8 fencers.
                var tooSmall = draftPools.Where(p => p.FencerIds.Count < minPoolSize).ToList();
                var tooLarge = draftPools.Where(p => p.FencerIds.Count > maxPoolSize).ToList();
                if (tooSmall.Count > 0 || tooLarge.Count > 0)
                {
                    var parts = new List<string>();
                    if (tooSmall.Count > 0)
                        parts.Add($"too few fencers (need at least {minPoolSize}): " +
                                  string.Join(", ",
                                      tooSmall.Select(p => $"{p.Name} has {p.FencerIds.Count}")));
                    if (tooLarge.Count > 0)
                        parts.Add($"too many fencers (max {maxPoolSize}): " +
                                  string.Join(", ",
                                      tooLarge.Select(p => $"{p.Name} has {p.FencerIds.Count}")));
                    ErrorMessage = "Cannot start — " + string.Join("; ", parts) + ". ";
                    return;
                }

                // Every active fencer must be assigned to one of those pools.
                var assignedSet = new HashSet<string>(
                    draftPools.SelectMany(p => p.FencerIds), StringComparer.Ordinal);
                var unassigned = Fencers
                    .Where(f => !f.Fencer.IsWithdrawn && !assignedSet.Contains(f.Fencer.Id))
                    .Select(f => f.Fencer.Name)
                    .ToList();
                if (unassigned.Count > 0)
                {
                    ErrorMessage =
                        $"Cannot start: {unassigned.Count} active fencer(s) not assigned to any pool: " +
                        $"{string.Join(", ", unassigned)}.";
                    return;
                }

                // Re-index 0..n-1 because the empty pools that used to sit in
                // between were just dropped.
                for (int i = 0; i < draftPools.Count; i++) draftPools[i].Index = i;
                TournamentEngine.GeneratePoolMatches(draftPools);
                pools = draftPools;
            }
            else
            {
                // No draft at all — fall back to auto-build (PartitionIntoPools yields 4..6).
                var activeFencers = Fencers
                    .Where(r => !r.Fencer.IsWithdrawn)
                    .Select(r => r.Fencer)
                    .ToList();
                pools = TournamentEngine.BuildPools(activeFencers, new Random());
            }

            // Persist the pool memberships (existing rows), then bulk-append matches.
            foreach (var pool in pools)
                await _sheets.UpsertPoolAsync(Tournament.Id, pool);
            await _sheets.AppendMatchesAsync(Tournament.Id, pools.SelectMany(p => p.Matches).ToList());

            // Drop empty pools from the in-memory aggregate too so the hub doesn't
            // see them in its Pools tab. Old empty rows still live in the sheet but
            // the display VMs (PoolsTabViewModel, PoolStandingsTabViewModel) skip
            // any pool with no fencers.
            Tournament.Pools = pools;
            Tournament.State = TournamentState.PoolsInProgress;
            await _sheets.UpsertTournamentHeaderAsync(Tournament);

            _cache.InvalidateTournaments();
            NotifyStateChanged();
        }
        catch (Exception ex) { ErrorMessage = $"Start failed: {ex.Message}"; }
        finally { IsStarting = false; }
    }

    [RelayCommand]
    public async Task DeleteTournamentAsync()
    {
        if (Tournament is null || _isInitialSave) return;
        IsLoading = true;
        try
        {
            await _sheets.DeleteTournamentAsync(Tournament.Id);
            _cache.InvalidateTournaments();
        }
        catch (Exception ex) { ErrorMessage = $"Delete failed: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(IsExisting));
        OnPropertyChanged(nameof(IsSetupState));
        OnPropertyChanged(nameof(CanAddFencers));
        OnPropertyChanged(nameof(CanRemoveFencers));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(ActiveFencerCount));
        OnPropertyChanged(nameof(FencerCountText));
        OnPropertyChanged(nameof(StartHintText));
        OnPropertyChanged(nameof(ShowPoolAllocation));
        OnPropertyChanged(nameof(ShowLiveWithdrawHint));
        OnPropertyChanged(nameof(HasUnassignedFencers));
        OnPropertyChanged(nameof(UnassignedFencers));
    }
}

/// <summary>Editor-time view of one draft pool (no matches yet).</summary>
public sealed class EditorPoolVm
{
    public Pool Pool { get; }
    public string PoolId => Pool.Id;
    public string Title => Pool.Name;
    public IReadOnlyList<EditorPoolFencerVm> Members { get; }
    public string CountText => $"{Members.Count} fencer{(Members.Count == 1 ? "" : "s")}{(Members.Count == 0 ? " (empty)" : "")}";
    public bool IsEmpty => Members.Count == 0;

    public EditorPoolVm(Pool pool, IReadOnlyList<EditorPoolFencerVm> members)
    {
        Pool = pool;
        Members = members;
    }
}

/// <summary>One row inside the editor's pool / unassigned panels.</summary>
public sealed class EditorPoolFencerVm
{
    public string FencerId { get; }
    public string Name { get; }
    public EditorPoolFencerVm(string fencerId, string name)
    {
        FencerId = fencerId;
        Name = name;
    }
}