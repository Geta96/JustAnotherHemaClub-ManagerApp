namespace JustAnotherHemaClub.Services;

/// <summary>
/// MAUI implementation of <see cref="ICredentialStore"/>. Secret values are
/// stored in the platform <c>SecureStorage</c> (Keychain / Keystore); the small
/// synchronous flags use <c>Preferences</c>, matching the original
/// <see cref="AuthService"/> behaviour.
/// </summary>
public sealed class MauiCredentialStore : ICredentialStore
{
    public Task SetAsync(string key, string value)
        => SecureStorage.Default.SetAsync(key, value ?? "");

    public Task<string?> GetAsync(string key)
        => SecureStorage.Default.GetAsync(key);

    public void Remove(string key)
        => SecureStorage.Default.Remove(key);

    public bool GetFlag(string key, bool fallback)
        => Preferences.Default.Get(key, fallback);

    public void SetFlag(string key, bool value)
        => Preferences.Default.Set(key, value);

    public void RemoveFlag(string key)
        => Preferences.Default.Remove(key);
}
