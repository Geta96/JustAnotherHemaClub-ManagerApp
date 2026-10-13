using System.Globalization;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Tests.Mapping;

/// <summary>
/// Regression guard for the "boxed DateTime rendered as locale text" bug family.
///
/// Google.Apis deserializes with Newtonsoft, whose default DateParseHandling
/// auto-converts any date-looking cell into a boxed <see cref="DateTime"/> /
/// <see cref="DateTimeOffset"/> BEFORE our code sees it. A naive
/// <c>cell.ToString()</c> then renders invariant/locale "MM/dd/yyyy HH:mm:ss",
/// which the STRICT ISO reader (<see cref="SheetRowMapper.TryParseDate"/>)
/// rejects. That silently:
///   • made every price-rule StartDate fall back to today / EndDate go null
///     (killing the custom-period pass, producing phantom overpay credit), and
///   • dropped income rows (understated Finance totals).
///
/// The contract these tests enforce is: <see cref="SheetRowMapper.S"/> MUST turn
/// any boxed date back into ISO "o" so the strict reader round-trips it — under
/// ANY device culture. If a cell helper is ever reintroduced that calls plain
/// ToString(), route it through SheetRowMapper.S and these stay green.
/// </summary>
public class SheetCellStringifyTests
{
    private static List<object> Row(params object[] cells) => cells.ToList();

    /// <summary>The locales that triggered the production bugs (US m/d, UK d/m,
    /// hu non-ASCII separators, de comma-decimal, ar non-Gregorian calendar).</summary>
    public static IEnumerable<object[]> HostileCultures => new[]
    {
        new object[] { "en-US" },
        new object[] { "en-GB" },
        new object[] { "hu-HU" },
        new object[] { "de-DE" },
        new object[] { "ar-SA" },
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

    // ------------------------------------------------------------------
    // Boxed DateTime cell → must come back out as strict-ISO-parseable text
    // under every culture, preserving the exact instant.
    // ------------------------------------------------------------------
    [Theory]
    [MemberData(nameof(HostileCultures))]
    public void S_BoxedDateTime_RoundTripsThroughStrictReader_UnderAnyCulture(string culture)
    {
        var original = new DateTime(2026, 9, 18, 19, 0, 0);
        IList<object> row = new List<object> { original };

        var read = UnderCulture(culture, () =>
        {
            var cell = SheetRowMapper.S(row, 0);
            SheetRowMapper.TryParseDate(cell, out var v)
                .Should().BeTrue($"S() must yield ISO text under {culture}, got '{cell}'");
            return v;
        });

        read.Should().Be(original);
        read.Month.Should().Be(9);
        read.Day.Should().Be(18);
    }

    // ------------------------------------------------------------------
    // Boxed DateTimeOffset (cells that carry a +HH:mm offset, e.g. "…+02:00")
    // must also survive. We compare the local components so a correct offset
    // isn't mistaken for a failure.
    // ------------------------------------------------------------------
    [Theory]
    [MemberData(nameof(HostileCultures))]
    public void S_BoxedDateTimeOffset_RoundTripsThroughStrictReader_UnderAnyCulture(string culture)
    {
        var original = new DateTimeOffset(2026, 10, 16, 19, 0, 0, TimeSpan.FromHours(2));
        IList<object> row = new List<object> { original };

        var read = UnderCulture(culture, () =>
        {
            var cell = SheetRowMapper.S(row, 0);
            SheetRowMapper.TryParseDate(cell, out var v)
                .Should().BeTrue($"offset cell must parse under {culture}, got '{cell}'");
            return v;
        });

        read.Month.Should().Be(10);
        read.Day.Should().Be(16);
    }

    // ------------------------------------------------------------------
    // The exact month-flip date from the bug report, as a boxed DateTime:
    // Oct 12 must never read back as Dec 10 via the S() → TryParseDate path.
    // ------------------------------------------------------------------
    [Theory]
    [MemberData(nameof(HostileCultures))]
    public void S_BoxedDateTime_Oct12_NeverBecomesDec10(string culture)
    {
        IList<object> row = new List<object> { new DateTime(2026, 10, 12, 8, 0, 0) };

        var read = UnderCulture(culture, () =>
        {
            SheetRowMapper.TryParseDate(SheetRowMapper.S(row, 0), out var v).Should().BeTrue();
            return v;
        });

        read.Month.Should().Be(10);
        read.Day.Should().Be(12);
    }

    // ------------------------------------------------------------------
    // Already-ISO string cells pass through untouched.
    // ------------------------------------------------------------------
    [Fact]
    public void S_IsoStringCell_PassesThroughUnchanged()
    {
        var iso = new DateTime(2026, 9, 21, 19, 0, 0).ToString("o", CultureInfo.InvariantCulture);
        IList<object> row = new List<object> { iso };
        SheetRowMapper.S(row, 0).Should().Be(iso);
    }

    // ------------------------------------------------------------------
    // Numeric (non-date) cells must stay invariant so a comma-decimal locale
    // can't reshape e.g. an amount or a serial before it's parsed.
    // ------------------------------------------------------------------
    [Theory]
    [MemberData(nameof(HostileCultures))]
    public void S_NumericCell_StaysInvariant_UnderAnyCulture(string culture)
    {
        IList<object> row = new List<object> { 46012.5d };
        var cell = UnderCulture(culture, () => SheetRowMapper.S(row, 0));
        cell.Should().Be("46012.5", "invariant '.' separator regardless of device locale");
    }

    // ------------------------------------------------------------------
    // Safety: missing / short / null cells return "".
    // ------------------------------------------------------------------
    [Fact]
    public void S_MissingOrNullCell_ReturnsEmpty()
    {
        IList<object> row = new List<object> { "id", null! };
        SheetRowMapper.S(row, 1).Should().Be("");  // null cell
        SheetRowMapper.S(row, 5).Should().Be("");  // past end
    }

    // ------------------------------------------------------------------
    // The write→read contract that EVERY date column depends on: a value
    // written as a boxed DateTime and read back via S() must equal a value
    // written as ISO text and read back. Pins both ingestion shapes together.
    // ------------------------------------------------------------------
    [Theory]
    [MemberData(nameof(HostileCultures))]
    public void S_BoxedDateTime_AndIsoString_AgreeAfterParse(string culture)
    {
        var original = new DateTime(2026, 7, 1, 0, 0, 0);

        var (fromBoxed, fromText) = UnderCulture(culture, () =>
        {
            IList<object> boxedRow = new List<object> { original };
            IList<object> textRow  = new List<object>
                { original.ToString("o", CultureInfo.InvariantCulture) };

            SheetRowMapper.TryParseDate(SheetRowMapper.S(boxedRow, 0), out var a).Should().BeTrue();
            SheetRowMapper.TryParseDate(SheetRowMapper.S(textRow, 0),  out var b).Should().BeTrue();
            return (a, b);
        });

        fromBoxed.Should().Be(fromText);
        fromBoxed.Should().Be(original);
    }
}