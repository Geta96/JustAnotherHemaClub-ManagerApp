using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;
using Moq;
using Match = JustAnotherHemaClub.Models.Match;

namespace JustAnotherHemaClub.Tests;

public class TournamentAutoSaveServiceTests
{
    private readonly Mock<IGoogleSheetsService> _sheetsMock = new();
    private readonly TournamentAutoSaveService _sut;

    public TournamentAutoSaveServiceTests()
    {
        _sut = new TournamentAutoSaveService(_sheetsMock.Object);
    }

    // ---------- FlushMatchAsync ----------

    [Fact]
    public async Task FlushMatchAsync_CallsUpsertMatch()
    {
        var match = new Match { Id = "m1", LeftScore = 3 };
        _sheetsMock
            .Setup(s => s.UpsertMatchAsync("t1", match))
            .Returns(Task.CompletedTask);

        await _sut.FlushMatchAsync("t1", match, m => m.LeftScore = 5);

        _sheetsMock.Verify(s => s.UpsertMatchAsync("t1", match), Times.Once);
    }

    [Fact]
    public async Task FlushMatchAsync_OnConflict_RefetchesAndRetries()
    {
        var match = new Match { Id = "m1", LeftScore = 3, Version = 1 };
        var serverMatch = new Match { Id = "m1", LeftScore = 2, Version = 2 };

        int callCount = 0;
        _sheetsMock
            .Setup(s => s.UpsertMatchAsync("t1", It.IsAny<Match>()))
            .Returns((string _, Match _) =>
            {
                callCount++;
                if (callCount == 1)
                    throw new ConcurrencyConflictException("Match", "m1", 1, 2);
                return Task.CompletedTask;
            });

        _sheetsMock
            .Setup(s => s.GetMatchesAsync("t1"))
            .ReturnsAsync(new List<Match> { serverMatch });

        bool applyChangeCalled = false;
        await _sut.FlushMatchAsync("t1", match, m =>
        {
            m.LeftScore = 5;
            applyChangeCalled = true;
        });

        applyChangeCalled.Should().BeTrue();
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task FlushMatchAsync_OnConflict_WhenMatchNotFound_Throws()
    {
        var match = new Match { Id = "m1", LeftScore = 3 };

        _sheetsMock
            .Setup(s => s.UpsertMatchAsync("t1", It.IsAny<Match>()))
            .ThrowsAsync(new ConcurrencyConflictException("Match", "m1", 1, 2));

        _sheetsMock
            .Setup(s => s.GetMatchesAsync("t1"))
            .ReturnsAsync(new List<Match>()); // not found

        var act = () => _sut.FlushMatchAsync("t1", match, _ => { });

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    // ---------- FlushMatchOverwriteAsync ----------

    [Fact]
    public async Task FlushMatchOverwriteAsync_CallsUpsertMatch()
    {
        var match = new Match { Id = "m1" };
        _sheetsMock
            .Setup(s => s.UpsertMatchAsync("t1", match))
            .Returns(Task.CompletedTask);

        await _sut.FlushMatchOverwriteAsync("t1", match, "user1");

        _sheetsMock.Verify(s => s.UpsertMatchAsync("t1", match), Times.Once);
    }

    [Fact]
    public async Task FlushMatchOverwriteAsync_OnConflict_LockStolen_RaisesEvent()
    {
        var match = new Match { Id = "m1", LockedByUserId = "user1", Version = 1 };
        var serverMatch = new Match { Id = "m1", LockedByUserId = "user2", Version = 2 };

        _sheetsMock
            .Setup(s => s.UpsertMatchAsync("t1", match))
            .ThrowsAsync(new ConcurrencyConflictException("Match", "m1", 1, 2));

        _sheetsMock
            .Setup(s => s.GetMatchesAsync("t1"))
            .ReturnsAsync(new List<Match> { serverMatch });

        Match? eventMatch = null;
        _sut.MatchLockTakenOver += (tid, m) => eventMatch = m;

        await _sut.FlushMatchOverwriteAsync("t1", match, "user1");

        eventMatch.Should().NotBeNull();
        eventMatch!.LockedByUserId.Should().Be("user2");
    }

    [Fact]
    public async Task FlushMatchOverwriteAsync_OnConflict_LockStillOurs_Retries()
    {
        var match = new Match { Id = "m1", LockedByUserId = "user1", Version = 1 };
        var serverMatch = new Match { Id = "m1", LockedByUserId = "user1", Version = 2 };

        int callCount = 0;
        _sheetsMock
            .Setup(s => s.UpsertMatchAsync("t1", It.IsAny<Match>()))
            .Returns((string _, Match _) =>
            {
                callCount++;
                if (callCount == 1)
                    throw new ConcurrencyConflictException("Match", "m1", 1, 2);
                return Task.CompletedTask;
            });

        _sheetsMock
            .Setup(s => s.GetMatchesAsync("t1"))
            .ReturnsAsync(new List<Match> { serverMatch });

        await _sut.FlushMatchOverwriteAsync("t1", match, "user1");

        callCount.Should().Be(2);
        match.Version.Should().Be(2);
    }

    // ---------- ScheduleMatch debounce ----------

    [Fact]
    public async Task ScheduleMatch_DebouncesMultipleCalls()
    {
        var match = new Match { Id = "m1", LeftScore = 0 };
        _sheetsMock
            .Setup(s => s.UpsertMatchAsync("t1", match))
            .Returns(Task.CompletedTask);

        // Fire 3 rapid calls — only the last one should persist.
        _sut.ScheduleMatch("t1", match, m => m.LeftScore = 1);
        _sut.ScheduleMatch("t1", match, m => m.LeftScore = 2);
        _sut.ScheduleMatch("t1", match, m => m.LeftScore = 3);

        // Wait for debounce (750ms delay + margin)
        await Task.Delay(1200);

        // At most one call should have reached the backend.
        _sheetsMock.Verify(s => s.UpsertMatchAsync("t1", match), Times.AtMostOnce());
    }

    // ---------- MatchReloadedFromConflict event ----------

    [Fact]
    public async Task FlushMatchAsync_OnConflict_RaisesMatchReloadedEvent()
    {
        var match = new Match { Id = "m1", LeftScore = 3, Version = 1 };
        var serverMatch = new Match { Id = "m1", LeftScore = 2, Version = 2 };

        int callCount = 0;
        _sheetsMock
            .Setup(s => s.UpsertMatchAsync("t1", It.IsAny<Match>()))
            .Returns((string _, Match _) =>
            {
                callCount++;
                if (callCount == 1)
                    throw new ConcurrencyConflictException("Match", "m1", 1, 2);
                return Task.CompletedTask;
            });

        _sheetsMock
            .Setup(s => s.GetMatchesAsync("t1"))
            .ReturnsAsync(new List<Match> { serverMatch });

        Match? reloaded = null;
        _sut.MatchReloadedFromConflict += (_, m) => reloaded = m;

        await _sut.FlushMatchAsync("t1", match, m => m.LeftScore = 5);

        reloaded.Should().NotBeNull();
    }
}
