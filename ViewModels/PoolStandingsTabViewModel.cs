using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

/// <summary>
/// Live pool standings — recomputed whenever a match changes (locally or via polling).
/// Sort criteria (most → least important):
///   1. Win % (wins / matchesDone)              — higher is better
///   2. Average points for / match               — higher is better
///   3. Average points against / match           — lower is better
///   4. Red card count                           — lower is better
/// Qualification for the elimination is decided by <see cref="TournamentEngine.ComputeQualifyingFencerIds"/>
/// (per-pool top 60%, topped up to a minimum of 8, or everyone if fewer than 8 fencers exist).
/// The cut-off separator in each pool and in the overall card is drawn directly under the last
/// fencer who is in that authoritative set.
/// </summary>
public partial class PoolStandingsTabViewModel : ObservableObject, IDisposable
{
    private readonly TournamentRefreshService _refresh;
    private TournamentSession? _session;

    /// <summary>Per-pool standings cards.</summary>
    public ObservableCollection<PoolStandingsGroupVm> Groups { get; } = new();

    /// <summary>Aggregated cross-pool standings (every fencer in one ranked list).</summary>
    public ObservableCollection<PoolStandingRowVm> OverallRows { get; } = new();

    public bool HasNoPools => Groups.Count == 0;
    public bool HasOverall => OverallRows.Count > 0;

    public PoolStandingsTabViewModel(TournamentRefreshService refresh)
    {
        _refresh = refresh;
        _refresh.MatchUpdated += OnRemoteMatchUpdated;
    }

    public void AttachTo(TournamentSession session) => _session = session;

    /// <summary>Rebuild every pool's standings and the overall card from the session aggregate.</summary>
    public void Recompute()
    {
        Groups.Clear();
        OverallRows.Clear();
        if (_session?.Current is null)
        {
            OnPropertyChanged(nameof(HasNoPools));
            OnPropertyChanged(nameof(HasOverall));
            return;
        }

        var nameById = _session.Current.Fencers.ToDictionary(f => f.Id, f => f.Name);

        // Single source of truth — the engine decides who actually enters the elimination.
        // This drives every separator on this page (per-pool AND overall).
        var qualifiedIds = new HashSet<string>(
            TournamentEngine.ComputeQualifyingFencerIds(_session.Current));

        foreach (var pool in _session.Current.Pools.OrderBy(p => p.Index))
        {
            var group = new PoolStandingsGroupVm(pool);
            FillGroup(group, pool, nameById, qualifiedIds);
            Groups.Add(group);
        }

        RebuildOverall(nameById, qualifiedIds);

        OnPropertyChanged(nameof(HasNoPools));
        OnPropertyChanged(nameof(HasOverall));
    }

    private static void FillGroup(
        PoolStandingsGroupVm group,
        Pool pool,
        IReadOnlyDictionary<string, string> nameById,
        IReadOnlySet<string> qualifiedIds)
    {
        // Aggregate per-fencer stats across this pool's finished matches.
        var stats = pool.FencerIds.ToDictionary(
            id => id,
            id => new MutableStats { FencerId = id });

        foreach (var m in pool.Matches.Where(m => m.Status == MatchStatus.Finished))
        {
            if (!stats.TryGetValue(m.LeftFencerId,  out var ls)) continue;
            if (!stats.TryGetValue(m.RightFencerId, out var rs)) continue;

            ls.MatchesDone++;                       rs.MatchesDone++;
            ls.PointsFor     += m.LeftScore;        rs.PointsFor     += m.RightScore;
            ls.PointsAgainst += m.RightScore;       rs.PointsAgainst += m.LeftScore;
            ls.RedCards      += m.LeftRedCards;     rs.RedCards      += m.RightRedCards;
            if      (m.WinnerFencerId == m.LeftFencerId)  ls.Wins++;
            else if (m.WinnerFencerId == m.RightFencerId) rs.Wins++;
        }

        var ordered = SortStats(stats.Values);

        // Separator goes under the LAST fencer in this pool who actually qualifies.
        // The qualifier set is decided globally, so we just look each fencer up.
        int lastQualifierIndex = -1;
        for (int i = 0; i < ordered.Count; i++)
            if (qualifiedIds.Contains(ordered[i].FencerId))
                lastQualifierIndex = i;
        bool showSeparator = lastQualifierIndex >= 0 && lastQualifierIndex < ordered.Count - 1;

        group.Rows.Clear();
        for (int i = 0; i < ordered.Count; i++)
        {
            var s = ordered[i];
            var name = nameById.TryGetValue(s.FencerId, out var n) ? n : "?";
            group.Rows.Add(new PoolStandingRowVm(
                s.FencerId, name, i + 1,
                s.MatchesDone, s.Wins, s.PointsFor, s.PointsAgainst, s.RedCards,
                showQualificationSeparator: showSeparator && i == lastQualifierIndex));
        }
    }

