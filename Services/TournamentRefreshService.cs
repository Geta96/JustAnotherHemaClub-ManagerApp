using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

/// <summary>
/// Polls the Matches sheet for the currently open tournament every few seconds
/// and raises events when a row's Version changes. Pages subscribe in
/// OnAppearing and call <see cref="Stop"/> in OnDisappearing.
/// </summary>
public sealed class TournamentRefreshService
{
    private readonly IGoogleSheetsService _sheets;
    private readonly Dictionary<string, int> _knownVersions = new();
    private CancellationTokenSource? _cts;
    private string? _tournamentId;

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(5);

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
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, ct);
                if (_tournamentId is null) continue;

                var latest = await _sheets.GetMatchesAsync(_tournamentId);
                foreach (var m in latest)
                {
                    if (_knownVersions.TryGetValue(m.Id, out var v) && v == m.Version) continue;
                    _knownVersions[m.Id] = m.Version;
                    MatchUpdated?.Invoke(m);
                }
            }
            catch (TaskCanceledException) { return; }
            catch { /* swallow transient failures; next tick retries */ }
        }
    }
}