using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Tests.TournamentEngine;

public class LargeBracketTests
{
    [Theory]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    public void PickBracketSize_LargeValues(int seeded)
    {
        var size = Services.TournamentEngine.PickBracketSize(seeded);
        size.Should().BeGreaterThanOrEqualTo(seeded);
        // Should be a power of 2
        (size & (size - 1)).Should().Be(0);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    public void BuildBracketSeedOrder_LargeSize_ContainsAllSeeds(int size)
    {
        var order = Services.TournamentEngine.BuildBracketSeedOrder(size);

        order.Should().HaveCount(size);
        order.Should().BeEquivalentTo(Enumerable.Range(1, size));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    public void BuildBracketSeedOrder_Seeds1And2InDifferentHalves(int size)
    {
        var order = Services.TournamentEngine.BuildBracketSeedOrder(size);

        var indexOf1 = Array.IndexOf(order, 1);
        var indexOf2 = Array.IndexOf(order, 2);
        int halfSize = size / 2;
        (indexOf1 / halfSize).Should().NotBe(indexOf2 / halfSize,
            "seeds 1 and 2 should be in different halves");
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    public void BuildBracketSeedOrder_TopFourInDifferentQuarters(int size)
    {
        var order = Services.TournamentEngine.BuildBracketSeedOrder(size);

        int quarterSize = size / 4;
        var quarters = new[] { 1, 2, 3, 4 }
            .Select(seed => Array.IndexOf(order, seed) / quarterSize)
            .ToList();

        quarters.Should().OnlyHaveUniqueItems("top 4 seeds should each be in a different quarter");
    }

    [Fact]
    public void BuildBracket_16FencerField_CreatesValidBracket()
    {
        var t = CreateTournamentWith16Fencers();

        var bracket = Services.TournamentEngine.BuildBracketFromPoolStandings(t);

        bracket.Size.Should().BeGreaterThanOrEqualTo(8);
        bracket.Rounds.Should().NotBeEmpty();
        bracket.BronzeMatch.Should().NotBeNull();
        bracket.Rounds[^1].Matches[0].BracketTag.Should().Be("Final");
    }

    [Fact]
    public void PartitionIntoPools_50Fencers_AllPoolsHave4To6()
    {
        var pools = Services.TournamentEngine.PartitionIntoPools(50);

        pools.Should().NotBeEmpty();
        pools.SelectMany(p => p).Should().HaveCount(50);
        foreach (var pool in pools)
        {
            pool.Count.Should().BeInRange(4, 6);
        }
    }

    [Fact]
    public void PartitionIntoPools_100Fencers_AllPoolsHave4To6()
    {
        var pools = Services.TournamentEngine.PartitionIntoPools(100);

        pools.Should().NotBeEmpty();
        pools.SelectMany(p => p).Should().HaveCount(100);
        foreach (var pool in pools)
        {
            pool.Count.Should().BeInRange(4, 6);
        }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(11)]
    [InlineData(13)]
    [InlineData(17)]
    [InlineData(19)]
    [InlineData(23)]
    [InlineData(37)]
    [InlineData(41)]
    public void PartitionIntoPools_VariousCounts_NoPoolLessThan4OrGreaterThan6(int count)
    {
        var pools = Services.TournamentEngine.PartitionIntoPools(count);

        pools.SelectMany(p => p).Should().HaveCount(count);
        foreach (var pool in pools)
        {
            pool.Count.Should().BeInRange(4, 6,
                $"pool with {pool.Count} fencers violates constraint (total: {count})");
        }
    }

    private static Tournament CreateTournamentWith16Fencers()
    {
        var t = new Tournament();
        var fencers = Enumerable.Range(0, 16)
            .Select(i => new TournamentFencer { Id = $"F{i:00}", Name = $"Fencer {i}" })
            .ToList();
        t.Fencers = fencers;

        var partitions = Services.TournamentEngine.PartitionIntoPools(16);
        foreach (var (indices, p) in partitions.Select((x, i) => (x, i)))
        {
            var pool = new Pool { Index = p, FencerIds = indices.Select(i => fencers[i].Id).ToList() };
            Services.TournamentEngine.GeneratePoolMatches(new List<Pool> { pool });
            // Finish all matches — leftFencer always wins
            foreach (var m in pool.Matches)
            {
                m.Status = MatchStatus.Finished;
                m.LeftScore = 3;
                m.RightScore = 1;
                m.WinnerFencerId = m.LeftFencerId;
            }
            t.Pools.Add(pool);
        }
        return t;
    }
}
