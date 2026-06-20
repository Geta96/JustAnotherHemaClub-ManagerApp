using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Tests.TournamentEngine;

public class WithdrawalCascadeTests
{
    [Fact]
    public void ApplyWithdrawalCascade_WalksOverPendingPoolMatches()
    {
        var t = new Tournament
        {
            State = TournamentState.PoolsInProgress,
            Fencers = new() { new() { Id = "A" }, new() { Id = "B" }, new() { Id = "C" } },
            Pools = new()
            {
                new Pool
                {
                    FencerIds = new() { "A", "B", "C" },
                    Matches = new()
                    {
                        new Match { LeftFencerId = "A", RightFencerId = "B", Status = MatchStatus.Pending },
                        new Match { LeftFencerId = "A", RightFencerId = "C", Status = MatchStatus.Pending },
                        new Match { LeftFencerId = "B", RightFencerId = "C", Status = MatchStatus.Pending },
                    }
                }
            }
        };

        var cascade = Services.TournamentEngine.ApplyWithdrawalCascade(t, "A");

        cascade.ChangedPoolMatches.Should().HaveCount(2); // A vs B, A vs C
        cascade.ChangedPoolMatches.Should().AllSatisfy(m =>
        {
            m.Status.Should().Be(MatchStatus.Finished);
            m.WinnerFencerId.Should().NotBe("A");
            m.LeftScore.Should().Be(0);
            m.RightScore.Should().Be(0);
        });

        // B vs C should remain pending
        t.Pools[0].Matches[2].Status.Should().Be(MatchStatus.Pending);
    }

    [Fact]
    public void ApplyWithdrawalCascade_DoesNotTouchFinishedMatches()
    {
        var t = new Tournament
        {
            State = TournamentState.PoolsInProgress,
            Fencers = new() { new() { Id = "A" }, new() { Id = "B" } },
            Pools = new()
            {
                new Pool
                {
                    FencerIds = new() { "A", "B" },
                    Matches = new()
                    {
                        new Match
                        {
                            LeftFencerId = "A", RightFencerId = "B",
                            LeftScore = 5, RightScore = 2,
                            Status = MatchStatus.Finished, WinnerFencerId = "A"
                        }
                    }
                }
            }
        };

        var cascade = Services.TournamentEngine.ApplyWithdrawalCascade(t, "A");

        cascade.ChangedPoolMatches.Should().BeEmpty();
        t.Pools[0].Matches[0].WinnerFencerId.Should().Be("A"); // unchanged
        t.Pools[0].Matches[0].LeftScore.Should().Be(5); // unchanged
    }

    [Fact]
    public void ApplyWithdrawalCascade_PropagatesBracketWalkover()
    {
        var bracket = new EliminationBracket { Size = 4 };
        var round1 = new EliminationRound { Index = 0, Name = "Semi-finals" };
        round1.Matches.Add(new Match
        {
            BracketRound = 0, BracketSlot = 0,
            LeftFencerId = "A", RightFencerId = "B",
            Status = MatchStatus.Pending
        });
        round1.Matches.Add(new Match
        {
            BracketRound = 0, BracketSlot = 1,
            LeftFencerId = "C", RightFencerId = "D",
            Status = MatchStatus.Pending
        });
        bracket.Rounds.Add(round1);

        var final = new EliminationRound { Index = 1, Name = "Final" };
        final.Matches.Add(new Match { BracketRound = 1, BracketSlot = 0, BracketTag = "Final" });
        bracket.Rounds.Add(final);
        bracket.BronzeMatch = new Match { BracketTag = "Bronze" };

        var t = new Tournament
        {
            State = TournamentState.EliminationInProgress,
            Fencers = new() { new() { Id = "A" }, new() { Id = "B" }, new() { Id = "C" }, new() { Id = "D" } },
            Pools = new(),
            Bracket = bracket
        };

        var cascade = Services.TournamentEngine.ApplyWithdrawalCascade(t, "A");

        // A vs B should be walked over ? B wins
        var semi1 = bracket.Rounds[0].Matches[0];
        semi1.Status.Should().Be(MatchStatus.Finished);
        semi1.WinnerFencerId.Should().Be("B");

        // B should propagate to the final
        cascade.ChangedBracketMatches.Should().Contain(m => m.BracketTag == "Final" || m.BracketRound == 1);
    }

    [Fact]
    public void ApplyWithdrawalCascade_EmptyFencerId_ReturnsEmpty()
    {
        var t = new Tournament { Pools = new() };
        var cascade = Services.TournamentEngine.ApplyWithdrawalCascade(t, "");
        cascade.ChangedPoolMatches.Should().BeEmpty();
        cascade.ChangedBracketMatches.Should().BeEmpty();
    }
}
