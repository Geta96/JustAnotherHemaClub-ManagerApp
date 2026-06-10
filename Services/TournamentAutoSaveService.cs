using System.Collections.Concurrent;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

/// <summary>
/// Debounced, per-entity autosave with conflict handling. Each match (or pool, or
/// tournament header) has its own debounce slot, so changes on Match A never
/// delay or overwrite changes on Match B.
///
/// On <see cref="ConcurrencyConflictException"/> the latest server state is fetched,
/// the user's pending change is re-applied, and the write is retried once.
/// </summary>
public sealed class TournamentAutoSaveService
{
    private readonly IGoogleSheetsService _sheets;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pending = new();
    private readonly TimeSpan _delay = TimeSpan.FromMilliseconds(750);

    public event Action<string, Match>? MatchReloadedFromConflict;
    public event Action<string, Match>? MatchLockTakenOver;

    public TournamentAutoSaveService(IGoogleSheetsService sheets) => _sheets = sheets;

    public void ScheduleMatch(string tournamentId, Match match, Action<Match> applyChange)
        => Schedule($"M:{match.Id}", () => SaveMatchAsync(tournamentId, match, applyChange));

    public void SchedulePool(string tournamentId, Pool pool, Action<Pool> applyChange)
        => Schedule($"P:{pool.Id}", () => SavePoolAsync(tournamentId, pool, applyChange));

    public void ScheduleTournament(Tournament t, Action<Tournament> applyChange)
        => Schedule($"T:{t.Id}", () => SaveTournamentAsync(t, applyChange));

    public async Task FlushMatchAsync(string tournamentId, Match match, Action<Match> applyChange)
    {
        Cancel($"M:{match.Id}");
        await SaveMatchAsync(tournamentId, match, applyChange);
    }

    /// <summary>
    /// Debounced write of a match that's currently held under our soft lock.
    /// On conflict: refetch; if our lock is no longer present, raise
    /// <see cref="MatchLockTakenOver"/> and do NOT overwrite (the other judge wins).
    /// </summary>
    public void ScheduleMatchOverwrite(string tournamentId, Match match, string myUserId)
        => Schedule($"M:{match.Id}", () => SaveMatchOverwriteAsync(tournamentId, match, myUserId));

    public async Task FlushMatchOverwriteAsync(string tournamentId, Match match, string myUserId)
    {
        if (_pending.TryRemove($"M:{match.Id}", out var cts)) cts.Cancel();
        await SaveMatchOverwriteAsync(tournamentId, match, myUserId);
    }

    private void Schedule(string key, Func<Task> work)
    {
        var cts = new CancellationTokenSource();
        if (_pending.TryRemove(key, out var old)) old.Cancel();
        _pending[key] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_delay, cts.Token);
                await work();
                _pending.TryRemove(key, out _);
            }
            catch (TaskCanceledException) { /* superseded */ }
            catch { _pending.TryRemove(key, out _); throw; }
        }, cts.Token);
    }

    private void Cancel(string key)
    {
        if (_pending.TryRemove(key, out var cts)) cts.Cancel();
    }

    // ---------- Match save with one-shot retry on conflict ----------

    private async Task SaveMatchAsync(string tournamentId, Match match, Action<Match> applyChange)
    {
        try
        {
            await _sheets.UpsertMatchAsync(tournamentId, match);
        }
        catch (ConcurrencyConflictException)
        {
            // Someone else wrote this match while we were debouncing.
            // Pull the latest, re-apply our intent on top, retry once.
            var latest = await _sheets.GetMatchAsyncSafe(tournamentId, match.Id);
            if (latest is null) throw;

            applyChange(latest);                     // user's intent is encoded in this delegate
            CopyVolatileMetadata(match, latest);     // keep our lock/heartbeat
            await _sheets.UpsertMatchAsync(tournamentId, latest);

            // Bring the in-memory match the UI is bound to back in sync.
            CopyAll(latest, match);
            MatchReloadedFromConflict?.Invoke(tournamentId, latest);
        }
    }

    private async Task SavePoolAsync(string tournamentId, Pool pool, Action<Pool> applyChange)
    {
        try { await _sheets.UpsertPoolAsync(tournamentId, pool); }
        catch (ConcurrencyConflictException)
        {
            var t = await _sheets.GetTournamentAsync(tournamentId);
            var latest = t?.Pools.FirstOrDefault(p => p.Id == pool.Id);
            if (latest is null) throw;
            applyChange(latest);
            await _sheets.UpsertPoolAsync(tournamentId, latest);
            CopyAllPool(latest, pool);
        }
    }

    private async Task SaveTournamentAsync(Tournament t, Action<Tournament> applyChange)
    {
        try { await _sheets.UpsertTournamentHeaderAsync(t); }
        catch (ConcurrencyConflictException)
        {
            var latest = (await _sheets.GetTournamentHeadersAsync()).FirstOrDefault(x => x.Id == t.Id);
            if (latest is null) throw;
            applyChange(latest);
            await _sheets.UpsertTournamentHeaderAsync(latest);
            t.Version = latest.Version;
            t.State = latest.State;
            t.Name = latest.Name;
        }
    }

    private async Task SaveMatchOverwriteAsync(string tournamentId, Match match, string myUserId)
    {
        try
        {
            await _sheets.UpsertMatchAsync(tournamentId, match);
        }
        catch (ConcurrencyConflictException)
        {
            var latest = await _sheets.GetMatchAsyncSafe(tournamentId, match.Id);
            if (latest is null) return;

            // Did someone steal our lock?
            if (latest.LockedByUserId != myUserId &&
                !string.IsNullOrEmpty(latest.LockedByUserId))
            {
                MatchLockTakenOver?.Invoke(tournamentId, latest);
                return;
            }

            // Lock still ours (or stale) — fast-forward version and retry once.
            match.Version = latest.Version;
            await _sheets.UpsertMatchAsync(tournamentId, match);
        }
    }

    private static void CopyVolatileMetadata(Match from, Match to)
    {
        to.LockedByUserId = from.LockedByUserId;
        to.LockedAtUtc    = from.LockedAtUtc;
        to.UpdatedByUserId = from.UpdatedByUserId;
    }

    private static void CopyAll(Match from, Match to)
    {
        to.LeftScore = from.LeftScore;       to.RightScore = from.RightScore;
        to.LeftYellowCards = from.LeftYellowCards; to.LeftRedCards = from.LeftRedCards;
        to.RightYellowCards = from.RightYellowCards; to.RightRedCards = from.RightRedCards;
        to.RemainingTimeSeconds = from.RemainingTimeSeconds;
        to.Status = from.Status; to.WinnerFencerId = from.WinnerFencerId;
        to.StartedAtUtc = from.StartedAtUtc; to.FinishedAtUtc = from.FinishedAtUtc;
        to.Version = from.Version;
        to.UpdatedAtUtc = from.UpdatedAtUtc; to.UpdatedByUserId = from.UpdatedByUserId;
        to.LockedByUserId = from.LockedByUserId; to.LockedAtUtc = from.LockedAtUtc;
    }

    private static void CopyAllPool(Pool from, Pool to)
    {
        to.IsClosed = from.IsClosed;
        to.Version = from.Version;
    }
}

internal static class SheetsServiceExtensions
{
    /// <summary>Helper to fetch a single match (cache layer doesn't expose this directly).</summary>
    public static async Task<Match?> GetMatchAsyncSafe(this IGoogleSheetsService sheets,
                                                       string tournamentId, string matchId)
    {
        var matches = await sheets.GetMatchesAsync(tournamentId);
        return matches.FirstOrDefault(m => m.Id == matchId);
    }
}