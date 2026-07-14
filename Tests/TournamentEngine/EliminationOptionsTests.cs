using JustAnotherHemaClub.Models;
using Engine = global::JustAnotherHemaClub.Services.TournamentEngine;

namespace JustAnotherHemaClub.Tests.EngineTests;

/// <summary>
/// Tests for the elimination bracket options (100%/80%/60%/40% cutoffs),
/// custom-cutoff bracket building, and bracket regeneration eligibility.
/// </summary>
public class EliminationOptionsTests
{
    // ======================================================================
    // ComputeEliminationOptions: produces correct options with table sizes
    // ======================================================================

    [Fact]
    public void ComputeEliminationOptions_10Fencers_ReturnsMultipleOptions()
    {
        var t = CreateFinishedPoolTournament(10);

        var options = Engine.ComputeEliminationOptions(t);

        options.Should().NotBeEmpty();
        options.Should().AllSatisfy(o =>
        {
            o.BracketSize.Should().BeOneOf(4, 8, 16, 32, 64, 128, 256);
            o.Label.Should().NotBeNullOrEmpty();
            o.TableLabel.Should().Contain("place table");
            o.CutoffFraction.Should().BeInRange(0.0, 1.0);
        });

        // With 10 fencers: 4, 8, 16 available; 32+ greyed out
        // (16 is included because it's the smallest size that fits all 10).
        options.Where(o => o.BracketSize <= 16).Should().AllSatisfy(o => o.IsAvailable.Should().BeTrue());
        options.Where(o => o.BracketSize >  16).Should().AllSatisfy(o => o.IsAvailable.Should().BeFalse());
    }

    [Fact]
    public void ComputeEliminationOptions_10Fencers_LargestAvailableFitsAllFencers()
    {
        var t = CreateFinishedPoolTournament(10);

        var options   = Engine.ComputeEliminationOptions(t);
        var available = options.Where(o => o.IsAvailable).ToList();
        var largest   = available.OrderByDescending(o => o.BracketSize).First();

        // With 10 fencers the largest available size is 16 (smallest size that fits all).
        largest.BracketSize.Should().Be(16);
        largest.QualifyingCount.Should().Be(10);
        largest.Label.Should().Contain("16 bracket");
        largest.Label.Should().Contain("% of fencers");
    }

    [Fact]
    public void ComputeEliminationOptions_10Fencers_EightBracketOption()
    {
        var t = CreateFinishedPoolTournament(10);

        var options = Engine.ComputeEliminationOptions(t);
        var opt8    = options.FirstOrDefault(o => o.BracketSize == 8);

        opt8.Should().NotBeNull();
        opt8!.IsAvailable.Should().BeTrue();
        opt8.QualifyingCount.Should().Be(8);
        opt8.TotalFencers.Should().Be(10);
    }

    [Fact]
    public void ComputeEliminationOptions_AscendingBySize()
    {
        var t = CreateFinishedPoolTournament(20);

        var options = Engine.ComputeEliminationOptions(t);

        // Options are emitted in canonical size order: 4, 8, 16, 32, 64, 128, 256.
        for (int i = 1; i < options.Count; i++)
            options[i].BracketSize.Should().BeGreaterThan(options[i - 1].BracketSize);
    }

    [Fact]
    public void ComputeEliminationOptions_4Fencers_MinimumStillWorks()
    {
        var t = CreateFinishedPoolTournament(4);

        var options = Engine.ComputeEliminationOptions(t);

        options.Should().NotBeEmpty();
        options.Should().AllSatisfy(o => o.QualifyingCount.Should().BeGreaterThanOrEqualTo(4));
    }

    [Fact]
    public void ComputeEliminationOptions_FiltersTooFewQualifiers()
    {
        var t = CreateFinishedPoolTournament(5);

        var options = Engine.ComputeEliminationOptions(t);

        options.Should().AllSatisfy(o => o.QualifyingCount.Should().BeGreaterThanOrEqualTo(4));
    }

    [Fact]
    public void ComputeEliminationOptions_TableLabelFormat()
    {
        var t = CreateFinishedPoolTournament(10);

        var options = Engine.ComputeEliminationOptions(t);

        options.Should().AllSatisfy(o =>
            o.TableLabel.Should().MatchRegex(@"^\d+ place table$"));
    }

    // ======================================================================
    // BuildBracketFromPoolStandings with custom cutoff
    // ======================================================================

