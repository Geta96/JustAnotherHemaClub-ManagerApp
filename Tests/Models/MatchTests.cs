using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Tests.Models;

public class MatchTests
{
    [Fact]
    public void IsLockedByOther_ReturnsFalse_WhenNoLock()
    {
        var match = new Match();

        match.IsLockedByOther("user1", DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void IsLockedByOther_ReturnsFalse_WhenLockedBySameUser()
    {
        var now = DateTime.UtcNow;
        var match = new Match
        {
            LockedByUserId = "user1",
            LockedAtUtc = now.AddSeconds(-30)
        };

        match.IsLockedByOther("user1", now).Should().BeFalse();
    }

    [Fact]
    public void IsLockedByOther_ReturnsTrue_WhenLockedByDifferentUser_WithFreshLock()
    {
        var now = DateTime.UtcNow;
        var match = new Match
        {
            LockedByUserId = "user2",
            LockedAtUtc = now.AddSeconds(-30) // 30 seconds ago = fresh
        };

        match.IsLockedByOther("user1", now).Should().BeTrue();
    }

    [Fact]
    public void IsLockedByOther_ReturnsFalse_WhenLockIsStale()
    {
        var now = DateTime.UtcNow;
        var match = new Match
        {
            LockedByUserId = "user2",
            LockedAtUtc = now.AddMinutes(-3) // 3 minutes ago = stale (>2 min)
        };

        match.IsLockedByOther("user1", now).Should().BeFalse();
    }

    [Fact]
    public void IsLockedByOther_ReturnsFalse_WhenLockedAtUtcIsNull()
    {
        var match = new Match
        {
            LockedByUserId = "user2",
            LockedAtUtc = null
        };

        match.IsLockedByOther("user1", DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void IsLockedByOther_ReturnsFalse_WhenLockedByUserIdIsEmpty()
    {
        var now = DateTime.UtcNow;
        var match = new Match
        {
            LockedByUserId = "",
            LockedAtUtc = now.AddSeconds(-30)
        };

        match.IsLockedByOther("user1", now).Should().BeFalse();
    }

    [Fact]
    public void IsLockedByOther_ReturnsTrue_AtExactly1Minute59Seconds()
    {
        var now = DateTime.UtcNow;
        var match = new Match
        {
            LockedByUserId = "user2",
            LockedAtUtc = now.AddMinutes(-2).AddSeconds(1) // just under 2 min
        };

        match.IsLockedByOther("user1", now).Should().BeTrue();
    }

    [Fact]
    public void NewMatch_HasDefaultValues()
    {
        var match = new Match();

        match.Id.Should().NotBeNullOrEmpty();
        match.Status.Should().Be(MatchStatus.Pending);
        match.RemainingTimeSeconds.Should().Be(180);
        match.LeftScore.Should().Be(0);
        match.RightScore.Should().Be(0);
        match.LeftFencerId.Should().BeEmpty();
        match.RightFencerId.Should().BeEmpty();
        match.WinnerFencerId.Should().BeNull();
    }
}
