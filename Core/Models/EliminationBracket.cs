namespace JustAnotherHemaClub.Models;

public class EliminationBracket
{
    /// <summary>Bracket size: 8, 16, 32, 64 or 128.</summary>
    public int Size { get; set; }

    /// <summary>Ordered rounds; index 0 = first round, last = final.</summary>
    public List<EliminationRound> Rounds { get; set; } = new();

    /// <summary>Bronze (3rd-place) match, played alongside the final.</summary>
    public Match? BronzeMatch { get; set; }
}

public class EliminationRound
{
    public int Index { get; set; }

    /// <summary>"Round of 16", "Quarter-finals", "Semi-finals", "Final".</summary>
    public string Name { get; set; } = string.Empty;

    public List<Match> Matches { get; set; } = new();
}