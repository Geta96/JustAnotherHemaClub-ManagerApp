using CommunityToolkit.Mvvm.ComponentModel;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class FencerDueRow : ObservableObject
{
    public Fencer Fencer { get; }

    [ObservableProperty] private int sessionsAttended;

    /// <summary>Cheapest applicable tier cost for this attendance (gross, before credit).</summary>
    [ObservableProperty] private decimal totalCost;

    /// <summary>Cash payments actually recorded for this fencer this month (excludes credit carry).</summary>
    [ObservableProperty] private decimal alreadyPaid;

    /// <summary>Outstanding after applying both cash and credit — what Mark Paid will charge.</summary>
    [ObservableProperty] private decimal amountDue;

    /// <summary>
    /// Amount the fencer is ahead by going forward. Non-zero whenever
    /// (cash this month + credit carried in) exceeds the tier cost — this
    /// is the credit that will be applied to future months.
    /// </summary>
    [ObservableProperty] private decimal overpayment;

    [ObservableProperty] private string tierLabel = "—";

    /// <summary>True iff <see cref="AmountDue"/> is zero (covers both exactly-paid and overpaid).</summary>
    [ObservableProperty] private bool isPaid;

    /// <summary>True when <see cref="Overpayment"/> &gt; 0 — drives the "Overpayed by X" badge.</summary>
    [ObservableProperty] private bool isOverpaid;

    /// <summary>True when cash was paid this month but more is still owed (a mid-month tier upgrade).</summary>
    [ObservableProperty] private bool isUpgrade;

    public bool IsNotPaid     => !IsPaid;
    public bool IsExactlyPaid => IsPaid && !IsOverpaid;

    public string Summary
    {
        get
        {
            var student = Fencer.IsStudent ? " · student" : "";

            if (SessionsAttended == 0) return "No sessions";

            var sessionsText = $"{SessionsAttended} session{(SessionsAttended == 1 ? "" : "s")}";

            // Overpayment wins over the "Paid"/"Upgrade" branches — the badge
            // says "Overpayed by X" and Summary mirrors that.
            if (IsOverpaid)
                return $"{sessionsText} · {TierLabel} · overpayed by {Overpayment:N0} Ft{student}";

            if (IsUpgrade)
                return $"{sessionsText} · {TierLabel} · paid {AlreadyPaid:N0}, +{AmountDue:N0} Ft due{student}";

            if (IsPaid)
                return AlreadyPaid > 0
                    ? $"{sessionsText} · {TierLabel} · paid {AlreadyPaid:N0} Ft ✓{student}"
                    : $"{sessionsText} · {TierLabel} · covered by credit ✓{student}";

            // Fresh bill — outstanding is the post-credit amount.
            return $"{sessionsText} · {TierLabel} · {AmountDue:N0} Ft{student}";
        }
    }

    /// <summary>
    /// Builds the row from the calculator's quote, with the cash actually paid
    /// this month tracked separately so month-level income aggregates stay
    /// correct (the quote bundles cash + carried credit into EffectivePaid).
    /// </summary>
    public FencerDueRow(Fencer fencer, DuesQuote quote, decimal cashPaidThisMonth)
    {
        Fencer = fencer;
        sessionsAttended = quote.SessionsAttended;
        totalCost        = quote.TotalDue;
        alreadyPaid      = cashPaidThisMonth;
        amountDue        = quote.Outstanding;
        overpayment      = quote.Overpayment;
        tierLabel        = quote.TierLabel;
        isPaid           = quote.IsCovered;
        isOverpaid       = quote.IsOverpaid;
        // Upgrade message only makes sense when real cash was paid this month
        // (not when credit alone made it look like funds had been applied).
        isUpgrade        = cashPaidThisMonth > 0m && amountDue > 0m;
    }

    /// <summary>
    /// Patches the row in-place after a top-up payment has been recorded, so
    /// the UI reflects the new paid state without a full Finance reload.
    /// Mark Paid records exactly <see cref="AmountDue"/>, so this never moves
    /// the row into the overpaid state.
    /// </summary>
    public void ApplyTopUp(decimal extraPaid)
    {
        if (extraPaid <= 0m) return;
        AlreadyPaid += extraPaid;
        AmountDue    = Math.Max(0m, AmountDue - extraPaid);
        IsPaid       = AmountDue == 0m;
        IsUpgrade    = !IsPaid && AlreadyPaid > 0m;
        // Overpayment / IsOverpaid intentionally unchanged.
    }

    // Summary depends on most fields; cheapest correct approach is to re-raise
    // it from each setter rather than try to be clever about dependency tracking.
    private void RaiseSummary() => OnPropertyChanged(nameof(Summary));

    partial void OnSessionsAttendedChanged(int value)  => RaiseSummary();
    partial void OnTotalCostChanged(decimal value)     => RaiseSummary();
    partial void OnAlreadyPaidChanged(decimal value)   => RaiseSummary();
    partial void OnAmountDueChanged(decimal value)     => RaiseSummary();
    partial void OnOverpaymentChanged(decimal value)   => RaiseSummary();
    partial void OnTierLabelChanged(string value)      => RaiseSummary();
    partial void OnIsUpgradeChanged(bool value)        => RaiseSummary();

    partial void OnIsPaidChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotPaid));
        OnPropertyChanged(nameof(IsExactlyPaid));
        RaiseSummary();
    }

    partial void OnIsOverpaidChanged(bool value)
    {
        OnPropertyChanged(nameof(IsExactlyPaid));
        RaiseSummary();
    }
}