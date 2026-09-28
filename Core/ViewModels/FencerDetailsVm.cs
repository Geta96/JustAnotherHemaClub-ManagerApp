using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.ViewModels;

public class FencerSessionRow
{
    public string Topic { get; }
    public DateTime Date { get; }
    public string DateText => Date.ToString("yyyy-MM-dd (ddd)");

    public FencerSessionRow(string topic, DateTime date)
    {
        Topic = string.IsNullOrWhiteSpace(topic) ? "(no topic)" : topic;
        Date = date;
    }
}

/// <summary>One month that still owes money, for the "missing payments" list.</summary>
public class FencerUnpaidMonthRow
{
    public int Year { get; }
    public int Month { get; }
    public decimal Amount { get; }

    public string MonthText =>
        new DateTime(Year, Month, 1).ToString("MMM yyyy", CultureInfo.InvariantCulture);
    public string AmountText => $"{Amount:N0} Ft";

    public FencerUnpaidMonthRow(int year, int month, decimal amount)
    {
        Year = year;
        Month = month;
        Amount = amount;
    }
}

/// <summary>A single payment line inside a month group of the Payment History card.</summary>
public class FencerPaymentRow
{
    public DateTime Date { get; }
    public decimal Amount { get; }

    public string DateText => Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string AmountText => $"{Amount:N0} Ft";

    public FencerPaymentRow(DateTime date, decimal amount)
    {
        Date = date;
        Amount = amount;
    }
}

/// <summary>
/// One month's worth of payments for the Payment History card, headed by the
/// month name and how many sessions the fencer attended that month.
/// </summary>
public class FencerPaymentMonthGroup
{
    public int Year { get; }
    public int Month { get; }
    public int SessionsAttended { get; }
    public ObservableCollection<FencerPaymentRow> Payments { get; } = new();

    public string HeaderText =>
        $"{new DateTime(Year, Month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture)}: " +
        $"{SessionsAttended} session{(SessionsAttended == 1 ? "" : "s")}";


    public FencerPaymentMonthGroup(int year, int month, int sessionsAttended,
                                   IEnumerable<FencerPaymentRow> payments)
    {
        Year = year;
        Month = month;
        SessionsAttended = sessionsAttended;
        foreach (var p in payments) Payments.Add(p);
    }
}

public partial class FencerDetailsVm : ObservableObject
{
    public Fencer Fencer { get; }

    public string Name => Fencer.Name;
    public string Username => Fencer.Username ?? "";
    public string Email => Fencer.Email ?? "";

