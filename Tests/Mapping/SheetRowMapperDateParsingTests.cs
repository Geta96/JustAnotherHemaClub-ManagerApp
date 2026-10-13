using System.Globalization;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Tests.Mapping;

/// <summary>
/// Tests the STRICT ISO-8601 date reading in <see cref="SheetRowMapper"/>. The
/// reader deliberately accepts ONLY the canonical "o" round-trip shapes we write
/// under InvariantCulture — no tolerant locale guess and no Sheets-serial
/// fallback. Non-ISO cells are refused (TryParseDate → false) or throw
/// (ParseDate) so corruption surfaces loudly instead of showing a wrong date.
/// </summary>
public class SheetRowMapperDateParsingTests
{
    private static List<object> Row(params object[] cells) => cells.ToList();

    // ---------------- TryParseDate ----------------

    [Fact]
    public void TryParseDate_IsoRoundTripString_Parses()
    {
        var iso = new DateTime(2026, 9, 15, 18, 0, 0).ToString("o", CultureInfo.InvariantCulture);
        SheetRowMapper.TryParseDate(iso, out var value).Should().BeTrue();
        value.Should().Be(new DateTime(2026, 9, 15, 18, 0, 0));
    }

    [Fact]
    public void TryParseDate_SheetsSerialNumber_NowRejected()
    {
        // Strict ISO-only: a bare serial number is no longer silently accepted.
        var serial = new DateTime(2026, 9, 15, 18, 0, 0)
            .ToOADate().ToString(CultureInfo.InvariantCulture);
        SheetRowMapper.TryParseDate(serial, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-date")]
    [InlineData("12/10/2026")] // locale guess must be refused, not reinterpreted
    public void TryParseDate_BlankOrGarbageOrLocale_ReturnsFalse(string input)
    {
        SheetRowMapper.TryParseDate(input, out _).Should().BeFalse();
    }

    // ---------------- ParseDate (throwing) ----------------

    [Fact]
    public void ParseDate_NonIso_Throws()
    {
        Action act = () => SheetRowMapper.ParseDate("12/10/2026");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ParseDate_SheetsSerialNumber_Throws()
    {
        var serial = new DateTime(2026, 9, 15).ToOADate().ToString(CultureInfo.InvariantCulture);
        Action act = () => SheetRowMapper.ParseDate(serial);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ParseDate_ValidIso_ReturnsParsedValue()
    {
        var iso = new DateTime(2026, 3, 5).ToString("o", CultureInfo.InvariantCulture);
        SheetRowMapper.ParseDate(iso).Should().Be(new DateTime(2026, 3, 5));
    }

    // ---------------- Mappers reject corrupt serial dates ----------------

    [Fact]
    public void MapTraining_SerialNumberDate_IsDropped()
    {
        // A non-ISO (serial) date is no longer recovered; the row is skipped.
        var start = new DateTime(2026, 9, 15, 18, 0, 0);
        var r = Row("t1",
                    start.ToOADate().ToString(CultureInfo.InvariantCulture),
                    "Footwork",
                    "F1",
                    "");

        SheetRowMapper.MapTraining(r).Should().BeNull(
            "a non-ISO serial date must be refused rather than guessed");
    }

    [Fact]
    public void MapTraining_IsoDate_IsMapped()
    {
        var start = new DateTime(2026, 9, 15, 18, 0, 0);
        var end   = new DateTime(2026, 9, 15, 20, 0, 0);
        var r = Row("t1",
                    start.ToString("o", CultureInfo.InvariantCulture),
                    "Footwork",
                    "F1",
                    end.ToString("o", CultureInfo.InvariantCulture));

        var t = SheetRowMapper.MapTraining(r)!;
        t.Date.Should().Be(start);
        t.EndDate.Should().Be(end);
    }

    [Fact]
    public void MapPayment_SerialNumberPaidOn_Throws()
    {
        var paidOn = new DateTime(2026, 3, 5);
        var r = Row("F1", "2026", "3", "45.50",
                    paidOn.ToOADate().ToString(CultureInfo.InvariantCulture));

        Action act = () => SheetRowMapper.MapPayment(r);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void MapPayment_IsoPaidOn_IsMapped()
    {
        var paidOn = new DateTime(2026, 3, 5);
        var r = Row("F1", "2026", "3", "45.50",
                    paidOn.ToString("o", CultureInfo.InvariantCulture));

        SheetRowMapper.MapPayment(r)!.PaidOn.Should().Be(paidOn);
    }

    [Fact]
    public void MapRecurring_SerialNumberStartDate_Throws()
    {
        var startDate = new DateTime(2026, 2, 1);
        var r = Row("id1", "Wednesday", "17:30", "Longsword",
                    startDate.ToOADate().ToString(CultureInfo.InvariantCulture),
                    "", "coach1", "19:00");

        Action act = () => SheetRowMapper.MapRecurring(r);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void MapRecurring_IsoStartDate_IsMapped()
    {
        var startDate = new DateTime(2026, 2, 1);
        var r = Row("id1", "Wednesday", "17:30", "Longsword",
                    startDate.ToString("o", CultureInfo.InvariantCulture),
                    "", "coach1", "19:00");

        SheetRowMapper.MapRecurring(r)!.StartDate.Should().Be(startDate);
    }

    [Fact]
    public void S_BoxedDateTimeCell_RendersIsoNotLocale()
    {
        IList<object> row = new List<object> { new DateTime(2026, 9, 18, 19, 0, 0) };
        var cell = SheetRowMapper.S(row, 0);
        SheetRowMapper.TryParseDate(cell, out var v).Should().BeTrue();
        v.Month.Should().Be(9);
        v.Day.Should().Be(18);
    }
}
