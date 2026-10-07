using System.Security.Cryptography;
using System.Text;
using JustAnotherHemaClub.Models;
using Microsoft.Extensions.DependencyInjection;

namespace JustAnotherHemaClub.Services;

public class AuthService
{
    private readonly Lazy<IGoogleSheetsService> _sheets;
    private readonly ICredentialStore _store;

    private const string KeyUsername = "auth.username";
    private const string KeyPasswordHash = "auth.passwordHash";
    private const string KeyBiometricEnabled = "auth.biometricEnabled";

    public Fencer? CurrentFencer { get; private set; }
    public bool IsGuest { get; private set; }

    /// <summary>True when the current session is using the in-memory test data service.</summary>
    public bool IsTestMode { get; private set; }

    public bool IsLoggedInInstructor =>
        CurrentFencer is not null && CurrentFencer.IsInstructor && !IsGuest;

    public bool IsLoggedInFencer =>
        CurrentFencer is not null && !IsGuest;

    /// <summary>
    /// When false, the built-in <c>testuser</c> account shortcut is disabled.
    /// The web app sets this to false outside of Development so the in-memory
    /// test backdoor can never be used in production.
    /// </summary>
    public static bool TestAccountEnabled { get; set; } = true;

    public AuthService(IServiceProvider services, ICredentialStore store)
    {
        _sheets = new Lazy<IGoogleSheetsService>(services.GetRequiredService<IGoogleSheetsService>);
        _store = store;
    }

    /// <summary>
    /// Verifies a candidate (already SHA-256 pre-hashed) secret against a stored
    /// value in either the legacy (raw SHA-256 hex) or new PBKDF2 format, and
    /// reports whether the stored value should be upgraded to PBKDF2.
    /// </summary>
    private static bool VerifyPreHashed(string preHash, string? stored, out bool needsUpgrade)
    {
        needsUpgrade = false;
        var s = (stored ?? string.Empty).Trim();
        if (s.Length == 0) return false;

        if (PasswordHasher.IsPbkdf2(s))
            return PasswordHasher.Verify(preHash, s);

        // Legacy: constant-time compare against the stored SHA-256 hex.
        var ok = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(preHash),
            Encoding.UTF8.GetBytes(s));
        if (ok) needsUpgrade = true;   // migrate this account on successful login
        return ok;
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        IsGuest = false;
        CurrentFencer = null;
        IsTestMode = false;

        var inputUser = (username ?? string.Empty).Trim();
        var inputHash = Hash(password ?? string.Empty);   // SHA-256 pre-hash

        // --- Test user shortcut (disabled in production via TestAccountEnabled) ---
        if (TestAccountEnabled &&
            string.Equals(inputUser, TestDataService.TestUsername, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(inputHash, Hash(TestDataService.TestPassword), StringComparison.OrdinalIgnoreCase))
        {
            IsTestMode = true;
            CurrentFencer = new Fencer
            {
                Id = "test-001", Name = "Test User", Username = TestDataService.TestUsername,
                PasswordHash = inputHash, Email = "test@example.com",
                Active = true, IsInstructor = true, IsStudent = false,
                GdprAccepted = true, LiabilityAccepted = true
            };
            return true;
        }

        var fencers = await _sheets.Value.GetFencersAsync();
        Fencer? match = null;
        bool upgrade = false;

        foreach (var f in fencers)
        {
            if (string.IsNullOrWhiteSpace(f.Username) ||
                !string.Equals(f.Username.Trim(), inputUser, StringComparison.OrdinalIgnoreCase))
                continue;

            if (VerifyPreHashed(inputHash, f.PasswordHash, out upgrade))
            {
                match = f;
                break;
            }
        }

        CurrentFencer = match;

        // Upgrade-on-login: silently re-hash legacy accounts to PBKDF2.
        if (match is not null && upgrade)
        {
            try
            {
                match.PasswordHash = PasswordHasher.Hash(inputHash);
                await _sheets.Value.UpsertFencerAsync(match);
            }
            catch { /* best-effort; login still succeeds */ }
        }

        return match is not null;
    }

    /// <summary>Attempts to log in using the already-hashed password stored in SecureStorage.</summary>
    public async Task<bool> LoginWithStoredHashAsync(string username, string passwordHash)
    {
        IsGuest = false;
        CurrentFencer = null;

        var inputUser = (username ?? string.Empty).Trim();
        var inputHash = (passwordHash ?? string.Empty).Trim();   // device-stored SHA-256 hex

        var fencers = await _sheets.Value.GetFencersAsync();
        Fencer? match = null;
        bool upgrade = false;

        foreach (var f in fencers)
        {
            if (string.IsNullOrWhiteSpace(f.Username) ||
                !string.Equals(f.Username.Trim(), inputUser, StringComparison.OrdinalIgnoreCase))
                continue;

            if (VerifyPreHashed(inputHash, f.PasswordHash, out upgrade))
            {
                match = f;
                break;
            }
        }

        CurrentFencer = match;

        if (match is not null && upgrade)
        {
            try
            {
                match.PasswordHash = PasswordHasher.Hash(inputHash);
                await _sheets.Value.UpsertFencerAsync(match);
            }
            catch { /* ignored */ }
        }

        return match is not null;
    }

    public void LoginAsGuest()
    {
        IsGuest = true;
        CurrentFencer = null;
    }

    /// <summary>
    /// Rehydrates <see cref="CurrentFencer"/> from an already-authenticated
    /// identity (e.g. a web auth cookie) without re-hitting the backend. Used by
    /// the Blazor web app to restore the signed-in fencer on each new scope.
    /// </summary>
    public void RestoreSession(Fencer fencer)
    {
        IsGuest = false;
        CurrentFencer = fencer;
    }

    public void Logout()
    {
        CurrentFencer = null;
        IsGuest = false;
        IsTestMode = false;
        ServiceSwap.Deactivate();
        ClearPersistedCredentials();
    }

    // ------- Persistence (ICredentialStore) -------

    public async Task PersistCredentialsAsync(string username, string passwordHash, bool useBiometric)
    {
        await _store.SetAsync(KeyUsername, username ?? "");
        await _store.SetAsync(KeyPasswordHash, passwordHash ?? "");
        await _store.SetAsync(KeyBiometricEnabled, useBiometric ? "1" : "0");
    }

    public async Task<(string? Username, string? PasswordHash, bool BiometricEnabled)> TryGetPersistedAsync()
    {
        var u = await _store.GetAsync(KeyUsername);
        var h = await _store.GetAsync(KeyPasswordHash);
        var b = await _store.GetAsync(KeyBiometricEnabled);
        return (u, h, b == "1");
    }

    public bool HasPersistedCredentials
    {
        get
        {
            // Secure storage is async-only; a synchronous flag is cached for fast startup.
            return _store.GetFlag(KeyUsername + ".set", false);
        }
    }

    public void ClearPersistedCredentials()
    {
        _store.Remove(KeyUsername);
        _store.Remove(KeyPasswordHash);
        _store.Remove(KeyBiometricEnabled);
        _store.RemoveFlag(KeyUsername + ".set");
    }

    public void MarkPersisted(bool persisted)
        => _store.SetFlag(KeyUsername + ".set", persisted);

    /// <summary>Uppercase hex SHA-256 of the UTF-8 bytes of the input.</summary>
    public static string Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}