namespace JustAnotherHemaClub.Models;

public enum TournamentState
{
    Setup,
    PoolsInProgress,
    PoolsClosed,
    EliminationInProgress,
    Finished
}

public enum TournamentRole
{
    Viewer,
    Fencer,
    Organiser
}

public class Tournament
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;

    /// <summary>Stored plain on purpose so organisers can recover it from the backend.</summary>
    public string PasswordPlain { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public TournamentState State { get; set; } = TournamentState.Setup;

    /// <summary>Optimistic-concurrency token for the tournament header row.</summary>
    public int Version { get; set; }

    // In-memory aggregate. Persisted across TournamentFencers / Pools / Matches / FinalStandings sheets.
    public List<TournamentFencer> Fencers { get; set; } = new();
    public List<Pool> Pools { get; set; } = new();
    public EliminationBracket? Bracket { get; set; }
    public List<string> FinalStandingFencerIds { get; set; } = new();
}

public class TournamentFencer
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public bool IsWithdrawn { get; set; }

    /// <summary>Display order in the roster. Matches reference fencers by Id, not by index.</summary>
    public int OrderIndex { get; set; }
}