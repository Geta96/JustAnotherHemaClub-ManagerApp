using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Tests.TournamentEngine;

public class PoolPartitionTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(15)]
    [InlineData(20)]
    [InlineData(25)]
    [InlineData(30)]
    public void PartitionIntoPools_AllPoolsHave4To6Fencers(int fencerCount)
    {
        var pools = Services.TournamentEngine.PartitionIntoPools(fencerCount);

        pools.Should().NotBeEmpty();
        pools.SelectMany(p => p).Should().HaveCount(fencerCount);
        foreach (var pool in pools)
        {
            pool.Count.Should().BeInRange(4, 6,
                $"pool with {pool.Count} fencers violates 4-6 range (total fencers: {fencerCount})");
        }
    }

    [Fact]
    public void PartitionIntoPools_ThrowsWhenFewerThan4()
    {
        var act = () => Services.TournamentEngine.PartitionIntoPools(3);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PartitionIntoPools_EachFencerAppearsExactlyOnce()
    {
        var pools = Services.TournamentEngine.PartitionIntoPools(18);

        var allIndices = pools.SelectMany(p => p).ToList();
        allIndices.Should().OnlyHaveUniqueItems();
        allIndices.Should().HaveCount(18);
    }

    [Fact]
    public void PartitionIntoPools_WithRng_ShufflesFencers()
    {
        var rng = new Random(42);
        var pools1 = Services.TournamentEngine.PartitionIntoPools(12, rng);

        // Without RNG, indices are sequential
        var pools2 = Services.TournamentEngine.PartitionIntoPools(12, rng: null);

        // At least one pool should differ (with extremely high probability)
        var flat1 = pools1.SelectMany(p => p).ToList();
        var flat2 = pools2.SelectMany(p => p).ToList();
        flat1.Should().NotEqual(flat2);
    }
}
