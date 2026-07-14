using System.Globalization;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Tests.Mapping;

/// <summary>
/// Unit tests for the pure row→model mapping used by GoogleSheetsService.
/// Focuses on the failure modes seen in production: blank interior rows,
/// short/misaligned rows, unparseable cells, and legacy-default fallbacks.
/// </summary>
public class SheetRowMapperTests
{
    private static List<object> Row(params object[] cells) => cells.ToList();

    // ---------------- S() cell helper ----------------

    [Fact]
    public void S_MissingColumn_ReturnsEmptyString()
    {
        var r = Row("a", "b");
        SheetRowMapper.S(r, 5).Should().Be("");
    }

    [Fact]
    public void S_EmptyRow_ReturnsEmptyForAllColumns()
    {
        var r = new List<object>();
        SheetRowMapper.S(r, 0).Should().Be("");
        SheetRowMapper.S(r, 3).Should().Be("");
    }

    [Theory]
    [InlineData("TRUE", true)]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("yes", true)]
    [InlineData("FALSE", false)]
    [InlineData("", false)]
    [InlineData("0", false)]
    public void ParseBool_MatchesTruthyTokens(string input, bool expected) =>
        SheetRowMapper.ParseBool(input).Should().Be(expected);

    // ---------------- Training ----------------

    [Fact]
    public void MapTraining_BlankRow_ReturnsNull()
    {
        SheetRowMapper.MapTraining(new List<object>()).Should().BeNull();
    }

    [Fact]
    public void MapTraining_BlankId_ReturnsNull()
    {
        var r = Row("", "2026-01-01T18:00:00", "Topic", "", "");
        SheetRowMapper.MapTraining(r).Should().BeNull();
    }

    [Fact]
    public void MapTraining_UnparseableDate_ReturnsNull_DoesNotThrow()
    {
        var r = Row("t1", "not-a-date", "Topic", "", "");
        SheetRowMapper.MapTraining(r).Should().BeNull();
    }

    [Fact]
    public void MapTraining_MissingEndDate_Defaults90Minutes()
    {
        var r = Row("t1", "2026-01-01T18:00:00", "Topic", "F1,F2");
        var t = SheetRowMapper.MapTraining(r);

        t.Should().NotBeNull();
        t!.EndDate.Should().Be(t.Date.AddMinutes(90));
        t.AttendeeFencerIds.Should().BeEquivalentTo(new[] { "F1", "F2" });
    }

    [Fact]
    public void MapTraining_ValidRow_MapsAllFields()
    {
        var start = "2026-01-01T18:00:00";
        var end = "2026-01-01T20:00:00";
        var r = Row("t1", start, "Footwork", "F1", end);

        var t = SheetRowMapper.MapTraining(r)!;
        t.Id.Should().Be("t1");
        t.Topic.Should().Be("Footwork");
        t.Date.Should().Be(DateTime.Parse(start, CultureInfo.InvariantCulture));
        t.EndDate.Should().Be(DateTime.Parse(end, CultureInfo.InvariantCulture));
    }

    // ---------------- Payment ----------------

    [Fact]
    public void MapPayment_BlankRow_ReturnsNull_DoesNotThrow()
    {
        SheetRowMapper.MapPayment(new List<object>()).Should().BeNull();
    }

    [Fact]
    public void MapPayment_BadNumbers_FallBackToZero()
    {
        var r = Row("F1", "notyear", "notmonth", "notamount", "notdate");
        var p = SheetRowMapper.MapPayment(r)!;

        p.FencerId.Should().Be("F1");
        p.Year.Should().Be(0);
        p.Month.Should().Be(0);
        p.Amount.Should().Be(0m);
        p.PaidOn.Should().Be(default);
    }

    [Fact]
    public void MapPayment_ValidRow_ParsesInvariantAmount()
    {
        var r = Row("F1", "2026", "3", "45.50", "2026-03-05T00:00:00");
        var p = SheetRowMapper.MapPayment(r)!;

        p.Year.Should().Be(2026);
        p.Month.Should().Be(3);
        p.Amount.Should().Be(45.50m);
    }

    // ---------------- Expense ----------------

    [Fact]
    public void MapExpense_BlankId_ReturnsNull()
    {
        var r = Row("", "2026-01-01T00:00:00", "Cat", "Desc", "10");
        SheetRowMapper.MapExpense(r).Should().BeNull();
    }

    [Fact]
    public void MapExpense_BadAmount_FallsBackToZero()
    {
        var r = Row("e1", "2026-01-01T00:00:00", "Cat", "Desc", "abc");
        SheetRowMapper.MapExpense(r)!.Amount.Should().Be(0m);
    }

    // ---------------- RecurringTrainingRule (the original bug) ----------------

    [Fact]
    public void MapRecurring_BlankId_ReturnsNull()
    {
        var r = Row("", "Monday", "18:00", "Topic", "2026-01-01T00:00:00", "", "creator", "20:00");
        SheetRowMapper.MapRecurring(r).Should().BeNull();
    }

    [Fact]
    public void MapRecurring_UnparseableDay_ReturnsNull()
    {
        var r = Row("id1", "NotADay", "18:00", "Topic", "2026-01-01T00:00:00", "", "creator", "20:00");
        SheetRowMapper.MapRecurring(r).Should().BeNull();
    }

    [Fact]
    public void MapRecurring_ShortRow_FromColumnDrift_ReturnsNullNotCrash()
    {
        // Simulates the misaligned row: Id present, but DayOfWeek column empty
        // because data was shifted right (the production drift bug).
        var r = Row("id1", "", "", "", "");
        SheetRowMapper.MapRecurring(r).Should().BeNull();
    }

    [Fact]
    public void MapRecurring_MissingEndTime_Defaults90MinutesAfterStart()
    {
        // No column H (EndTimeOfDay) → legacy 90-min default.
        var r = Row("id1", "Tuesday", "18:00", "Sparring", "2026-01-01T00:00:00", "", "creator");
        var rule = SheetRowMapper.MapRecurring(r)!;

        rule.DayOfWeek.Should().Be(DayOfWeek.Tuesday);
        rule.TimeOfDay.Should().Be(new TimeSpan(18, 0, 0));
        rule.EndTimeOfDay.Should().Be(new TimeSpan(19, 30, 0));
    }

    [Fact]
    public void MapRecurring_ValidFullRow_MapsAllFields()
    {
        var r = Row("id1", "Wednesday", "17:30", "Longsword",
                    "2026-02-01T00:00:00", "2026-06-01T00:00:00", "coach1", "19:00");
        var rule = SheetRowMapper.MapRecurring(r)!;

        rule.Id.Should().Be("id1");
        rule.DayOfWeek.Should().Be(DayOfWeek.Wednesday);
        rule.TimeOfDay.Should().Be(new TimeSpan(17, 30, 0));
        rule.EndTimeOfDay.Should().Be(new TimeSpan(19, 0, 0));
        rule.Topic.Should().Be("Longsword");
        rule.EndDate.Should().Be(DateTime.Parse("2026-06-01T00:00:00", CultureInfo.InvariantCulture));
        rule.CreatedByFencerId.Should().Be("coach1");
    }

    // ---------------- Fencer ----------------

    [Fact]
    public void MapFencer_ShortRow_FillsMissingWithDefaults_DoesNotThrow()
    {
        var r = Row("f1", "user1"); // only 2 of 10 columns
        var f = SheetRowMapper.MapFencer(r);

        f.Id.Should().Be("f1");
        f.Username.Should().Be("user1");
        f.PasswordHash.Should().Be("");
        f.Active.Should().BeFalse();
        f.IsInstructor.Should().BeFalse();
    }
}