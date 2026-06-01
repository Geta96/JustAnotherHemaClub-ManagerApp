using System.Security.Cryptography;
using System.Text;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

public class AuthService
{
    private readonly Lazy<IGoogleSheetsService> _sheets;

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

    public void LoginAsGuest()
    {
        IsGuest = true;
        CurrentFencer = null;
    }

    public void Logout()
    {
        CurrentFencer = null;
        IsGuest = false;
    }

    /// <summary>Uppercase hex SHA-256 of the UTF-8 bytes of the input.</summary>
    public static string Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}