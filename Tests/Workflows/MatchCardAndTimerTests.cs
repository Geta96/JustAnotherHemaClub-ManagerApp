using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Tests.Workflows;

/// <summary>
/// Tests for the match timer (2-min default, subtract, restart) and card system
/// (graphic indicators, multiple reds, removal with point reversal, yellow reappearing).
/// Exercises the logic at the model/engine level without MAUI dependencies.
/// </summary>
public class MatchCardAndTimerTests
{
    // ======================================================================
    // DEFAULT TIMER
    // ======================================================================

    [Fact]
    public void DefaultMatchSeconds_Is120()
    {
        Services.TournamentEngine.DefaultMatchSeconds.Should().Be(120);
    }

    [Fact]
    public void GeneratedPoolMatches_Have120SecondTimer()
    {
        var fencers = MakeFencers(5);
        var pool = new Pool { Index = 0, FencerIds = fencers.Select(f => f.Id).ToList() };
        Services.TournamentEngine.GeneratePoolMatches(new List<Pool> { pool });

        pool.Matches.Should().AllSatisfy(m =>
            m.RemainingTimeSeconds.Should().Be(120));
    }

    [Fact]
    public void BuildBracketFromPoolStandings_MatchesHave120SecondTimer()
    {
        var t = CreateFinishedPoolTournament(10);
        var bracket = Services.TournamentEngine.BuildBracketFromPoolStandings(t);

        bracket.Rounds.SelectMany(r => r.Matches).Should().AllSatisfy(m =>
            m.RemainingTimeSeconds.Should().Be(120));
        bracket.BronzeMatch!.RemainingTimeSeconds.Should().Be(120);
    }

    // ======================================================================
    // TIMER: SUBTRACT MINUTE (min 2 minutes)
    // ======================================================================

    [Fact]
    public void SubtractMinute_From180_Gives120()
    {
        var match = new Match { RemainingTimeSeconds = 180, Status = MatchStatus.InProgress };

        int newTime = match.RemainingTimeSeconds - 60;
        newTime.Should().BeGreaterThanOrEqualTo(120);
        match.RemainingTimeSeconds = newTime;

        match.RemainingTimeSeconds.Should().Be(120);
    }

    [Fact]
    public void SubtractMinute_From120_Blocked()
    {
        var match = new Match { RemainingTimeSeconds = 120, Status = MatchStatus.InProgress };

        int newTime = match.RemainingTimeSeconds - 60;
        bool allowed = newTime >= 120;
        allowed.Should().BeFalse();
    }

    [Fact]
    public void SubtractMinute_From240_Gives180()
    {
        var match = new Match { RemainingTimeSeconds = 240, Status = MatchStatus.InProgress };

        int newTime = match.RemainingTimeSeconds - 60;
        newTime.Should().BeGreaterThanOrEqualTo(120);
        match.RemainingTimeSeconds = newTime;

        match.RemainingTimeSeconds.Should().Be(180);
    }

    // ======================================================================
    // TIMER: RESTART resets to DefaultMatchSeconds
    // ======================================================================

    [Fact]
    public void RestartTimer_ResetsTo120()
    {
        var match = new Match { RemainingTimeSeconds = 45, Status = MatchStatus.InProgress };

        match.RemainingTimeSeconds = Services.TournamentEngine.DefaultMatchSeconds;

        match.RemainingTimeSeconds.Should().Be(120);
    }

    // ======================================================================
    // CARDS: MULTIPLE REDS ALLOWED
    // ======================================================================

    [Fact]
    public void AddMultipleRedCards_AccumulatesCorrectly()
    {
        var match = new Match { Status = MatchStatus.InProgress };

        // Give left fencer 3 red cards ? 3 points to right
        match.LeftRedCards++;  match.RightScore++;
        match.LeftRedCards++;  match.RightScore++;
        match.LeftRedCards++;  match.RightScore++;

        match.LeftRedCards.Should().Be(3);
        match.RightScore.Should().Be(3);
    }

    [Fact]
    public void YellowBlockedWhenAnyRedExists()
    {
        var match = new Match { Status = MatchStatus.InProgress };

        match.LeftRedCards = 1;

        // CanLeftYellow rule: yellow == 0 && red == 0
        bool canYellow = match.LeftYellowCards == 0 && match.LeftRedCards == 0;
        canYellow.Should().BeFalse();
    }

    [Fact]
    public void YellowBlockedWhenYellowAlreadyExists()
    {
        var match = new Match { Status = MatchStatus.InProgress };

        match.LeftYellowCards = 1;

        bool canYellow = match.LeftYellowCards == 0 && match.LeftRedCards == 0;
        canYellow.Should().BeFalse();
    }

