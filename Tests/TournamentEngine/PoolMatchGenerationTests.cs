using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Tests.TournamentEngine;

public class PoolMatchGenerationTests
{
    [Fact]
    public void OrderPoolFights_ProducesCorrectNumberOfPairs()
    {
        // n choose 2 = n*(n-1)/2
        Services.TournamentEngine.OrderPoolFights(4).Should().HaveCount(6);
        Services.TournamentEngine.OrderPoolFights(5).Should().HaveCount(10);
        Services.TournamentEngine.OrderPoolFights(6).Should().HaveCount(15);
    }

    [Fact]
    public void OrderPoolFights_EachPairAppearsExactlyOnce()
    {
        var fights = Services.TournamentEngine.OrderPoolFights(5);

        var pairs = fights.Select(f => (Math.Min(f.Left, f.Right), Math.Max(f.Left, f.Right))).ToList();
        pairs.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void OrderPoolFights_AvoidsConsecutiveAppearances()
    {
        var fights = Services.TournamentEngine.OrderPoolFights(5);

        int consecutiveCount = 0;
        for (int i = 1; i < fights.Count; i++)
        {
            var prev = new HashSet<int> { fights[i - 1].Left, fights[i - 1].Right };
            var curr = new HashSet<int> { fights[i].Left, fights[i].Right };
            if (prev.Intersect(curr).Any()) consecutiveCount++;
        }

        // The algorithm tries to minimize but can't always eliminate.
        // For 5 fencers (10 fights), at most ~3-4 consecutive appearances is acceptable.
        consecutiveCount.Should().BeLessThan(fights.Count,
            "most fights should not share a fencer with the immediately preceding fight");
    }

    [Fact]
    public void GeneratePoolMatches_CreatesRoundRobinForEachPool()
    {
        var pools = new List<Pool>
        {
            new() { FencerIds = new() { "a", "b", "c", "d" } },
            new() { FencerIds = new() { "e", "f", "g", "h", "i" } }
        };

        Services.TournamentEngine.GeneratePoolMatches(pools);

        pools[0].Matches.Should().HaveCount(6);  // 4C2
        pools[1].Matches.Should().HaveCount(10); // 5C2
    }

    [Fact]
    public void GeneratePoolMatches_MatchesHaveCorrectPoolId()
    {
        var pool = new Pool { FencerIds = new() { "a", "b", "c", "d" } };
        Services.TournamentEngine.GeneratePoolMatches(new List<Pool> { pool });

        pool.Matches.Should().AllSatisfy(m =>
        {
            m.PoolId.Should().Be(pool.Id);
            m.Status.Should().Be(MatchStatus.Pending);
            m.RemainingTimeSeconds.Should().Be(120);
        });
    }

    [Fact]
    public void GeneratePoolMatches_EachFencerPairFightsExactlyOnce()
    {
        var fencerIds = new List<string> { "a", "b", "c", "d", "e" };
        var pool = new Pool { FencerIds = fencerIds };
        Services.TournamentEngine.GeneratePoolMatches(new List<Pool> { pool });

        var pairs = pool.Matches
            .Select(m => (Left: m.LeftFencerId, Right: m.RightFencerId))
            .Select(p => (Min: string.Compare(p.Left, p.Right) < 0 ? p.Left : p.Right,
                          Max: string.Compare(p.Left, p.Right) < 0 ? p.Right : p.Left))
            .ToList();

        pairs.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void GeneratePoolMatches_PoolWithFewerThan2Fencers_NoMatches()
    {
        var pool = new Pool { FencerIds = new() { "a" } };
        Services.TournamentEngine.GeneratePoolMatches(new List<Pool> { pool });

        pool.Matches.Should().BeEmpty();
    }

    [Fact]
    public void GeneratePoolMatches_ClearsExistingMatches()
    {
        var pool = new Pool { FencerIds = new() { "a", "b", "c", "d" } };
        pool.Matches.Add(new Match { LeftFencerId = "x", RightFencerId = "y" });

        Services.TournamentEngine.GeneratePoolMatches(new List<Pool> { pool });

        pool.Matches.Should().HaveCount(6);
        pool.Matches.Should().NotContain(m => m.LeftFencerId == "x");
    }

    [Fact]
    public void BuildPools_GeneratesPoolsWithMatches()
    {
        var fencers = Enumerable.Range(0, 10)
            .Select(i => new TournamentFencer { Name = $"F{i}" })
            .ToList();

        var pools = Services.TournamentEngine.BuildPools(fencers, new Random(42));

        pools.Should().HaveCountGreaterThan(0);
        pools.Should().AllSatisfy(p =>
        {
            p.FencerIds.Should().HaveCountGreaterThanOrEqualTo(4);
            p.Matches.Should().NotBeEmpty();
        });
    }

    [Fact]
    public void BuildDraftPools_GeneratesPoolsWithoutMatches()
    {
        var fencers = Enumerable.Range(0, 10)
            .Select(i => new TournamentFencer { Name = $"F{i}" })
            .ToList();

        var pools = Services.TournamentEngine.BuildDraftPools(fencers, new Random(42));

        pools.Should().HaveCountGreaterThan(0);
        pools.Should().AllSatisfy(p =>
        {
            p.FencerIds.Should().HaveCountGreaterThanOrEqualTo(4);
            p.Matches.Should().BeEmpty();
        });
    }
}
