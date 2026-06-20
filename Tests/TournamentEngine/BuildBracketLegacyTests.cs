using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Tests.TournamentEngine;

public class BuildBracketLegacyTests
{
    [Fact]
    public void BuildBracket_CreatesValidBracketFromGlobalStandings()
    {
        var t = CreateTournamentWithFinishedPools(fencersPerPool: 5, poolCount: 2);

        var bracket = Services.TournamentEngine.BuildBracket(t);

        bracket.Should().NotBeNull();
        bracket.Size.Should().BeGreaterThanOrEqualTo(8);
        bracket.Rounds.Should().NotBeEmpty();
        bracket.BronzeMatch.Should().NotBeNull();
        bracket.Rounds[^1].Matches[0].BracketTag.Should().Be("Final");
    }

    [Fact]
    public void BuildBracket_SeededBy60PercentOfField()
    {
        var t = CreateTournamentWithFinishedPools(fencersPerPool: 5, poolCount: 2); // 10 fencers

        var bracket = Services.TournamentEngine.BuildBracket(t);

        // 60% of 10 = 6, bracket size 8
        bracket.Size.Should().Be(8);
        // Should have round 1 with 4 matches (8/2)
        bracket.Rounds[0].Matches.Should().HaveCount(4);
    }

    [Fact]
    public void BuildBracket_AutoResolvesByes()
    {
        var t = CreateTournamentWithFinishedPools(fencersPerPool: 5, poolCount: 2); // 10 ? 6 seeded ? 2 byes

        var bracket = Services.TournamentEngine.BuildBracket(t);

        var byeMatches = bracket.Rounds[0].Matches
            .Where(m => m.Status == MatchStatus.Finished &&
                       (string.IsNullOrEmpty(m.LeftFencerId) || string.IsNullOrEmpty(m.RightFencerId)))
            .ToList();

        byeMatches.Should().HaveCount(2, "6 seeded in size-8 = 2 byes");
    }

    [Fact]
    public void BuildBracket_PropagatesByeWinnersToNextRound()
    {
        var t = CreateTournamentWithFinishedPools(fencersPerPool: 5, poolCount: 2);

        var bracket = Services.TournamentEngine.BuildBracket(t);

        // After propagation, next round should have some fencer IDs filled
        if (bracket.Rounds.Count > 1)
        {
            var round2 = bracket.Rounds[1];
            var filledSlots = round2.Matches
                .Where(m => !string.IsNullOrEmpty(m.LeftFencerId) || !string.IsNullOrEmpty(m.RightFencerId))
                .ToList();
            filledSlots.Should().NotBeEmpty("bye winners should propagate to next round");
        }
    }

    [Fact]
    public void IsBracketComplete_NoBronze_OnlyChecksFinal()
    {
        var bracket = new EliminationBracket { Size = 4 };
        bracket.Rounds.Add(new EliminationRound
        {
            Index = 0,
            Matches = new() { new Match { Status = MatchStatus.Finished }, new Match { Status = MatchStatus.Finished } }
        });
        bracket.Rounds.Add(new EliminationRound
        {
            Index = 1,
            Matches = new() { new Match { Status = MatchStatus.Finished, BracketTag = "Final" } }
        });
        bracket.BronzeMatch = null;

        Services.TournamentEngine.IsBracketComplete(bracket).Should().BeTrue();
    }

    [Fact]
    public void IsBracketComplete_EmptyBracket_ReturnsFalse()
    {
        var bracket = new EliminationBracket { Size = 8 };

        Services.TournamentEngine.IsBracketComplete(bracket).Should().BeFalse();
    }

    private static Tournament CreateTournamentWithFinishedPools(int fencersPerPool, int poolCount)
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
            Services.TournamentEngine.GeneratePoolMatches(new List<Pool> { pool });
            int score = fencersPerPool;
            foreach (var m in pool.Matches)
            {
                m.Status = MatchStatus.Finished;
                m.LeftScore = score--;
                m.RightScore = 1;
                m.WinnerFencerId = m.LeftFencerId;
            }
            t.Pools.Add(pool);
        }
        return t;
    }
}
