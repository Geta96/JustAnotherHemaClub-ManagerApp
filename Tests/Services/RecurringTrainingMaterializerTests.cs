using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;
using Moq;

namespace JustAnotherHemaClub.Tests.Materialization;

/// <summary>
/// Tests for <see cref="RecurringTrainingMaterializer"/> — the component that
/// turns weekly <see cref="RecurringTrainingRule"/>s into concrete
/// <see cref="TrainingSession"/> rows. The critical guarantees are:
///   • idempotence (never creates duplicate rows), enforced by the deterministic
///     rec_{ruleId}_{yyyyMMdd} id AND a (date, start-time, topic) look-alike guard, and
///   • the new EnsureOccurrenceAsync helper the Home "Next lesson" card uses to
///     proactively materialize the upcoming session without duplicating anything.
/// </summary>
public class RecurringTrainingMaterializerTests
{
    private readonly Mock<IGoogleSheetsService> _sheets = new(MockBehavior.Loose);
    private readonly Mock<ICacheControl> _cache = new(MockBehavior.Loose);

    private RecurringTrainingMaterializer CreateSut() => new(_sheets.Object, _cache.Object);

    /// <summary>
    /// Backs the mock with an in-memory training store that mimics the real
    /// upsert-by-id behaviour, so repeated materialization runs see the rows
    /// created by earlier ones.
    /// </summary>
    private List<TrainingSession> SetupTrainingStore(IEnumerable<TrainingSession>? seed = null)
    {
        var store = seed?.ToList() ?? new List<TrainingSession>();

        _sheets.Setup(s => s.GetTrainingsAsync())
               .ReturnsAsync(() => store.Select(Clone).ToList());

        _sheets.Setup(s => s.UpsertTrainingAsync(It.IsAny<TrainingSession>()))
               .Returns((TrainingSession t) =>
               {
                   var idx = store.FindIndex(x => x.Id == t.Id);
                   if (idx >= 0) store[idx] = Clone(t);
                   else store.Add(Clone(t));
                   return Task.CompletedTask;
               });

        return store;
    }

    private void SetupRules(params RecurringTrainingRule[] rules) =>
        _sheets.Setup(s => s.GetRecurringTrainingsAsync())
               .ReturnsAsync(rules.ToList());

    private static TrainingSession Clone(TrainingSession t) => new()
    {
        Id = t.Id,
        Date = t.Date,
        EndDate = t.EndDate,
        Topic = t.Topic,
        AttendeeFencerIds = new List<string>(t.AttendeeFencerIds)
    };

    private static RecurringTrainingRule Rule(
        string id, DayOfWeek day, DateTime start, DateTime? end = null, string topic = "Longsword") => new()
    {
        Id = id,
        DayOfWeek = day,
        TimeOfDay = new TimeSpan(18, 0, 0),
        EndTimeOfDay = new TimeSpan(20, 0, 0),
        Topic = topic,
        StartDate = start,
        EndDate = end
    };

    // ---------------- OccurrenceId ----------------

    [Fact]
    public void OccurrenceId_UsesDeterministicRecPrefixedFormat()
    {
        var rule = Rule("abc", DayOfWeek.Tuesday, new DateTime(2026, 1, 1));
        var id = RecurringTrainingMaterializer.OccurrenceId(rule, new DateTime(2026, 9, 15, 18, 0, 0));
        id.Should().Be("rec_abc_20260915");
    }

    // ---------------- EnsureOccurrenceAsync ----------------

    [Fact]
    public async Task EnsureOccurrence_WhenNoRowExists_CreatesOne()
    {
        var store = SetupTrainingStore();
        var rule = Rule("r1", DayOfWeek.Tuesday, new DateTime(2026, 1, 1));
        var date = new DateTime(2026, 9, 15); // a Tuesday

        var sut = CreateSut();
        var session = await sut.EnsureOccurrenceAsync(rule, date);

        store.Should().ContainSingle();
        session.Id.Should().Be("rec_r1_20260915");
        session.Date.Should().Be(date.Date + rule.TimeOfDay);
        session.EndDate.Should().Be(date.Date + rule.EndTimeOfDay);
        session.Topic.Should().Be("Longsword");
        _cache.Verify(c => c.InvalidateTrainings(), Times.Once);
    }

    [Fact]
    public async Task EnsureOccurrence_CalledTwice_CreatesOnlyOneRow()
    {
        var store = SetupTrainingStore();
        var rule = Rule("r1", DayOfWeek.Tuesday, new DateTime(2026, 1, 1));
        var date = new DateTime(2026, 9, 15);

        var sut = CreateSut();
        var first  = await sut.EnsureOccurrenceAsync(rule, date);
        var second = await sut.EnsureOccurrenceAsync(rule, date);

        store.Should().ContainSingle("the deterministic id must prevent a duplicate");
        second.Id.Should().Be(first.Id);
        // Only the first call actually creates (and invalidates the cache).
        _sheets.Verify(s => s.UpsertTrainingAsync(It.IsAny<TrainingSession>()), Times.Once);
    }

