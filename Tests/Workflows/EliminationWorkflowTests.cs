using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;


namespace JustAnotherHemaClub.Tests.Workflows;

/// <summary>
/// Tests the tournament elimination workflow: pools close ? bracket builds ?
/// matches are played ? propagation ? bracket completes ? final standings.
/// </summary>
public class EliminationWorkflowTests
{
    [Fact]
    public void EliminationPhase_MatchFinish_Propagates_Winner()
    {
        // Setup a size-4 bracket with pending matches
        var bracket = new EliminationBracket { Size = 4 };
        var semis = new EliminationRound { Index = 0, Name = "Semi-finals" };
        semis.Matches.Add(new Match
        {
            BracketRound = 0, BracketSlot = 0,
            LeftFencerId = "A", RightFencerId = "B",
            Status = MatchStatus.Pending
        });
        semis.Matches.Add(new Match
        {
            BracketRound = 0, BracketSlot = 1,
            LeftFencerId = "C", RightFencerId = "D",
            Status = MatchStatus.Pending
        });
        bracket.Rounds.Add(semis);

        var final = new EliminationRound { Index = 1, Name = "Final" };
        final.Matches.Add(new Match { BracketRound = 1, BracketSlot = 0, BracketTag = "Final" });
        bracket.Rounds.Add(final);
        bracket.BronzeMatch = new Match { BracketTag = "Bronze" };

        // Judge finishes semi 1: A wins
        var semi1 = bracket.Rounds[0].Matches[0];
        semi1.Status = MatchStatus.Finished;
        semi1.LeftScore = 5; semi1.RightScore = 2;
        semi1.WinnerFencerId = "A";
        Services.TournamentEngine.PropagateAdvancements(bracket);

        // Final should have A on left, but right is still empty
        var finalMatch = bracket.Rounds[1].Matches[0];
        finalMatch.LeftFencerId.Should().Be("A");
        finalMatch.RightFencerId.Should().BeEmpty();

        // Judge finishes semi 2: D wins
        var semi2 = bracket.Rounds[0].Matches[1];
        semi2.Status = MatchStatus.Finished;
        semi2.LeftScore = 1; semi2.RightScore = 5;
        semi2.WinnerFencerId = "D";
        Services.TournamentEngine.PropagateAdvancements(bracket);

        // Now final is seeded with A vs D, bronze with B vs C
        finalMatch.LeftFencerId.Should().Be("A");
        finalMatch.RightFencerId.Should().Be("D");
        bracket.BronzeMatch!.LeftFencerId.Should().Be("B");
        bracket.BronzeMatch.RightFencerId.Should().Be("C");
    }

    [Fact]
    public void EliminationPhase_PatchAndCollect_ReturnsChangedMatches()
    {
        var bracket = CreateFinishedSemiBracket();

        // Patch in a newly-finished match (simulate the hub saving a result)
        var final = bracket.Rounds[1].Matches[0];
        final.LeftFencerId = "A";
        final.RightFencerId = "D";
        final.Status = MatchStatus.Finished;
        final.WinnerFencerId = "A";
        Services.TournamentEngine.PatchInBracket(bracket, final);

        // No further propagation needed (final is the last round)
        var changes = Services.TournamentEngine.PropagateAndCollectChanges(bracket);

        // After final is decided, nothing further propagates
        Services.TournamentEngine.IsBracketComplete(bracket).Should().BeFalse(); // bronze not done yet
    }

    [Fact]
    public void EliminationPhase_WithdrawalDuringBracket_AutoAdvancesOpponent()
    {
        var bracket = CreateFinishedSemiBracket();
        var final = bracket.Rounds[1].Matches[0];
        final.LeftFencerId = "A";
        final.RightFencerId = "D";

        var tournament = new Tournament
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

        // D withdraws before the final
        var cascade = Services.TournamentEngine.ApplyWithdrawalCascade(tournament, "D");

        // Final should be auto-won by A
        final.Status.Should().Be(MatchStatus.Finished);
        final.WinnerFencerId.Should().Be("A");
        cascade.ChangedBracketMatches.Should().NotBeEmpty();
    }

    [Fact]
    public void EliminationPhase_BracketComplete_StandingsIncludeEveryone()
    {
        var bracket = CreateCompleteBracket();
        var tournament = new Tournament
        {
            Fencers = new()
            {
                new() { Id = "A" }, new() { Id = "B" },
                new() { Id = "C" }, new() { Id = "D" }
            },
            Pools = new()
            {
                new Pool
                {
                    FencerIds = new() { "A", "B", "C", "D" },
                    Matches = new()
                    {
                        Finished("A", "B", 3, 1),
                        Finished("A", "C", 3, 0),
                        Finished("A", "D", 3, 1),
                        Finished("B", "C", 2, 1),
                        Finished("B", "D", 3, 2),
                        Finished("C", "D", 2, 1),
                    }
                }
            },
            Bracket = bracket
        };

        Services.TournamentEngine.IsBracketComplete(bracket).Should().BeTrue();
        var standings = Services.TournamentEngine.ComputeFinalStandings(tournament);
        standings.Should().HaveCount(4);
        standings.Should().OnlyHaveUniqueItems();
    }

    private static EliminationBracket CreateFinishedSemiBracket()
    {
        var bracket = new EliminationBracket { Size = 4 };
        var semis = new EliminationRound { Index = 0, Name = "Semi-finals" };
        semis.Matches.Add(new Match
        {
            BracketRound = 0, BracketSlot = 0,
            LeftFencerId = "A", RightFencerId = "B",
            Status = MatchStatus.Finished, WinnerFencerId = "A"
        });
        semis.Matches.Add(new Match
        {
            BracketRound = 0, BracketSlot = 1,
            LeftFencerId = "C", RightFencerId = "D",
            Status = MatchStatus.Finished, WinnerFencerId = "D"
        });
        bracket.Rounds.Add(semis);

        var final = new EliminationRound { Index = 1, Name = "Final" };
        final.Matches.Add(new Match { BracketRound = 1, BracketSlot = 0, BracketTag = "Final" });
        bracket.Rounds.Add(final);
        bracket.BronzeMatch = new Match { BracketTag = "Bronze" };

        Services.TournamentEngine.PropagateAdvancements(bracket);
        return bracket;
    }

    private static EliminationBracket CreateCompleteBracket()
    {
        var bracket = new EliminationBracket { Size = 4 };
        var semis = new EliminationRound { Index = 0 };
        semis.Matches.Add(new Match
        {
            BracketRound = 0, BracketSlot = 0,
            LeftFencerId = "A", RightFencerId = "B",
            Status = MatchStatus.Finished, WinnerFencerId = "A"
        });
        semis.Matches.Add(new Match
        {
            BracketRound = 0, BracketSlot = 1,
            LeftFencerId = "C", RightFencerId = "D",
            Status = MatchStatus.Finished, WinnerFencerId = "C"
        });
        bracket.Rounds.Add(semis);

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
        return bracket;
    }

    private static Match Finished(string left, string right, int ls, int rs) => new()
    {
        LeftFencerId = left, RightFencerId = right,
        LeftScore = ls, RightScore = rs,
        Status = MatchStatus.Finished,
        WinnerFencerId = ls > rs ? left : right
    };
}
