using JustAnotherHemaClub.Models;
using Engine = global::JustAnotherHemaClub.Services.TournamentEngine;

namespace JustAnotherHemaClub.Tests.EngineTests;

public class QualificationTests
{
    private static Tournament CreateTournamentWithPools(int fencersPerPool, int poolCount)
    {
        var t = new Tournament();
        for (int p = 0; p < poolCount; p++)
        {
            var pool = new Pool { Index = p };
            for (int f = 0; f < fencersPerPool; f++)
            {
                var id = $"P{p}F{f}";
                t.Fencers.Add(new TournamentFencer { Id = id });
                pool.FencerIds.Add(id);
            }
            // Generate round-robin and finish all matches (first fencer always wins)
            Engine.GeneratePoolMatches(new List<Pool> { pool });
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

    [Fact]
    public void ComputeQualifyingFencerIds_AllQualify_WhenFewerThan8Total()
    {
        var t = CreateTournamentWithPools(fencersPerPool: 4, poolCount: 1); // 4 total

        var qualifiers = Engine.ComputeQualifyingFencerIds(t);

        qualifiers.Should().HaveCount(4); // all qualify
    }

    [Fact]
    public void ComputeQualifyingFencerIds_AtLeast8Qualify_WhenEnoughFencers()
    {
        var t = CreateTournamentWithPools(fencersPerPool: 5, poolCount: 3); // 15 total

        var qualifiers = Engine.ComputeQualifyingFencerIds(t);

        qualifiers.Count.Should().BeGreaterThanOrEqualTo(8);
    }

    [Fact]
    public void ComputeQualifyingFencerIds_NoDuplicates()
    {
        var t = CreateTournamentWithPools(fencersPerPool: 5, poolCount: 2);

        var qualifiers = Engine.ComputeQualifyingFencerIds(t);

        qualifiers.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ComputeQualifyingFencerIds_EmptyPools_ReturnsEmpty()
    {
        var t = new Tournament { Pools = new() };
        Engine.ComputeQualifyingFencerIds(t).Should().BeEmpty();
    }

    [Fact]
    public void BuildBracketFromPoolStandings_CreatesValidBracket()
    {
        var t = CreateTournamentWithPools(fencersPerPool: 5, poolCount: 2);

        var bracket = Engine.BuildBracketFromPoolStandings(t);

        bracket.Should().NotBeNull();
        bracket.Rounds.Should().NotBeEmpty();
        bracket.BronzeMatch.Should().NotBeNull();
        bracket.Rounds[^1].Matches[0].BracketTag.Should().Be("Final");
    }
}
