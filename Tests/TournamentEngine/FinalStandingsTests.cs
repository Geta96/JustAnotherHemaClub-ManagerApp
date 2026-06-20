using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Tests.TournamentEngine;

public class FinalStandingsTests
{
    [Fact]
    public void ComputeFinalStandings_PlacesGoldSilverBronzeCorrectly()
    {
        var bracket = new EliminationBracket { Size = 4 };

        var semis = new EliminationRound { Index = 0, Name = "Semi-finals" };
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

        var final = new EliminationRound { Index = 1, Name = "Final" };
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
            Status = MatchStatus.Finished, WinnerFencerId = "D"
        };

        var t = new Tournament
        {
            Fencers = new() { new() { Id = "A" }, new() { Id = "B" }, new() { Id = "C" }, new() { Id = "D" } },
            Pools = new(),
            Bracket = bracket
        };

        var standings = Services.TournamentEngine.ComputeFinalStandings(t);

        standings[0].Should().Be("A"); // Gold
        standings[1].Should().Be("C"); // Silver (loser of final)
        standings[2].Should().Be("D"); // Bronze winner
        standings[3].Should().Be("B"); // Bronze loser
    }

    [Fact]
    public void ComputeFinalStandings_AppendsFencersNotInBracket()
    {
        var t = new Tournament
        {
            Fencers = new()
            {
                new() { Id = "A" }, new() { Id = "B" },
                new() { Id = "C" }, new() { Id = "D" },
                new() { Id = "E" }  // not in bracket
            },
            Pools = new()
            {
                new Pool
                {
                    FencerIds = new() { "A", "B", "C", "D", "E" },
                    Matches = new()
                    {
                        new Match { LeftFencerId = "E", RightFencerId = "D", LeftScore = 3, RightScore = 0,
                                    Status = MatchStatus.Finished, WinnerFencerId = "E" }
                    }
                }
            },
            Bracket = CreateMinimalBracket("A", "B", "C", "D")
        };

        var standings = Services.TournamentEngine.ComputeFinalStandings(t);

        standings.Should().Contain("E");
        standings.IndexOf("E").Should().BeGreaterThan(3); // after the bracket 4
    }

    private static EliminationBracket CreateMinimalBracket(string a, string b, string c, string d)
    {
        var bracket = new EliminationBracket { Size = 4 };
        var semis = new EliminationRound { Index = 0 };
        semis.Matches.Add(new Match
        {
            BracketRound = 0, BracketSlot = 0,
            LeftFencerId = a, RightFencerId = b,
            Status = MatchStatus.Finished, WinnerFencerId = a
        });
        semis.Matches.Add(new Match
        {
            BracketRound = 0, BracketSlot = 1,
            LeftFencerId = c, RightFencerId = d,
            Status = MatchStatus.Finished, WinnerFencerId = c
        });
        bracket.Rounds.Add(semis);

        var final = new EliminationRound { Index = 1 };
        final.Matches.Add(new Match
        {
            BracketRound = 1, BracketSlot = 0, BracketTag = "Final",
            LeftFencerId = a, RightFencerId = c,
            Status = MatchStatus.Finished, WinnerFencerId = a
        });
        bracket.Rounds.Add(final);

        bracket.BronzeMatch = new Match
        {
            BracketTag = "Bronze",
            LeftFencerId = b, RightFencerId = d,
            Status = MatchStatus.Finished, WinnerFencerId = b
        };
        return bracket;
    }
}
