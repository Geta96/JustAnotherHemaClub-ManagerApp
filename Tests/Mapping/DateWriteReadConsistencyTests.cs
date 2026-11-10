using System.Globalization;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Tests.Mapping;

/// <summary>
/// Proves the WRITE side of the date contract is consistent with the READ side
/// for every model that stores a date, across every screen in both apps.
///
/// The real GoogleSheetsService writers all serialize dates with
/// <c>.ToString("o", CultureInfo.InvariantCulture)</c>. These tests reproduce
/// that exact serialization for each date-bearing column and assert that
/// <see cref="SheetRowMapper.TryParseDate"/> (the single reader used by every
/// hardened getter) reads back the identical instant — even under a hostile
/// device culture. If a new writer is added with a different format, mirror it
/// here and this will catch a round-trip mismatch before it ships.
/// </summary>
public class DateWriteReadConsistencyTests
{
    /// <summary>Exactly how every GoogleSheetsService writer serializes a date cell.</summary>
    private static string WriterCell(DateTime value) =>
        value.ToString("o", CultureInfo.InvariantCulture);

    /// <summary>Nullable variant: open-ended dates are written as "" (blank).</summary>
    private static string WriterCell(DateTime? value) =>
        value?.ToString("o", CultureInfo.InvariantCulture) ?? "";

    private static DateTime ReadBack(string cell)
    {
        SheetRowMapper.TryParseDate(cell, out var v).Should().BeTrue($"'{cell}' must be readable");
        return v;
    }

    public static IEnumerable<object[]> Cultures => new[]
    {
        new object[] { "en-US" }, new object[] { "en-GB" }, new object[] { "hu-HU" },
    };