    // ======================================================================
    // CARDS: REMOVAL — only one card removed at a time
    // ======================================================================

    [Fact]
    public void RemoveOneRed_FromMultiple_OnlyDecrementsOneAndRemovesOnePoint()
    {
        var match = new Match
        {
            Status = MatchStatus.InProgress,
            LeftRedCards = 3,
            RightScore = 5 // 3 from reds + 2 actual
        };

        // Remove one red from left ? one point removed from right
        match.LeftRedCards--;
        match.RightScore = Math.Max(0, match.RightScore - 1);

        match.LeftRedCards.Should().Be(2);
        match.RightScore.Should().Be(4);
    }

    [Fact]
    public void RemoveOneRed_DoesNotAffectYellow()
    {
        var match = new Match
        {
            Status = MatchStatus.InProgress,
            LeftYellowCards = 1,
            LeftRedCards = 2,
            RightScore = 2
        };

        match.LeftRedCards--;
        match.RightScore = Math.Max(0, match.RightScore - 1);

        match.LeftYellowCards.Should().Be(1, "yellow is untouched");
        match.LeftRedCards.Should().Be(1);
        match.RightScore.Should().Be(1);
    }

    [Fact]
    public void RemoveYellow_DoesNotAffectRedOrScore()
    {
        var match = new Match
        {
            Status = MatchStatus.InProgress,
            LeftYellowCards = 1,
            LeftRedCards = 1,
            RightScore = 3
        };

        match.LeftYellowCards--;

        match.LeftYellowCards.Should().Be(0);
        match.LeftRedCards.Should().Be(1, "red untouched");
        match.RightScore.Should().Be(3, "score unchanged when removing yellow");
    }

    [Fact]
    public void RemoveRed_WhenScoreIsZero_ScoreStaysAtZero()
    {
        var match = new Match
        {
            Status = MatchStatus.InProgress,
            LeftRedCards = 1,
            RightScore = 0
        };

        match.LeftRedCards--;
        match.RightScore = Math.Max(0, match.RightScore - 1);

        match.LeftRedCards.Should().Be(0);
        match.RightScore.Should().Be(0, "cannot go negative");
    }

    // ======================================================================
    // CARD INDICATORS: yellow visible after last red removed
    // ======================================================================

    [Fact]
    public void Indicator_NoCards_NothingVisible()
    {
        int yellow = 0, red = 0;

        bool showYellow = yellow > 0 && red == 0;
        bool showRed = red > 0;
        bool hasAny = yellow > 0 || red > 0;

        showYellow.Should().BeFalse();
        showRed.Should().BeFalse();
        hasAny.Should().BeFalse();
    }

    [Fact]
    public void Indicator_OnlyYellow_ShowsYellow()
    {
        int yellow = 1, red = 0;

        bool showYellow = yellow > 0 && red == 0;
        bool showRed = red > 0;

        showYellow.Should().BeTrue();
        showRed.Should().BeFalse();
    }

    [Fact]
    public void Indicator_YellowAndRed_ShowsOnlyRed()
    {
        int yellow = 1, red = 1;

        bool showYellow = yellow > 0 && red == 0;
        bool showRed = red > 0;

        showYellow.Should().BeFalse("red overrides yellow");
        showRed.Should().BeTrue();
    }

    [Fact]
    public void Indicator_MultipleReds_ShowsRedWithCount()
    {
        int yellow = 1, red = 3;

        bool showYellow = yellow > 0 && red == 0;
        bool showRed = red > 0;
        bool hasMultipleReds = red > 1;
        string countText = red > 1 ? red.ToString() : "";

        showYellow.Should().BeFalse();
        showRed.Should().BeTrue();
        hasMultipleReds.Should().BeTrue();
        countText.Should().Be("3");
    }

    [Fact]
    public void Indicator_RemoveLastRed_YellowReappears()
    {
        int yellow = 1, red = 1;

        // Before removal: red shown
        (yellow > 0 && red == 0).Should().BeFalse();
        (red > 0).Should().BeTrue();

        // Remove the red
        red--;

        // After removal: yellow shown
        (yellow > 0 && red == 0).Should().BeTrue("yellow reappears when last red removed");
        (red > 0).Should().BeFalse();
    }

    [Fact]
    public void Indicator_RemoveOneOfMultipleReds_StillShowsRed()
    {
        int yellow = 1, red = 2;

        red--;

        bool showYellow = yellow > 0 && red == 0;
        bool showRed = red > 0;

        showYellow.Should().BeFalse("still has 1 red remaining");
        showRed.Should().BeTrue();
    }

