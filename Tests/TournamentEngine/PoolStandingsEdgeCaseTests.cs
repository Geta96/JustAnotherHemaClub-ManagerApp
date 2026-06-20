using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Tests.TournamentEngine;

public class PoolStandingsEdgeCaseTests
{
    [Fact]
    public void ComputePoolStandings_TiedWinPct_RankedByAvgPointsFor()
    {
        // A and B both 1W/2M (50%), but A has more points for
        var pool = new Pool { FencerIds = new() { "A", "B", "C" } };
        pool.Matches = new()
        {
            Finished("A", "B", 5, 3), // A wins
            Finished("B", "C", 4, 0), // B wins
            Finished("C", "A", 2, 1), // C wins
        };

        var standings = Services.TournamentEngine.ComputePoolStandings(pool);

        // All are 1W/2M. A: for=6, avg=3; B: for=7, avg=3.5; C: for=2, avg=1.
        standings[0].FencerId.Should().Be("B"); // highest avg for
        standings[1].FencerId.Should().Be("A");
        standings[2].FencerId.Should().Be("C");
    }

    [Fact]
    public void ComputePoolStandings_TiedWinPctAndFor_RankedByAvgPointsAgainst()
    {
        // Two fencers with same win% and same avg-for but different avg-against
        var pool = new Pool { FencerIds = new() { "A", "B", "C", "D" } };
        pool.Matches = new()
        {
            Finished("A", "B", 3, 1), // A wins
            Finished("C", "D", 3, 1), // C wins
            Finished("A", "C", 2, 3), // C wins
            Finished("B", "D", 2, 3), // D wins
            Finished("A", "D", 3, 0), // A wins
            Finished("B", "C", 3, 0), // B wins
        };

        var standings = Services.TournamentEngine.ComputePoolStandings(pool);

        // Verify standings have 4 entries
        standings.Should().HaveCount(4);
        // All entries should have MatchesPlayed = 3
        standings.Should().AllSatisfy(s => s.MatchesPlayed.Should().Be(3));
    }

    [Fact]
    public void ComputePoolStandings_EmptyPool_ReturnsAllWithZeroStats()
    {
        var pool = new Pool { FencerIds = new() { "A", "B", "C" } };
        pool.Matches = new(); // no matches at all

        var standings = Services.TournamentEngine.ComputePoolStandings(pool);

        standings.Should().HaveCount(3);
        standings.Should().AllSatisfy(s =>
        {
            s.MatchesPlayed.Should().Be(0);
            s.MatchesWon.Should().Be(0);
            s.PointsFor.Should().Be(0);
            s.PointsAgainst.Should().Be(0);
        });
    }

    [Fact]
    public void ComputePoolStandings_Windicator_CalculatedCorrectly()
    {
        var pool = new Pool { FencerIds = new() { "A", "B", "C" } };
        pool.Matches = new()
        {
            Finished("A", "B", 3, 1),
            Finished("A", "C", 2, 0),
            Finished("B", "C", 1, 0),
        };

        var standings = Services.TournamentEngine.ComputePoolStandings(pool);

        var a = standings.First(s => s.FencerId == "A");
        var b = standings.First(s => s.FencerId == "B");
        var c = standings.First(s => s.FencerId == "C");

        a.Windicator.Should().BeApproximately(1.0, 0.001); // 2/2
        b.Windicator.Should().BeApproximately(0.5, 0.001); // 1/2
        c.Windicator.Should().BeApproximately(0.0, 0.001); // 0/2
    }

    [Fact]
    public void ComputeGlobalStandings_MultiplePoolsWithDifferentSizes()
    {
        var t = new Tournament
        {
            Fencers = new()
            {
                new() { Id = "A" }, new() { Id = "B" }, new() { Id = "C" },
                new() { Id = "D" }, new() { Id = "E" }
            },
            Pools = new()
            {
                new Pool
                {
                    FencerIds = new() { "A", "B", "C" },
                    Matches = new()
                    {
                        Finished("A", "B", 5, 0),
                        Finished("A", "C", 5, 0),
                        Finished("B", "C", 3, 1),
                    }
                },
                new Pool
                {
                    FencerIds = new() { "D", "E" },
                    Matches = new()
                    {
                        Finished("D", "E", 1, 0),
                    }
                }
            }
        };

        var global = Services.TournamentEngine.ComputeGlobalStandings(t);

        global.Should().HaveCount(5);
        // A: 100% win rate, highest avg for
        global[0].FencerId.Should().Be("A");
        // D also 100% but lower avg for (1/match vs 5/match)
        global[1].FencerId.Should().Be("D");
    }

    [Fact]
    public void ComputePoolStandings_DrawMatch_NoWinnerSet_BothGetZeroWins()
    {
        var pool = new Pool { FencerIds = new() { "A", "B" } };
        pool.Matches = new()
        {
            new Match
            {
                LeftFencerId = "A", RightFencerId = "B",
                LeftScore = 2, RightScore = 2,
                Status = MatchStatus.Finished,
                WinnerFencerId = null // draw — no winner
            }
        };

        var standings = Services.TournamentEngine.ComputePoolStandings(pool);

        standings.Should().HaveCount(2);
        standings.Should().AllSatisfy(s =>
        {
            s.MatchesPlayed.Should().Be(1);
            s.MatchesWon.Should().Be(0);
        });
    }

    private static Match Finished(string left, string right, int ls, int rs) => new()
    {
        LeftFencerId = left,
        RightFencerId = right,
        LeftScore = ls,
        RightScore = rs,
        Status = MatchStatus.Finished,
        WinnerFencerId = ls > rs ? left : (rs > ls ? right : null)
    };
}
