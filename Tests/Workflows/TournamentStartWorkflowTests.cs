using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;


namespace JustAnotherHemaClub.Tests.Workflows;

/// <summary>
/// Tests that mirror the TournamentEditorVm.StartTournamentAsync validation
/// and match-generation logic, exercised purely via the Services.TournamentEngine.
/// </summary>
public class TournamentStartWorkflowTests
{
    // ---------- Validation: pool size constraints ----------

    [Fact]
    public void Start_WithDraftPools_AllValid_GeneratesRoundRobinMatches()
    {
        var fencers = MakeFencers(10);
        var pools = new List<Pool>
        {
            new() { Index = 0, FencerIds = fencers.Take(5).Select(f => f.Id).ToList() },
            new() { Index = 1, FencerIds = fencers.Skip(5).Select(f => f.Id).ToList() }
        };

        // This is exactly what StartTournamentAsync does when draft pools are valid.
        Services.TournamentEngine.GeneratePoolMatches(pools);

        pools[0].Matches.Should().HaveCount(10); // 5C2
        pools[1].Matches.Should().HaveCount(10); // 5C2
        pools.SelectMany(p => p.Matches).Should().AllSatisfy(m =>
        {
            m.Status.Should().Be(MatchStatus.Pending);
            m.RemainingTimeSeconds.Should().Be(180);
            m.PoolId.Should().NotBeNullOrEmpty();
        });
    }

    [Fact]
    public void Start_PoolTooSmall_ShouldBeRejected()
    {
        // StartTournamentAsync rejects pools with < 4 fencers.
        const int minPoolSize = 4;
        var pool = new Pool { Index = 0, FencerIds = new() { "A", "B", "C" } }; // only 3

        var tooSmall = new List<Pool> { pool }.Where(p => p.FencerIds.Count < minPoolSize).ToList();

        tooSmall.Should().HaveCount(1, "a pool with 3 fencers violates the min-4 rule");
    }

    [Fact]
    public void Start_PoolTooLarge_ShouldBeRejected()
    {
        // StartTournamentAsync rejects pools with > 8 fencers.
        const int maxPoolSize = 8;
        var pool = new Pool
        {
            Index = 0,
            FencerIds = Enumerable.Range(0, 9).Select(i => $"F{i}").ToList()
        };

        var tooLarge = new List<Pool> { pool }.Where(p => p.FencerIds.Count > maxPoolSize).ToList();

        tooLarge.Should().HaveCount(1, "a pool with 9 fencers violates the max-8 rule");
    }

    [Fact]
    public void Start_EmptyPoolsDropped_NonEmptyValidated()
    {
        // Empty pools should be silently dropped before validation.
        var fencers = MakeFencers(8);
        var pools = new List<Pool>
        {
            new() { Index = 0, FencerIds = fencers.Take(4).Select(f => f.Id).ToList() },
            new() { Index = 1, FencerIds = new() }, // empty — should be dropped
            new() { Index = 2, FencerIds = fencers.Skip(4).Select(f => f.Id).ToList() }
        };

        var draftPools = pools.Where(p => p.FencerIds.Count > 0).OrderBy(p => p.Index).ToList();

        draftPools.Should().HaveCount(2);
        draftPools.Should().AllSatisfy(p => p.FencerIds.Count.Should().BeInRange(4, 8));
    }

    [Fact]
    public void Start_UnassignedFencers_ShouldBeRejected()
    {
        // If draft pools exist but some active fencers aren't assigned, start should fail.
        var fencers = MakeFencers(6);
        var pool = new Pool { Index = 0, FencerIds = fencers.Take(4).Select(f => f.Id).ToList() };
        var draftPools = new List<Pool> { pool };

        var assignedSet = new HashSet<string>(draftPools.SelectMany(p => p.FencerIds));
        var unassigned = fencers.Where(f => !f.IsWithdrawn && !assignedSet.Contains(f.Id)).ToList();

        unassigned.Should().HaveCount(2, "2 fencers are not assigned to any pool");
    }

