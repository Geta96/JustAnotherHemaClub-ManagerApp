using CommunityToolkit.Mvvm.ComponentModel;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.ViewModels;

public partial class TournamentFencerRow : ObservableObject
{
    public TournamentFencer Fencer { get; }

    public TournamentFencerRow(TournamentFencer fencer) => Fencer = fencer;

    public string Name => Fencer.Name;
    public bool IsWithdrawn => Fencer.IsWithdrawn;
    public string StatusText => Fencer.IsWithdrawn ? "Withdrawn" : "";
    public string WithdrawButtonText => Fencer.IsWithdrawn ? "Reinstate" : "Withdraw";

    public void RaiseStatusChanged()
    {
        OnPropertyChanged(nameof(IsWithdrawn));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(WithdrawButtonText));
    }
}