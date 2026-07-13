using CommunityToolkit.Mvvm.ComponentModel;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.ViewModels;

/// <summary>Display row for a single elimination match (one card in a round column).</summary>
public sealed class ElimMatchRowVm : ObservableObject
{
    // Layout constants used by the bracket-tree visualization.
    private const double BaseCardHeight     = 110;
    private const double GapBetweenSiblings = 12;

    public Match Match { get; private set; }
    public string LeftName  { get; private set; }
    public string RightName { get; private set; }
    public string Tag       { get; }   // "Final", "Bronze" or ""

    /// <summary>0-based round index (0 = first round). Bronze always reports 0.</summary>
    public int  RoundIndex { get; }
    public bool IsFinal    { get; }
    public bool IsBronze   { get; }

    public ElimMatchRowVm(
        Match match,
        IReadOnlyDictionary<string, string> nameById,
        int roundIndex,
        bool isFinal,
        bool isBronze = false,
        string? overrideTag = null)
    {
        Match      = match;
        // An empty slot in round 0 (and not the bronze match) is a bye — that
        // fencer is never coming, so show "---" instead of the future-winner
        // placeholder "TBD" used by later rounds and the bronze match.
        var emptyPlaceholder = (roundIndex == 0 && !isBronze) ? "---" : "TBD";
        LeftName   = ResolveName(match.LeftFencerId,  nameById, emptyPlaceholder);
        RightName  = ResolveName(match.RightFencerId, nameById, emptyPlaceholder);
        Tag        = overrideTag ?? match.BracketTag ?? "";
        RoundIndex = roundIndex;
        IsFinal    = isFinal;
        IsBronze   = isBronze;
    }

    private static string ResolveName(
        string id,
        IReadOnlyDictionary<string, string> map,
        string emptyPlaceholder) =>
        string.IsNullOrEmpty(id) ? emptyPlaceholder : (map.TryGetValue(id, out var n) ? n : "?");

    public string ScoreText => Match.Status == MatchStatus.Pending
        ? "—"
        : $"{Match.LeftScore} – {Match.RightScore}";

    public string StatusText => Match.Status switch
    {
        MatchStatus.Pending    => "Pending",
        MatchStatus.InProgress => "In progress",
        MatchStatus.Finished   => "Finished",
        _                      => ""
    };

    public string StatusColor => Match.Status switch
    {
        MatchStatus.Pending    => "#8A8A8A",
        MatchStatus.InProgress => "#476FB5",
        MatchStatus.Finished   => "#1F8A2E",
        _                      => "#8A8A8A"
    };

    /// <summary>
    /// Tappable when both sides are populated (i.e. not a placeholder for a future
    /// winner). Finished matches are also tappable so an organiser can open them
    /// and reopen/correct the result while the downstream match is still Pending —
    /// the MatchViewModel enforces whether reopening is actually allowed.
    /// </summary>
    public bool IsTappable =>
        !string.IsNullOrEmpty(Match.LeftFencerId) &&
        !string.IsNullOrEmpty(Match.RightFencerId);

    public bool HasTag    => !string.IsNullOrEmpty(Tag);
    public bool LeftWon   => Match.Status == MatchStatus.Finished && Match.WinnerFencerId == Match.LeftFencerId;
    public bool RightWon  => Match.Status == MatchStatus.Finished && Match.WinnerFencerId == Match.RightFencerId;

    /// <summary>
    /// Replace the underlying match (and refresh derived display state) without
    /// recreating the row. Used by polling so the bound XAML view is preserved —
    /// rebuilding the row while a tap/swipe is in flight crashes the Android UI host.
    /// </summary>
    public void Patch(Match latest, IReadOnlyDictionary<string, string> nameById)
    {
        Match = latest;
        var emptyPlaceholder = (RoundIndex == 0 && !IsBronze) ? "---" : "TBD";
        LeftName  = ResolveName(latest.LeftFencerId,  nameById, emptyPlaceholder);
        RightName = ResolveName(latest.RightFencerId, nameById, emptyPlaceholder);

        OnPropertyChanged(nameof(LeftName));
        OnPropertyChanged(nameof(RightName));
        OnPropertyChanged(nameof(ScoreText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(IsTappable));
        OnPropertyChanged(nameof(LeftWon));
        OnPropertyChanged(nameof(RightWon));
    }

    // ---------------- Bracket-tree layout helpers ----------------

    /// <summary>Total height of this card's slot in the bracket grid.</summary>
    public double CardHeight => IsBronze
        ? BaseCardHeight
        : BaseCardHeight * Math.Pow(2, RoundIndex) + GapBetweenSiblings * (Math.Pow(2, RoundIndex) - 1);

    /// <summary>True when a vertical spine should be drawn on the LEFT (joining the two feeder cards).</summary>
    public bool HasLeftConnector  => RoundIndex > 0 && !IsBronze;

    /// <summary>True when a small horizontal tick should be drawn on the RIGHT (toward the next round).</summary>
    public bool HasRightConnector => !IsFinal && !IsBronze;

    /// <summary>
    /// Margin applied to the left spine so that it spans exactly from the upper feeder's
    /// vertical center down to the lower feeder's vertical center.
    /// </summary>
    public Thickness SpineMargin
    {
        get
        {
            if (!HasLeftConnector) return new Thickness(0);

            // Previous-round card height; the spine starts halfway down it and ends halfway up the lower one.
            var prevHeight = BaseCardHeight * Math.Pow(2, RoundIndex - 1)
                           + GapBetweenSiblings * (Math.Pow(2, RoundIndex - 1) - 1);
            var topAndBottom = prevHeight / 2.0;
            return new Thickness(0, topAndBottom, 0, topAndBottom);
        }
    }
}