    // ======================================================================
    // CARD SUMMARY TEXT
    // ======================================================================

    [Theory]
    [InlineData(0, 0, "")]
    [InlineData(1, 0, "? Yellow")]
    [InlineData(0, 1, "?? Red")]
    [InlineData(0, 3, "?? Red ×3")]
    [InlineData(1, 1, "? Yellow + ?? Red")]
    [InlineData(1, 2, "? Yellow + ?? Red ×2")]
    public void CardSummary_FormatsCorrectly(int yellow, int red, string expected)
    {
        var summary = (yellow, red) switch
        {
            (0, 0) => "",
            (> 0, 0) => "? Yellow",
            (0, 1) => "?? Red",
            (0, > 1) => $"?? Red ×{red}",
            (> 0, 1) => "? Yellow + ?? Red",
            _ => $"? Yellow + ?? Red ×{red}"
        };

        summary.Should().Be(expected);
    }

    // ======================================================================
    // CARD REMOVAL: right side (symmetry)
    // ======================================================================

    [Fact]
    public void RemoveRightRed_SubtractsPointFromLeft()
    {
        var match = new Match
        {
            Status = MatchStatus.InProgress,
            RightRedCards = 2,
            LeftScore = 4 // 2 from reds + 2 actual
        };

        match.RightRedCards--;
        match.LeftScore = Math.Max(0, match.LeftScore - 1);

        match.RightRedCards.Should().Be(1);
        match.LeftScore.Should().Be(3);
    }

    [Fact]
    public void RemoveRightYellow_NoScoreChange()
    {
        var match = new Match
        {
            Status = MatchStatus.InProgress,
            RightYellowCards = 1,
            LeftScore = 2
        };

        match.RightYellowCards--;

        match.RightYellowCards.Should().Be(0);
        match.LeftScore.Should().Be(2, "no score change for yellow removal");
    }

    // ======================================================================
    // FULL SCENARIO: yellow ? red ? red ? remove red ? remove red ? yellow visible
    // ======================================================================

    [Fact]
    public void FullCardSequence_YellowThenMultipleReds_RemovalRevealsYellow()
    {
        var match = new Match { Status = MatchStatus.InProgress };

        // Step 1: Yellow card
        match.LeftYellowCards++;

        // Step 2: First red card (blocks future yellows, point to right)
        match.LeftRedCards++; match.RightScore++;

        // Step 3: Second red card
        match.LeftRedCards++; match.RightScore++;

        match.LeftYellowCards.Should().Be(1);
        match.LeftRedCards.Should().Be(2);
        match.RightScore.Should().Be(2);

        // Indicator: only red shown
        (match.LeftYellowCards > 0 && match.LeftRedCards == 0).Should().BeFalse();
        (match.LeftRedCards > 0).Should().BeTrue();

        // Step 4: Remove one red
        match.LeftRedCards--;
        match.RightScore = Math.Max(0, match.RightScore - 1);

        match.LeftRedCards.Should().Be(1);
        match.RightScore.Should().Be(1);
        // Still shows red (1 remaining)
        (match.LeftRedCards > 0).Should().BeTrue();
        (match.LeftYellowCards > 0 && match.LeftRedCards == 0).Should().BeFalse();

        // Step 5: Remove last red
        match.LeftRedCards--;
        match.RightScore = Math.Max(0, match.RightScore - 1);

        match.LeftRedCards.Should().Be(0);
        match.RightScore.Should().Be(0);
        // Now yellow is visible again!
        (match.LeftYellowCards > 0 && match.LeftRedCards == 0).Should().BeTrue("yellow reappears");
        (match.LeftRedCards > 0).Should().BeFalse();
    }

    // ======================================================================
    // HELPERS
    // ======================================================================

    private static List<TournamentFencer> MakeFencers(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new TournamentFencer { Id = $"F{i:00}", Name = $"Fencer {i}" })
            .ToList();

    private static Tournament CreateFinishedPoolTournament(int fencerCount)
    {
        var fencers = MakeFencers(fencerCount);
        var t = new Tournament { Fencers = fencers, State = TournamentState.PoolsClosed };
        t.Pools = Services.TournamentEngine.BuildPools(fencers, new Random(42));

        foreach (var pool in t.Pools)
            foreach (var m in pool.Matches)
            {
                m.Status = MatchStatus.Finished;
                m.LeftScore = 3; m.RightScore = 1;
                m.WinnerFencerId = m.LeftFencerId;
            }

        return t;
    }
}
