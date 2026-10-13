using System.Globalization;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Tests.Mapping;

/// <summary>
/// Regression tests for the "wrong month on a different phone language" bug.
///
/// Root cause: a date written as the ambiguous text "2026-10-12" (or a locale
/// formatted "12/10/2026") was re-parsed using the device's regional settings,
/// so October 12 on a US phone became December 10 on a UK phone. The fix is a
/// two-sided contract that MUST hold everywhere in BOTH apps (MAUI + Blazor
/// Web), because they share one Core <see cref="GoogleSheetsService"/>:
///
///   • WRITE every date as ISO-8601 round-trip ("o", InvariantCulture).
///   • READ every date via <see cref="SheetRowMapper.TryParseDate"/>, which only
///     accepts strict ISO or a Sheets serial number — never a locale guess.
///
/// These tests pin the device/thread culture to US, UK and Hungarian locales and
/// prove the exact instant survives the write?read round-trip in each. If anyone
/// reintroduces a naive DateTime.Parse/ToString on the sheet path, a case here
/// will flip months and fail.
/// </summary>
public class DateCultureInvarianceTests
{
    /// <summary>
    /// The cultures that actually triggered the production bug: US is M/d/y,
    /// UK is d/M/y, Hungarian is y. M. d. with non-ASCII separators.
    /// </summary>
    public static IEnumerable<object[]> HostileCultures => new[]
    {
        new object[] { "en-US" },
        new object[] { "en-GB" },
        new object[] { "hu-HU" },
        new object[] { "de-DE" },
        new object[] { "ar-SA" }, // non-Gregorian default calendar — the nastiest case
    };

    private static T UnderCulture<T>(string culture, Func<T> body)
    {
        var prevCulture = CultureInfo.CurrentCulture;
        var prevUi = CultureInfo.CurrentUICulture;
        try
        {
            var ci = new CultureInfo(culture);
            CultureInfo.CurrentCulture = ci;
            CultureInfo.CurrentUICulture = ci;
            return body();
        }
        finally
        {
            CultureInfo.CurrentCulture = prevCulture;
            CultureInfo.CurrentUICulture = prevUi;
        }
    }

