using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

/// <summary>
/// A single tappable payment option shown inside an expanded fencer card
/// (e.g. "Pay Remaining (9 000 Ft)"). Its <see cref="ChooseCommand"/> records
/// the payment via the parent-supplied delegate.
/// </summary>
public partial class PaymentOptionVm : ObservableObject
{
    private readonly Func<Task> _invoke;
    public string Text { get; }

    /// <summary>
    /// Visual grouping for the option. "primary" = the key actions
    /// (Pay Remaining / Pay Custom Amount); "pass" = a specific tier/pass
    /// payment. Drives button styling in the Finance view so the two groups
    /// read differently.
    /// </summary>
    public string Kind { get; }

    public bool IsPrimary => Kind == "primary";
    public bool IsPass => Kind == "pass";
    public bool IsRemaining => Kind == "remaining";

    public PaymentOptionVm(string text, Func<Task> invoke, string kind = "pass")
    {
        Text = text;
        _invoke = invoke;
        Kind = kind;
    }

    [RelayCommand]
    private Task Choose() => _invoke();
}

public partial class FencerDueRow : ObservableObject
{
    public Fencer Fencer { get; }

    /// <summary>
    /// Parent-supplied Mark Paid handler. The button lives in a nested
    /// DataTemplate whose BindingContext is THIS row, so a direct
    /// {Binding MarkPaidCommand} always resolves — unlike RelativeSource walks.
    /// </summary>
    public Func<FencerDueRow, Task>? MarkPaidAction { get; set; }

    /// <summary>
    /// Parent-supplied handler that (re)builds <see cref="PaymentOptions"/> when
    /// the card is expanded. Only the instructor's rows populate options.
    /// </summary>
    public Func<FencerDueRow, Task>? BuildOptionsAction { get; set; }

    /// <summary>Inline payment options shown while the card is expanded.</summary>
    public ObservableCollection<PaymentOptionVm> PaymentOptions { get; } = new();

    [ObservableProperty] private bool isExpanded;

    public string ExpandGlyph => IsExpanded ? "\u25BE" : "\u25B8";

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpandGlyph));

    [RelayCommand]
    private async Task ToggleExpand()
    {
        // Members have no payment options to show, so keep their cards inert.
        if (!CanMarkPaid) return;

        IsExpanded = !IsExpanded;
        if (IsExpanded && BuildOptionsAction is not null)
            await BuildOptionsAction(this);
    }

    /// <summary>
    /// Overpayment credit carried into this month from prior months.
    /// </summary>
    public decimal CreditIn { get; }

    /// <summary>
    /// Set by the parent when building the row: true only for instructors.
    /// Combined with the paid state so a single property drives button
    /// visibility (no DataTrigger vs. IsVisible-binding conflict).
    /// </summary>
    private bool _canMarkPaid;
    public bool CanMarkPaid
    {
        get => _canMarkPaid;
        set { _canMarkPaid = value; OnPropertyChanged(nameof(ShowNotPaidHint)); }
    }

    /// <summary>"Not payed yet" hint shows only for non-instructors on unpaid rows.</summary>
    public bool ShowNotPaidHint => !CanMarkPaid && IsNotPaid;

    [RelayCommand]
    private Task MarkPaid() => MarkPaidAction?.Invoke(this) ?? Task.CompletedTask;

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

    [ObservableProperty] private string tierLabel = "\u2014";

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
            var student = Fencer.IsStudent ? " \u00B7 student" : "";

            if (SessionsAttended == 0) return "No sessions";

            var sessionsText = $"{SessionsAttended} session{(SessionsAttended == 1 ? "" : "s")}";

            // Overpayment wins over the "Paid"/"Upgrade" branches — the badge
            // says "Overpayed by X" and Summary mirrors that.
            if (IsOverpaid)
                return $"{sessionsText} \u00B7 {TierLabel} \u00B7 overpayed by {Overpayment:N0} Ft{student}";

            if (IsUpgrade)
                return $"{sessionsText} \u00B7 {TierLabel} \u00B7 paid {AlreadyPaid:N0}, +{AmountDue:N0} Ft due{student}";

            if (IsPaid)
                return AlreadyPaid > 0
                    ? $"{sessionsText} \u00B7 {TierLabel} \u00B7 paid {AlreadyPaid:N0} Ft \u2713{student}"
                    : $"{sessionsText} \u00B7 {TierLabel} \u00B7 covered by credit \u2713{student}";

            // Fresh bill — outstanding is the post-credit amount.
            return $"{sessionsText} \u00B7 {TierLabel} \u00B7 {AmountDue:N0} Ft{student}";
        }
    }

    /// <summary>
    /// Builds the row from the calculator's quote, with the cash actually paid
    /// this month tracked separately so month-level income aggregates stay
    /// correct (the quote bundles cash + carried credit into EffectivePaid).
    /// </summary>
    public FencerDueRow(Fencer fencer, DuesQuote quote, decimal cashPaidThisMonth, decimal creditIn = 0m)
    {
        Fencer = fencer;
        CreditIn = creditIn;
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
        OnPropertyChanged(nameof(ShowNotPaidHint));
        RaiseSummary();
    }

    partial void OnIsOverpaidChanged(bool value)
    {
        OnPropertyChanged(nameof(IsExactlyPaid));
        RaiseSummary();
    }
}
