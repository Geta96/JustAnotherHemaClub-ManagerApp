using System.Security.Cryptography;
using System.Text;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

public class AuthService
{
    private readonly Lazy<IGoogleSheetsService> _sheets;

    private const string KeyUsername = "auth.username";
    private const string KeyPasswordHash = "auth.passwordHash";
    private const string KeyBiometricEnabled = "auth.biometricEnabled";

    public Fencer? CurrentFencer { get; private set; }
    public bool IsGuest { get; private set; }

    public bool IsLoggedInInstructor =>
        CurrentFencer is not null && CurrentFencer.IsInstructor && !IsGuest;

    public bool IsLoggedInFencer =>
        CurrentFencer is not null && !IsGuest;

    public AuthService(IServiceProvider services)
    {
        _sheets = new Lazy<IGoogleSheetsService>(services.GetRequiredService<IGoogleSheetsService>);
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        IsGuest = false;
        CurrentFencer = null;

        var inputUser = (username ?? string.Empty).Trim();
        var inputHash = Hash(password ?? string.Empty);

        var fencers = await _sheets.Value.GetFencersAsync();
        var match = fencers.FirstOrDefault(f =>
            !string.IsNullOrWhiteSpace(f.Username) &&
            string.Equals(f.Username.Trim(), inputUser, StringComparison.OrdinalIgnoreCase) &&
            string.Equals((f.PasswordHash ?? "").Trim(), inputHash, StringComparison.OrdinalIgnoreCase));

        CurrentFencer = match;
        return match is not null;
    }

    /// <summary>Attempts to log in using the already-hashed password stored in SecureStorage.</summary>
    public async Task<bool> LoginWithStoredHashAsync(string username, string passwordHash)
    {
        IsGuest = false;
        CurrentFencer = null;

        var inputUser = (username ?? string.Empty).Trim();
        var inputHash = (passwordHash ?? string.Empty).Trim();

        var fencers = await _sheets.Value.GetFencersAsync();
        var match = fencers.FirstOrDefault(f =>
            !string.IsNullOrWhiteSpace(f.Username) &&
            string.Equals(f.Username.Trim(), inputUser, StringComparison.OrdinalIgnoreCase) &&
            string.Equals((f.PasswordHash ?? "").Trim(), inputHash, StringComparison.OrdinalIgnoreCase));

        CurrentFencer = match;
        return match is not null;
    }

    public void LoginAsGuest()
    {
        IsGuest = true;
        CurrentFencer = null;
    }

    public void Logout()
    {
        CurrentFencer = null;
        IsGuest = false;
        ClearPersistedCredentials();
    }

    // ------- Persistence (SecureStorage) -------

    public async Task PersistCredentialsAsync(string username, string passwordHash, bool useBiometric)
    {
        await SecureStorage.Default.SetAsync(KeyUsername, username ?? "");
        await SecureStorage.Default.SetAsync(KeyPasswordHash, passwordHash ?? "");
        await SecureStorage.Default.SetAsync(KeyBiometricEnabled, useBiometric ? "1" : "0");
    }

    public async Task<(string? Username, string? PasswordHash, bool BiometricEnabled)> TryGetPersistedAsync()
    {
        var u = await SecureStorage.Default.GetAsync(KeyUsername);
        var h = await SecureStorage.Default.GetAsync(KeyPasswordHash);
        var b = await SecureStorage.Default.GetAsync(KeyBiometricEnabled);
        return (u, h, b == "1");
    }

    public bool HasPersistedCredentials
    {
        get
        {
            // SecureStorage is async-only; the platform layer caches a sync flag in Preferences for speed.
            return Preferences.Default.Get(KeyUsername + ".set", false);
        }
    }

    public void ClearPersistedCredentials()
    {
        SecureStorage.Default.Remove(KeyUsername);
        SecureStorage.Default.Remove(KeyPasswordHash);
        SecureStorage.Default.Remove(KeyBiometricEnabled);
        Preferences.Default.Remove(KeyUsername + ".set");
    }

    public void MarkPersisted(bool persisted)
        => Preferences.Default.Set(KeyUsername + ".set", persisted);

    /// <summary>Uppercase hex SHA-256 of the UTF-8 bytes of the input.</summary>
    public static string Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}