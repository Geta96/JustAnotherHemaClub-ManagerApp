using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Tests.TournamentEngine;

public class FinalStandingsEdgeCaseTests
{
    [Fact]
    public void ComputeFinalStandings_NoBracket_ReturnsPoolRankedFencers()
    {
        var pool = new Pool { FencerIds = new() { "A", "B", "C" } };
        pool.Matches = new()
        {
            Finished("A", "B", 5, 2),
            Finished("A", "C", 3, 1),
            Finished("B", "C", 4, 0),
        };

        var t = new Tournament
        {
            Fencers = new() { new() { Id = "A" }, new() { Id = "B" }, new() { Id = "C" } },
            Pools = new() { pool },
            Bracket = null
        };

        var standings = Services.TournamentEngine.ComputeFinalStandings(t);

        standings.Should().HaveCount(3);
        standings[0].Should().Be("A"); // 2W, highest avg for
        standings[1].Should().Be("B"); // 1W
        standings[2].Should().Be("C"); // 0W
    }

    [Fact]
    public void ComputeFinalStandings_EmptyTournament_ReturnsEmpty()
    {
        var t = new Tournament
        {
            Fencers = new(),
            Pools = new(),
            Bracket = null
        };

        var standings = Services.TournamentEngine.ComputeFinalStandings(t);

        standings.Should().BeEmpty();
    }

    [Fact]
    public void ComputeFinalStandings_8FencerBracket_OrdersEarlyRoundLosersByConquerorPlacement()
    {
        // 8-bracket: QF ? SF ? Final
        var bracket = new EliminationBracket { Size = 8 };

        var qf = new EliminationRound { Index = 0, Name = "Quarter-finals" };
        // A beats E, B beats F, C beats G, D beats H
        qf.Matches.Add(Finished("A", "E", 5, 1, bracketRound: 0, bracketSlot: 0));
        qf.Matches.Add(Finished("B", "F", 4, 2, bracketRound: 0, bracketSlot: 1));
        qf.Matches.Add(Finished("C", "G", 3, 0, bracketRound: 0, bracketSlot: 2));
        qf.Matches.Add(Finished("D", "H", 5, 3, bracketRound: 0, bracketSlot: 3));
        bracket.Rounds.Add(qf);

        var sf = new EliminationRound { Index = 1, Name = "Semi-finals" };
        // A beats B, C beats D
        sf.Matches.Add(Finished("A", "B", 5, 2, bracketRound: 1, bracketSlot: 0));
        sf.Matches.Add(Finished("C", "D", 4, 1, bracketRound: 1, bracketSlot: 1));
        bracket.Rounds.Add(sf);

        var final = new EliminationRound { Index = 2, Name = "Final" };
        final.Matches.Add(new Match
        {
            BracketRound = 2, BracketSlot = 0, BracketTag = "Final",
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
            Fencers = "ABCDEFGH".Select(c => new TournamentFencer { Id = c.ToString() }).ToList(),
            Pools = new(),
            Bracket = bracket
        };

        var standings = Services.TournamentEngine.ComputeFinalStandings(t);

        standings.Should().HaveCount(8);
        standings[0].Should().Be("A"); // Gold
        standings[1].Should().Be("C"); // Silver
        standings[2].Should().Be("B"); // Bronze winner
        standings[3].Should().Be("D"); // Bronze loser
        // 5–8 are the QF losers: E lost to A(1st), F lost to B(3rd), G lost to C(2nd), H lost to D(4th)
        // Order: E (conqueror A=1st), G (conqueror C=2nd), F (conqueror B=3rd), H (conqueror D=4th)
        standings[4].Should().Be("E");
        standings[5].Should().Be("G");
        standings[6].Should().Be("F");
        standings[7].Should().Be("H");
    }

    [Fact]
    public void ComputeFinalStandings_IncludesAllFencers_EvenWithdrawn()
    {
        var pool = new Pool { FencerIds = new() { "A", "B", "C", "D", "W" } };
        pool.Matches = new()
        {
            Finished("A", "B", 3, 1),
            Finished("A", "C", 2, 0),
            Finished("A", "D", 2, 1),
            Finished("A", "W", 3, 0), // walkover
            Finished("B", "C", 1, 0),
            Finished("B", "D", 3, 2),
            Finished("B", "W", 3, 0),
            Finished("C", "D", 2, 1),
            Finished("C", "W", 3, 0),
            Finished("D", "W", 3, 0),
        };

        var t = new Tournament
        {
            Fencers = new()
            {
                new() { Id = "A" }, new() { Id = "B" },
                new() { Id = "C" }, new() { Id = "D" },
                new() { Id = "W", IsWithdrawn = true }
            },
            Pools = new() { pool },
            Bracket = null
        };

        var standings = Services.TournamentEngine.ComputeFinalStandings(t);

        standings.Should().HaveCount(5);
        standings.Should().Contain("W");
    }

    [Fact]
    public void ComputePlacementOf_Maps1stTo1()
    {
        var t = new Tournament
        {
            Fencers = new() { new() { Id = "A" }, new() { Id = "B" } },
            Pools = new()
            {
                new Pool
                {
                    FencerIds = new() { "A", "B" },
                    Matches = new() { Finished("A", "B", 5, 1) }
                }
            },
            Bracket = null
        };

        var placement = Services.TournamentEngine.ComputePlacementOf(t);

        placement["A"].Should().Be(1);
        placement["B"].Should().Be(2);
    }

    private static Match Finished(string left, string right, int ls, int rs,
        int? bracketRound = null, int? bracketSlot = null) => new()
    {
        LeftFencerId = left,
        RightFencerId = right,
        LeftScore = ls,
        RightScore = rs,
        Status = MatchStatus.Finished,
        WinnerFencerId = ls > rs ? left : right,
        BracketRound = bracketRound,
        BracketSlot = bracketSlot
    };
}