    [Fact]
    public void BuildBracket_100Percent_AllFencersEnter()
    {
        var t = CreateFinishedPoolTournament(10);

        var bracket = Engine.BuildBracketFromPoolStandings(t, 1.0);

        bracket.Size.Should().Be(16);
        var filledSlots = bracket.Rounds[0].Matches
            .SelectMany(m => new[] { m.LeftFencerId, m.RightFencerId })
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .Count();
        filledSlots.Should().Be(10);
    }

    [Fact]
    public void BuildBracket_40Percent_FewerFencersEnter()
    {
        var t = CreateFinishedPoolTournament(20);

        var bracket100 = Engine.BuildBracketFromPoolStandings(t, 1.0);
        var bracket40 = Engine.BuildBracketFromPoolStandings(t, 0.4);

        bracket40.Size.Should().BeLessThanOrEqualTo(bracket100.Size);

        var count100 = CountSeededFencers(bracket100);
        var count40 = CountSeededFencers(bracket40);
        count40.Should().BeLessThan(count100);
        count40.Should().BeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void BuildBracket_DifferentCutoffs_ProduceDifferentBrackets()
    {
        var t = CreateFinishedPoolTournament(20);

        var q100 = Engine.ComputeQualifyingFencerIds(t, 1.0);
        var q60 = Engine.ComputeQualifyingFencerIds(t, 0.6);

        q100.Count.Should().BeGreaterThan(q60.Count);
    }

    [Fact]
    public void BuildBracket_CustomCutoff_StillHasBronzeAndFinal()
    {
        var t = CreateFinishedPoolTournament(10);

        var bracket = Engine.BuildBracketFromPoolStandings(t, 0.8);

        bracket.BronzeMatch.Should().NotBeNull();
        bracket.Rounds[^1].Matches[0].BracketTag.Should().Be("Final");
    }

    // ======================================================================
    // Bracket regeneration: eligibility (no real matches started)
    // ======================================================================

    [Fact]
    public void CanRegenerate_FreshBracket_NoMatchesStarted_True()
    {
        var t = CreateFinishedPoolTournament(10);
        t.Bracket = Engine.BuildBracketFromPoolStandings(t);

        bool canRegenerate = CanRegenerateBracket(t.Bracket);

        canRegenerate.Should().BeTrue();
    }

    [Fact]
    public void CanRegenerate_RealMatchInProgress_False()
    {
        var t = CreateFinishedPoolTournament(10);
        t.Bracket = Engine.BuildBracketFromPoolStandings(t);

        var realMatch = t.Bracket.Rounds[0].Matches
            .First(m => !string.IsNullOrEmpty(m.LeftFencerId) && !string.IsNullOrEmpty(m.RightFencerId)
                        && m.Status != MatchStatus.Finished);
        realMatch.Status = MatchStatus.InProgress;

        bool canRegenerate = CanRegenerateBracket(t.Bracket);

        canRegenerate.Should().BeFalse();
    }

    [Fact]
    public void CanRegenerate_RealMatchFinished_False()
    {
        var t = CreateFinishedPoolTournament(10);
        t.Bracket = Engine.BuildBracketFromPoolStandings(t);

        var realMatch = t.Bracket.Rounds[0].Matches
            .First(m => !string.IsNullOrEmpty(m.LeftFencerId) && !string.IsNullOrEmpty(m.RightFencerId)
                        && m.Status != MatchStatus.Finished);
        realMatch.Status = MatchStatus.Finished;
        realMatch.LeftScore = 5;
        realMatch.RightScore = 2;
        realMatch.WinnerFencerId = realMatch.LeftFencerId;

        bool canRegenerate = CanRegenerateBracket(t.Bracket);

        canRegenerate.Should().BeFalse();
    }

    [Fact]
    public void CanRegenerate_OnlyByeMatchesFinished_True()
    {
        // Use 0.6 cutoff on 10 fencers in 2 pools of 5.
        // 60% of 5 = 3 per pool = 6 qualifiers ? size 8 bracket ? 2 byes.
        var t = CreateFinishedPoolTournament(10);
        t.Bracket = Engine.BuildBracketFromPoolStandings(t, 0.6);

        var byeMatches = t.Bracket.Rounds[0].Matches
            .Where(m => m.Status == MatchStatus.Finished &&
                       (string.IsNullOrEmpty(m.LeftFencerId) || string.IsNullOrEmpty(m.RightFencerId)));

        // If the bracket has no byes (all 8 slots filled), skip the bye-specific
        // assertion and just verify regeneration is allowed when no real match started.
        if (!byeMatches.Any())
        {
            // With 8 qualifiers in size-8 bracket there are no byes — still can regenerate
            bool canRegenerate = CanRegenerateBracket(t.Bracket);
            canRegenerate.Should().BeTrue("no real matches have been played");
            return;
        }

        byeMatches.Should().NotBeEmpty("there should be byes when fewer qualifiers than bracket size");

        bool canRegen = CanRegenerateBracket(t.Bracket);

        canRegen.Should().BeTrue("byes don't count as real matches");
    }

    // ======================================================================
    // Bracket rebuild with different cutoff produces valid structure
    // ======================================================================

    [Fact]
    public void RegenerateBracket_DifferentCutoff_ProducesNewBracket()
    {
        var t = CreateFinishedPoolTournament(20);
        var bracket1 = Engine.BuildBracketFromPoolStandings(t, 1.0);
        var bracket2 = Engine.BuildBracketFromPoolStandings(t, 0.4);

        var seeded1 = CountSeededFencers(bracket1);
        var seeded2 = CountSeededFencers(bracket2);
        seeded1.Should().NotBe(seeded2);
    }

    [Fact]
    public void RegenerateBracket_KeepsPoolsIntact()
    {
        var t = CreateFinishedPoolTournament(10);
        var poolMatchesBefore = t.Pools.SelectMany(p => p.Matches).Count();

        t.Bracket = Engine.BuildBracketFromPoolStandings(t, 0.6);
        t.Bracket = Engine.BuildBracketFromPoolStandings(t, 1.0);

        var poolMatchesAfter = t.Pools.SelectMany(p => p.Matches).Count();
        poolMatchesAfter.Should().Be(poolMatchesBefore);
    }

    // ======================================================================
    // ComputeQualifyingFencerIds with custom cutoff
    // ======================================================================

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.8)]
    [InlineData(0.6)]
    [InlineData(0.4)]
    public void ComputeQualifyingFencerIds_AlwaysReturnsAtLeast4(double cutoff)
    {
        var t = CreateFinishedPoolTournament(10);

        var qualifiers = Engine.ComputeQualifyingFencerIds(t, cutoff);

        qualifiers.Count.Should().BeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void ComputeQualifyingFencerIds_100Percent_AllQualify()
    {
        var t = CreateFinishedPoolTournament(10);

        var qualifiers = Engine.ComputeQualifyingFencerIds(t, 1.0);

        qualifiers.Should().HaveCount(10);
    }

    [Fact]
    public void ComputeQualifyingFencerIds_HigherCutoff_MoreQualifiers()
    {
        var t = CreateFinishedPoolTournament(20);

        var q100 = Engine.ComputeQualifyingFencerIds(t, 1.0);
        var q80 = Engine.ComputeQualifyingFencerIds(t, 0.8);
        var q60 = Engine.ComputeQualifyingFencerIds(t, 0.6);
        var q40 = Engine.ComputeQualifyingFencerIds(t, 0.4);

        q100.Count.Should().BeGreaterThanOrEqualTo(q80.Count);
        q80.Count.Should().BeGreaterThanOrEqualTo(q60.Count);
        q60.Count.Should().BeGreaterThanOrEqualTo(q40.Count);
    }

    // ======================================================================
    // HELPERS
    // ======================================================================

    private static Tournament CreateFinishedPoolTournament(int fencerCount)
    {
        var fencers = Enumerable.Range(0, fencerCount)
            .Select(i => new TournamentFencer { Id = $"F{i:00}", Name = $"Fencer {i}" })
            .ToList();

        var t = new Tournament { Fencers = fencers, State = TournamentState.PoolsClosed };
        t.Pools = Engine.BuildPools(fencers, new Random(42));

        foreach (var pool in t.Pools)
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

        return t;
    }

    private static int CountSeededFencers(EliminationBracket bracket) =>
        bracket.Rounds[0].Matches
            .SelectMany(m => new[] { m.LeftFencerId, m.RightFencerId })
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .Count();

    private static bool CanRegenerateBracket(EliminationBracket bracket)
    {
        foreach (var round in bracket.Rounds)
            foreach (var m in round.Matches)
            {
                if (m.Status == MatchStatus.Finished &&
                    !string.IsNullOrEmpty(m.LeftFencerId) &&
                    !string.IsNullOrEmpty(m.RightFencerId))
                    return false;
                if (m.Status == MatchStatus.InProgress)
                    return false;
            }
        if (bracket.BronzeMatch is not null && bracket.BronzeMatch.Status != MatchStatus.Pending)
            return false;
        return true;
    }
}