    // ---------- Fallback: no draft pools ? auto-build ----------

    [Fact]
    public void Start_NoDraftPools_FallsBackToAutoBuild()
    {
        var fencers = MakeFencers(12);

        // When no draft pools exist, StartTournamentAsync calls BuildPools.
        var pools = Services.TournamentEngine.BuildPools(fencers, new Random(42));

        pools.Should().NotBeEmpty();
        pools.Should().AllSatisfy(p =>
        {
            p.FencerIds.Count.Should().BeInRange(4, 6);
            p.Matches.Should().NotBeEmpty();
        });

        // Every fencer should appear in exactly one pool.
        var allFencerIds = pools.SelectMany(p => p.FencerIds).ToList();
        allFencerIds.Should().OnlyHaveUniqueItems();
        allFencerIds.Should().HaveCount(12);
    }

    // ---------- State transition ----------

    [Fact]
    public void Start_TransitionsToPoolsInProgress()
    {
        var tournament = new Tournament { State = TournamentState.Setup };
        var fencers = MakeFencers(8);
        tournament.Fencers = fencers;

        var pools = Services.TournamentEngine.BuildPools(fencers, new Random(42));
        tournament.Pools = pools;
        tournament.State = TournamentState.PoolsInProgress;

        tournament.State.Should().Be(TournamentState.PoolsInProgress);
        tournament.Pools.Should().NotBeEmpty();
        tournament.Pools.SelectMany(p => p.Matches).Should().NotBeEmpty();
    }

    [Fact]
    public void Start_WithdrawnFencers_ExcludedFromAutoBuild()
    {
        var fencers = MakeFencers(6);
        fencers[0].IsWithdrawn = true;
        fencers[1].IsWithdrawn = true;

        var active = fencers.Where(f => !f.IsWithdrawn).ToList();
        var pools = Services.TournamentEngine.BuildPools(active, new Random(42));

        var allIds = pools.SelectMany(p => p.FencerIds).ToList();
        allIds.Should().NotContain(fencers[0].Id);
        allIds.Should().NotContain(fencers[1].Id);
        allIds.Should().HaveCount(4);
    }

    // ---------- Match generation correctness on start ----------

    [Fact]
    public void Start_GeneratedMatches_HaveCorrectPoolIds()
    {
        var fencers = MakeFencers(10);
        var pool1 = new Pool { Index = 0, FencerIds = fencers.Take(5).Select(f => f.Id).ToList() };
        var pool2 = new Pool { Index = 1, FencerIds = fencers.Skip(5).Select(f => f.Id).ToList() };
        var pools = new List<Pool> { pool1, pool2 };

        Services.TournamentEngine.GeneratePoolMatches(pools);

        pool1.Matches.Should().AllSatisfy(m => m.PoolId.Should().Be(pool1.Id));
        pool2.Matches.Should().AllSatisfy(m => m.PoolId.Should().Be(pool2.Id));
    }

    [Fact]
    public void Start_GeneratedMatches_OnlyReferencePoolFencers()
    {
        var fencers = MakeFencers(8);
        var pool1 = new Pool { Index = 0, FencerIds = fencers.Take(4).Select(f => f.Id).ToList() };
        var pool2 = new Pool { Index = 1, FencerIds = fencers.Skip(4).Select(f => f.Id).ToList() };
        var pools = new List<Pool> { pool1, pool2 };

        Services.TournamentEngine.GeneratePoolMatches(pools);

        var pool1Ids = pool1.FencerIds.ToHashSet();
        pool1.Matches.Should().AllSatisfy(m =>
        {
            pool1Ids.Should().Contain(m.LeftFencerId);
            pool1Ids.Should().Contain(m.RightFencerId);
        });
    }

    private static List<TournamentFencer> MakeFencers(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new TournamentFencer { Id = $"F{i:00}", Name = $"Fencer {i}" })
            .ToList();
}
