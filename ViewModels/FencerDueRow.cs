using CommunityToolkit.Mvvm.ComponentModel;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.ViewModels;

public partial class FencerDueRow : ObservableObject
{
    public Fencer Fencer { get; }
    public int SessionsAttended { get; }
    public decimal AmountDue { get; }

    [ObservableProperty] private bool isPaid;

    public bool IsNotPaid => !IsPaid;

    public string Summary =>
        SessionsAttended == 0
            ? "No sessions"
            : $"{SessionsAttended} session{(SessionsAttended == 1 ? "" : "s")} · {AmountDue:N0} Ft";

    public FencerDueRow(Fencer fencer, int sessionsAttended, decimal amountDue, bool isPaid)
    {
        Fencer = fencer;
        SessionsAttended = sessionsAttended;
        AmountDue = amountDue;
        this.isPaid = isPaid;
    }

    partial void OnIsPaidChanged(bool value) => OnPropertyChanged(nameof(IsNotPaid));
}