using CommunityToolkit.Mvvm.ComponentModel;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.ViewModels;

public partial class FencerDetailsVm : ObservableObject
{
    public Fencer Fencer { get; }

    public string Name => Fencer.Name;
    public string Nickname => Fencer.Nickname ?? "";
    public string Email => Fencer.Email ?? "";
    public bool Active => Fencer.Active;
    public bool IsStudent => Fencer.IsStudent;
    public bool GdprAccepted => Fencer.GdprAccepted;
    public bool LiabilityAccepted => Fencer.LiabilityAccepted;

    public int SessionsThisMonth { get; }
    public decimal AmountDue { get; }
    public bool IsPaid { get; }

    public bool HasNickname => !string.IsNullOrWhiteSpace(Nickname);
    public bool OwesMoney => !IsPaid && AmountDue > 0;

    public string PaymentSummary =>
        IsPaid
            ? "All fees paid for this month."
            : AmountDue > 0
                ? $"Owes {AmountDue:N0} Ft for {SessionsThisMonth} session{(SessionsThisMonth == 1 ? "" : "s")} this month."
                : "No sessions attended this month.";

    public FencerDetailsVm(Fencer fencer, int sessionsThisMonth, decimal amountDue, bool isPaid)
    {
        Fencer = fencer;
        SessionsThisMonth = sessionsThisMonth;
        AmountDue = amountDue;
        IsPaid = isPaid;
    }
}