using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;


namespace JustAnotherHemaClub.Tests.Workflows;

/// <summary>
/// End-to-end tournament lifecycle: Setup ? Pools ? Elimination ? Final standings.
/// Exercises the engine in the same sequence the app uses.
/// </summary>
public class TournamentFullLifecycleTests
{
    [Fact]
    public void FullLifecycle_8Fencers_ProducesCompleteStandings()
    {
        // 1. SETUP: create fencers
        var fencers = Enumerable.Range(0, 8)
            .Select(i => new TournamentFencer { Id = $"F{i}", Name = $"Fencer {i}" })
            .ToList();

        var tournament = new Tournament
        {
            State = TournamentState.Setup,
            Fencers = fencers
        };

        // 2. BUILD POOLS + generate matches (start)
        tournament.Pools = Services.TournamentEngine.BuildPools(fencers, new Random(42));
        tournament.State = TournamentState.PoolsInProgress;

        tournament.Pools.Should().NotBeEmpty();
        tournament.Pools.SelectMany(p => p.Matches).Should().NotBeEmpty();

        // 3. PLAY ALL POOL MATCHES (left fencer wins by index order)
        foreach (var pool in tournament.Pools)
        {
            foreach (var m in pool.Matches)
            {
                int leftIdx = int.Parse(m.LeftFencerId[1..]);
                int rightIdx = int.Parse(m.RightFencerId[1..]);
                m.Status = MatchStatus.Finished;
                if (leftIdx < rightIdx)
                {
                    m.LeftScore = 5; m.RightScore = 2;
                    m.WinnerFencerId = m.LeftFencerId;
                }
                else
                {
                    m.LeftScore = 2; m.RightScore = 5;
                    m.WinnerFencerId = m.RightFencerId;
                }
            }
        }

        // 4. CLOSE POOLS ? BUILD BRACKET
        tournament.State = TournamentState.PoolsClosed;
        var bracket = Services.TournamentEngine.BuildBracketFromPoolStandings(tournament);
        tournament.Bracket = bracket;
        tournament.State = TournamentState.EliminationInProgress;

        bracket.Should().NotBeNull();
        bracket.Size.Should().Be(8);
        bracket.Rounds.Should().NotBeEmpty();
        bracket.BronzeMatch.Should().NotBeNull();

        // 5. PLAY ALL BRACKET MATCHES (left always wins)
        foreach (var round in bracket.Rounds)
        {
            foreach (var m in round.Matches)
            {
                if (m.Status == MatchStatus.Finished) continue;
                if (string.IsNullOrEmpty(m.LeftFencerId) || string.IsNullOrEmpty(m.RightFencerId))
                    continue;

                m.Status = MatchStatus.Finished;
                m.LeftScore = 5; m.RightScore = 2;
                m.WinnerFencerId = m.LeftFencerId;
            }
            Services.TournamentEngine.PropagateAdvancements(bracket);
        }

        // Play bronze
        if (bracket.BronzeMatch is not null &&
            !string.IsNullOrEmpty(bracket.BronzeMatch.LeftFencerId) &&
            !string.IsNullOrEmpty(bracket.BronzeMatch.RightFencerId))
        {
            bracket.BronzeMatch.Status = MatchStatus.Finished;
            bracket.BronzeMatch.LeftScore = 5;
            bracket.BronzeMatch.RightScore = 2;
            bracket.BronzeMatch.WinnerFencerId = bracket.BronzeMatch.LeftFencerId;
        }

        // 6. VERIFY COMPLETE
        Services.TournamentEngine.IsBracketComplete(bracket).Should().BeTrue();
        tournament.State = TournamentState.Finished;

        // 7. COMPUTE FINAL STANDINGS
        var standings = Services.TournamentEngine.ComputeFinalStandings(tournament);

        standings.Should().HaveCount(8, "all 8 fencers should appear in final standings");
        standings.Should().OnlyHaveUniqueItems();
        standings[0].Should().NotBeNullOrEmpty("gold medal fencer");
    }

    [Fact]
    public void FullLifecycle_WithWithdrawal_StillCompletes()
    {
        // Setup
        var fencers = Enumerable.Range(0, 10)
            .Select(i => new TournamentFencer { Id = $"F{i}", Name = $"Fencer {i}" })
            .ToList();

        var tournament = new Tournament { State = TournamentState.Setup, Fencers = fencers };
        tournament.Pools = Services.TournamentEngine.BuildPools(fencers, new Random(42));
        tournament.State = TournamentState.PoolsInProgress;

        // Play a few matches
        var firstPool = tournament.Pools[0];
        foreach (var m in firstPool.Matches.Take(3))
        {
            m.Status = MatchStatus.Finished;
            m.LeftScore = 3; m.RightScore = 1;
            m.WinnerFencerId = m.LeftFencerId;
        }

        // WITHDRAW one fencer mid-pools
        var withdrawnId = firstPool.FencerIds[0];
        fencers.First(f => f.Id == withdrawnId).IsWithdrawn = true;
        var cascade = Services.TournamentEngine.ApplyWithdrawalCascade(tournament, withdrawnId);

        cascade.ChangedPoolMatches.Should().NotBeEmpty();

        // Finish remaining matches
        foreach (var pool in tournament.Pools)
        {
            foreach (var m in pool.Matches.Where(m => m.Status != MatchStatus.Finished))
            {
                m.Status = MatchStatus.Finished;
                m.LeftScore = 3; m.RightScore = 1;
                m.WinnerFencerId = m.LeftFencerId;
            }
        }

        // Build bracket and verify it works
        var bracket = Services.TournamentEngine.BuildBracketFromPoolStandings(tournament);
        tournament.Bracket = bracket;

        bracket.Should().NotBeNull();
        bracket.Rounds.Should().NotBeEmpty();
    }

    [Fact]
    public void FullLifecycle_MinimumField_4Fencers()
    {
        var fencers = Enumerable.Range(0, 4)
            .Select(i => new TournamentFencer { Id = $"F{i}", Name = $"Fencer {i}" })
            .ToList();

        var tournament = new Tournament { State = TournamentState.Setup, Fencers = fencers };
        tournament.Pools = Services.TournamentEngine.BuildPools(fencers, new Random(42));
        tournament.State = TournamentState.PoolsInProgress;

        // Single pool with 4 fencers ? 6 matches
        tournament.Pools.Should().HaveCount(1);
        tournament.Pools[0].Matches.Should().HaveCount(6);

        // Finish all pool matches
        foreach (var m in tournament.Pools[0].Matches)
        {
            m.Status = MatchStatus.Finished;
            m.LeftScore = 3; m.RightScore = 1;
            m.WinnerFencerId = m.LeftFencerId;
        }

        // Build bracket � all 4 qualify (< 8 rule)
        var qualifiers = Services.TournamentEngine.ComputeQualifyingFencerIds(tournament);
        qualifiers.Should().HaveCount(4);

        var bracket = Services.TournamentEngine.BuildBracketFromPoolStandings(tournament);
        bracket.Size.Should().Be(8); // minimum bracket size
    }
}
