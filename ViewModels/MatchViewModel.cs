using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class MatchViewModel : ObservableObject, IDisposable
{
    private readonly IGoogleSheetsService _sheets;
    private readonly TournamentAutoSaveService _autoSave;
    private readonly TournamentRefreshService _refresh;
    private readonly TournamentSession _session;
    private readonly AuthService _auth;

    private IDispatcherTimer? _clockTimer;
    private IDispatcherTimer? _heartbeat;
    private bool _lockTakenOverHandled;
    private string _myUserId = "";

    public Match? Match { get; private set; }
    public string LeftName { get; private set; } = "";
    public string RightName { get; private set; } = "";
    public string PoolName { get; private set; } = "";

    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string errorMessage = "";
    [ObservableProperty] private bool isTimerRunning;
    [ObservableProperty] private string clockText = "02:00";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(EditOpacity))]
    [NotifyPropertyChangedFor(nameof(CanReopenMatch))]
    [NotifyPropertyChangedFor(nameof(CanLeftYellow))]
    [NotifyPropertyChangedFor(nameof(CanLeftRed))]
    [NotifyPropertyChangedFor(nameof(CanRightYellow))]
    [NotifyPropertyChangedFor(nameof(CanRightRed))]
    [NotifyPropertyChangedFor(nameof(CanRemoveLeftCard))]
    [NotifyPropertyChangedFor(nameof(CanRemoveRightCard))]
    [NotifyPropertyChangedFor(nameof(LeftUndoOpacity))]
    [NotifyPropertyChangedFor(nameof(RightUndoOpacity))]
    private bool isReadOnly;
    [ObservableProperty] private string lockHolderText = "";

    /// <summary>Raised when our lock has been taken over; the page should pop.</summary>
    public event Action? LockTakenOver;

    /// <summary>Raised so the page can confirm the take-over UI before we actually claim the lock.</summary>
    public Func<string /*otherUserId*/, Task<bool>>? ConfirmTakeOverAsync { get; set; }

    public bool CanEdit => !IsReadOnly && _session.CanEdit && Match?.Status != MatchStatus.Finished;
    public string Title => $"{PoolName} · {LeftName} vs {RightName}";

    /// <summary>Red when running (Stop), blue when stopped (Start).</summary>
    public Color TimerButtonColor => IsTimerRunning
        ? Color.FromArgb("#C62828")
        : Color.FromArgb("#476FB5");

    /// <summary>Undo button is always present for layout alignment; fade when no cards to undo.</summary>
    public double LeftUndoOpacity => CanRemoveLeftCard ? 1.0 : 0.25;
    public double RightUndoOpacity => CanRemoveRightCard ? 1.0 : 0.25;

    /// <summary>
    /// A finished match can be reopened for editing if:
    /// • Pool match: only if the elimination bracket hasn't been generated yet.
    /// • Elim match: only if the downstream match (next round) hasn't started yet
    ///   (i.e. still Pending). The winner's next match must not have any score/progress.
    /// </summary>
    public bool CanReopenMatch
    {
        get
        {
            if (!_session.CanEdit || Match?.Status != MatchStatus.Finished) return false;
            var t = _session.Current;
            if (t is null) return false;

            // Pool match: no bracket yet
            if (!Match.BracketRound.HasValue)
                return t.Bracket is null;

            // Elimination match: check if the downstream match is still Pending
            if (t.Bracket is null) return false;
            return IsDownstreamMatchPending(t.Bracket, Match);
        }
    }

    /// <summary>
    /// Returns true if the match in the next round that receives this match's winner
    /// is still Pending (hasn't started). Also checks bronze match if applicable.
    /// </summary>
    private static bool IsDownstreamMatchPending(EliminationBracket bracket, Match match)
    {
        if (!match.BracketRound.HasValue || !match.BracketSlot.HasValue) return false;

        int nextRoundIdx = match.BracketRound.Value + 1;

        // Check if the winner was propagated into the bronze match
        // (semi-final losers go to bronze)
        if (bracket.BronzeMatch is not null)
        {
            int semiRoundIdx = bracket.Rounds.Count - 2; // semi-finals are second-to-last
            if (match.BracketRound.Value == semiRoundIdx)
            {
                // This match's loser feeds into the bronze match
                if (bracket.BronzeMatch.Status != MatchStatus.Pending)
                    return false;
            }
        }

        // If this is the final round, there's no downstream — allow reopen
        if (nextRoundIdx >= bracket.Rounds.Count) return true;

        int nextSlot = match.BracketSlot.Value / 2;
        var nextRound = bracket.Rounds[nextRoundIdx];
        var downstream = nextRound.Matches.FirstOrDefault(m => m.BracketSlot == nextSlot);

        // If downstream doesn't exist or is still Pending, it's safe to reopen
        return downstream is null || downstream.Status == MatchStatus.Pending;
    }

    public int LeftScore   => Match?.LeftScore   ?? 0;
    public int RightScore  => Match?.RightScore  ?? 0;
    public int LeftYellow  => Match?.LeftYellowCards  ?? 0;
    public int LeftRed     => Match?.LeftRedCards     ?? 0;
    public int RightYellow => Match?.RightYellowCards ?? 0;
    public int RightRed    => Match?.RightRedCards    ?? 0;

    // ----- Card eligibility -----
    // Rule: a yellow may only be issued while the fencer has no yellow AND no red yet.
    //       A red may always be issued (while CanEdit) — it also implicitly blocks future yellows.
    public bool CanLeftYellow  => CanEdit && LeftYellow  == 0 && LeftRed  == 0;
    public bool CanLeftRed     => CanEdit;
    public bool CanRightYellow => CanEdit && RightYellow == 0 && RightRed == 0;
    public bool CanRightRed    => CanEdit;

    // ----- Card removal eligibility -----
    public bool CanRemoveLeftYellow  => CanEdit && LeftYellow  > 0;
    public bool CanRemoveLeftRed     => CanEdit && LeftRed     > 0;
    public bool CanRemoveRightYellow => CanEdit && RightYellow > 0;
    public bool CanRemoveRightRed    => CanEdit && RightRed    > 0;

    // ----- Card indicator visibility -----
    // If a fencer has a red card, show red only (hides yellow). Otherwise show yellow if present.
    public bool LeftShowRedIndicator    => LeftRed > 0;
    public bool LeftShowYellowIndicator => LeftYellow > 0 && LeftRed == 0;
    public bool LeftHasAnyCard          => LeftYellow > 0 || LeftRed > 0;
    public bool RightShowRedIndicator    => RightRed > 0;
    public bool RightShowYellowIndicator => RightYellow > 0 && RightRed == 0;
    public bool RightHasAnyCard          => RightYellow > 0 || RightRed > 0;

    // ----- Card count text (shown inside the indicator when multiple) -----
    public string LeftRedCountText     => LeftRed > 1 ? LeftRed.ToString() : "";
    public string RightRedCountText    => RightRed > 1 ? RightRed.ToString() : "";
    public bool LeftHasMultipleReds    => LeftRed > 1;
    public bool RightHasMultipleReds   => RightRed > 1;

    // ----- Card summary text (shown under score for full status) -----
    public string LeftCardSummary =>
        (LeftYellow, LeftRed) switch
        {
            (0, 0) => "",
            (> 0, 0) => "⚠ Yellow",
            (0, 1) => "🟥 Red",
            (0, > 1) => $"🟥 Red ×{LeftRed}",
            (> 0, 1) => "⚠ Yellow + 🟥 Red",
            _ => $"⚠ Yellow + 🟥 Red ×{LeftRed}"
        };

    public string RightCardSummary =>
        (RightYellow, RightRed) switch
        {
            (0, 0) => "",
            (> 0, 0) => "⚠ Yellow",
            (0, 1) => "🟥 Red",
            (0, > 1) => $"🟥 Red ×{RightRed}",
            (> 0, 1) => "⚠ Yellow + 🟥 Red",
            _ => $"⚠ Yellow + 🟥 Red ×{RightRed}"
        };

    public bool LeftHasCardSummary  => LeftHasAnyCard;
    public bool RightHasCardSummary => RightHasAnyCard;

    public string StatusBadge => Match?.Status switch
    {
        MatchStatus.Pending    => "Pending",
        MatchStatus.InProgress => "In progress",
        MatchStatus.Finished   => "Finished",
        _                      => ""
    };
    public string TimerButtonText => IsTimerRunning ? "Stop" : "Start";

    /// <summary>Visual opacity for controls that depend on CanEdit. 
    /// MAUI Android has a bug where Button.IsEnabled=false on first render 
    /// doesn't re-enable when the binding later becomes true. Using Opacity 
    /// instead lets taps through (commands guard with if(!CanEdit)), while 
    /// correctly showing the disabled visual state.</summary>
    public double EditOpacity => CanEdit ? 1.0 : 0.4;

    public MatchViewModel(IGoogleSheetsService sheets,
                          TournamentAutoSaveService autoSave,
                          TournamentRefreshService refresh,
                          TournamentSession session,
                          AuthService auth)
    {
        _sheets = sheets;
        _autoSave = autoSave;
        _refresh = refresh;
        _session = session;
        _auth = auth;

        _autoSave.MatchLockTakenOver += OnLockTakenOverFromAutoSave;
        _refresh.MatchUpdated += OnRemoteMatchUpdated;
    }

    public async Task LoadAsync(string matchId)
    {
        if (_session.Current is null) { ErrorMessage = "No tournament open."; return; }
        IsLoading = true;
        ErrorMessage = "";
        try
        {
            _myUserId = _auth.CurrentFencer?.Id ?? $"anon-{Guid.NewGuid():N}";

            // Try the in-memory session first — avoids a full sheet read in the
            // common case where we just navigated here from the hub.
            Match = FindMatchInSession(matchId);

            // Fall back to the sheet only when the match isn't in memory yet
            // (rare: eventual-consistency delay right after AppendMatchesAsync).
            if (Match is null)
            {
                var matches = await _sheets.GetMatchesAsync(_session.Current.Id);
                Match = matches.FirstOrDefault(m => m.Id == matchId);
            }

            if (Match is null) { ErrorMessage = "Match not found."; return; }

            HydrateNames();

            // Take-over flow.
            if (_session.CanEdit && Match.IsLockedByOther(_myUserId, DateTime.UtcNow))
            {
                var go = ConfirmTakeOverAsync is null
                       ? false
                       : await ConfirmTakeOverAsync(Match.LockedByUserId ?? "another judge");
                if (!go)
                {
                    IsReadOnly = true;
                    LockHolderText = $"Locked by {Match.LockedByUserId} — read-only.";
                    RefreshAll();
                    return;
                }
            }

            if (_session.CanEdit && Match.Status != MatchStatus.Finished)
            {
                Match.LockedByUserId = _myUserId;
                Match.LockedAtUtc    = DateTime.UtcNow;
                Match.UpdatedByUserId = _myUserId;
                if (Match.Status == MatchStatus.Pending) Match.Status = MatchStatus.InProgress;
                if (Match.StartedAtUtc is null)          Match.StartedAtUtc = DateTime.UtcNow;
                await _autoSave.FlushMatchOverwriteAsync(_session.Current.Id, Match, _myUserId);
            }
            else
            {
                IsReadOnly = true;
            }

            ClockText = FormatClock(Match.RemainingTimeSeconds);
            RefreshAll();
            if (!IsReadOnly) StartHeartbeat();
        }
        catch (Exception ex) { ErrorMessage = $"Open failed: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    private void HydrateNames()
    {
        if (_session.Current is null || Match is null) return;
        // Deduplicate by ID in case in-memory store has dupes from by-reference storage
        var byId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in _session.Current.Fencers)
            byId[f.Id] = f.Name;
        LeftName  = byId.TryGetValue(Match.LeftFencerId,  out var l) ? l : "?";
        RightName = byId.TryGetValue(Match.RightFencerId, out var r) ? r : "?";
        var pool = _session.Current.Pools.FirstOrDefault(p => p.Id == Match.PoolId);
        PoolName = pool?.Name ?? "Elimination";
    }

    /// <summary>
    /// Searches the in-memory session (pools + bracket) for a match by ID.
    /// Used as a fallback when the sheet read hasn't replicated yet.
    /// </summary>
    private Match? FindMatchInSession(string matchId)
    {
        if (_session.Current is null) return null;

        foreach (var pool in _session.Current.Pools)
        {
            var m = pool.Matches.FirstOrDefault(m => m.Id == matchId);
            if (m is not null) return m;
        }

        if (_session.Current.Bracket is not null)
        {
            foreach (var round in _session.Current.Bracket.Rounds)
            {
                var m = round.Matches.FirstOrDefault(m => m.Id == matchId);
                if (m is not null) return m;
            }
            if (_session.Current.Bracket.BronzeMatch?.Id == matchId)
                return _session.Current.Bracket.BronzeMatch;
        }

        return null;
    }

    // ---------------- Score / cards ----------------

    [RelayCommand] private Task AddLeftPoint()    => AdjustScoreAsync(+1, true);
    [RelayCommand] private Task SubLeftPoint()    => AdjustScoreAsync(-1, true);
    [RelayCommand] private Task AddRightPoint()   => AdjustScoreAsync(+1, false);
    [RelayCommand] private Task SubRightPoint()   => AdjustScoreAsync(-1, false);
    [RelayCommand] private Task AddLeftYellow()   => AddCardAsync(true,  yellow: true);
    [RelayCommand] private Task AddLeftRed()      => AddCardAsync(true,  yellow: false);
    [RelayCommand] private Task AddRightYellow()  => AddCardAsync(false, yellow: true);
    [RelayCommand] private Task AddRightRed()     => AddCardAsync(false, yellow: false);
    [RelayCommand] private Task RemoveLeftYellow()  => RemoveCardAsync(true,  yellow: true);
    [RelayCommand] private Task RemoveLeftRed()     => RemoveCardAsync(true,  yellow: false);
    [RelayCommand] private Task RemoveRightYellow() => RemoveCardAsync(false, yellow: true);
    [RelayCommand] private Task RemoveRightRed()    => RemoveCardAsync(false, yellow: false);

    /// <summary>
    /// Removes the last card added to the left fencer.
    /// Priority: red first (since it was added last if both exist), then yellow.
    /// </summary>
    [RelayCommand]
    private Task RemoveLeftLastCard()
    {
        if (!CanEdit || Match is null) return Task.CompletedTask;
        if (Match.LeftRedCards > 0) return RemoveCardAsync(true, yellow: false);
        if (Match.LeftYellowCards > 0) return RemoveCardAsync(true, yellow: true);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes the last card added to the right fencer.
    /// Priority: red first (since it was added last if both exist), then yellow.
    /// </summary>
    [RelayCommand]
    private Task RemoveRightLastCard()
    {
        if (!CanEdit || Match is null) return Task.CompletedTask;
        if (Match.RightRedCards > 0) return RemoveCardAsync(false, yellow: false);
        if (Match.RightYellowCards > 0) return RemoveCardAsync(false, yellow: true);
        return Task.CompletedTask;
    }

    // ----- "- Card" button visibility -----
    public bool CanRemoveLeftCard  => CanEdit && (LeftYellow > 0 || LeftRed > 0);
    public bool CanRemoveRightCard => CanEdit && (RightYellow > 0 || RightRed > 0);

    private Task AdjustScoreAsync(int delta, bool leftSide)
    {
        if (!CanEdit || Match is null) return Task.CompletedTask;
        if (leftSide) Match.LeftScore  = Math.Max(0, Match.LeftScore  + delta);
        else          Match.RightScore = Math.Max(0, Match.RightScore + delta);
        return PersistAsync();
    }

    private Task AddCardAsync(bool leftSide, bool yellow)
    {
        if (!CanEdit || Match is null) return Task.CompletedTask;

        // Defensive guards — buttons are also disabled in XAML, but enforce the rules here too.
        if (leftSide)
        {
            if (yellow && !CanLeftYellow) return Task.CompletedTask;
            if (yellow) Match.LeftYellowCards++;
            else       { Match.LeftRedCards++; Match.RightScore++; } // red ⇒ point to opponent
        }
        else
        {
            if (yellow && !CanRightYellow) return Task.CompletedTask;
            if (yellow) Match.RightYellowCards++;
            else       { Match.RightRedCards++; Match.LeftScore++; }
        }
        return PersistAsync();
    }

    private Task RemoveCardAsync(bool leftSide, bool yellow)
    {
        if (!CanEdit || Match is null) return Task.CompletedTask;

        if (leftSide)
        {
            if (yellow)
            {
                if (Match.LeftYellowCards <= 0) return Task.CompletedTask;
                Match.LeftYellowCards--;
            }
            else
            {
                if (Match.LeftRedCards <= 0) return Task.CompletedTask;
                Match.LeftRedCards--;
                // Removing a red card removes the point that was awarded to the opponent.
                Match.RightScore = Math.Max(0, Match.RightScore - 1);
            }
        }
        else
        {
            if (yellow)
            {
                if (Match.RightYellowCards <= 0) return Task.CompletedTask;
                Match.RightYellowCards--;
            }
            else
            {
                if (Match.RightRedCards <= 0) return Task.CompletedTask;
                Match.RightRedCards--;
                // Removing a red card removes the point that was awarded to the opponent.
                Match.LeftScore = Math.Max(0, Match.LeftScore - 1);
            }
        }
        return PersistAsync();
    }

    // ---------------- Timer ----------------

    [RelayCommand]
    public void ToggleTimer()
    {
        if (!CanEdit || Match is null) return;
        if (IsTimerRunning) PauseTimer();
        else                StartTimerInternal();
        OnPropertyChanged(nameof(TimerButtonText));
        OnPropertyChanged(nameof(TimerButtonColor));
    }

    [RelayCommand]
    public Task AddMinute()
    {
        if (!CanEdit || Match is null) return Task.CompletedTask;
        Match.RemainingTimeSeconds += 60;
        ClockText = FormatClock(Match.RemainingTimeSeconds);
        return PersistAsync();
    }

    [RelayCommand]
    public Task SubtractMinute()
    {
        if (!CanEdit || Match is null) return Task.CompletedTask;
        // Minimum time is 2 minutes (120 seconds)
        int newTime = Match.RemainingTimeSeconds - 60;
        if (newTime < 120) return Task.CompletedTask;
        Match.RemainingTimeSeconds = newTime;
        ClockText = FormatClock(Match.RemainingTimeSeconds);
        return PersistAsync();
    }

    [RelayCommand]
    public Task RestartTimer()
    {
        if (!CanEdit || Match is null) return Task.CompletedTask;
        // Stop the running timer first, reset to default (2 minutes)
        if (IsTimerRunning)
        {
            IsTimerRunning = false;
            _clockTimer?.Stop();
        }
        Match.RemainingTimeSeconds = TournamentEngine.DefaultMatchSeconds;
        ClockText = FormatClock(Match.RemainingTimeSeconds);
        OnPropertyChanged(nameof(TimerButtonText));
        return PersistAsync();
    }

    private void StartTimerInternal()
    {
        if (Match is null) return;
        if (Match.RemainingTimeSeconds <= 0) return;

        IsTimerRunning = true;
        _clockTimer ??= Application.Current!.Dispatcher.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick -= OnClockTick;
        _clockTimer.Tick += OnClockTick;
        _clockTimer.Start();
    }

    private void OnClockTick(object? sender, EventArgs e)
    {
        if (Match is null || !IsTimerRunning) return;
        Match.RemainingTimeSeconds = Math.Max(0, Match.RemainingTimeSeconds - 1);
        ClockText = FormatClock(Match.RemainingTimeSeconds);

        if (Match.RemainingTimeSeconds <= 0)
        {
            PauseTimer();
            _ = PersistAsync();
            OnPropertyChanged(nameof(TimerButtonText));
        }
    }

    private void PauseTimer()
    {
        IsTimerRunning = false;
        _clockTimer?.Stop();
        _ = PersistAsync();   // save the paused-at value
    }

    /// <summary>Stop the visual timer WITHOUT scheduling a debounced write — used by Finish.</summary>
    private void StopTimerLocalOnly()
    {
        IsTimerRunning = false;
        _clockTimer?.Stop();
        OnPropertyChanged(nameof(TimerButtonText));
    }

    private static string FormatClock(int seconds) =>
        $"{seconds / 60:00}:{seconds % 60:00}";

    // ---------------- Reopen finished match ----------------

    /// <summary>
    /// Reopens a finished match so the score/cards can be edited.
    /// For elimination matches, also clears the winner from the downstream match.
    /// </summary>
    [RelayCommand]
    public async Task ReopenMatchAsync()
    {
        if (!CanReopenMatch || Match is null || _session.Current is null) return;

        var t = _session.Current;

        // For elim matches: clear the propagated winner from the downstream match
        if (Match.BracketRound.HasValue && t.Bracket is not null)
        {
            var cleared = ClearDownstreamPropagation(t.Bracket, Match);
            foreach (var m in cleared)
            {
                try { await _sheets.UpsertMatchAsync(t.Id, m); }
                catch { /* best-effort */ }
            }
        }

        Match.Status = MatchStatus.InProgress;
        Match.WinnerFencerId = null;
        Match.FinishedAtUtc = null;
        Match.LockedByUserId = _myUserId;
        Match.LockedAtUtc = DateTime.UtcNow;
        Match.UpdatedByUserId = _myUserId;

        IsReadOnly = false;

        try
        {
            await _autoSave.FlushMatchOverwriteAsync(t.Id, Match, _myUserId);
        }
        catch (Exception ex)
        {
            // Rollback
            Match.Status = MatchStatus.Finished;
            IsReadOnly = true;
            ErrorMessage = $"Reopen failed: {ex.Message}";
            RefreshAll();
            return;
        }

        StartHeartbeat();
        RefreshAll();
    }

    /// <summary>
    /// Clears the winner/loser propagation from the downstream match and bronze match.
    /// Returns the list of matches that were modified and need persisting.
    /// </summary>
    private static List<Match> ClearDownstreamPropagation(EliminationBracket bracket, Match match)
    {
        var changed = new List<Match>();
        if (!match.BracketRound.HasValue || !match.BracketSlot.HasValue) return changed;

        int nextRoundIdx = match.BracketRound.Value + 1;
        int semiRoundIdx = bracket.Rounds.Count - 2;

        // Clear the winner from the next-round match
        if (nextRoundIdx < bracket.Rounds.Count)
        {
            int nextSlot = match.BracketSlot.Value / 2;
            var nextRound = bracket.Rounds[nextRoundIdx];
            var downstream = nextRound.Matches.FirstOrDefault(m => m.BracketSlot == nextSlot);
            if (downstream is not null)
            {
                bool isLeftFeeder = match.BracketSlot.Value % 2 == 0;
                if (isLeftFeeder)
                    downstream.LeftFencerId = "";
                else
                    downstream.RightFencerId = "";
                changed.Add(downstream);
            }
        }

        // Clear the loser from the bronze match if this was a semi-final
        if (bracket.BronzeMatch is not null && match.BracketRound.Value == semiRoundIdx)
        {
            bool isLeftFeeder = match.BracketSlot.Value % 2 == 0;
            if (isLeftFeeder)
                bracket.BronzeMatch.LeftFencerId = "";
            else
                bracket.BronzeMatch.RightFencerId = "";
            if (!changed.Contains(bracket.BronzeMatch))
                changed.Add(bracket.BronzeMatch);
        }

        return changed;
    }

    // ---------------- Finish ----------------

    [RelayCommand]
    public async Task FinishMatchAsync()
    {
        if (!CanEdit || Match is null || _session.Current is null) return;

        // Clear any stale error from a previous attempt (e.g. a tied-score
        // rejection). Otherwise a later, valid finish would leave the old
        // message visible and block the page's post-finish navigation.
        ErrorMessage = "";

        // Draws are only forbidden in elimination matches (someone must be eliminated).
        // Pool matches may finish with equal scores — WinnerFencerId stays null.
        bool isEliminationMatch = Match.BracketRound.HasValue;
        if (isEliminationMatch && Match.LeftScore == Match.RightScore)
        {
            ErrorMessage = "Cannot finish on a tied score in an elimination match.";
            return;
        }

        // Snapshot for rollback.
        var prevStatus       = Match.Status;
        var prevWinnerId     = Match.WinnerFencerId;
        var prevFinishedAt   = Match.FinishedAtUtc;
        var prevLockedBy     = Match.LockedByUserId;
        var prevLockedAt     = Match.LockedAtUtc;
        var prevTimerRunning = IsTimerRunning;

        StopHeartbeat();
        StopTimerLocalOnly();

        Match.Status = MatchStatus.Finished;

        // Pool match: null winner on a draw; otherwise highest score wins.
        // Elimination match: always has a winner (draw blocked above).
        Match.WinnerFencerId = Match.LeftScore == Match.RightScore
            ? null
            : (Match.LeftScore > Match.RightScore ? Match.LeftFencerId : Match.RightFencerId);

        Match.FinishedAtUtc  = DateTime.UtcNow;
        Match.LockedByUserId = null;
        Match.LockedAtUtc    = null;

        var t = _session.Current;
        try
        {
            await _autoSave.FlushMatchOverwriteAsync(t.Id, Match, _myUserId);
        }
        catch (Exception ex)
        {
            // Rollback so the user can retry from a consistent state.
            Match.Status         = prevStatus;
            Match.WinnerFencerId = prevWinnerId;
            Match.FinishedAtUtc  = prevFinishedAt;
            Match.LockedByUserId = prevLockedBy;
            Match.LockedAtUtc    = prevLockedAt;
            if (prevTimerRunning) StartTimerInternal();
            StartHeartbeat();
            ErrorMessage = $"Finish failed (nothing saved). Please retry: {ex.Message}";
            RefreshAll();
            return;
        }

        // Elimination follow-ups: propagate winner to the next round (and bronze if applicable),
        // then auto-finalise the tournament when the whole bracket is complete.
        if (isEliminationMatch && t.Bracket is not null)
        {
            TournamentEngine.PatchInBracket(t.Bracket, Match);
            var downstream = TournamentEngine.PropagateAndCollectChanges(t.Bracket);
            foreach (var m in downstream)
            {
                if (m.Id == Match.Id) continue;
                try { await _sheets.UpsertMatchAsync(t.Id, m); }
                catch { /* best-effort; UI will re-sync on next hub load */ }
            }

            if (TournamentEngine.IsBracketComplete(t.Bracket) && t.State != TournamentState.Finished)
            {
                try
                {
                    var order = TournamentEngine.ComputeFinalStandings(t);
                    await _sheets.SaveFinalStandingsAsync(t.Id, order);
                    t.FinalStandingFencerIds = order;
                    t.State = TournamentState.Finished;
                    await _sheets.UpsertTournamentHeaderAsync(t);
                }
                catch (Exception ex) { ErrorMessage = $"Finalize failed: {ex.Message}"; }
            }
        }

        RefreshAll();
    }

    // ---------------- Lifecycle ----------------

    public async Task ReleaseLockAndDisposeAsync()
    {
        StopHeartbeat();
        _clockTimer?.Stop();

        if (Match is null || _session.Current is null || IsReadOnly || _lockTakenOverHandled) return;
        if (Match.Status == MatchStatus.Finished) return;

        // Hand the lock back so others can claim immediately.
        Match.LockedByUserId = null;
        Match.LockedAtUtc = null;
        try { await _autoSave.FlushMatchOverwriteAsync(_session.Current.Id, Match, _myUserId); }
        catch { /* nothing more we can do on the way out */ }
    }

    private void StartHeartbeat()
    {
        StopHeartbeat();
        if (!_session.CanEdit) return;
        if (Match?.Status == MatchStatus.Finished) return;

        _heartbeat = Application.Current!.Dispatcher.CreateTimer();
        _heartbeat.Interval = TimeSpan.FromSeconds(30);
        _heartbeat.Tick += (_, _) =>
        {
            if (Match is null || _session.Current is null || IsReadOnly) return;
            // Never re-lock a finished match — would resurrect a stale lock on the sheet.
            if (Match.Status == MatchStatus.Finished) return;
            // Refresh our lock window — write through the debounced pipe.
            Match.LockedByUserId = _myUserId;
            Match.LockedAtUtc = DateTime.UtcNow;
            _autoSave.ScheduleMatchOverwrite(_session.Current.Id, Match, _myUserId);
        };
        _heartbeat.Start();
    }

    private void StopHeartbeat() { _heartbeat?.Stop(); _heartbeat = null; }

    private Task PersistAsync()
    {
        if (Match is null || _session.Current is null) return Task.CompletedTask;
        Match.UpdatedByUserId = _myUserId;
        Match.LockedByUserId  = _myUserId;
        Match.LockedAtUtc     = DateTime.UtcNow;
        _autoSave.ScheduleMatchOverwrite(_session.Current.Id, Match, _myUserId);
        RefreshAll();
        return Task.CompletedTask;
    }

    private void OnLockTakenOverFromAutoSave(string tournamentId, Match latest)
    {
        if (Match is null || latest.Id != Match.Id) return;
        _lockTakenOverHandled = true;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            LockHolderText = $"Taken over by {latest.LockedByUserId}.";
            IsReadOnly = true;
            LockTakenOver?.Invoke();
        });
    }

    private void OnRemoteMatchUpdated(Match remote)
    {
        if (Match is null || remote.Id != Match.Id) return;
        if (remote.LockedByUserId is { Length: > 0 } &&
            remote.LockedByUserId != _myUserId &&
            !IsReadOnly)
        {
            // Polling detected our lock was taken before our next save fired.
            OnLockTakenOverFromAutoSave("", remote);
        }
    }

    private void RefreshAll()
    {
        OnPropertyChanged(nameof(LeftName));
        OnPropertyChanged(nameof(RightName));
        OnPropertyChanged(nameof(LeftScore));
        OnPropertyChanged(nameof(RightScore));
        OnPropertyChanged(nameof(LeftYellow));
        OnPropertyChanged(nameof(LeftRed));
        OnPropertyChanged(nameof(RightYellow));
        OnPropertyChanged(nameof(RightRed));
        OnPropertyChanged(nameof(CanLeftYellow));
        OnPropertyChanged(nameof(CanLeftRed));
        OnPropertyChanged(nameof(CanRightYellow));
        OnPropertyChanged(nameof(CanRightRed));
        OnPropertyChanged(nameof(CanRemoveLeftYellow));
        OnPropertyChanged(nameof(CanRemoveLeftRed));
        OnPropertyChanged(nameof(CanRemoveRightYellow));
        OnPropertyChanged(nameof(CanRemoveRightRed));
        OnPropertyChanged(nameof(CanRemoveLeftCard));
        OnPropertyChanged(nameof(CanRemoveRightCard));
        OnPropertyChanged(nameof(LeftUndoOpacity));
        OnPropertyChanged(nameof(RightUndoOpacity));
        OnPropertyChanged(nameof(LeftShowRedIndicator));
        OnPropertyChanged(nameof(LeftShowYellowIndicator));
        OnPropertyChanged(nameof(LeftHasAnyCard));
        OnPropertyChanged(nameof(RightShowRedIndicator));
        OnPropertyChanged(nameof(RightShowYellowIndicator));
        OnPropertyChanged(nameof(RightHasAnyCard));
        OnPropertyChanged(nameof(LeftRedCountText));
        OnPropertyChanged(nameof(RightRedCountText));
        OnPropertyChanged(nameof(LeftHasMultipleReds));
        OnPropertyChanged(nameof(RightHasMultipleReds));
        OnPropertyChanged(nameof(LeftCardSummary));
        OnPropertyChanged(nameof(RightCardSummary));
        OnPropertyChanged(nameof(LeftHasCardSummary));
        OnPropertyChanged(nameof(RightHasCardSummary));
        OnPropertyChanged(nameof(StatusBadge));
        OnPropertyChanged(nameof(TimerButtonColor));
        OnPropertyChanged(nameof(TimerButtonText));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(EditOpacity));
        OnPropertyChanged(nameof(CanReopenMatch));
        OnPropertyChanged(nameof(Title));
    }

    public void Dispose()
    {
        _autoSave.MatchLockTakenOver -= OnLockTakenOverFromAutoSave;
        _refresh.MatchUpdated -= OnRemoteMatchUpdated;
        _clockTimer?.Stop();
        _heartbeat?.Stop();
    }
}