using CommunityToolkit.Mvvm.ComponentModel;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.ViewModels;

public partial class FencerToggle : ObservableObject
{
    public Fencer Fencer { get; }

    [ObservableProperty] private bool isAttending;

    public FencerToggle(Fencer fencer, bool isAttending)
    {
        Fencer = fencer;
        this.isAttending = isAttending;
    }
}