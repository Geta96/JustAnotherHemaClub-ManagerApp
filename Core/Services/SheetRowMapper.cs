using System.Globalization;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

/// <summary>
/// Pure, network-free mapping between raw Google Sheets rows (jagged object lists)
/// and domain models. Extracted from <see cref="GoogleSheetsService"/> so the
/// parsing rules ? blank-row skipping, TryParse resilience, legacy defaults ?
/// can be unit-tested without touching the Sheets API.
/// </summary>
public static class SheetRowMapper
{
    /// <summary>Safe cell read: returns "" for missing/short/null cells.
    /// Non-string cells (e.g. a Sheets serial <see cref="double"/> from an
    /// UNFORMATTED read, or a boxed <see cref="DateTime"/>) are stringified with
    /// InvariantCulture so a device locale can never reshape the value before
    /// <see cref="TryParseDate"/> sees it.</summary>
    public static string S(IList<object> row, int i)
    {
        if (i >= row.Count) return "";
        return row[i] switch
        {
            null => "",
            string s => s,
            // Newtonsoft (inside Google.Apis) auto-parses date-looking cell text
            // into a boxed DateTime/DateTimeOffset before we see it. A null format
            // would then render invariant "MM/dd/yyyy HH:mm:ss", which the STRICT
            // ISO reader rejects. Round-trip it back to ISO "o" so parsing works.
            DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("o", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            var o => o.ToString() ?? ""
        };
    }

    public static bool ParseBool(string s) =>
        s.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
        s == "1" ||
        s.Equals("yes", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Canonical ISO 8601 shapes we write ("o" round-trip, with/without offset).
    /// </summary>
    private static readonly string[] Iso8601Formats =
    {
        "o",                              // 2026-08-12T08:14:01.1370812+02:00
        "yyyy-MM-ddTHH:mm:ss.fffffffK",
        "yyyy-MM-ddTHH:mm:ssK",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-dd",
    };

    /// <summary>
    /// STRICT ISO-8601 date reader. Accepts ONLY the canonical round-trip shapes
    /// we write ("o" / yyyy-MM-ddTHH:mm:ss[.fffffff][K] / yyyy-MM-dd) under
    /// InvariantCulture. There is deliberately NO tolerant fallback and NO
    /// Sheets-serial fallback: a locale guess or a stray numeric cell is exactly
    /// how the "October 12 → December 10 on an English (UK) phone" bug slipped
    /// through, so anything non-ISO must be treated as corrupt, not guessed.
    /// </summary>
    public static bool TryParseDate(string s, out DateTime value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(s)) return false;

        // Strict ISO 8601 ONLY — unambiguous year/month/day regardless of locale.
        return DateTime.TryParseExact(s, Iso8601Formats, CultureInfo.InvariantCulture,
                                      DateTimeStyles.RoundtripKind, out value);
    }

    /// <summary>
    /// Parses a required date cell, THROWING when the value is not strict ISO-8601.
    /// Use this on the sheet read path instead of silently falling back to
    /// "now"/default — a loud failure surfaces corruption instead of hiding it
    /// behind a wrong date.
    /// </summary>
    public static DateTime ParseDate(string s)
    {
        if (TryParseDate(s, out var value)) return value;
        throw new FormatException(
            $"Date cell '{s}' is not strict ISO-8601 ('o' round-trip). " +
            "Dates must be stored invariant-culture ISO-8601; refusing to guess a locale.");
    }

    // ---------- Fencer ----------
    public static Fencer MapFencer(IList<object> r) => new()
    {
        Id = S(r, 0),
        Username = S(r, 1),
        PasswordHash = S(r, 2),
        Name = S(r, 3),
        Email = S(r, 4),
        Active = ParseBool(S(r, 5)),
        IsStudent = ParseBool(S(r, 6)),
        GdprAccepted = ParseBool(S(r, 7)),
        LiabilityAccepted = ParseBool(S(r, 8)),
        IsInstructor = ParseBool(S(r, 9))
    };

    // ---------- TrainingSession ----------
    /// <summary>Returns null for blank-Id or unparseable-date rows (skipped by the reader).</summary>
    public static TrainingSession? MapTraining(IList<object> r)
    {
        var id = S(r, 0);
        if (string.IsNullOrWhiteSpace(id)) return null;
        if (!TryParseDate(S(r, 1), out var date))
            return null;

        DateTime end;
        var endStr = S(r, 4);
        if (!string.IsNullOrWhiteSpace(endStr) &&
            TryParseDate(endStr, out var parsedEnd))
            end = parsedEnd;
        else
            end = date.AddMinutes(90);

        return new TrainingSession
        {
            Id = id,
            Date = date,
            EndDate = end,
            Topic = S(r, 2),
            AttendeeFencerIds = S(r, 3).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
        };
    }

    // ---------- Payment ----------
    /// <summary>Returns null for blank-FencerId rows. Throws on a non-ISO PaidOn cell.</summary>
    public static Payment? MapPayment(IList<object> r)
    {
        if (string.IsNullOrWhiteSpace(S(r, 0))) return null;
        return new Payment
        {
            FencerId = S(r, 0),
            Year = int.TryParse(S(r, 1), out var y) ? y : 0,
            Month = int.TryParse(S(r, 2), out var mo) ? mo : 0,
            Amount = decimal.TryParse(S(r, 3), NumberStyles.Any, CultureInfo.InvariantCulture, out var a) ? a : 0m,
            PaidOn = ParseDate(S(r, 4))
        };
    }

    // ---------- Expense ----------
    /// <summary>Returns null for blank-Id rows. Throws on a non-ISO Date cell.</summary>
    public static Expense? MapExpense(IList<object> r)
    {
        if (string.IsNullOrWhiteSpace(S(r, 0))) return null;
        return new Expense
        {
            Id = S(r, 0),
            Date = ParseDate(S(r, 1)),
            Category = S(r, 2),
            Description = S(r, 3),
            Amount = decimal.TryParse(S(r, 4), NumberStyles.Any, CultureInfo.InvariantCulture, out var a) ? a : 0m
        };
    }

    // ---------- RecurringTrainingRule ----------
    /// <summary>Returns null for blank-Id or unparseable-day rows.</summary>
    public static RecurringTrainingRule? MapRecurring(IList<object> r)
    {
        var id = S(r, 0);
        if (string.IsNullOrWhiteSpace(id)) return null;
        if (!Enum.TryParse<DayOfWeek>(S(r, 1), true, out var dow)) return null;

        if (!TimeSpan.TryParse(S(r, 2), CultureInfo.InvariantCulture, out var tod)) tod = TimeSpan.Zero;
        var start = ParseDate(S(r, 4));

        DateTime? end = null;
        var endStr = S(r, 5);
        if (!string.IsNullOrWhiteSpace(endStr) &&
            TryParseDate(endStr, out var e))
            end = e;

        TimeSpan endTod;
        var endTodStr = S(r, 7);
        if (!string.IsNullOrWhiteSpace(endTodStr) &&
            TimeSpan.TryParse(endTodStr, CultureInfo.InvariantCulture, out var parsedEnd))
            endTod = parsedEnd;
        else
            endTod = tod.Add(TimeSpan.FromMinutes(90));

        return new RecurringTrainingRule
        {
            Id = id,
            DayOfWeek = dow,
            TimeOfDay = tod,
            EndTimeOfDay = endTod,
            Topic = S(r, 3),
            StartDate = start,
            EndDate = end,
            CreatedByFencerId = S(r, 6)
        };
    }
}
