using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.ViewModels;

namespace JustAnotherHemaClub.Services;

/// <summary>
/// Tracks the tournament currently being viewed and the user's role inside it.
/// Organisers can edit; fencers (and anonymous viewers) are read-only.
/// Registered as a singleton so every page/VM sees the same open tournament.
/// </summary>
public sealed class TournamentSession
{
    /// <summary>The tournament the user is currently inside, or null when on the list page.</summary>
    public Tournament? Current { get; private set; }

    /// <summary>The role the user entered with (set by <see cref="TournamentAccessVm"/>).</summary>
    public TournamentRole Role { get; private set; } = TournamentRole.Viewer;

    /// <summary>
    /// True when the open tournament can be edited from this device.
    /// A <see cref="TournamentState.Finished"/> tournament is locked for everyone until reopened.
    /// </summary>
    public bool CanEdit =>
        Current is not null &&
        Role == TournamentRole.Organiser &&
        Current.State != TournamentState.Finished;

    /// <summary>True for organisers regardless of state — used by the Reopen action.</summary>
    public bool IsOrganiser => Role == TournamentRole.Organiser;

    /// <summary>
    /// True when the current user may open the Match page (read-only or otherwise).
    /// Fencers entered the tournament for spectating only and must not navigate
    /// into individual matches from the pools/elim screens.
    /// </summary>
    public bool CanOpenMatches => Role != TournamentRole.Fencer;

    /// <summary>Open a tournament with the given role. Called from the password gate.</summary>
    public void Open(Tournament tournament, TournamentRole role)
    {
        Current = tournament;
        Role = role;
    }

    /// <summary>Clear the session. Called from <see cref="Views.TournamentsPage"/> on appear.</summary>
    public void Close()
    {
        Current = null;
        Role = TournamentRole.Viewer;
    }
}