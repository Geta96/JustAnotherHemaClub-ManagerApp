using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

/// <summary>
/// Owns the elimination bracket view: building it from the pool standings, rendering the
/// columns (rounds + bronze), opening individual matches, and reacting to remote updates.
/// </summary>
public partial class ElimTabViewModel : ObservableObject, IDisposable
{
    private readonly IGoogleSheetsService _sheets;
    private readonly TournamentRefreshService _refresh;
    private TournamentSession? _session;

    public ObservableCollection<ElimRoundColumnVm> Columns { get; } = new();

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string errorMessage = "";

    /// <summary>Raised when an elim match card is tapped, so the page can push the MatchPage.</summary>
    public event Action<Match>? MatchSelected;

    /// <summary>
    /// Set by the page so the VM can prompt the user for which elimination option to use.
    /// Returns the picked option, or null if the user cancelled.
    /// </summary>
    public Func<IReadOnlyList<TournamentEngine.EliminationOption>, Task<TournamentEngine.EliminationOption?>>? PickEliminationOptionAsync { get; set; }

    public bool HasBracket   => Columns.Count > 0;
    public bool HasNoBracket => Columns.Count == 0;

    public bool CanEdit => _session?.CanEdit ?? false;

    public bool AllPoolsFinished
    {
        get
        {
            var t = _session?.Current;
            if (t is null || t.Pools.Count == 0) return false;
            return t.Pools.SelectMany(p => p.Matches).All(m => m.Status == MatchStatus.Finished);
        }
    }

    public bool CanGenerateBracket =>
        CanEdit && _session?.Current?.Bracket is null && AllPoolsFinished;

    /// <summary>
    /// True when the bracket exists but no elimination match has been started yet
    /// (all non-bye matches are Pending), meaning the organiser can regenerate.
    /// </summary>
    public bool CanRegenerateBracket
    {
        get
        {
            if (!CanEdit) return false;
            var bracket = _session?.Current?.Bracket;
            if (bracket is null) return false;

            // Check that no real match (non-bye) has been started or finished.
            foreach (var round in bracket.Rounds)
                foreach (var m in round.Matches)
                {
                    if (m.Status == MatchStatus.Finished &&
                        !string.IsNullOrEmpty(m.LeftFencerId) &&
                        !string.IsNullOrEmpty(m.RightFencerId))
                        return false; // a real (non-bye) match was played
                    if (m.Status == MatchStatus.InProgress)
                        return false;
                }
            if (bracket.BronzeMatch is not null && bracket.BronzeMatch.Status != MatchStatus.Pending)
                return false;

            return true;
        }
    }

    public string GenerateHint =>
        _session?.Current?.Bracket is not null
            ? (CanRegenerateBracket ? "Bracket generated. You can regenerate with different rules." : "Bracket has been generated.")
        : !AllPoolsFinished                    ? "Finish every pool match first."
        : "Ready to generate the bracket.";

    public ElimTabViewModel(IGoogleSheetsService sheets, TournamentRefreshService refresh)
    {
        _sheets  = sheets;
        _refresh = refresh;
        _refresh.MatchUpdated += OnRemoteMatchUpdated;
    }

    public void AttachTo(TournamentSession session) => _session = session;

    /// <summary>Rebuild the column list from the current in-memory bracket.</summary>
    public void Recompute()
    {
        Columns.Clear();
        var t = _session?.Current;
        if (t?.Bracket is null) { RefreshOptions(); RaiseStateChanged(); return; }

        // Deduplicate by ID in case in-memory store has dupes from by-reference storage
        var nameById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in t.Fencers)
            nameById[f.Id] = f.Name;
        int lastRoundIndex = t.Bracket.Rounds.Count - 1;

        for (int r = 0; r < t.Bracket.Rounds.Count; r++)
        {
            var round = t.Bracket.Rounds[r];
            var col   = new ElimRoundColumnVm(round.Name);
            bool isFinalRound = r == lastRoundIndex;
            foreach (var m in round.Matches)
                col.Matches.Add(new ElimMatchRowVm(
                    m, nameById,
                    roundIndex: r,
                    isFinal: isFinalRound));

            // Bronze is rendered directly under the final in the same (rightmost) column.
            if (isFinalRound && t.Bracket.BronzeMatch is not null)
            {
                col.Matches.Add(new ElimMatchRowVm(
                    t.Bracket.BronzeMatch, nameById,
                    roundIndex: 0,
                    isFinal: true,
                    isBronze: true,
                    overrideTag: "Bronze"));
            }

            Columns.Add(col);
        }

