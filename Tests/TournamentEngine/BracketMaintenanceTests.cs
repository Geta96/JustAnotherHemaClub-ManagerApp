using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Tests.TournamentEngine;

public class BracketMaintenanceTests
{
    [Fact]
    public void PatchInBracket_ReplacesMatchInRound()
    {
        var bracket = new EliminationBracket { Size = 4 };
        var round = new EliminationRound { Index = 0 };
        var originalMatch = new Match { BracketRound = 0, BracketSlot = 0, LeftFencerId = "A", RightFencerId = "B" };
        round.Matches.Add(originalMatch);
        bracket.Rounds.Add(round);

        var updatedMatch = new Match
        {
            Id = originalMatch.Id,
            BracketRound = 0,
            BracketSlot = 0,
            LeftFencerId = "A",
            RightFencerId = "B",
            LeftScore = 5,
            RightScore = 2,
            Status = MatchStatus.Finished,
            WinnerFencerId = "A"
        };

        Services.TournamentEngine.PatchInBracket(bracket, updatedMatch);

        bracket.Rounds[0].Matches[0].Status.Should().Be(MatchStatus.Finished);
        bracket.Rounds[0].Matches[0].LeftScore.Should().Be(5);
        bracket.Rounds[0].Matches[0].WinnerFencerId.Should().Be("A");
    }

    [Fact]
    public void PatchInBracket_ReplacesBronzeMatch()
    {
        var bracket = new EliminationBracket { Size = 4 };
        bracket.Rounds.Add(new EliminationRound { Index = 0 });
        var bronzeMatch = new Match { BracketTag = "Bronze", LeftFencerId = "C", RightFencerId = "D" };
        bracket.BronzeMatch = bronzeMatch;

        var updatedBronze = new Match
        {
            Id = bronzeMatch.Id,
            BracketTag = "Bronze",
            LeftFencerId = "C",
            RightFencerId = "D",
            Status = MatchStatus.Finished,
            WinnerFencerId = "C"
        };

        Services.TournamentEngine.PatchInBracket(bracket, updatedBronze);

        bracket.BronzeMatch.Status.Should().Be(MatchStatus.Finished);
        bracket.BronzeMatch.WinnerFencerId.Should().Be("C");
    }

    [Fact]
    public void PropagateAndCollectChanges_ReturnsChangedMatches()
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
            Status = MatchStatus.Finished, WinnerFencerId = "C"
        });
        bracket.Rounds.Add(round1);

        var final = new EliminationRound { Index = 1, Name = "Final" };
        final.Matches.Add(new Match { BracketRound = 1, BracketSlot = 0, BracketTag = "Final" });
        bracket.Rounds.Add(final);
        bracket.BronzeMatch = new Match { BracketTag = "Bronze" };

        var changed = Services.TournamentEngine.PropagateAndCollectChanges(bracket);

        changed.Should().NotBeEmpty();
        // Final should have gotten A and C
        var finalMatch = bracket.Rounds[1].Matches[0];
        finalMatch.LeftFencerId.Should().Be("A");
        finalMatch.RightFencerId.Should().Be("C");
        // Bronze should have gotten B and D
        bracket.BronzeMatch.LeftFencerId.Should().Be("B");
        bracket.BronzeMatch.RightFencerId.Should().Be("D");
    }

    [Fact]
    public void PropagateAndCollectChanges_ReturnsEmpty_WhenNothingChanged()
    {
        var bracket = new EliminationBracket { Size = 4 };
        var round1 = new EliminationRound { Index = 0, Name = "Semi-finals" };
        round1.Matches.Add(new Match
        {
            BracketRound = 0, BracketSlot = 0,
            LeftFencerId = "A", RightFencerId = "B",
            Status = MatchStatus.Pending // not finished yet
        });
        round1.Matches.Add(new Match
        {
            BracketRound = 0, BracketSlot = 1,
            LeftFencerId = "C", RightFencerId = "D",
            Status = MatchStatus.Pending
        });
        bracket.Rounds.Add(round1);

        var final = new EliminationRound { Index = 1, Name = "Final" };
        final.Matches.Add(new Match { BracketRound = 1, BracketSlot = 0, BracketTag = "Final" });
        bracket.Rounds.Add(final);
        bracket.BronzeMatch = new Match { BracketTag = "Bronze" };

        var changed = Services.TournamentEngine.PropagateAndCollectChanges(bracket);

        changed.Should().BeEmpty();
    }

    [Fact]
    public void ComputePlacementOf_ReturnsDictionary()
    {
        var bracket = new EliminationBracket { Size = 4 };
        var semis = new EliminationRound { Index = 0 };
        semis.Matches.Add(new Match
        {
            BracketRound = 0, BracketSlot = 0,
            LeftFencerId = "A", RightFencerId = "B",
            Status = MatchStatus.Finished, WinnerFencerId = "A"
        });
        semis.Matches.Add(new Match
        {
            BracketRound = 0, BracketSlot = 1,
            LeftFencerId = "C", RightFencerId = "D",
            Status = MatchStatus.Finished, WinnerFencerId = "C"
        });
        bracket.Rounds.Add(semis);

        var final = new EliminationRound { Index = 1 };
        final.Matches.Add(new Match
        {
            BracketRound = 1, BracketSlot = 0, BracketTag = "Final",
            LeftFencerId = "A", RightFencerId = "C",
            Status = MatchStatus.Finished, WinnerFencerId = "A"
        });
        bracket.Rounds.Add(final);

        bracket.BronzeMatch = new Match
        {
            BracketTag = "Bronze",
            LeftFencerId = "B", RightFencerId = "D",
            Status = MatchStatus.Finished, WinnerFencerId = "B"
        };

        var t = new Tournament
        {
            Fencers = new() { new() { Id = "A" }, new() { Id = "B" }, new() { Id = "C" }, new() { Id = "D" } },
            Pools = new(),
            Bracket = bracket
        };

        var placement = Services.TournamentEngine.ComputePlacementOf(t);

        placement["A"].Should().Be(1); // Gold
        placement["C"].Should().Be(2); // Silver
        placement["B"].Should().Be(3); // Bronze winner
        placement["D"].Should().Be(4); // Bronze loser
    }
}
