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
    [ObservableProperty] private string clockText = "03:00";
    [ObservableProperty] private bool isReadOnly;
    [ObservableProperty] private string lockHolderText = "";

    /// <summary>Raised when our lock has been taken over; the page should pop.</summary>
    public event Action? LockTakenOver;

    /// <summary>Raised so the page can confirm the take-over UI before we actually claim the lock.</summary>
    public Func<string /*otherUserId*/, Task<bool>>? ConfirmTakeOverAsync { get; set; }

    public bool CanEdit => !IsReadOnly && _session.CanEdit && Match?.Status != MatchStatus.Finished;
    public string Title => $"{PoolName} · {LeftName} vs {RightName}";

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

    public string StatusBadge => Match?.Status switch
    {
        MatchStatus.Pending    => "Pending",
        MatchStatus.InProgress => "In progress",
        MatchStatus.Finished   => "Finished",
        _                      => ""
    };
    public string TimerButtonText => IsTimerRunning ? "Stop" : "Start";

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

            // Always read latest so we see the current lock state.
            var matches = await _sheets.GetMatchesAsync(_session.Current.Id);
            var latest = matches.FirstOrDefault(m => m.Id == matchId);
            if (latest is null) { ErrorMessage = "Match not found."; return; }

            Match = latest;
            HydrateNames();

            // Take-over flow.
            if (_session.CanEdit && latest.IsLockedByOther(_myUserId, DateTime.UtcNow))
            {
                var go = ConfirmTakeOverAsync is null
                       ? false
                       : await ConfirmTakeOverAsync(latest.LockedByUserId ?? "another judge");
                if (!go)
                {
                    IsReadOnly = true;
                    LockHolderText = $"Locked by {latest.LockedByUserId} — read-only.";
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
        var byId = _session.Current.Fencers.ToDictionary(f => f.Id, f => f.Name);
        LeftName  = byId.TryGetValue(Match.LeftFencerId,  out var l) ? l : "?";
        RightName = byId.TryGetValue(Match.RightFencerId, out var r) ? r : "?";
        var pool = _session.Current.Pools.FirstOrDefault(p => p.Id == Match.PoolId);
        PoolName = pool?.Name ?? "Elimination";
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

    // ---------------- Timer ----------------

    [RelayCommand]
    public void ToggleTimer()
    {
        if (!CanEdit || Match is null) return;
        if (IsTimerRunning) PauseTimer();
        else                StartTimerInternal();
        OnPropertyChanged(nameof(TimerButtonText));
    }

    [RelayCommand]
    public Task AddMinute()
    {
        if (!CanEdit || Match is null) return Task.CompletedTask;
        Match.RemainingTimeSeconds += 60;
        ClockText = FormatClock(Match.RemainingTimeSeconds);
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

    // ---------------- Finish ----------------

    [RelayCommand]
    public async Task FinishMatchAsync()
    {
        if (!CanEdit || Match is null || _session.Current is null) return;
        if (Match.LeftScore == Match.RightScore)
        {
            ErrorMessage = "Cannot finish on a tied score.";
            return;
        }

        // Snapshot for rollback. If the save below throws (slow network /
        // Sheets timeout) we MUST NOT leave the in-memory match marked as
        // Finished — the page would then pop, the sheet would still say
        // "InProgress + locked + no winner", and the user's score would be
        // gone the next time they opened the match.
        var prevStatus       = Match.Status;
        var prevWinnerId     = Match.WinnerFencerId;
        var prevFinishedAt   = Match.FinishedAtUtc;
        var prevLockedBy     = Match.LockedByUserId;
        var prevLockedAt     = Match.LockedAtUtc;
        var prevTimerRunning = IsTimerRunning;

        // Stop background activity FIRST so no heartbeat / clock write can
        // race the flush. Use the local stop so PauseTimer's PersistAsync
        // doesn't queue an extra debounced write while we're finishing.
        StopHeartbeat();
        StopTimerLocalOnly();

        Match.Status         = MatchStatus.Finished;
        Match.WinnerFencerId = Match.LeftScore > Match.RightScore
            ? Match.LeftFencerId : Match.RightFencerId;
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
        if (Match.BracketRound.HasValue && t.Bracket is not null)
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
        OnPropertyChanged(nameof(StatusBadge));
        OnPropertyChanged(nameof(CanEdit));
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