namespace JustAnotherHemaClub.ViewModels;

/// <summary>One fencer's row in a pool's standings, with derived stats + display strings.</summary>
public sealed class PoolStandingRowVm
{
    public string FencerId { get; }
    public string Name { get; }
    public int Rank { get; }
    public int MatchesDone { get; }
    public int Wins { get; }
    public int Losses { get; }
    public int Draws { get; }
    public int PointsFor { get; }
    public int PointsAgainst { get; }
    public int RedCards { get; }
    public double WinPercent { get; }
    public double AvgPointsFor { get; }
    public double AvgPointsAgainst { get; }

    /// <summary>True when a "↑ Qualified for elimination" separator should render below this row.</summary>
    public bool ShowQualificationSeparator { get; }

    public PoolStandingRowVm(
        string fencerId, string name, int rank,
        int matchesDone, int wins, int draws, int pointsFor, int pointsAgainst, int redCards,
        bool showQualificationSeparator)
    {
        FencerId      = fencerId;
        Name          = name;
        Rank          = rank;
        MatchesDone   = matchesDone;
        Wins          = wins;
        Draws         = draws;
        Losses        = System.Math.Max(0, matchesDone - wins - draws);
        PointsFor     = pointsFor;
        PointsAgainst = pointsAgainst;
        RedCards      = redCards;
        WinPercent       = matchesDone == 0 ? 0 : (double)wins          / matchesDone;
        AvgPointsFor     = matchesDone == 0 ? 0 : (double)pointsFor     / matchesDone;
        AvgPointsAgainst = matchesDone == 0 ? 0 : (double)pointsAgainst / matchesDone;
        ShowQualificationSeparator = showQualificationSeparator;
    }

    public string RankText        => $"#{Rank}";
    public string WinsText        => Wins.ToString();
    public string LossesText      => Losses.ToString();
    public string DrawsText       => Draws.ToString();
    public string WinPctText      => MatchesDone == 0 ? "—" : $"{WinPercent * 100:0}%";
    public string AvgForText      => MatchesDone == 0 ? "—" : AvgPointsFor.ToString("0.0");
    public string AvgAgainstText  => MatchesDone == 0 ? "—" : AvgPointsAgainst.ToString("0.0");
    public string RedCardsText    => RedCards.ToString();
}