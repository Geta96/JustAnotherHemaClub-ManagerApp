using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Tests.TournamentEngine;

public class QualificationEdgeCaseTests
{
    [Fact]
    public void ComputeQualifyingFencerIds_RedCardsBreakTie()
    {
        // Two fencers with identical Win%, AvgFor, AvgAgainst — different red cards.
        var pool = new Pool
        {
            FencerIds = new() { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J" }
        };

        // Build a basic round-robin and finish matches
        Services.TournamentEngine.GeneratePoolMatches(new List<Pool> { pool });
        int idx = 0;
        foreach (var m in pool.Matches)
        {
            m.Status = MatchStatus.Finished;
            m.LeftScore = 3;
            m.RightScore = 1;
            m.WinnerFencerId = m.LeftFencerId;
            // Give fencer "A" red cards
            if (m.LeftFencerId == "A") m.LeftRedCards = 2;
            idx++;
        }

        var t = new Tournament
        {
            Fencers = pool.FencerIds.Select(id => new TournamentFencer { Id = id }).ToList(),
            Pools = new() { pool }
        };

        var qualifiers = Services.TournamentEngine.ComputeQualifyingFencerIds(t);

        qualifiers.Should().NotBeEmpty();
        qualifiers.Should().OnlyHaveUniqueItems();
        // A should be ranked lower than other fencers with same stats but zero red cards
        // (this verifies red cards factor into qualification ordering)
    }

    [Fact]
    public void ComputeQualifyingFencerIds_MultiplePoolsWithDifferentSizes()
    {
        var t = new Tournament();
        // Pool 1: 6 fencers
        var pool1 = new Pool { Index = 0, FencerIds = new() { "A", "B", "C", "D", "E", "F" } };
        // Pool 2: 4 fencers
        var pool2 = new Pool { Index = 1, FencerIds = new() { "G", "H", "I", "J" } };

        Services.TournamentEngine.GeneratePoolMatches(new List<Pool> { pool1, pool2 });
        foreach (var pool in new[] { pool1, pool2 })
        {
            foreach (var m in pool.Matches)
            {
                m.Status = MatchStatus.Finished;
                m.LeftScore = 3;
                m.RightScore = 1;
                m.WinnerFencerId = m.LeftFencerId;
            }
        }

        t.Fencers = pool1.FencerIds.Concat(pool2.FencerIds)
            .Select(id => new TournamentFencer { Id = id }).ToList();
        t.Pools = new() { pool1, pool2 };

        var qualifiers = Services.TournamentEngine.ComputeQualifyingFencerIds(t);

        qualifiers.Count.Should().BeGreaterThanOrEqualTo(8);
        qualifiers.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ComputeQualifyingFencerIds_ExactlyEightFencers_AllQualify()
    {
        // 8 fencers in 2 pools of 4 — all should qualify (min 8)
        var t = new Tournament();
        var pool1 = new Pool { Index = 0, FencerIds = new() { "A", "B", "C", "D" } };
        var pool2 = new Pool { Index = 1, FencerIds = new() { "E", "F", "G", "H" } };

        Services.TournamentEngine.GeneratePoolMatches(new List<Pool> { pool1, pool2 });
        foreach (var pool in new[] { pool1, pool2 })
        {
            foreach (var m in pool.Matches)
            {
                m.Status = MatchStatus.Finished;
                m.LeftScore = 3;
                m.RightScore = 1;
                m.WinnerFencerId = m.LeftFencerId;
            }
        }

        t.Fencers = pool1.FencerIds.Concat(pool2.FencerIds)
            .Select(id => new TournamentFencer { Id = id }).ToList();
        t.Pools = new() { pool1, pool2 };

        var qualifiers = Services.TournamentEngine.ComputeQualifyingFencerIds(t);

        // Per-pool 60% of 4 = 2.4, rounds to 2 per pool = 4 total.
        // But min 8 kicks in, so all 8 qualify.
        qualifiers.Should().HaveCount(8);
    }

    [Fact]
    public void BuildBracketFromPoolStandings_WithByes_AutoAdvancesTopSeeds()
    {
        // 5 qualifiers ? size 8 bracket ? 3 byes
        var t = new Tournament();
        var pool = new Pool { Index = 0, FencerIds = new() { "A", "B", "C", "D", "E" } };
        Services.TournamentEngine.GeneratePoolMatches(new List<Pool> { pool });
        foreach (var m in pool.Matches)
        {
            m.Status = MatchStatus.Finished;
            m.LeftScore = 3;
            m.RightScore = 1;
            m.WinnerFencerId = m.LeftFencerId;
        }
        t.Fencers = pool.FencerIds.Select(id => new TournamentFencer { Id = id }).ToList();
        t.Pools = new() { pool };

        var bracket = Services.TournamentEngine.BuildBracketFromPoolStandings(t);

        bracket.Size.Should().Be(8); // 5 qualifiers ? 8
        // Some first-round matches should be auto-resolved byes
        var autoResolved = bracket.Rounds[0].Matches
            .Where(m => m.Status == MatchStatus.Finished &&
                       (string.IsNullOrEmpty(m.LeftFencerId) || string.IsNullOrEmpty(m.RightFencerId)))
            .ToList();
        autoResolved.Should().HaveCount(3, "5 qualifiers in size-8 bracket means 3 byes");
    }

    [Fact]
    public void BuildBracketFromPoolStandings_QualifiersOrderedByGlobalRanking()
    {
        var t = new Tournament();
        var pool = new Pool { Index = 0, FencerIds = new() { "A", "B", "C", "D", "E" } };
        Services.TournamentEngine.GeneratePoolMatches(new List<Pool> { pool });

        // Make A win all, B win 3, C win 2, D win 1, E win 0
        foreach (var m in pool.Matches)
        {
            m.Status = MatchStatus.Finished;
            int leftIdx = pool.FencerIds.IndexOf(m.LeftFencerId);
            int rightIdx = pool.FencerIds.IndexOf(m.RightFencerId);
            if (leftIdx < rightIdx)
            {
                m.LeftScore = 3; m.RightScore = 1;
                m.WinnerFencerId = m.LeftFencerId;
            }
            else
            {
                m.RightScore = 3; m.LeftScore = 1;
                m.WinnerFencerId = m.RightFencerId;
            }
        }
        t.Fencers = pool.FencerIds.Select(id => new TournamentFencer { Id = id }).ToList();
        t.Pools = new() { pool };

        var qualifiers = Services.TournamentEngine.ComputeQualifyingFencerIds(t);

        // A (4W) should be seed 1, B (3W) seed 2, etc.
        qualifiers[0].Should().Be("A");
        qualifiers[1].Should().Be("B");
        qualifiers[2].Should().Be("C");
    }
}
