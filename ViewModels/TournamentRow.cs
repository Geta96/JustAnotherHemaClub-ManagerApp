using CommunityToolkit.Mvvm.ComponentModel;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.ViewModels;

public partial class TournamentRow : ObservableObject
{
    public Tournament Tournament { get; }

    public TournamentRow(Tournament tournament) => Tournament = tournament;

    public string Id => Tournament.Id;
    public string Name => string.IsNullOrWhiteSpace(Tournament.Name) ? "(unnamed)" : Tournament.Name;
    public int FencerCount => Tournament.Fencers.Count(f => !f.IsWithdrawn);
    public string FencerCountText => $"{FencerCount} fencer{(FencerCount == 1 ? "" : "s")}";
    public string CreatedAtText => Tournament.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd");

    public string StateText => Tournament.State switch
    {
        TournamentState.Setup                 => "Setup",
        TournamentState.PoolsInProgress       => "Pools in progress",
        TournamentState.PoolsClosed           => "Pools closed",
        TournamentState.EliminationInProgress => "Elimination in progress",
        TournamentState.Finished              => "Finished",
        _                                     => "—"
    };

    public string StateBadgeColor => Tournament.State switch
    {
        TournamentState.Setup    => "#8A8A8A",
        TournamentState.Finished => "#1F8A2E",
        _                        => "#476FB5"
    };
}