    [Fact]
    public async Task EnsureOccurrence_WhenLookAlikeRowExists_ReusesItWithoutCreating()
    {
        // A manually-created session on the same (date, start-time, topic) but
        // with a NON-rec id must be treated as covering the slot.
        var date = new DateTime(2026, 9, 15);
        var manual = new TrainingSession
        {
            Id = "manual-xyz",
            Date = date.Date + new TimeSpan(18, 0, 0),
            EndDate = date.Date + new TimeSpan(20, 0, 0),
            Topic = "Longsword"
        };
        var store = SetupTrainingStore(new[] { manual });
        var rule = Rule("r1", DayOfWeek.Tuesday, new DateTime(2026, 1, 1));

        var sut = CreateSut();
        var session = await sut.EnsureOccurrenceAsync(rule, date);

        store.Should().ContainSingle();
        session.Id.Should().Be("manual-xyz");
        _sheets.Verify(s => s.UpsertTrainingAsync(It.IsAny<TrainingSession>()), Times.Never);
    }

    [Fact]
    public async Task EnsureOccurrence_ReturnsExistingRecRow_WhenAlreadyMaterialized()
    {
        var date = new DateTime(2026, 9, 15);
        var existing = new TrainingSession
        {
            Id = "rec_r1_20260915",
            Date = date.Date + new TimeSpan(18, 0, 0),
            EndDate = date.Date + new TimeSpan(20, 0, 0),
            Topic = "Longsword",
            AttendeeFencerIds = new List<string> { "f1" }
        };
        var store = SetupTrainingStore(new[] { existing });
        var rule = Rule("r1", DayOfWeek.Tuesday, new DateTime(2026, 1, 1));

        var sut = CreateSut();
        var session = await sut.EnsureOccurrenceAsync(rule, date);

        session.Id.Should().Be("rec_r1_20260915");
        session.AttendeeFencerIds.Should().Contain("f1");
        _sheets.Verify(s => s.UpsertTrainingAsync(It.IsAny<TrainingSession>()), Times.Never);
    }

    // ---------------- MaterializeDueAsync ----------------

    [Fact]
    public async Task MaterializeDue_DefaultLookAhead_CreatesWholeWeekAhead()
    {
        // Anchor "today" is emulated by picking a rule whose next occurrence is
        // several days out; the default 7-day look-ahead must reach it.
        var store = SetupTrainingStore();
        // Rule fires on whatever weekday is 5 days from today.
        var target = DateTime.Today.AddDays(5);
        SetupRules(Rule("r1", target.DayOfWeek, DateTime.Today.AddMonths(-1)));

        var sut = CreateSut();
        await sut.MaterializeDueAsync(); // default 7

        store.Should().Contain(t => t.Id == $"rec_r1_{target:yyyyMMdd}");
    }

    [Fact]
    public async Task MaterializeDue_RunTwice_DoesNotDuplicate()
    {
        var store = SetupTrainingStore();
        var target = DateTime.Today.AddDays(3);
        SetupRules(Rule("r1", target.DayOfWeek, DateTime.Today.AddMonths(-1)));

        var sut = CreateSut();
        await sut.MaterializeDueAsync();
        var countAfterFirst = store.Count;
        await sut.MaterializeDueAsync();

        store.Count.Should().Be(countAfterFirst, "deterministic ids make a second run a no-op");
    }

    [Fact]
    public async Task MaterializeDue_RespectsRuleEndDate()
    {
        var store = SetupTrainingStore();
        var target = DateTime.Today.AddDays(4);
        // Rule ended yesterday ? no occurrence should be created.
        SetupRules(Rule("r1", target.DayOfWeek, DateTime.Today.AddMonths(-1),
                        end: DateTime.Today.AddDays(-1)));

        var sut = CreateSut();
        await sut.MaterializeDueAsync();

        store.Should().BeEmpty();
    }

    [Fact]
    public async Task MaterializeDue_DoesNotCreate_WhenLookAlikeSlotAlreadyCovered()
    {
        var target = DateTime.Today.AddDays(2);
        var manual = new TrainingSession
        {
            Id = "manual-1",
            Date = target.Date + new TimeSpan(18, 0, 0),
            EndDate = target.Date + new TimeSpan(20, 0, 0),
            Topic = "Longsword"
        };
        var store = SetupTrainingStore(new[] { manual });
        SetupRules(Rule("r1", target.DayOfWeek, DateTime.Today.AddMonths(-1)));

        var sut = CreateSut();
        await sut.MaterializeDueAsync();

        store.Should().ContainSingle("the look-alike guard must suppress a duplicate for the covered slot");
        store[0].Id.Should().Be("manual-1");
    }
}
