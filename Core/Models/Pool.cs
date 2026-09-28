namespace JustAnotherHemaClub.Models;

public enum MatchStatus
{
    Pending,
    InProgress,
    Finished
}

public class Pool
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Index { get; set; }
    public List<string> FencerIds { get; set; } = new();
    public List<Match> Matches { get; set; } = new();
    public bool IsClosed { get; set; }

    /// <summary>Optimistic-concurrency token for this pool's header row.</summary>
    public int Version { get; set; }

    public string Name => $"Pool {Index + 1}";
}

public class Match
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string? PoolId { get; set; }
    public int? BracketRound { get; set; }
    public int? BracketSlot { get; set; }

    /// <summary>"Final" or "Bronze".</summary>
    public string? BracketTag { get; set; }

    /// <summary>Order index inside a pool's fight list.</summary>
    public int OrderInPool { get; set; }

    public string LeftFencerId { get; set; } = string.Empty;
    public string RightFencerId { get; set; } = string.Empty;

    public int LeftScore { get; set; }
    public int RightScore { get; set; }
    public int LeftYellowCards { get; set; }
    public int LeftRedCards { get; set; }
    public int RightYellowCards { get; set; }
    public int RightRedCards { get; set; }

    public int RemainingTimeSeconds { get; set; } = 180;
    public MatchStatus Status { get; set; } = MatchStatus.Pending;
    public string? WinnerFencerId { get; set; }

    public DateTime? StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }

    // ----- Concurrency + soft-lock metadata -----

    /// <summary>Optimistic-concurrency token. Incremented on every successful write.</summary>
    public int Version { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
    public string? UpdatedByUserId { get; set; }

    /// <summary>User id currently editing this match. Refreshed via heartbeat.</summary>
    public string? LockedByUserId { get; set; }
    public DateTime? LockedAtUtc { get; set; }

    /// <summary>True when another judge holds a fresh (≤2 min) lock on this match.</summary>
    public bool IsLockedByOther(string myUserId, DateTime nowUtc) =>
        !string.IsNullOrEmpty(LockedByUserId) &&
        LockedByUserId != myUserId &&
        LockedAtUtc.HasValue &&
        nowUtc - LockedAtUtc.Value < TimeSpan.FromMinutes(2);
}