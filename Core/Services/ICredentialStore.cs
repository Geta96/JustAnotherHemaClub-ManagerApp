namespace JustAnotherHemaClub.Services;

/// <summary>
/// Platform-agnostic secure key/value store for the "remember me / biometric"
/// credential persistence used by <see cref="AuthService"/>.
///
/// This is the seam that lets <see cref="AuthService"/> run on both MAUI (backed
/// by <c>SecureStorage</c> + <c>Preferences</c>) and on the Blazor web server
/// (backed by protected browser storage, a session store, or a no-op when
/// "remember me" is not offered on the web). Secret string values (username /
/// password hash) go through the async methods; the small synchronous flag
/// mirrors the MAUI pattern of caching a "has credentials" bit for fast startup.
/// </summary>
public interface ICredentialStore
{
    /// <summary>Stores a secret string value under <paramref name="key"/>.</summary>
    Task SetAsync(string key, string value);

    /// <summary>Reads a previously stored secret string, or <c>null</c> if absent.</summary>
    Task<string?> GetAsync(string key);

    /// <summary>Removes a stored secret string.</summary>
    void Remove(string key);

    /// <summary>Reads a small synchronous boolean flag (fast, non-secret).</summary>
    bool GetFlag(string key, bool fallback);

    /// <summary>Writes a small synchronous boolean flag (fast, non-secret).</summary>
    void SetFlag(string key, bool value);

    /// <summary>Removes a synchronous boolean flag.</summary>
    void RemoveFlag(string key);
}
