using System.Security.Cryptography;
using System.Text;
using JustAnotherHemaClub.Models;

namespace JustAnotherHemaClub.Services;

public class AuthService
{
    private readonly Lazy<IGoogleSheetsService> _sheets;

    public Instructor? CurrentUser { get; private set; }
    public bool IsGuest { get; private set; }

    // Lazy to avoid a circular dependency with the IGoogleSheetsService proxy
    // (which itself depends on AuthService).
    public AuthService(IServiceProvider services)
    {
        _sheets = new Lazy<IGoogleSheetsService>(services.GetRequiredService<IGoogleSheetsService>);
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        IsGuest = false;
        var hash = Hash(password);
        var all = await _sheets.Value.GetInstructorsAsync();
        var match = all.FirstOrDefault(i =>
            string.Equals(i.Username, username, StringComparison.OrdinalIgnoreCase) &&
            i.PasswordHash == hash);
        CurrentUser = match;
        return match is not null;
    }

    public void LoginAsGuest()
    {
        IsGuest = true;
        CurrentUser = new Instructor
        {
            Username = "guest",
            DisplayName = "Guest",
            PasswordHash = string.Empty
        };
    }

    public void Logout()
    {
        CurrentUser = null;
        IsGuest = false;
    }

    public static string Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}