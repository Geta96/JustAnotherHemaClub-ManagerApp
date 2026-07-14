using JustAnotherHemaClub.Models;
using Engine = global::JustAnotherHemaClub.Services.TournamentEngine;

namespace JustAnotherHemaClub.Tests.EngineTests;

public class BracketTests
{
    [Theory]
    [InlineData(3, 4)]
    [InlineData(4, 4)]
    [InlineData(5, 8)]
    [InlineData(8, 8)]
    [InlineData(9, 16)]
    [InlineData(16, 16)]
    [InlineData(17, 32)]
    public void PickBracketSize_RoundsUpToNextPowerOf2(int seeded, int expected)
    {
        Engine.PickBracketSize(seeded).Should().Be(expected);
    }

    [Theory]
    [InlineData(2, "Final")]
    [InlineData(4, "Semi-finals")]
    [InlineData(8, "Quarter-finals")]
    [InlineData(16, "Round of 16")]
    public void RoundName_ReturnsExpected(int participants, string expected)
    {
        Engine.RoundName(participants).Should().Be(expected);
    }

    [Fact]
    public void BuildBracketSeedOrder_Size8_SeparatesTopSeeds()
    {
        var order = Engine.BuildBracketSeedOrder(8);

        order.Should().HaveCount(8);
        // Seeds 1 and 2 should be in different halves
        var indexOf1 = Array.IndexOf(order, 1);
        var indexOf2 = Array.IndexOf(order, 2);
        (indexOf1 / 4).Should().NotBe(indexOf2 / 4, "seeds 1 and 2 should be in different halves");
    }

    [Fact]
    public void BuildBracketSeedOrder_ContainsAllSeeds()
    {
        var order = Engine.BuildBracketSeedOrder(16);

        order.Should().HaveCount(16);
        order.Should().BeEquivalentTo(Enumerable.Range(1, 16));
    }

    [Fact]
    public void PropagateAdvancements_PromotesWinnersToNextRound()
    {
        var bracket = new EliminationBracket { Size = 4 };
        var round1 = new EliminationRound { Index = 0, Name = "Semi-finals" };
        round1.Matches.Add(new Match
        {
            BracketRound = 0, BracketSlot = 0,
            LeftFencerId = "A", RightFencerId = "B",
            Status = MatchStatus.Finished, WinnerFencerId = "A"
        });
        round1.Matches.Add(new Match
        {
            BracketRound = 0, BracketSlot = 1,
            LeftFencerId = "C", RightFencerId = "D",
            Status = MatchStatus.Finished, WinnerFencerId = "D"
        });
        bracket.Rounds.Add(round1);

        var final = new EliminationRound { Index = 1, Name = "Final" };
        final.Matches.Add(new Match { BracketRound = 1, BracketSlot = 0, BracketTag = "Final" });
        bracket.Rounds.Add(final);

        bracket.BronzeMatch = new Match { BracketTag = "Bronze" };

        Engine.PropagateAdvancements(bracket);

        var finalMatch = bracket.Rounds[1].Matches[0];
        finalMatch.LeftFencerId.Should().Be("A");
        finalMatch.RightFencerId.Should().Be("D");

        bracket.BronzeMatch.LeftFencerId.Should().Be("B");
        bracket.BronzeMatch.RightFencerId.Should().Be("C");
    }

    [Fact]
    public void PropagateAdvancements_AutoByeWhenOneSlotEmpty()
    {
        var bracket = new EliminationBracket { Size = 4 };
        var round1 = new EliminationRound { Index = 0, Name = "Semi-finals" };
        round1.Matches.Add(new Match
        {
            BracketRound = 0, BracketSlot = 0,
            LeftFencerId = "A", RightFencerId = "",
            Status = MatchStatus.Finished, WinnerFencerId = "A"
        });
        round1.Matches.Add(new Match
        {
            BracketRound = 0, BracketSlot = 1,
            LeftFencerId = "C", RightFencerId = "",
            Status = MatchStatus.Finished, WinnerFencerId = "C"
        });
        bracket.Rounds.Add(round1);

        var final = new EliminationRound { Index = 1, Name = "Final" };
        final.Matches.Add(new Match { BracketRound = 1, BracketSlot = 0, BracketTag = "Final" });
        bracket.Rounds.Add(final);

        bracket.BronzeMatch = new Match { BracketTag = "Bronze" };

        Engine.PropagateAdvancements(bracket);

        var finalMatch = bracket.Rounds[1].Matches[0];
        finalMatch.LeftFencerId.Should().Be("A");
        finalMatch.RightFencerId.Should().Be("C");
    }

    [Fact]
    public void IsBracketComplete_ReturnsFalse_WhenFinalNotFinished()
    {
        var bracket = new EliminationBracket { Size = 4 };
        bracket.Rounds.Add(new EliminationRound
        {
            Index = 0,
            Matches = new() { new Match { Status = MatchStatus.Finished }, new Match { Status = MatchStatus.Finished } }
        });
        bracket.Rounds.Add(new EliminationRound
        {
            Index = 1,
            Matches = new() { new Match { Status = MatchStatus.Pending, BracketTag = "Final" } }
        });
        bracket.BronzeMatch = new Match { Status = MatchStatus.Finished, BracketTag = "Bronze" };

        Engine.IsBracketComplete(bracket).Should().BeFalse();
    }

    [Fact]
    public void IsBracketComplete_ReturnsTrue_WhenFinalAndBronzeFinished()
    {
        var bracket = new EliminationBracket { Size = 4 };
        bracket.Rounds.Add(new EliminationRound
        {
            Index = 0,
            Matches = new() { new Match { Status = MatchStatus.Finished }, new Match { Status = MatchStatus.Finished } }
        });
        bracket.Rounds.Add(new EliminationRound
        {
            Index = 1,
            Matches = new() { new Match { Status = MatchStatus.Finished, BracketTag = "Final" } }
        });
        bracket.BronzeMatch = new Match { Status = MatchStatus.Finished, BracketTag = "Bronze" };

        Engine.IsBracketComplete(bracket).Should().BeTrue();
    }
}