    private static void InCulture(string culture, Action body)
    {
        var prev = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = new CultureInfo(culture); body(); }
        finally { CultureInfo.CurrentCulture = prev; }
    }

    // ---------------- Trainings (Date + EndDate) ----------------
    [Theory]
    [MemberData(nameof(Cultures))]
    public void Training_DateAndEndDate_RoundTrip(string culture) => InCulture(culture, () =>
    {
        var t = new TrainingSession
        {
            Date = new DateTime(2026, 10, 12, 18, 0, 0),
            EndDate = new DateTime(2026, 10, 12, 20, 0, 0),
        };
        ReadBack(WriterCell(t.Date)).Should().Be(t.Date);
        ReadBack(WriterCell(t.EndDate)).Should().Be(t.EndDate);
    });

    // ---------------- Payments (PaidOn) ----------------
    [Theory]
    [MemberData(nameof(Cultures))]
    public void Payment_PaidOn_RoundTrip(string culture) => InCulture(culture, () =>
    {
        var p = new Payment { PaidOn = new DateTime(2026, 11, 3, 9, 30, 0) };
        ReadBack(WriterCell(p.PaidOn)).Should().Be(p.PaidOn);
    });

    // ---------------- Expenses (Date) ----------------
    [Theory]
    [MemberData(nameof(Cultures))]
    public void Expense_Date_RoundTrip(string culture) => InCulture(culture, () =>
    {
        var e = new Expense { Date = new DateTime(2026, 12, 10) };
        var read = ReadBack(WriterCell(e.Date));
        read.Should().Be(e.Date);
        read.Month.Should().Be(12, "Dec 10 must not read back as Oct 12");
    });

    // ---------------- Incomes (Date) ----------------
    [Theory]
    [MemberData(nameof(Cultures))]
    public void Income_Date_RoundTrip(string culture) => InCulture(culture, () =>
    {
        var i = new Income { Date = new DateTime(2026, 4, 5) };
        ReadBack(WriterCell(i.Date)).Should().Be(i.Date);
    });

    // ---------------- Price rules (StartDate + optional EndDate) ----------------
    [Theory]
    [MemberData(nameof(Cultures))]
    public void PriceRule_StartAndEndDate_RoundTrip(string culture) => InCulture(culture, () =>
    {
        var rule = new PriceRule
        {
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 6, 30),
        };
        ReadBack(WriterCell(rule.StartDate)).Should().Be(rule.StartDate);
        ReadBack(WriterCell(rule.EndDate)).Should().Be(rule.EndDate.Value);

        // Open-ended rule: blank cell, reader reports "not a date" (left as null).
        SheetRowMapper.TryParseDate(WriterCell((DateTime?)null), out _).Should().BeFalse();
    });

    // ---------------- Individual lessons (Date) ----------------
    [Theory]
    [MemberData(nameof(Cultures))]
    public void IndividualLesson_Date_RoundTrip(string culture) => InCulture(culture, () =>
    {
        var l = new IndividualLesson { Date = new DateTime(2026, 2, 11, 17, 0, 0) };
        ReadBack(WriterCell(l.Date)).Should().Be(l.Date);
    });

    // ---------------- Recurring trainings (StartDate + optional EndDate) ----------------
    [Theory]
    [MemberData(nameof(Cultures))]
    public void RecurringRule_StartAndEndDate_RoundTrip(string culture) => InCulture(culture, () =>
    {
        var rule = new RecurringTrainingRule
        {
            StartDate = new DateTime(2026, 3, 1),
            EndDate = new DateTime(2026, 9, 1),
        };
        ReadBack(WriterCell(rule.StartDate)).Should().Be(rule.StartDate);
        ReadBack(WriterCell(rule.EndDate)).Should().Be(rule.EndDate.Value);
    });

    // ---------------- Tournament header (CreatedAt, UTC) ----------------
    [Theory]
    [MemberData(nameof(Cultures))]
    public void Tournament_CreatedAt_RoundTrip(string culture) => InCulture(culture, () =>
    {
        var t = new Tournament { CreatedAt = new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc) };
        ReadBack(WriterCell(t.CreatedAt)).Should().BeCloseTo(t.CreatedAt, TimeSpan.FromSeconds(1));
    });

    // ---------------- Match timestamps (Started/Finished/Updated/Locked, all nullable UTC) ----------------
    [Theory]
    [MemberData(nameof(Cultures))]
    public void Match_AllTimestamps_RoundTrip(string culture) => InCulture(culture, () =>
    {
        var m = new Match
        {
            StartedAtUtc = new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc),
            FinishedAtUtc = new DateTime(2026, 10, 12, 10, 5, 0, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 10, 12, 10, 5, 1, DateTimeKind.Utc),
            LockedAtUtc = new DateTime(2026, 10, 12, 10, 4, 0, DateTimeKind.Utc),
        };
        ReadBack(WriterCell(m.StartedAtUtc)).Should().BeCloseTo(m.StartedAtUtc.Value, TimeSpan.FromSeconds(1));
        ReadBack(WriterCell(m.FinishedAtUtc)).Should().BeCloseTo(m.FinishedAtUtc.Value, TimeSpan.FromSeconds(1));
        ReadBack(WriterCell(m.UpdatedAtUtc)).Should().BeCloseTo(m.UpdatedAtUtc.Value, TimeSpan.FromSeconds(1));
        ReadBack(WriterCell(m.LockedAtUtc)).Should().BeCloseTo(m.LockedAtUtc.Value, TimeSpan.FromSeconds(1));
    });

    // ---------------- Fencer password-reset timestamp (UTC) ----------------
    [Theory]
    [MemberData(nameof(Cultures))]
    public void Fencer_PasswordResetRequestedAtUtc_RoundTrip(string culture) => InCulture(culture, () =>
    {
        var requested = new DateTime(2026, 10, 12, 7, 45, 0, DateTimeKind.Utc);
        // Fencer writer uses ToUniversalTime().ToString("o").
        var cell = requested.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        ReadBack(cell).Should().BeCloseTo(requested, TimeSpan.FromSeconds(1));
    });
}
