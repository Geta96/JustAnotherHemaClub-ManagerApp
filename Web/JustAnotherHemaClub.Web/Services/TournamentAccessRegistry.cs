using System.Collections.Concurrent;

namespace JustAnotherHemaClub.Web.Services;

/// <summary>
/// Server-side record of which users have unlocked ORGANISER access to which
/// tournaments (by entering the correct password). This is the web parallel of
/// the MAUI <c>TournamentSession</c> role: organisers can edit, everyone else is
/// read-only. Registered as a singleton so it survives across the independent DI
/// scopes of separate SSR POST requests.
/// </summary>
public sealed class TournamentAccessRegistry
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _organiserByUser = new();

    public void GrantOrganiser(string userId, string tournamentId)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(tournamentId)) return;
        var set = _organiserByUser.GetOrAdd(userId, _ => new HashSet<string>(StringComparer.Ordinal));
        lock (set) set.Add(tournamentId);
    }

    public void Revoke(string userId, string tournamentId)
    {
        if (string.IsNullOrEmpty(userId)) return;
        if (_organiserByUser.TryGetValue(userId, out var set))
            lock (set) set.Remove(tournamentId);
    }

    public bool IsOrganiser(string? userId, string? tournamentId)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(tournamentId)) return false;
        return _organiserByUser.TryGetValue(userId, out var set) &&
               ContainsLocked(set, tournamentId);
    }

    private static bool ContainsLocked(HashSet<string> set, string id)
    {
        lock (set) return set.Contains(id);
    }
}
