using System.Globalization;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

/// <summary>
/// Pure, network-free mapping between raw Google Sheets rows (jagged object lists)
/// and domain models. Extracted from <see cref="GoogleSheetsService"/> so the
/// parsing rules — blank-row skipping, TryParse resilience, legacy defaults —
/// can be unit-tested without touching the Sheets API.
/// </summary>
public static class SheetRowMapper
{
    /// <summary>Safe cell read: returns "" for missing/short/null cells.</summary>
    public static string S(IList<object> row, int i) =>
        i < row.Count ? row[i]?.ToString() ?? "" : "";

    public static bool ParseBool(string s) =>
        s.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
        s == "1" ||
        s.Equals("yes", StringComparison.OrdinalIgnoreCase);

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
        if (!DateTime.TryParse(S(r, 1), CultureInfo.InvariantCulture,
                               DateTimeStyles.RoundtripKind, out var date))
            return null;

        DateTime end;
        var endStr = S(r, 4);
        if (!string.IsNullOrWhiteSpace(endStr) &&
            DateTime.TryParse(endStr, CultureInfo.InvariantCulture,
                              DateTimeStyles.RoundtripKind, out var parsedEnd))
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
    /// <summary>Returns null for blank-FencerId rows.</summary>
    public static Payment? MapPayment(IList<object> r)
    {
        if (string.IsNullOrWhiteSpace(S(r, 0))) return null;
        return new Payment
        {
            FencerId = S(r, 0),
            Year = int.TryParse(S(r, 1), out var y) ? y : 0,
            Month = int.TryParse(S(r, 2), out var mo) ? mo : 0,
            Amount = decimal.TryParse(S(r, 3), NumberStyles.Any, CultureInfo.InvariantCulture, out var a) ? a : 0m,
            PaidOn = DateTime.TryParse(S(r, 4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : default
        };
    }

    // ---------- Expense ----------
    /// <summary>Returns null for blank-Id rows.</summary>
    public static Expense? MapExpense(IList<object> r)
    {
        if (string.IsNullOrWhiteSpace(S(r, 0))) return null;
        return new Expense
        {
            Id = S(r, 0),
            Date = DateTime.TryParse(S(r, 1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : default,
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
        if (!DateTime.TryParse(S(r, 4), CultureInfo.InvariantCulture,
                               DateTimeStyles.RoundtripKind, out var start)) start = DateTime.Today;

        DateTime? end = null;
        var endStr = S(r, 5);
        if (!string.IsNullOrWhiteSpace(endStr) &&
            DateTime.TryParse(endStr, CultureInfo.InvariantCulture,
                              DateTimeStyles.RoundtripKind, out var e))
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