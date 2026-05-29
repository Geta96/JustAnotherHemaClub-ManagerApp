using CommunityToolkit.Mvvm.ComponentModel;
using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.ViewModels;

public partial class FencerDueRow : ObservableObject
{
    public Fencer Fencer { get; }
    public int SessionsAttended { get; }
    public decimal AmountDue { get; }

    [ObservableProperty] private bool isPaid;
    public bool IsNotPaid => !IsPaid;

    public string Summary
    {
        get
        {
            if (SessionsAttended == 0) return "No sessions";
            var tier = DuesCalculator.TierLabel(SessionsAttended);
            var student = Fencer.IsStudent ? " · student" : "";
            return $"{SessionsAttended} session{(SessionsAttended == 1 ? "" : "s")} · {tier} · {AmountDue:N0} Ft{student}";
        }
    }

    public FencerDueRow(Fencer fencer, int sessionsAttended, decimal amountDue, bool isPaid)
    {
        Fencer = fencer;
        SessionsAttended = sessionsAttended;
        AmountDue = amountDue;
        this.isPaid = isPaid;
    }

    partial void OnIsPaidChanged(bool value) => OnPropertyChanged(nameof(IsNotPaid));
}