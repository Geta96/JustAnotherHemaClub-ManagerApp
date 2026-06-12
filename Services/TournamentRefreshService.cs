using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

/// <summary>
/// Polls the Matches sheet for the currently open tournament and raises
/// events when a row's Version changes.
///
/// The polling interval starts at <see cref="Interval"/> when the tournament
/// is active and grows toward <see cref="MaxInterval"/> on each idle tick
/// (no version changes seen). The first detected change snaps the cadence
/// back to <see cref="Interval"/>. Net effect: scoring matches stay snappy,
/// idle viewing stops hammering the API.
///
/// Pages subscribe in OnAppearing and call <see cref="Stop"/> in OnDisappearing.
/// </summary>
public sealed class TournamentRefreshService
{
    private readonly IGoogleSheetsService _sheets;
    private readonly Dictionary<string, int> _knownVersions = new();
    private CancellationTokenSource? _cts;
    private string? _tournamentId;

    /// <summary>Fast-poll cadence used when a change was just observed.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Slowest cadence the back-off is allowed to reach.</summary>
    public TimeSpan MaxInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Increment added to the current cadence on each idle tick.</summary>
    public TimeSpan BackoffStep { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Raised on the thread the timer fires from; subscribers should marshal to UI.</summary>
    public event Action<Match>? MatchUpdated;

    public TournamentRefreshService(IGoogleSheetsService sheets) => _sheets = sheets;

    public void Start(string tournamentId)
    {
        Stop();
        _tournamentId = tournamentId;
        _knownVersions.Clear();
        _cts = new CancellationTokenSource();
        _ = RunAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _tournamentId = null;
    }

    /// <summary>Seed the known versions with what the UI just loaded; avoids spurious events.</summary>
    public void Prime(IEnumerable<Match> matches)
    {
        foreach (var m in matches) _knownVersions[m.Id] = m.Version;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var currentInterval = Interval;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(currentInterval, ct);
                if (_tournamentId is null) continue;

                var latest = await _sheets.GetMatchesAsync(_tournamentId);
                bool anyChange = false;
                foreach (var m in latest)
                {
                    if (_knownVersions.TryGetValue(m.Id, out var v) && v == m.Version) continue;
                    _knownVersions[m.Id] = m.Version;
                    MatchUpdated?.Invoke(m);
                    anyChange = true;
                }

                // Reset on activity, otherwise back off toward MaxInterval.
                if (anyChange)
                {
                    currentInterval = Interval;
                }
                else
                {
                    var next = currentInterval + BackoffStep;
                    currentInterval = next > MaxInterval ? MaxInterval : next;
                }
            }
            catch (TaskCanceledException) { return; }
            catch
            {
                // Swallow transient failures; keep the back-off so a flaky network
                // doesn't burn quota retrying every 5 s.
            }
        }
    }
}