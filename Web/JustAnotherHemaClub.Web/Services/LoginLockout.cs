using Microsoft.Extensions.Caching.Memory;

namespace JustAnotherHemaClub.Web.Services;

/// <summary>
/// Per-account login lockout backed by <see cref="IMemoryCache"/>. Complements the
/// IP-based rate limiter: IP throttling slows a single source, while this stops an
/// attacker who rotates IPs from hammering one username. Counters are best-effort
/// and in-memory only (reset on app restart), which is acceptable for brute-force
/// mitigation on a small single-instance deployment.
/// </summary>
public sealed class LoginLockout
{
    private readonly IMemoryCache _cache;

    private const int MaxFailures = 5;
    private static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(15);

    public LoginLockout(IMemoryCache cache) => _cache = cache;

    private static string Key(string username) =>
        $"login-fail:{(username ?? string.Empty).Trim().ToLowerInvariant()}";

    /// <summary>True when the account currently has too many recent failures.</summary>
    public bool IsLockedOut(string username) =>
        _cache.TryGetValue(Key(username), out int failures) && failures >= MaxFailures;

    /// <summary>Records a failed attempt, sliding the lockout window forward.</summary>
    public void RegisterFailure(string username)
    {
        var key = Key(username);
        var failures = _cache.TryGetValue(key, out int current) ? current + 1 : 1;
        _cache.Set(key, failures, LockoutWindow);
    }

    /// <summary>Clears the counter after a successful login.</summary>
    public void Reset(string username) => _cache.Remove(Key(username));
}
