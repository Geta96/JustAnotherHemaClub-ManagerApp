using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Tests.Models;

public class EliminationBracketTests
{
    [Fact]
    public void NewEliminationBracket_HasEmptyRoundsAndNoBronze()
    {
        var bracket = new EliminationBracket();

        bracket.Rounds.Should().BeEmpty();
        bracket.BronzeMatch.Should().BeNull();
        bracket.Size.Should().Be(0);
    }

    [Fact]
    public void EliminationRound_HasEmptyMatches()
    {
        var round = new EliminationRound();

        round.Matches.Should().BeEmpty();
        round.Name.Should().BeEmpty();
        round.Index.Should().Be(0);
    }

    [Fact]
    public void EliminationBracket_CanBuildMultipleRounds()
    {
        var bracket = new EliminationBracket { Size = 8 };
        bracket.Rounds.Add(new EliminationRound { Index = 0, Name = "Quarter-finals" });
        bracket.Rounds.Add(new EliminationRound { Index = 1, Name = "Semi-finals" });
        bracket.Rounds.Add(new EliminationRound { Index = 2, Name = "Final" });

        bracket.Rounds.Should().HaveCount(3);
        bracket.Rounds[0].Name.Should().Be("Quarter-finals");
        bracket.Rounds[2].Name.Should().Be("Final");
    }
}
