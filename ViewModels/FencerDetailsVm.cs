using System.Collections.ObjectModel;
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

public partial class FencerDetailsVm : ObservableObject
{
    public Fencer Fencer { get; }

    public string Name => Fencer.Name;
    public string Username => Fencer.Username ?? "";
    public string Email => Fencer.Email ?? "";

    /// <summary>1–2 character avatar fallback derived from the fencer's name.</summary>
    public string Initials
    {
        get
        {
            var name = (Fencer.Name ?? "").Trim();
            if (name.Length == 0) return "?";

            var parts = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1)
                return parts[0].Substring(0, 1).ToUpperInvariant();

            // Most-significant first; for "Pongrácz Ágnes" this gives "PÁ".
            return (parts[0][0].ToString() + parts[^1][0]).ToUpperInvariant();
        }
    }

    public bool Active => Fencer.Active;
    public bool IsStudent => Fencer.IsStudent;
    public bool GdprAccepted => Fencer.GdprAccepted;
    public bool LiabilityAccepted => Fencer.LiabilityAccepted;

    public int SessionsThisMonth { get; }
    public decimal AmountDue { get; }
    public bool IsPaid { get; }

    public bool HasUsername => !string.IsNullOrWhiteSpace(Username);
    public bool OwesMoney => !IsPaid && AmountDue > 0;

    public string PaymentSummary =>
        IsPaid
            ? "All fees paid for this month."
            : AmountDue > 0
                ? $"Owes {AmountDue:N0} Ft for {SessionsThisMonth} session{(SessionsThisMonth == 1 ? "" : "s")} this month."
                : "No sessions attended this month.";

    // --- New stats surface ---
    public ObservableCollection<FencerSessionRow> RecentSessions { get; } = new();
    public bool HasRecentSessions => RecentSessions.Count > 0;

    public string ActiveMonthsText { get; }
    public bool HasActiveMonths => !string.IsNullOrWhiteSpace(ActiveMonthsText) &&
                                   ActiveMonthsText != "—";

    public string AverageAttendanceText { get; }
    public string MostAttendanceText { get; }
    public int OneOnOneReceived { get; }
    public int OneOnOneGiven { get; }
    public bool ShowOneOnOneGiven => Fencer.IsInstructor;

    public FencerDetailsVm(Fencer fencer, int sessionsThisMonth, decimal amountDue, bool isPaid)
        : this(fencer, sessionsThisMonth, amountDue, isPaid,
               recentSessions: Array.Empty<FencerSessionRow>(),
               activeMonthsText: "—",
               averageAttendanceText: "—",
               mostAttendanceText: "—",
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
                           int oneOnOneGiven)
    {
        Fencer = fencer;
        SessionsThisMonth = sessionsThisMonth;
        AmountDue = amountDue;
        IsPaid = isPaid;

        foreach (var s in recentSessions) RecentSessions.Add(s);
        ActiveMonthsText = activeMonthsText;
        AverageAttendanceText = averageAttendanceText;
        MostAttendanceText = mostAttendanceText;
        OneOnOneReceived = oneOnOneReceived;
        OneOnOneGiven = oneOnOneGiven;
    }
}