    /// <summary>The canonical on-sheet representation produced by every writer.</summary>
    private static string Write(DateTime value) => value.ToString("o", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------
    // The month-flip date from the bug report: 2026-10-12 must ALWAYS read
    // back as October 12, never December 10, under any device language.
    // ------------------------------------------------------------------
    [Theory]
    [MemberData(nameof(HostileCultures))]
    public void BugReportDate_Oct12_NeverBecomesDec10_UnderAnyCulture(string culture)
    {
        var october12 = new DateTime(2026, 10, 12, 19, 0, 0);
        var onSheet = Write(october12);

        var read = UnderCulture(culture, () =>
        {
            SheetRowMapper.TryParseDate(onSheet, out var v).Should().BeTrue();
            return v;
        });

        read.Month.Should().Be(10, "the ISO month must survive regardless of device locale");
        read.Day.Should().Be(12);
        read.Should().Be(october12);
    }

    // ------------------------------------------------------------------
    // Full write?read round-trip for an arbitrary instant, every culture.
    // ------------------------------------------------------------------
    [Theory]
    [MemberData(nameof(HostileCultures))]
    public void IsoRoundTrip_PreservesExactInstant_UnderAnyCulture(string culture)
    {
        var original = new DateTime(2026, 3, 7, 8, 14, 1);
        var onSheet = Write(original);

        var read = UnderCulture(culture, () =>
        {
            SheetRowMapper.TryParseDate(onSheet, out var v).Should().BeTrue();
            return v;
        });

        read.Should().Be(original);
    }

    // ------------------------------------------------------------------
    // A stale old-client write leaves a bare Sheets serial number. Under the
    // strict ISO-only contract it must NOT be recovered — it is refused under
    // every culture so corruption surfaces instead of a silently-wrong date.
    // ------------------------------------------------------------------
    [Theory]
    [MemberData(nameof(HostileCultures))]
    public void SerialNumber_RecoversSameInstant_UnderAnyCulture(string culture)
    {
        var original = new DateTime(2026, 10, 12, 19, 0, 0);
        // The serial itself is always written invariantly by the migration path.
        var serial = original.ToOADate().ToString(CultureInfo.InvariantCulture);

        UnderCulture(culture, () =>
        {
            SheetRowMapper.TryParseDate(serial, out _).Should().BeFalse();
            return true;
        });
    }

    // ------------------------------------------------------------------
    // Every month boundary is unambiguous after round-trip — catches a
    // day/month swap that would only show up for days ? 12.
    // ------------------------------------------------------------------
    [Theory]
    [MemberData(nameof(HostileCultures))]
    public void AllAmbiguousDays_1To12_SurviveEachMonth(string culture)
    {
        UnderCulture(culture, () =>
        {
            for (int month = 1; month <= 12; month++)
            {
                for (int day = 1; day <= 12; day++) // the ambiguous range
                {
                    var original = new DateTime(2026, month, day, 12, 0, 0);
                    var onSheet = Write(original);

                    SheetRowMapper.TryParseDate(onSheet, out var read)
                        .Should().BeTrue($"{onSheet} must parse under {culture}");
                    read.Should().Be(original,
                        $"{month:00}-{day:00} must not swap month/day under {culture}");
                }
            }
            return true;
        });
    }

    // ------------------------------------------------------------------
    // Regression for the field bug the pure-string tests missed: an
    // UNFORMATTED read returns a boxed serial *double*, and S() must
    // stringify it invariantly BEFORE TryParseDate — otherwise a device
    // locale reshapes the decimal separator and the date is lost.
    // ------------------------------------------------------------------
    [Theory]
    [MemberData(nameof(HostileCultures))]
    public void BoxedSerialCell_ReadsThroughS_UnderAnyCulture(string culture)
    {
        var original = new DateTime(2026, 10, 12, 19, 0, 0);
        // Simulate the real API row: a single cell holding a serial *double*.
        IList<object> row = new List<object> { original.ToOADate() };

        UnderCulture(culture, () =>
        {
            var cell = SheetRowMapper.S(row, 0);
            SheetRowMapper.TryParseDate(cell, out _).Should().BeFalse();
            return true;
        });
    }

    // A boxed DateTime cell (some reads deserialize to DateTime) must also
    // survive S() without a locale-formatted intermediate string.
    [Theory]
    [MemberData(nameof(HostileCultures))]
    public void BoxedDateTimeCell_ReadsThroughS_UnderAnyCulture(string culture)
    {
        var original = new DateTime(2026, 10, 12, 19, 0, 0);
        IList<object> row = new List<object> { original };

        var read = UnderCulture(culture, () =>
        {
            var cell = SheetRowMapper.S(row, 0);
            SheetRowMapper.TryParseDate(cell, out var v).Should().BeTrue();
            return v;
        });

        read.Month.Should().Be(10);
        read.Day.Should().Be(12);
    }

    // ------------------------------------------------------------------
    // Invalidates any locale-guessed reads: d/M/y or M/d/y text must be
    // refused outright, not reinterpreted.
    // ------------------------------------------------------------------
    [Theory]
    [MemberData(nameof(HostileCultures))]
    public void LocaleFormattedDate_IsRejected_NeverGuessed(string culture)
    {
        UnderCulture(culture, () =>
        {
            // d/M/y or M/d/y text must be refused outright, not reinterpreted.
            SheetRowMapper.TryParseDate("12/10/2026", out _).Should().BeFalse();
            return true;
        });
    }

    // ------------------------------------------------------------------
    // A bare Sheets serial number is NO LONGER accepted — strict ISO only.
    // It must be refused under every culture, not silently recovered.
    // ------------------------------------------------------------------
    [Theory]
    [MemberData(nameof(HostileCultures))]
    public void SerialNumber_IsRejected_UnderAnyCulture(string culture)
    {
        var serial = new DateTime(2026, 10, 12, 19, 0, 0)
            .ToOADate().ToString(CultureInfo.InvariantCulture);

        UnderCulture(culture, () =>
        {
            SheetRowMapper.TryParseDate(serial, out _).Should().BeFalse();
            return true;
        });
    }
}