        RefreshOptions();
        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(HasBracket));
        OnPropertyChanged(nameof(HasNoBracket));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(AllPoolsFinished));
        OnPropertyChanged(nameof(CanGenerateBracket));
        OnPropertyChanged(nameof(CanRegenerateBracket));
        OnPropertyChanged(nameof(GenerateHint));
    }

    /// <summary>
    /// Bracket-size options rendered as buttons on the Elim tab. Unavailable
    /// options (size > next-power-of-two of fencer count) are kept in the list
    /// but flagged so the UI can grey them out.
    /// </summary>
    public ObservableCollection<TournamentEngine.EliminationOption> Options { get; } = new();

    public void RefreshOptions()
    {
        Options.Clear();
        var t = _session?.Current;
        if (t is null) return;
        foreach (var opt in TournamentEngine.ComputeEliminationOptions(t))
            Options.Add(opt);
    }

    // ---------------- Commands ----------------

    /// <summary>
    /// Build the bracket for the chosen size. Bound to per-size buttons on the
    /// Elim tab. No user prompt — the size is picked up straight from the tapped button.
    /// </summary>
    [RelayCommand]
    private async Task GenerateBracketWithOptionAsync(TournamentEngine.EliminationOption? option)
    {
        if (option is null || !option.IsAvailable) return;
        if (_session?.Current is null || !CanGenerateBracket) return;
        var t = _session.Current;

        IsBusy = true;
        ErrorMessage = "";
        try
        {
            var bracket = TournamentEngine.BuildBracketFromPoolStandingsBySize(t, option.BracketSize);
            t.Bracket = bracket;

            var allInitial = new List<Match>(bracket.Rounds.SelectMany(r => r.Matches));
            if (bracket.BronzeMatch is not null) allInitial.Add(bracket.BronzeMatch);
            await _sheets.AppendMatchesAsync(t.Id, allInitial);

            var changed = TournamentEngine.PropagateAndCollectChanges(bracket);
            foreach (var m in changed)
                await _sheets.UpsertMatchAsync(t.Id, m);

            t.State = TournamentState.EliminationInProgress;
            await _sheets.UpsertTournamentHeaderAsync(t);

            Recompute();
        }
        catch (Exception ex) { ErrorMessage = $"Generate failed: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void OpenMatch(ElimMatchRowVm row)
    {
        if (row is null || !row.IsTappable) return;
        // Fencers entered for spectating only — they must not navigate into the match screen.
        if (_session is not null && !_session.CanOpenMatches) return;
        MatchSelected?.Invoke(row.Match);
    }

    // ---------------- Live updates ----------------

    private void OnRemoteMatchUpdated(Match remote)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var t = _session?.Current;
            var bracket = t?.Bracket;
            if (bracket is null) return;

            // 1. Apply the remote update into the bracket model.
            bool touched = false;
            foreach (var round in bracket.Rounds)
            {
                var idx = round.Matches.FindIndex(m => m.Id == remote.Id);
                if (idx >= 0) { round.Matches[idx] = remote; touched = true; break; }
            }
            if (!touched && bracket.BronzeMatch?.Id == remote.Id)
            {
                bracket.BronzeMatch = remote;
                touched = true;
            }
            if (!touched) return;

            // 2. Cascade winners to downstream rounds / bronze in the model.
            TournamentEngine.PropagateAdvancements(bracket);

            // 3. Patch every bound row in place. We never Clear / re-add Columns
            //    here — rebuilding the bracket UI while a tap or swipe is in flight
            //    will crash the Android UI host because the recycled view's
            //    TapGestureRecognizer ends up dispatching on a freed handler.
            PatchRowsInPlace(bracket, t!);
        });
    }

    private void PatchRowsInPlace(EliminationBracket bracket, Tournament t)
    {
        // Quick id-to-match lookup of everything currently in the bracket.
        var modelById = new Dictionary<string, Match>(StringComparer.Ordinal);
        foreach (var round in bracket.Rounds)
            foreach (var m in round.Matches) modelById[m.Id] = m;
        if (bracket.BronzeMatch is not null) modelById[bracket.BronzeMatch.Id] = bracket.BronzeMatch;

        // Deduplicate by ID in case in-memory store has dupes from by-reference storage
        var nameById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in t.Fencers)
            nameById[f.Id] = f.Name;

        // Defensive: if the column structure no longer matches the bracket (e.g.
        // the bracket was regenerated under us), fall back to a full rebuild —
        // but this branch is not exercised during normal polling.
        bool structureMatches = true;
        foreach (var col in Columns)
        {
            foreach (var row in col.Matches)
            {
                if (!modelById.ContainsKey(row.Match.Id)) { structureMatches = false; break; }
            }
            if (!structureMatches) break;
        }
        if (!structureMatches) { Recompute(); return; }

        foreach (var col in Columns)
            foreach (var row in col.Matches)
                if (modelById.TryGetValue(row.Match.Id, out var latest))
                    row.Patch(latest, nameById);
    }

    public void Dispose() => _refresh.MatchUpdated -= OnRemoteMatchUpdated;
}