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

    /// <summary>
    /// Serialises every fire-and-forget backend write started by this VM so the
    /// sheet sees them in the order the user triggered them. Without this, two
    /// quick taps on the same pool can race and trip <see cref="ConcurrencyConflictException"/>.
    /// </summary>
    private readonly SemaphoreSlim _backgroundQueue = new(1, 1);

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

    /// <summary>Restart button: visible when the tournament has already started (not Setup, not new).</summary>
    public bool CanRestart => IsExisting && !IsSetupState;

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

            // Persist any roster the user entered before saving — one HTTP call.
            for (int i = 0; i < Fencers.Count; i++)
                Fencers[i].Fencer.OrderIndex = i;
            await _sheets.AppendTournamentFencersAsync(
                Tournament.Id,
                Fencers.Select(r => r.Fencer).ToList());

            _isInitialSave = false;
            _cache.InvalidateTournaments();
            RebuildDraftPools();
            NotifyStateChanged();
        }
        catch (Exception ex) { ErrorMessage = $"Save failed: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    // -------- Roster operations --------

    /// <summary>
    /// Optimistic add: shows the new fencer instantly and persists it in the
    /// background. Failures surface in the error banner.
    /// </summary>
    [RelayCommand]
    public Task AddFencerAsync()
    {
        if (Tournament is null || !CanAddFencers) return Task.CompletedTask;
        var n = (NewFencerName ?? "").Trim();
        if (n.Length == 0) return Task.CompletedTask;

        // Prevent duplicate names (case-insensitive)
        if (Fencers.Any(f => string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorMessage = $"A fencer named '{n}' is already on the roster.";
            return Task.CompletedTask;
        }

        var fencer = new TournamentFencer { Name = n, OrderIndex = Fencers.Count };
        Tournament.Fencers.Add(fencer);
        Fencers.Add(new TournamentFencerRow(fencer));
        NewFencerName = "";

        // UI first.
        RebuildDraftPools();
        NotifyStateChanged();

        // Persist in background (skip if we haven't saved the tournament yet —
        // SaveNewAsync will upsert the whole roster then).
        if (!_isInitialSave)
        {
            var tournamentId = Tournament.Id;
            RunInBackground(
                () => _sheets.UpsertTournamentFencerAsync(tournamentId, fencer),
                "Add failed");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Optimistic remove: the fencer disappears from the roster + draft pools
    /// instantly; the delete + per-pool upserts run in the background.
    /// </summary>
    [RelayCommand]
    public Task RemoveFencerAsync(TournamentFencerRow row)
    {
        if (Tournament is null || row is null || !CanRemoveFencers) return Task.CompletedTask;

        Fencers.Remove(row);
        Tournament.Fencers.Remove(row.Fencer);

        // Drop them from any draft pool they were placed in.
        var affectedPools = new List<Pool>();
        foreach (var pool in Tournament.Pools)
            if (pool.FencerIds.Remove(row.Fencer.Id))
                affectedPools.Add(pool);

        RebuildDraftPools();
        NotifyStateChanged();

        if (!_isInitialSave)
        {
            var tournamentId = Tournament.Id;
            var fencerId = row.Fencer.Id;
            var poolSnapshot = affectedPools.ToList();
            RunInBackground(async () =>
            {
                await _sheets.DeleteTournamentFencerAsync(tournamentId, fencerId);
                foreach (var pool in poolSnapshot)
                    await _sheets.UpsertPoolAsync(tournamentId, pool);
            }, "Remove failed");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Mark a fencer as withdrawn. Behaviour depends on tournament state:
    /// • Setup        — just flip the flag; the fencer can't be picked into pools.
    /// • Pools / Elim — flip the flag AND walk-over every unfinished match they're in
    ///                  (opponent wins 0–0). Already-finished matches are kept as-is.
    /// UI flips immediately; the backend cascade runs in the background, with all
    /// per-match upserts issued in parallel.
    /// </summary>
    [RelayCommand]
    public Task WithdrawFencerAsync(TournamentFencerRow row)
    {
        if (Tournament is null || row is null || _isInitialSave) return Task.CompletedTask;

        bool willBeWithdrawn = !row.Fencer.IsWithdrawn;
        row.Fencer.IsWithdrawn = willBeWithdrawn;
        row.RaiseStatusChanged();

        // Compute the cascade synchronously so the model is consistent before we
        // hand the changed Match list off to the background queue.
        TournamentEngine.WithdrawalCascade? cascade = null;
        if (willBeWithdrawn && Tournament.State is not TournamentState.Setup and not TournamentState.Finished)
            cascade = TournamentEngine.ApplyWithdrawalCascade(Tournament, row.Fencer.Id);

        if (IsSetupState) RebuildDraftPools();
        NotifyStateChanged();

        var tournamentId = Tournament.Id;
        var fencer = row.Fencer;
        var allChangedMatches = (cascade?.ChangedPoolMatches ?? Enumerable.Empty<Match>())
            .Concat(cascade?.ChangedBracketMatches ?? Enumerable.Empty<Match>())
            .ToList();

        RunInBackground(async () =>
        {
            await _sheets.UpsertTournamentFencerAsync(tournamentId, fencer);

            // All match upserts run in parallel — distinct Version tokens, no
            // contention. Cuts the cascade from O(N) round-trips serial to ~1
            // round-trip's worth of latency in practice.
            if (allChangedMatches.Count > 0)
                await Task.WhenAll(allChangedMatches.Select(m =>
                    _sheets.UpsertMatchAsync(tournamentId, m)));
        }, "Update failed");

        return Task.CompletedTask;
    }

    // -------- Pool allocation (Setup state only) --------

    /// <summary>Auto-distribute every active, unassigned fencer into draft pools.</summary>
    [RelayCommand]
    public Task AutoDistributePoolsAsync()
    {
        if (Tournament is null || !IsSetupState) return Task.CompletedTask;
        ErrorMessage = "";

        var active = Fencers.Where(f => !f.Fencer.IsWithdrawn).Select(f => f.Fencer).ToList();
        if (active.Count < MinFencersToStart)
        {
            ErrorMessage = $"Need at least {MinFencersToStart} active fencers to build pools.";
            return Task.CompletedTask;
        }

        var draft = TournamentEngine.BuildDraftPools(active, new Random());
        var toPersist = ApplyPoolReplacementLocal(draft);
        RebuildDraftPools();
        NotifyStateChanged();

        var tournamentId = Tournament.Id;
        var snapshot = toPersist.ToList();
        RunInBackground(async () =>
        {
            foreach (var pool in snapshot)
                await _sheets.UpsertPoolAsync(tournamentId, pool);
        }, "Auto-distribute failed");

        return Task.CompletedTask;
    }

    /// <summary>Remove all fencers from all pools and remove the empty pools, moving everyone to unassigned.</summary>
    [RelayCommand]
    public Task UnassignAllFencersAsync()
    {
        if (Tournament is null || !IsSetupState) return Task.CompletedTask;
        ErrorMessage = "";

        var affected = new List<Pool>();
        foreach (var pool in Tournament.Pools)
        {
            if (pool.FencerIds.Count > 0)
            {
                pool.FencerIds.Clear();
                affected.Add(pool);
            }
        }

        // Also remove all visible (now-empty) pools so the UI is clean
        var toPersist = ApplyPoolReplacementLocal(new List<Pool>());

        RebuildDraftPools();
        NotifyStateChanged();

        if ((affected.Count > 0 || toPersist.Count > 0) && !_isInitialSave)
        {
            var tournamentId = Tournament.Id;
            var snapshot = toPersist.ToList();
            RunInBackground(async () =>
            {
                foreach (var pool in snapshot)
                    await _sheets.UpsertPoolAsync(tournamentId, pool);
            }, "Unassign all failed");
        }

        return Task.CompletedTask;
    }

    /// <summary>Add one more empty pool to the draft.</summary>
    [RelayCommand]
    public Task AddPoolAsync()
    {
        if (Tournament is null || !IsSetupState) return Task.CompletedTask;
        ErrorMessage = "";

        var pools = GetVisiblePools();
        pools.Add(new Pool { Index = pools.Count });

        var toPersist = ApplyPoolReplacementLocal(pools);
        RebuildDraftPools();
        NotifyStateChanged();

        var tournamentId = Tournament.Id;
        var snapshot = toPersist.ToList();
        RunInBackground(async () =>
        {
            foreach (var pool in snapshot)
                await _sheets.UpsertPoolAsync(tournamentId, pool);
        }, "Add pool failed");

        return Task.CompletedTask;
    }

    /// <summary>Remove the given pool; its fencers fall back to the unassigned list.</summary>
    [RelayCommand]
    public Task RemovePoolAsync(EditorPoolVm poolVm)
    {
        if (Tournament is null || poolVm is null || !IsSetupState) return Task.CompletedTask;
        ErrorMessage = "";

        var pools = GetVisiblePools()
            .Where(p => p.Id != poolVm.Pool.Id)
            .ToList();

        // Re-index so they're contiguous 0..n-1.
        for (int i = 0; i < pools.Count; i++) pools[i].Index = i;

        var toPersist = ApplyPoolReplacementLocal(pools);
        RebuildDraftPools();
        NotifyStateChanged();

        var tournamentId = Tournament.Id;
        var snapshot = toPersist.ToList();
        RunInBackground(async () =>
        {
            foreach (var pool in snapshot)
                await _sheets.UpsertPoolAsync(tournamentId, pool);
        }, "Remove pool failed");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Move a fencer to <paramref name="targetPoolId"/> (empty string = unassigned).
    /// </summary>
    public Task MoveFencerToPoolAsync(string fencerId, string targetPoolId)
    {
        if (Tournament is null || !IsSetupState || string.IsNullOrEmpty(fencerId)) return Task.CompletedTask;
        ErrorMessage = "";

        var affected = new List<Pool>();
        foreach (var pool in Tournament.Pools)
        {
            bool removed = pool.FencerIds.Remove(fencerId);
            if (removed) affected.Add(pool);
        }

        if (!string.IsNullOrEmpty(targetPoolId))
        {
            var target = Tournament.Pools.FirstOrDefault(p => p.Id == targetPoolId);
            if (target is not null && !target.FencerIds.Contains(fencerId))
            {
                target.FencerIds.Add(fencerId);
                if (!affected.Contains(target)) affected.Add(target);
            }
        }

        // UI first — the chip jumps to its new card instantly.
        RebuildDraftPools();
        NotifyStateChanged();

        if (affected.Count > 0)
        {
            var tournamentId = Tournament.Id;
            var snapshot = affected.ToList();
            RunInBackground(async () =>
            {
                foreach (var pool in snapshot)
                    await _sheets.UpsertPoolAsync(tournamentId, pool);
            }, "Move failed");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns the currently-visible pools (contiguous Index 0..n-1), excluding
    /// leftover pools that were previously removed and still exist in the sheet
    /// with cleared FencerIds. This is the set the user sees and interacts with.
    /// </summary>
    private List<Pool> GetVisiblePools()
    {
        if (Tournament is null) return new List<Pool>();

        var sorted = Tournament.Pools.OrderBy(p => p.Index).ToList();
        var visible = new List<Pool>();
        for (int i = 0; i < sorted.Count; i++)
        {
            if (sorted[i].Index != i) break;
            visible.Add(sorted[i]);
        }
        return visible;
    }

    /// <summary>
    /// Synchronous half of the old <c>ReplacePoolsAsync</c>: mutate
    /// <see cref="Tournament.Pools"/> to reflect the new draft set and return the
    /// list of pools the caller must upsert (in any order — the background queue
    /// serialises them).
    ///
    /// Pools that the new set drops keep their backend row but get their
    /// <see cref="Pool.FencerIds"/> cleared, so the sheet stops associating
    /// fencers with a pool the user just removed.
    /// </summary>
    private List<Pool> ApplyPoolReplacementLocal(IList<Pool> newPools)
    {
        if (Tournament is null) return new List<Pool>();

        var keptIds = new HashSet<string>(newPools.Select(p => p.Id), StringComparer.Ordinal);
        var toUpsert = new List<Pool>();

        foreach (var old in Tournament.Pools)
        {
            if (!keptIds.Contains(old.Id))
            {
                old.FencerIds.Clear();
                // Push the leftover's index far out of the contiguous range so
                // RebuildDraftPools / GetVisiblePools won't pick it up.
                old.Index = int.MaxValue;
                toUpsert.Add(old);
            }
        }

        for (int i = 0; i < newPools.Count; i++)
        {
            newPools[i].Index = i;
            toUpsert.Add(newPools[i]);
        }

        var local = new List<Pool>(newPools);
        foreach (var old in Tournament.Pools)
            if (!keptIds.Contains(old.Id))
                local.Add(old);
        Tournament.Pools = local;

        return toUpsert;
    }

    /// <summary>
    /// Fire-and-forget helper. Captures exceptions and surfaces them via
    /// <see cref="ErrorMessage"/> on the UI thread. Writes are serialised by
    /// <see cref="_backgroundQueue"/> so two quick taps can't race.
    /// </summary>
    private void RunInBackground(Func<Task> work, string errorPrefix)
    {
        _ = Task.Run(async () =>
        {
            await _backgroundQueue.WaitAsync().ConfigureAwait(false);
            try
            {
                await work().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                MainThread.BeginInvokeOnMainThread(() => ErrorMessage = $"{errorPrefix}: {ex.Message}");
            }
            finally
            {
                _backgroundQueue.Release();
            }
        });
    }

    /// <summary>Rebuild the <see cref="DraftPools"/> + unassigned view from the model.</summary>
    private void RebuildDraftPools()
    {
        DraftPools.Clear();
        if (Tournament is null || !IsSetupState) { OnPropertyChanged(nameof(UnassignedFencers)); return; }

        // Deduplicate by ID in case in-memory store has dupes from by-reference storage
        var nameById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in Tournament.Fencers)
            nameById[f.Id] = f.Name;

        // Only show pools that have non-empty content OR have never been emptied
        // (i.e. those the user is currently editing). The "hidden" empty pools
        // from ApplyPoolReplacementLocal stay out of the UI but remain Ids we can reuse.
        var visiblePools = Tournament.Pools
            .OrderBy(p => p.Index)
            .Where(p => p.FencerIds.Count > 0 || ContainsId(Tournament.Pools, p.Id))
            .ToList();

        // Heuristic: a pool is "visible" if it's in the current top-level set
        // (i.e. its Index is contiguous from 0..n-1 in OrderBy). Otherwise it's
        // a leftover from ApplyPoolReplacementLocal. We just take the first
        // PoolCount by ordering and trust the caller to maintain contiguous indexes.
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
            // Flush any in-flight background writes (Move / Add pool / etc.) so the
            // sheet reflects the latest draft before we generate matches from it.
            await _backgroundQueue.WaitAsync();
            _backgroundQueue.Release();

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

            // NOTE: Do NOT call _cache.InvalidateTournaments() here.
            // UpsertTournamentHeaderAsync already placed the fully-hydrated
            // tournament (pools WITH matches) into the cache. Invalidating would
            // force the Hub page to re-fetch from the sheet, which can race against
            // the Sheets API's write-propagation and return pools without matches.
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

    /// <summary>
    /// Resets the tournament back to Setup state: deletes all matches, clears all
    /// pools, removes bracket and final standings, and reinstates all withdrawn
    /// fencers. After this the user can add/remove fencers and reassign pools.
    /// </summary>
    [RelayCommand]
    public async Task RestartTournamentAsync()
    {
        if (Tournament is null || _isInitialSave || IsSetupState) return;

        IsLoading = true;
        ErrorMessage = "";
        try
        {
            var tournamentId = Tournament.Id;

            // 1. Delete all matches (pool + bracket) — snapshot first to avoid enumeration issues
            var allMatches = Tournament.Pools.SelectMany(p => p.Matches).ToList();
            if (Tournament.Bracket is not null)
            {
                allMatches.AddRange(Tournament.Bracket.Rounds.SelectMany(r => r.Matches));
                if (Tournament.Bracket.BronzeMatch is not null)
                    allMatches.Add(Tournament.Bracket.BronzeMatch);
            }
            foreach (var m in allMatches)
                await _sheets.DeleteMatchAsync(tournamentId, m.Id);

            // 2. Clear final standings
            await _sheets.SaveFinalStandingsAsync(tournamentId, Array.Empty<string>());

            // 3. Clear all pool memberships — snapshot the list to avoid modifying during enumeration
            var poolsSnapshot = Tournament.Pools.ToList();
            foreach (var pool in poolsSnapshot)
            {
                pool.FencerIds.Clear();
                pool.Matches.Clear();
                pool.IsClosed = false;
                pool.Index = int.MaxValue;
                await _sheets.UpsertPoolAsync(tournamentId, pool);
            }

            // 4. Reinstate all withdrawn fencers — snapshot the list
            var fencersSnapshot = Tournament.Fencers.ToList();
            foreach (var fencer in fencersSnapshot)
            {
                if (fencer.IsWithdrawn)
                {
                    fencer.IsWithdrawn = false;
                    await _sheets.UpsertTournamentFencerAsync(tournamentId, fencer);
                }
            }

            // 5. Reset in-memory state
            Tournament.Pools = new List<Pool>();
            Tournament.Bracket = null;
            Tournament.FinalStandingFencerIds = new List<string>();
            Tournament.State = TournamentState.Setup;
            await _sheets.UpsertTournamentHeaderAsync(Tournament);

            // 6. Rebuild the editor UI
            _suppressAutoSave = true;
            Fencers.Clear();
            foreach (var f in Tournament.Fencers.OrderBy(f => f.OrderIndex))
                Fencers.Add(new TournamentFencerRow(f));
            _suppressAutoSave = false;

            _cache.InvalidateTournaments();
            RebuildDraftPools();
            NotifyStateChanged();
        }
        catch (Exception ex) { ErrorMessage = $"Restart failed: {ex.Message}"; }
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
        OnPropertyChanged(nameof(CanRestart));
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