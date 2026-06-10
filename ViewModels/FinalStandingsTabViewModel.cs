using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

/// <summary>
/// Live final-standings table for the elimination bracket.
/// Recomputed on hub load and on every remote match update; once a
/// fencer is eliminated their final position is locked in by the engine.
/// </summary>
public partial class FinalStandingsTabViewModel : ObservableObject, IDisposable
{
    private readonly TournamentRefreshService _refresh;
    private TournamentSession? _session;

    public ObservableCollection<FinalStandingRowVm> Rows { get; } = new();

    public bool HasRows   => Rows.Count > 0;
    public bool HasNoRows => Rows.Count == 0;

    public string EmptyHint =>
        _session?.Current?.Bracket is null
            ? "Standings appear once the elimination bracket is generated."
            : "No matches are finished yet.";

    public FinalStandingsTabViewModel(TournamentRefreshService refresh)
    {
        _refresh = refresh;
        _refresh.MatchUpdated += OnRemoteMatchUpdated;
    }

    public void AttachTo(TournamentSession session) => _session = session;

    /// <summary>Rebuild the rows from the current bracket state.</summary>
    public void Recompute()
    {
        Rows.Clear();
        var t = _session?.Current;
        if (t?.Bracket is null) { RaiseStateChanged(); return; }

        var order    = TournamentEngine.ComputeFinalStandings(t);
        if (order.Count == 0) { RaiseStateChanged(); return; }

        var nameById = t.Fencers.ToDictionary(f => f.Id, f => f.Name);
        var lossInfo = BuildLossInfo(t.Bracket, nameById);

        for (int i = 0; i < order.Count; i++)
        {
            var id = order[i];
            var name = nameById.TryGetValue(id, out var n) ? n : "?";
            lossInfo.TryGetValue(id, out var info);

            Rows.Add(new FinalStandingRowVm(
                place: i + 1,
                name: name,
                defeatedByName: info.DefeatedBy ?? "",
                eliminatedAt:   info.RoundLabel ?? "Champion"));
        }

        RaiseStateChanged();
    }

    /// <summary>For each fencer who has been eliminated, who beat them and in which round.</summary>
    private static Dictionary<string, (string? DefeatedBy, string? RoundLabel)> BuildLossInfo(
        EliminationBracket bracket, IReadOnlyDictionary<string, string> nameById)
    {
        var map = new Dictionary<string, (string?, string?)>();

        void Note(Match m, string label)
        {
            if (m.Status != MatchStatus.Finished) return;
            if (string.IsNullOrEmpty(m.WinnerFencerId)) return;

            var loserId = m.WinnerFencerId == m.LeftFencerId ? m.RightFencerId : m.LeftFencerId;
            if (string.IsNullOrEmpty(loserId)) return;
            if (map.ContainsKey(loserId)) return;

            var winnerName = nameById.TryGetValue(m.WinnerFencerId, out var w) ? w : "?";
            map[loserId] = (winnerName, label);
        }

        for (int r = 0; r < bracket.Rounds.Count; r++)
        {
            var participants = bracket.Rounds[r].Matches.Count * 2;
            var label = TournamentEngine.RoundName(participants);
            foreach (var m in bracket.Rounds[r].Matches) Note(m, label);
        }
        if (bracket.BronzeMatch is not null) Note(bracket.BronzeMatch, "Bronze");

        return map;
    }

    private void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HasNoRows));
        OnPropertyChanged(nameof(EmptyHint));
    }

    private void OnRemoteMatchUpdated(Match remote)
    {
        // Polling timer is on a background thread; marshal to UI.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // ElimTabViewModel already patched the bracket; we just re-render.
            if (_session?.Current?.Bracket is null) return;
            Recompute();
        });
    }

    public void Dispose() => _refresh.MatchUpdated -= OnRemoteMatchUpdated;
}