    /// <summary>Rebuild the aggregated "Overall" card from every pool's finished matches.</summary>
    private void RebuildOverall(
        IReadOnlyDictionary<string, string> nameById,
        IReadOnlySet<string> qualifiedIds)
    {
        OverallRows.Clear();
        if (_session?.Current is null) return;

        // Global per-fencer stats, used purely for ranking and display in this card.
        var stats = new Dictionary<string, MutableStats>();
        foreach (var pool in _session.Current.Pools)
        {
            foreach (var id in pool.FencerIds)
                if (!stats.ContainsKey(id))
                    stats[id] = new MutableStats { FencerId = id };

            foreach (var m in pool.Matches.Where(m => m.Status == MatchStatus.Finished))
            {
                if (!stats.TryGetValue(m.LeftFencerId,  out var ls)) continue;
                if (!stats.TryGetValue(m.RightFencerId, out var rs)) continue;

                ls.MatchesDone++;                       rs.MatchesDone++;
                ls.PointsFor     += m.LeftScore;        rs.PointsFor     += m.RightScore;
                ls.PointsAgainst += m.RightScore;       rs.PointsAgainst += m.LeftScore;
                ls.RedCards      += m.LeftRedCards;     rs.RedCards      += m.RightRedCards;
                if      (m.WinnerFencerId == m.LeftFencerId)  ls.Wins++;
                else if (m.WinnerFencerId == m.RightFencerId) rs.Wins++;
            }
        }

        var ordered = SortStats(stats.Values);

        // Last globally-ranked qualifier — the separator goes under that row.
        int lastQualifierIndex = -1;
        for (int i = 0; i < ordered.Count; i++)
            if (qualifiedIds.Contains(ordered[i].FencerId))
                lastQualifierIndex = i;
        bool showSeparator = lastQualifierIndex >= 0 && lastQualifierIndex < ordered.Count - 1;

        for (int i = 0; i < ordered.Count; i++)
        {
            var s = ordered[i];
            var name = nameById.TryGetValue(s.FencerId, out var n) ? n : "?";
            OverallRows.Add(new PoolStandingRowVm(
                s.FencerId, name, i + 1,
                s.MatchesDone, s.Wins, s.PointsFor, s.PointsAgainst, s.RedCards,
                showQualificationSeparator: showSeparator && i == lastQualifierIndex));
        }
    }

    private static List<MutableStats> SortStats(IEnumerable<MutableStats> rows) =>
        rows.OrderByDescending(s => s.MatchesDone == 0 ? 0d : (double)s.Wins      / s.MatchesDone)
            .ThenByDescending (s => s.MatchesDone == 0 ? 0d : (double)s.PointsFor / s.MatchesDone)
            .ThenBy           (s => s.MatchesDone == 0
                                    ? double.PositiveInfinity
                                    : (double)s.PointsAgainst / s.MatchesDone)
            .ThenBy           (s => s.RedCards)
            .ToList();

    private void OnRemoteMatchUpdated(Match remote)
    {
        // Polling timer fires on a background thread; marshal to UI.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_session?.Current is null) return;

            // PoolsTabViewModel also patches the session, but be defensive and
            // patch here too — idempotent if it already happened.
            bool patched = false;
            foreach (var pool in _session.Current.Pools)
            {
                var idx = pool.Matches.FindIndex(m => m.Id == remote.Id);
                if (idx < 0) continue;
                pool.Matches[idx] = remote;
                patched = true;
                break;
            }
            if (!patched) return;

            // A match in ANY pool can shift the global qualifier set (e.g. a
            // top-up beyond 60% may pick a different fencer when stats change),
            // so refill every group + the overall card with a freshly computed set.
            var nameById = _session.Current.Fencers.ToDictionary(f => f.Id, f => f.Name);
            var qualifiedIds = new HashSet<string>(
                TournamentEngine.ComputeQualifyingFencerIds(_session.Current));

            foreach (var group in Groups)
            {
                var pool = _session.Current.Pools.FirstOrDefault(p => p.Id == group.PoolId);
                if (pool is null) continue;
                FillGroup(group, pool, nameById, qualifiedIds);
            }

            RebuildOverall(nameById, qualifiedIds);
            OnPropertyChanged(nameof(HasOverall));
        });
    }

    public void Dispose() => _refresh.MatchUpdated -= OnRemoteMatchUpdated;

    private sealed class MutableStats
    {
        public string FencerId = "";
        public int MatchesDone, Wins, PointsFor, PointsAgainst, RedCards;
    }
}