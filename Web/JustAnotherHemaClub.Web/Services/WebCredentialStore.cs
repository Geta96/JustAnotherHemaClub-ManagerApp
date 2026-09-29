using System.Collections.Concurrent;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Web.Services;

/// <summary>
/// Scoped, in-memory credential store for the web app. This is a deliberate
/// no-persistence default: "remember me / biometric" is not offered on the web
/// yet, so secrets live only for the lifetime of the circuit. Swap this for
/// ProtectedBrowserStorage if you decide to offer persistent web sign-in.
/// </summary>
public sealed class WebCredentialStore : ICredentialStore
{
    private readonly ConcurrentDictionary<string, string> _values = new();
    private readonly ConcurrentDictionary<string, bool> _flags = new();

    public Task SetAsync(string key, string value)
    {
        _values[key] = value;
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string key)
        => Task.FromResult(_values.TryGetValue(key, out var v) ? v : null);

    public void Remove(string key) => _values.TryRemove(key, out _);

    public bool GetFlag(string key, bool fallback)
        => _flags.TryGetValue(key, out var v) ? v : fallback;

    public void SetFlag(string key, bool value) => _flags[key] = value;

    public void RemoveFlag(string key) => _flags.TryRemove(key, out _);
}