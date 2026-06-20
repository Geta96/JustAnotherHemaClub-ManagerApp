using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Tests.TournamentEngine;

public class PoolStandingsTests
{
    private static Pool CreateFinishedPool()
    {
        var pool = new Pool { FencerIds = new() { "A", "B", "C", "D" } };
        // A beats B 3-1, A beats C 2-0, A beats D 2-1
        // B beats C 1-0, B beats D 3-2
        // C beats D 2-1
        pool.Matches = new()
        {
            Finished("A", "B", 3, 1),
            Finished("A", "C", 2, 0),
            Finished("A", "D", 2, 1),
            Finished("B", "C", 1, 0),
            Finished("B", "D", 3, 2),
            Finished("C", "D", 2, 1),
        };
        return pool;
    }

    private static Match Finished(string left, string right, int ls, int rs) => new()
    {
        LeftFencerId = left,
        RightFencerId = right,
        LeftScore = ls,
        RightScore = rs,
        Status = MatchStatus.Finished,
        WinnerFencerId = ls > rs ? left : right
    };

    [Fact]
    public void ComputePoolStandings_RanksBy_WinPct_Then_AvgFor_Then_AvgAgainst()
    {
        var pool = CreateFinishedPool();
        var standings = Services.TournamentEngine.ComputePoolStandings(pool);

        // A: 3W/3M = 100%, B: 2W/3M ? 67%, C: 1W/3M ? 33%, D: 0W/3M = 0%
        standings.Should().HaveCount(4);
        standings[0].FencerId.Should().Be("A");
        standings[1].FencerId.Should().Be("B");
        standings[2].FencerId.Should().Be("C");
        standings[3].FencerId.Should().Be("D");
    }

    [Fact]
    public void ComputePoolStandings_CorrectStats()
    {
        var pool = CreateFinishedPool();
        var standings = Services.TournamentEngine.ComputePoolStandings(pool);

        var a = standings.First(s => s.FencerId == "A");
        a.MatchesPlayed.Should().Be(3);
        a.MatchesWon.Should().Be(3);
        a.PointsFor.Should().Be(7);     // 3+2+2
        a.PointsAgainst.Should().Be(2); // 1+0+1
    }

    [Fact]
    public void ComputePoolStandings_IgnoresPendingMatches()
    {
        var pool = new Pool { FencerIds = new() { "A", "B", "C", "D" } };
        pool.Matches = new()
        {
            Finished("A", "B", 3, 1),
            new Match { LeftFencerId = "A", RightFencerId = "C", Status = MatchStatus.Pending },
            new Match { LeftFencerId = "B", RightFencerId = "C", Status = MatchStatus.InProgress },
        };

        var standings = Services.TournamentEngine.ComputePoolStandings(pool);

        var a = standings.First(s => s.FencerId == "A");
        a.MatchesPlayed.Should().Be(1);
    }

    [Fact]
    public void ComputeGlobalStandings_CombinesAllPools()
    {
        var t = new Tournament
        {
            Fencers = new() { new() { Id = "A" }, new() { Id = "B" }, new() { Id = "C" }, new() { Id = "D" } },
            Pools = new()
            {
                new Pool
                {
                    FencerIds = new() { "A", "B" },
                    Matches = new() { Finished("A", "B", 3, 1) }
                },
                new Pool
                {
                    FencerIds = new() { "C", "D" },
                    Matches = new() { Finished("C", "D", 5, 0) }
                }
            }
        };

        var global = Services.TournamentEngine.ComputeGlobalStandings(t);

        global.Should().HaveCount(4);
        // C has higher avg points for (5 vs 3)
        global[0].FencerId.Should().Be("C");
        global[1].FencerId.Should().Be("A");
    }
}