    /// <summary>1?2 character avatar fallback derived from the fencer's name.</summary>
    public string Initials
    {
        get
        {
            var name = (Fencer.Name ?? "").Trim();
            if (name.Length == 0) return "?";

            var parts = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1)
                return parts[0].Substring(0, 1).ToUpperInvariant();

            // Most-significant first; for "Pongr?cz ?gnes" this gives "P?".
            return (parts[0][0].ToString() + parts[^1][0]).ToUpperInvariant();
        }
    }

    public bool Active => Fencer.Active;
    public bool IsStudent => Fencer.IsStudent;
    public bool GdprAccepted => Fencer.GdprAccepted;
    public bool LiabilityAccepted => Fencer.LiabilityAccepted;

    public int SessionsThisMonth { get; }

    /// <summary>Cumulative outstanding across every month (not just the current one).</summary>
    public decimal AmountDue { get; }
    public bool IsPaid { get; }

    public bool HasUsername => !string.IsNullOrWhiteSpace(Username);
    public bool OwesMoney => !IsPaid && AmountDue > 0;

    // --- Cumulative payment status (mirrors the Home payment-status card) ---
    /// <summary>Human-readable status, e.g. "All payed up" / "Due X by the end of this month" / "Due X".</summary>
    public string PaymentStatusText { get; }
    /// <summary>Green: everything settled or overpaid.</summary>
    public bool PaymentAllPaid { get; }
    /// <summary>Grey: only the current month is outstanding, all prior months settled.</summary>
    public bool PaymentDueThisMonth { get; }
    /// <summary>Red: at least one prior month is still outstanding (arrears).</summary>
    public bool PaymentArrears { get; }

    /// <summary>Every month that still has a missing payment (chronological).</summary>
    public ObservableCollection<FencerUnpaidMonthRow> UnpaidMonths { get; } = new();
    public bool HasUnpaidMonths => UnpaidMonths.Count > 0;

    public string PaymentSummary =>
        IsPaid
            ? "All fees paid."
            : AmountDue > 0
                ? $"Owes {AmountDue:N0} Ft in total."
                : "No sessions attended this month.";

    // --- New stats surface ---
    public ObservableCollection<FencerSessionRow> RecentSessions { get; } = new();
    public bool HasRecentSessions => RecentSessions.Count > 0;

    public string ActiveMonthsText { get; }
    public bool HasActiveMonths => !string.IsNullOrWhiteSpace(ActiveMonthsText) &&
                                   ActiveMonthsText != "?";

    public string AverageAttendanceText { get; }
    public string MostAttendanceText { get; }
    public int OneOnOneReceived { get; }
    public int OneOnOneGiven { get; }
    public bool ShowOneOnOneGiven => Fencer.IsInstructor;

    /// <summary>Per-month payment history (newest month first).</summary>
    public ObservableCollection<FencerPaymentMonthGroup> PaymentHistory { get; } = new();

    /// <summary>
    /// Whether the Payment History card is shown for this profile at all.
    /// Instructors may view anyone's; a regular fencer only sees their own.
    /// </summary>
    public bool ShowPaymentHistory { get; }

    public bool HasPaymentHistory => PaymentHistory.Count > 0;

    public FencerDetailsVm(Fencer fencer, int sessionsThisMonth, decimal amountDue, bool isPaid)
        : this(fencer, sessionsThisMonth, amountDue, isPaid,
               recentSessions: Array.Empty<FencerSessionRow>(),
               activeMonthsText: "?",
               averageAttendanceText: "?",
               mostAttendanceText: "?",
               oneOnOneReceived: 0,
               oneOnOneGiven: 0)
    { }

    public FencerDetailsVm(Fencer fencer,
                           int sessionsThisMonth,
                           decimal amountDue,
                           bool isPaid,
                           IEnumerable<FencerSessionRow> recentSessions,
                           string activeMonthsText,
                           string averageAttendanceText,
                           string mostAttendanceText,
                           int oneOnOneReceived,
                           int oneOnOneGiven,
                           string paymentStatusText = "All payed up",
                           bool paymentAllPaid = true,
                           bool paymentDueThisMonth = false,
                           bool paymentArrears = false,
                           IEnumerable<FencerUnpaidMonthRow>? unpaidMonths = null,
                           bool showPaymentHistory = false,
                           IEnumerable<FencerPaymentMonthGroup>? paymentHistory = null)
    {
        Fencer = fencer;
        SessionsThisMonth = sessionsThisMonth;
        AmountDue = amountDue;
        IsPaid = isPaid;

        PaymentStatusText = paymentStatusText;
        PaymentAllPaid = paymentAllPaid;
        PaymentDueThisMonth = paymentDueThisMonth;
        PaymentArrears = paymentArrears;

        if (unpaidMonths is not null)
            foreach (var u in unpaidMonths) UnpaidMonths.Add(u);

        ShowPaymentHistory = showPaymentHistory;
        if (paymentHistory is not null)
            foreach (var g in paymentHistory) PaymentHistory.Add(g);

        foreach (var s in recentSessions) RecentSessions.Add(s);
        ActiveMonthsText = activeMonthsText;
        AverageAttendanceText = averageAttendanceText;
        MostAttendanceText = mostAttendanceText;
        OneOnOneReceived = oneOnOneReceived;
        OneOnOneGiven = oneOnOneGiven;
    }
}
