using System.Globalization;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Tests.Mapping;

/// <summary>
/// Tests the tolerant date reading added to <see cref="SheetRowMapper"/> as the
/// "defense in depth" half of the date-corruption fix. New/migrated rows store
/// ISO "o" strings, but out-of-date clients can still write bare Google Sheets
/// serial numbers (OLE Automation dates). The reader must transparently accept
/// BOTH so a stray old-client write never breaks the UI.
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
    public void TryParseDate_SheetsSerialNumber_ParsesAsOleAutomationDate()
    {
        // A Sheets serial number is an OLE Automation date. Round-trip a known
        // instant through ToOADate and confirm we recover it.
        var expected = new DateTime(2026, 9, 15, 18, 0, 0);
        var serial = expected.ToOADate().ToString(CultureInfo.InvariantCulture);

        SheetRowMapper.TryParseDate(serial, out var value).Should().BeTrue();
        value.Should().BeCloseTo(expected, TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-date")]
    public void TryParseDate_BlankOrGarbage_ReturnsFalse(string input)
    {
        SheetRowMapper.TryParseDate(input, out _).Should().BeFalse();
    }

    // ---------------- ParseDateOr ----------------

    [Fact]
    public void ParseDateOr_UnparseableValue_ReturnsFallback()
    {
        var fallback = new DateTime(2000, 1, 1);
        SheetRowMapper.ParseDateOr("garbage", fallback).Should().Be(fallback);
    }

    [Fact]
    public void ParseDateOr_ValidIso_ReturnsParsedValue()
    {
        var iso = new DateTime(2026, 3, 5).ToString("o", CultureInfo.InvariantCulture);
        SheetRowMapper.ParseDateOr(iso, DateTime.MinValue).Should().Be(new DateTime(2026, 3, 5));
    }

    // ---------------- Mappers tolerate corrupt serial dates ----------------

    [Fact]
    public void MapTraining_SerialNumberDate_IsRecovered_NotDropped()
    {
        var start = new DateTime(2026, 9, 15, 18, 0, 0);
        var end   = new DateTime(2026, 9, 15, 20, 0, 0);
        var r = Row("t1",
                    start.ToOADate().ToString(CultureInfo.InvariantCulture),
                    "Footwork",
                    "F1",
                    end.ToOADate().ToString(CultureInfo.InvariantCulture));

        var t = SheetRowMapper.MapTraining(r);

        t.Should().NotBeNull("a serial-number date must be recovered rather than dropping the row");
        t!.Date.Should().BeCloseTo(start, TimeSpan.FromSeconds(1));
        t.EndDate.Should().BeCloseTo(end, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void MapPayment_SerialNumberPaidOn_IsRecovered()
    {
        var paidOn = new DateTime(2026, 3, 5);
        var r = Row("F1", "2026", "3", "45.50",
                    paidOn.ToOADate().ToString(CultureInfo.InvariantCulture));

        var p = SheetRowMapper.MapPayment(r)!;
        p.PaidOn.Should().BeCloseTo(paidOn, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void MapRecurring_SerialNumberStartDate_IsRecovered()
    {
        var startDate = new DateTime(2026, 2, 1);
        var r = Row("id1", "Wednesday", "17:30", "Longsword",
                    startDate.ToOADate().ToString(CultureInfo.InvariantCulture),
                    "", "coach1", "19:00");

        var rule = SheetRowMapper.MapRecurring(r)!;
        rule.StartDate.Should().BeCloseTo(startDate, TimeSpan.FromSeconds(1));
    }
}
