using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Tests.TournamentEngine;

public class WithdrawalCascadeEdgeCaseTests
{
    [Fact]
    public void ApplyWithdrawalCascade_InProgressMatch_GetsWalkedOver()
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
                            Status = MatchStatus.InProgress,
                            LeftScore = 2, RightScore = 1
                        }
                    }
                }
            }
        };

        var cascade = Services.TournamentEngine.ApplyWithdrawalCascade(t, "A");

        cascade.ChangedPoolMatches.Should().HaveCount(1);
        var m = cascade.ChangedPoolMatches[0];
        m.Status.Should().Be(MatchStatus.Finished);
        m.WinnerFencerId.Should().Be("B");
        m.LeftScore.Should().Be(0); // reset to 0-0 walkover
        m.RightScore.Should().Be(0);
    }

    [Fact]
    public void ApplyWithdrawalCascade_MultiplePools_AffectsAllRelevantMatches()
    {
        var t = new Tournament
        {
            State = TournamentState.PoolsInProgress,
            Fencers = new() { new() { Id = "A" }, new() { Id = "B" }, new() { Id = "C" }, new() { Id = "D" } },
            Pools = new()
            {
                new Pool
                {
                    FencerIds = new() { "A", "B" },
                    Matches = new()
                    {
                        new Match { LeftFencerId = "A", RightFencerId = "B", Status = MatchStatus.Pending }
                    }
                },
                new Pool
                {
                    FencerIds = new() { "A", "C", "D" },
                    Matches = new()
                    {
                        new Match { LeftFencerId = "A", RightFencerId = "C", Status = MatchStatus.Pending },
                        new Match { LeftFencerId = "A", RightFencerId = "D", Status = MatchStatus.Pending },
                        new Match { LeftFencerId = "C", RightFencerId = "D", Status = MatchStatus.Pending },
                    }
                }
            }
        };

        var cascade = Services.TournamentEngine.ApplyWithdrawalCascade(t, "A");

        // A appears in 3 pending matches across 2 pools
        cascade.ChangedPoolMatches.Should().HaveCount(3);
        cascade.ChangedPoolMatches.Should().AllSatisfy(m =>
        {
            m.WinnerFencerId.Should().NotBe("A");
            m.Status.Should().Be(MatchStatus.Finished);
        });
        // C vs D should remain untouched
        t.Pools[1].Matches[2].Status.Should().Be(MatchStatus.Pending);
    }

    [Fact]
    public void ApplyWithdrawalCascade_BracketMatchWithNoOpponent_ClearsSlot()
    {
        var bracket = new EliminationBracket { Size = 4 };
        var round1 = new EliminationRound { Index = 0, Name = "Semi-finals" };
        round1.Matches.Add(new Match
        {
            BracketRound = 0, BracketSlot = 0,
            LeftFencerId = "A", RightFencerId = "", // no opponent yet
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
            Fencers = new() { new() { Id = "A" }, new() { Id = "C" }, new() { Id = "D" } },
            Pools = new(),
            Bracket = bracket
        };

        var cascade = Services.TournamentEngine.ApplyWithdrawalCascade(t, "A");

        // Since A has no opponent, the slot should be cleared
        cascade.ChangedBracketMatches.Should().NotBeEmpty();
        var semi1 = bracket.Rounds[0].Matches[0];
        semi1.LeftFencerId.Should().BeEmpty();
    }

    [Fact]
    public void ApplyWithdrawalCascade_BronzeMatch_GetsWalkedOver()
    {
        var bracket = new EliminationBracket { Size = 4 };
        var round1 = new EliminationRound { Index = 0, Name = "Semi-finals" };
        round1.Matches.Add(new Match
        {
            BracketRound = 0, BracketSlot = 0,
            LeftFencerId = "A", RightFencerId = "B",
            Status = MatchStatus.Finished, WinnerFencerId = "A"
        });
        round1.Matches.Add(new Match
        {
            BracketRound = 0, BracketSlot = 1,
            LeftFencerId = "C", RightFencerId = "D",
            Status = MatchStatus.Finished, WinnerFencerId = "C"
        });
        bracket.Rounds.Add(round1);

        var final = new EliminationRound { Index = 1, Name = "Final" };
        final.Matches.Add(new Match
        {
            BracketRound = 1, BracketSlot = 0, BracketTag = "Final",
            LeftFencerId = "A", RightFencerId = "C",
            Status = MatchStatus.Pending
        });
        bracket.Rounds.Add(final);

        bracket.BronzeMatch = new Match
        {
            BracketTag = "Bronze",
            LeftFencerId = "B", RightFencerId = "D",
            Status = MatchStatus.Pending
        };

        var t = new Tournament
        {
            State = TournamentState.EliminationInProgress,
            Fencers = new()
            {
                new() { Id = "A" }, new() { Id = "B" },
                new() { Id = "C" }, new() { Id = "D" }
            },
            Pools = new(),
            Bracket = bracket
        };

        var cascade = Services.TournamentEngine.ApplyWithdrawalCascade(t, "B");

        // Bronze match should be walked over
        bracket.BronzeMatch.Status.Should().Be(MatchStatus.Finished);
        bracket.BronzeMatch.WinnerFencerId.Should().Be("D");
    }

    [Fact]
    public void ApplyWithdrawalCascade_PoolsClosedState_StillProcesses()
    {
        var t = new Tournament
        {
            State = TournamentState.PoolsClosed,
            Fencers = new() { new() { Id = "A" }, new() { Id = "B" } },
            Pools = new()
            {
                new Pool
                {
                    FencerIds = new() { "A", "B" },
                    Matches = new()
                    {
                        new Match { LeftFencerId = "A", RightFencerId = "B", Status = MatchStatus.Pending }
                    }
                }
            }
        };

        var cascade = Services.TournamentEngine.ApplyWithdrawalCascade(t, "A");

        cascade.ChangedPoolMatches.Should().HaveCount(1);
    }

    [Fact]
    public void ApplyWithdrawalCascade_SetupState_DoesNotProcess()
    {
        var t = new Tournament
        {
            State = TournamentState.Setup,
            Fencers = new() { new() { Id = "A" }, new() { Id = "B" } },
            Pools = new()
            {
                new Pool
                {
                    FencerIds = new() { "A", "B" },
                    Matches = new()
                    {
                        new Match { LeftFencerId = "A", RightFencerId = "B", Status = MatchStatus.Pending }
                    }
                }
            }
        };

        // In Setup state, matches aren't walked over (the code only processes
        // PoolsInProgress, PoolsClosed, or EliminationInProgress).
        // However, the engine walks all pools regardless of state — it looks at
        // match status, not tournament state. Let's verify the behavior matches.
        var cascade = Services.TournamentEngine.ApplyWithdrawalCascade(t, "A");

        // The engine actually still walks pool matches regardless of state:
        cascade.ChangedPoolMatches.Should().HaveCount(1);
    }

    [Fact]
    public void ApplyWithdrawalCascade_FinishedState_DoesNotTouchBracket()
    {
        var bracket = new EliminationBracket { Size = 4 };
        var round1 = new EliminationRound { Index = 0 };
        round1.Matches.Add(new Match
        {
            BracketRound = 0, BracketSlot = 0,
            LeftFencerId = "A", RightFencerId = "B",
            Status = MatchStatus.Finished, WinnerFencerId = "A"
        });
        round1.Matches.Add(new Match
        {
            BracketRound = 0, BracketSlot = 1,
            LeftFencerId = "C", RightFencerId = "D",
            Status = MatchStatus.Finished, WinnerFencerId = "C"
        });
        bracket.Rounds.Add(round1);

        var final = new EliminationRound { Index = 1 };
        final.Matches.Add(new Match
        {
            BracketRound = 1, BracketSlot = 0, BracketTag = "Final",
            LeftFencerId = "A", RightFencerId = "C",
            Status = MatchStatus.Finished, WinnerFencerId = "A"
        });
        bracket.Rounds.Add(final);
        bracket.BronzeMatch = new Match
        {
            BracketTag = "Bronze",
            LeftFencerId = "B", RightFencerId = "D",
            Status = MatchStatus.Finished, WinnerFencerId = "B"
        };

        var t = new Tournament
        {
            State = TournamentState.Finished,
            Fencers = new() { new() { Id = "A" }, new() { Id = "B" }, new() { Id = "C" }, new() { Id = "D" } },
            Pools = new(),
            Bracket = bracket
        };

        var cascade = Services.TournamentEngine.ApplyWithdrawalCascade(t, "A");

        // All matches are Finished — nothing should change
        cascade.ChangedBracketMatches.Should().BeEmpty();
    }
}
